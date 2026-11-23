using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ModelContextProtocol;
using Setu.Core;
using Setu.Core.Files;
using Setu.Core.Shell;

namespace Setu.Mac;

internal enum LaunchKind { App, Executable }

/// <summary>
/// start_process on the Mac. An app (a path ending in .app, or a bare name that is not an executable on PATH,
/// such as TextEdit) is started through LaunchServices with <c>open -a</c>, so it belongs to launchd and has its
/// own macOS identity; its PID is then read with <c>lsappinfo</c>. Anything else is spawned detached with
/// posix_spawn (see <see cref="PosixSpawn"/>). The arguments string is split like a POSIX shell does.
/// </summary>
public sealed partial class MacDetachedLauncher : IDetachedLauncher
{
    private const string Open = "/usr/bin/open";
    private const string LsAppInfo = "/usr/bin/lsappinfo";
    private const string PlUtil = "/usr/bin/plutil";
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(5);

    private readonly RequestLog log;
    private readonly ProtectedPaths protectedPaths;
    private readonly ICommandRunner run;
    private readonly Func<string, string?> findOnPath;
    private readonly TimeSpan pidWait;
    private readonly TimeSpan pollInterval;

    /// <param name="protectedPaths">Programs, documents and folders in these are refused before anything touches them.</param>
    public MacDetachedLauncher(RequestLog log, ProtectedPaths protectedPaths)
        : this(log, protectedPaths, new ProcessCommandRunner(),
            name => FindOnPath(name, Environment.GetEnvironmentVariable("PATH")),
            TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(200))
    {
    }

    internal MacDetachedLauncher(
        RequestLog log, ProtectedPaths protectedPaths, ICommandRunner run, Func<string, string?> findOnPath,
        TimeSpan pidWait, TimeSpan pollInterval)
    {
        this.log = log;
        this.protectedPaths = protectedPaths;
        this.run = run;
        this.findOnPath = findOnPath;
        this.pidWait = pidWait;
        this.pollInterval = pollInterval;
    }

    public int Start(string path, string? arguments, string? workingDirectory)
    {
        var program = path.Trim();
        if (program.Length == 0) throw new ToolInputException("path is required");
        var args = SplitArgs(arguments);
        return Classify(program, name => findOnPath(name) is not null) == LaunchKind.App
            ? StartApp(program, args)
            : StartExecutable(program, args, workingDirectory);
    }

    /// <summary>
    /// A path ending in .app is an app. A bare name (no '/') is an executable when one of that name is on PATH,
    /// otherwise an app name for LaunchServices. Any other path is an executable.
    /// </summary>
    internal static LaunchKind Classify(string path, Func<string, bool> isExecutableOnPath)
    {
        if (path.TrimEnd('/').EndsWith(".app", StringComparison.OrdinalIgnoreCase)) return LaunchKind.App;
        if (path.Contains('/', StringComparison.Ordinal)) return LaunchKind.Executable;
        return isExecutableOnPath(path) ? LaunchKind.Executable : LaunchKind.App;
    }

