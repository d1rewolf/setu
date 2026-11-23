using System.ComponentModel;
using ModelContextProtocol.Server;
using Setu.Core.Files;

namespace Setu.Core.Shell;

/// <summary>Where the <c>shell</c> tool finds perl (which sets up the process group) and the shell itself.</summary>
public sealed record ZshSettings(string PerlPath = "/usr/bin/perl", string ShellPath = "/bin/zsh");

[McpServerToolType]
public sealed class ZshShellTools(RequestLog log, IProcessRunner runner, FileToolsOptions options, ZshSettings settings)
{
    private const string Utf8Locale = "en_US.UTF-8";

    [McpServerTool(Name = "shell")]
    [Description("Runs a zsh command (as a login shell, so PATH matches Terminal) and waits for it to finish. Non-interactive: anything that waits for input fails instead of waiting. On timeout the command and everything it started is killed and the partial output is returned. Output is capped at 64 KiB per stream. Commands run with Setu's macOS permissions, and their permission prompts name Setu. Desktop, Documents, Downloads, iCloud Drive and other apps' data are off-limits: access fails with 'Operation not permitted'. Use start_process for GUI apps.")]
    public Task<string> Shell(
        [Description("zsh command text.")] string command,
        [Description("Optional absolute working directory. A leading ~ is your home folder.")] string? cwd = null,
        [Description("Timeout in seconds, 1 to 600.")] int timeout_s = 60,
        CancellationToken cancellationToken = default)
        => ShellCommand.RunAsync(
            log, runner, "shell", cwd, timeout_s, options.Protected,
            (dir, timeout) => new ProcessRequest(
                settings.PerlPath, CommandLines.Zsh(settings.ShellPath, command), dir, timeout,
                LocaleEnvironment(Environment.GetEnvironmentVariable)),
            static text => text, cancellationToken);

    /// <summary>
    /// LANG and LC_ALL set to UTF-8 for each one the server's own environment leaves unset or empty (a
    /// LaunchAgent gets neither), so command output is UTF-8. Null when both are already set.
    /// </summary>
    internal static Dictionary<string, string>? LocaleEnvironment(Func<string, string?> getenv)
    {
        var env = new Dictionary<string, string>();
        foreach (var name in (string[])["LANG", "LC_ALL"])
            if (string.IsNullOrEmpty(getenv(name))) env[name] = Utf8Locale;
        return env.Count == 0 ? null : env;
    }
}
