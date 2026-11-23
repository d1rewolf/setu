using System.ComponentModel;
using ModelContextProtocol.Server;
using Setu.Core.Files;

namespace Setu.Core.Shell;

/// <summary>
/// What start_process does, shared by the per-platform tool classes below: they differ only in the text the
/// agent reads, which is fixed per class by attributes.
/// </summary>
internal static class ProcessStart
{
    internal static string Run(
        RequestLog log, IDetachedLauncher launcher, FileToolsOptions options, string path, string? arguments, string? cwd)
        => ToolGuard.Run(log, "start_process", () =>
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ToolInputException("path is required");
            var dir = cwd is null ? null : PathResolver.Resolve(cwd, options.Protected);
            var pid = launcher.Start(path, arguments, dir);
            return $"Started {path} as PID {pid}";
        });
}

/// <summary>start_process as the Windows server offers it.</summary>
[McpServerToolType]
public sealed class WindowsProcessTools(RequestLog log, IDetachedLauncher launcher, FileToolsOptions options)
{
    [McpServerTool(Name = "start_process")]
    [Description("Starts a program in the user's desktop session and returns its PID without waiting for it. Use this, not a shell command's own background launch, for GUI apps or anything you want to keep running after the call returns.")]
    public string StartProcess(
        [Description("Program path or name on PATH, e.g. notepad.exe.")] string path,
        [Description("Optional command-line arguments as one string.")] string? arguments = null,
        [Description("Optional absolute working directory. %VARS% are expanded.")] string? cwd = null)
        => ProcessStart.Run(log, launcher, options, path, arguments, cwd);
}

/// <summary>start_process as the Mac server offers it (see MacDetachedLauncher for apps versus programs).</summary>
[McpServerToolType]
public sealed class MacProcessTools(RequestLog log, IDetachedLauncher launcher, FileToolsOptions options)
{
    [McpServerTool(Name = "start_process")]
    [Description("Starts a program in the user's desktop session and returns its PID without waiting for it. Use this, not a shell command's own background launch, for GUI apps or anything you want to keep running after the call returns. A path ending in .app, or an app name that is not a program on PATH (e.g. TextEdit), starts through LaunchServices; if that app is already running it is brought to the front and its existing PID is returned.")]
    public string StartProcess(
        [Description("Program path or name on PATH, or an app name or .app path, e.g. TextEdit. A bare name is looked up on /usr/bin:/bin:/usr/sbin:/sbin; give the full path for anything installed elsewhere (e.g. Homebrew).")] string path,
        [Description("Optional command-line arguments as one string, split into arguments like a POSIX shell does (single quotes, double quotes and backslash), with no variable, ~ or wildcard expansion. For an app, absolute paths of existing files are opened as documents and the rest are passed to the app.")] string? arguments = null,
        [Description("Optional absolute working directory; a leading ~ is your home folder. Ignored when starting an app.")] string? cwd = null)
        => ProcessStart.Run(log, launcher, options, path, arguments, cwd);
}