    /// <summary>
    /// Splits the arguments string by the POSIX shell's quoting rules: whitespace separates words; inside single
    /// quotes everything is literal; inside double quotes a backslash escapes only $ ` " \ and newline; outside
    /// quotes a backslash escapes any character, and backslash-newline is removed. Nothing is expanded
    /// ($VARS, ~ and wildcards stay as written).
    /// </summary>
    internal static List<string> SplitArgs(string? text)
    {
        var words = new List<string>();
        if (text is null) return words;
        var word = new StringBuilder();
        var inWord = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            switch (c)
            {
                case ' ' or '\t' or '\n' or '\r':
                    if (inWord) words.Add(word.ToString());
                    word.Clear();
                    inWord = false;
                    break;
                case '\'':
                    inWord = true;
                    var close = text.IndexOf('\'', i + 1);
                    if (close < 0) throw new ToolInputException("arguments has an unclosed single quote");
                    word.Append(text, i + 1, close - i - 1);
                    i = close;
                    break;
                case '"':
                    inWord = true;
                    for (i++; ; i++)
                    {
                        if (i >= text.Length) throw new ToolInputException("arguments has an unclosed double quote");
                        var d = text[i];
                        if (d == '"') break;
                        if (d == '\\' && i + 1 < text.Length && text[i + 1] is '$' or '`' or '"' or '\\' or '\n')
                        {
                            i++;
                            if (text[i] != '\n') word.Append(text[i]);
                        }
                        else
                        {
                            word.Append(d);
                        }
                    }
                    break;
                case '\\':
                    if (i + 1 >= text.Length) throw new ToolInputException("arguments ends with a lone backslash");
                    i++;
                    if (text[i] == '\n') break; // line continuation
                    inWord = true;
                    word.Append(text[i]);
                    break;
                default:
                    inWord = true;
                    word.Append(c);
                    break;
            }
        }
        if (inWord) words.Add(word.ToString());
        return words;
    }

    /// <summary>
    /// The arguments for <c>/usr/bin/open</c>: <c>-a app</c>, then the arguments that are documents, then the rest
    /// after <c>--args</c>. Never <c>-n</c>: a running app is activated and keeps its PID.
    /// </summary>
    internal static List<string> OpenArgs(string app, IReadOnlyList<string> args, Func<string, bool> isDocument)
    {
        var result = new List<string> { "-a", app };
        var rest = new List<string>();
        foreach (var a in args)
        {
            if (isDocument(a)) result.Add(a);
            else rest.Add(a);
        }
        if (rest.Count > 0) result.AddRange(["--args", .. rest]);
        return result;
    }

    /// <summary>The highest PID in <c>lsappinfo info -only pid</c> output (one <c>"pid"=N</c> per instance), or null.</summary>
    internal static int? NewestPid(string lsappinfoOutput)
    {
        int? newest = null;
        foreach (Match m in PidEntry().Matches(lsappinfoOutput))
        {
            if (int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var pid) && pid > 0
                && (newest is null || pid > newest))
                newest = pid;
        }
        return newest;
    }

    /// <summary>The first executable regular file called <paramref name="name"/> in the PATH directories, or null.</summary>
    internal static string? FindOnPath(string name, string? pathVariable)
    {
        if (string.IsNullOrEmpty(pathVariable)) return null;
        foreach (var dir in pathVariable.Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Path.IsPathFullyQualified(dir)) continue;
            var candidate = Path.Combine(dir, name);
            if (IsExecutableFile(candidate)) return candidate;
        }
        return null;
    }

    private static bool IsExecutableFile(string path)
    {
        try
        {
            const UnixFileMode anyExecute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            return !OperatingSystem.IsWindows() && File.Exists(path) && (File.GetUnixFileMode(path) & anyExecute) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private int StartApp(string app, List<string> args)
    {
        // A bundle path is resolved (~ expanded, must be absolute, refused in a protected folder); a name is
        // passed to LaunchServices as written.
        var target = app.Contains('/', StringComparison.Ordinal) ? PathResolver.Resolve(app, protectedPaths) : app;
        var openArgs = OpenArgs(target, args, IsDocument);

        var (exit, _, stderr) = run.RunCapture(Open, openArgs, CommandTimeout);
        if (exit != 0)
        {
            var detail = stderr.Trim();
            if (detail.Contains("Unable to find application", StringComparison.Ordinal))
                throw new McpException($"No application named '{app}'");
            throw new McpException($"Could not start {app}: {(detail.Length > 0 ? detail : $"open exited with code {exit}")}");
        }

        var id = AppIdentifier(target);
        var deadline = DateTime.UtcNow + pidWait;
        while (true)
        {
            var (_, stdout, _) = run.RunCapture(LsAppInfo, ["info", "-only", "pid", id], QueryTimeout);
            if (NewestPid(stdout) is { } pid)
            {
                log.Write($"start_process: open -a {target} gave PID {pid.ToString(CultureInfo.InvariantCulture)} ({id})");
                return pid;
            }
            if (DateTime.UtcNow >= deadline) break;
            Thread.Sleep(pollInterval);
        }
        throw new McpException($"Started {app} but could not find its PID");
    }

    /// <summary>An existing file or folder given by absolute path is a document; one in a protected folder is refused.</summary>
    private bool IsDocument(string arg)
    {
        if (!Path.IsPathFullyQualified(arg)) return false;
        var full = Path.GetFullPath(arg);
        if (protectedPaths.Match(full) is not null) throw new ToolInputException(ProtectedPaths.Message(arg));
        return File.Exists(full) || Directory.Exists(full);
    }

    /// <summary>The bundle id from Info.plist for a bundle path, else the app's name, for lsappinfo.</summary>
    private string AppIdentifier(string app)
    {
        var trimmed = app.TrimEnd('/');
        if (trimmed.Contains('/', StringComparison.Ordinal))
        {
            var plist = Path.Combine(trimmed, "Contents", "Info.plist");
            var (exit, stdout, _) = run.RunCapture(PlUtil, ["-extract", "CFBundleIdentifier", "raw", "-o", "-", plist], QueryTimeout);
            var id = stdout.Trim();
            if (exit == 0 && id.Length > 0) return id;
        }
        var name = Path.GetFileName(trimmed);
        return name.EndsWith(".app", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    private int StartExecutable(string program, List<string> args, string? workingDirectory)
    {
        string resolved;
        if (!program.Contains('/', StringComparison.Ordinal))
        {
            resolved = findOnPath(program) ?? throw new McpException($"No such program: {program}");
        }
        else if (program.StartsWith('/') || program.StartsWith('~'))
        {
            resolved = PathResolver.Resolve(program, protectedPaths);
        }
        else
        {
            resolved = Path.GetFullPath(program, workingDirectory ?? Environment.CurrentDirectory);
            if (protectedPaths.Match(resolved) is not null) throw new ToolInputException(ProtectedPaths.Message(program));
        }

        if (Directory.Exists(resolved)) throw new McpException($"{program} is a folder, not a program");
        if (!File.Exists(resolved)) throw new McpException($"No such program: {program}");
        if (!IsExecutableFile(resolved)) throw new McpException($"{program} is not executable");

        var pid = PosixSpawn.Spawn(resolved, [program, .. args], workingDirectory);
        log.Write($"start_process: spawned {resolved} as PID {pid.ToString(CultureInfo.InvariantCulture)}");
        return pid;
    }

    [GeneratedRegex(@"""?pid""?\s*=\s*(\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex PidEntry();
}
