using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using WindowsMcp.Core.Files;

namespace WindowsMcp.Core.Shell;

[McpServerToolType]
public sealed class ShellTools(RequestLog log, IProcessRunner runner, IDetachedLauncher launcher)
{
    private const int MinTimeoutSeconds = 1;
    private const int MaxTimeoutSeconds = 600;
    private const string TruncatedMarker = "[truncated at 64 KiB]\n";

    [McpServerTool(Name = "powershell")]
    [Description("Runs a Windows PowerShell command and waits for it to finish. Non-interactive: anything that waits for input fails instead of waiting. On timeout the command and everything it started is killed and the partial output is returned. Output is capped at 64 KiB per stream. Programs this command starts may be stopped when the server restarts; use start_process for apps that must keep running.")]
    public Task<string> PowerShell(
        [Description("PowerShell script text.")] string command,
        [Description("Optional absolute working directory. %VARS% are expanded.")] string? cwd = null,
        [Description("Timeout in seconds, 1 to 600.")] int timeout_s = 60,
        CancellationToken cancellationToken = default)
        => Run("powershell", "powershell.exe", CommandLines.PowerShell(command), cwd, timeout_s, ClixmlDecoder.Decode, cancellationToken);

    [McpServerTool(Name = "cmd")]
    [Description("Runs a cmd.exe command line and waits for it to finish. Non-interactive: anything that waits for input fails instead of waiting. On timeout the command and everything it started is killed and the partial output is returned. Output is capped at 64 KiB per stream. Programs this command starts may be stopped when the server restarts; use start_process for apps that must keep running.")]
    public Task<string> Cmd(
        [Description("cmd.exe command line.")] string command,
        [Description("Optional absolute working directory. %VARS% are expanded.")] string? cwd = null,
        [Description("Timeout in seconds, 1 to 600.")] int timeout_s = 60,
        CancellationToken cancellationToken = default)
        => Run("cmd", "cmd.exe", CommandLines.Cmd(command), cwd, timeout_s, static text => text, cancellationToken);

    [McpServerTool(Name = "start_process")]
    [Description("Starts a program in the user's desktop session and returns its PID without waiting for it. Use this, not powershell Start-Process or cmd start, for GUI apps or anything you want to keep running after the call returns.")]
    public string StartProcess(
        [Description("Program path or name on PATH, e.g. notepad.exe.")] string path,
        [Description("Optional command-line arguments as one string.")] string? arguments = null,
        [Description("Optional absolute working directory. %VARS% are expanded.")] string? cwd = null)
        => ToolGuard.Run(log, "start_process", () =>
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ToolInputException("path is required");
            var dir = cwd is null ? null : PathResolver.Resolve(cwd);
            var pid = launcher.Start(path, arguments, dir);
            return $"Started {path} as PID {pid}";
        });

    /// <summary>
    /// The SDK binds <paramref name="ct"/> to the request (it is not part of the tool's input schema), so a
    /// call the client abandons kills the command and everything it started.
    /// </summary>
    private Task<string> Run(
        string tool, string fileName, string arguments, string? cwd, int timeoutSeconds,
        Func<string, string> decode, CancellationToken ct)
        => ToolGuard.RunAsync(log, tool, async () =>
        {
            if (timeoutSeconds is < MinTimeoutSeconds or > MaxTimeoutSeconds)
                throw new ToolInputException($"timeout_s must be between {MinTimeoutSeconds} and {MaxTimeoutSeconds}");
            var dir = cwd is null ? null : PathResolver.Resolve(cwd);
            var result = await runner.RunAsync(
                new ProcessRequest(fileName, arguments, dir, TimeSpan.FromSeconds(timeoutSeconds)),
                ct);
            // Decoding after the cap only ever shortens the text, so the 64 KiB bound still holds.
            return Format(result with { Stdout = decode(result.Stdout), Stderr = decode(result.Stderr) });
        });

    private static string Format(ProcessResult r)
    {
        var sb = new StringBuilder();
        var exit = r.ExitCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none";
        sb.Append($"exit_code={exit} timed_out={(r.TimedOut ? "true" : "false")} elapsed_ms={(long)r.Elapsed.TotalMilliseconds}\n");
        AppendStream(sb, "stdout", r.Stdout, r.StdoutTruncated);
        AppendStream(sb, "stderr", r.Stderr, r.StderrTruncated);
        return sb.ToString();
    }

    private static void AppendStream(StringBuilder sb, string name, string text, bool truncated)
    {
        sb.Append($"--- {name} ---\n");
        sb.Append(text);
        if (text.Length > 0 && text[^1] != '\n') sb.Append('\n');
        if (truncated) sb.Append(TruncatedMarker);
    }
}
