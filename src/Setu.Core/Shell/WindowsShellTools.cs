using System.ComponentModel;
using ModelContextProtocol.Server;
using Setu.Core.Files;

namespace Setu.Core.Shell;

[McpServerToolType]
public sealed class WindowsShellTools(RequestLog log, IProcessRunner runner, FileToolsOptions options)
{
    [McpServerTool(Name = "powershell")]
    [Description("Runs a Windows PowerShell command and waits for it to finish. Non-interactive: anything that waits for input fails instead of waiting. On timeout the command and everything it started is killed and the partial output is returned. Output is capped at 64 KiB per stream. Programs this command starts may be stopped when the server restarts; use start_process for apps that must keep running.")]
    public Task<string> PowerShell(
        [Description("PowerShell script text.")] string command,
        [Description("Optional absolute working directory. %VARS% are expanded.")] string? cwd = null,
        [Description("Timeout in seconds, 1 to 600.")] int timeout_s = 60,
        CancellationToken cancellationToken = default)
        => ShellCommand.RunAsync(
            log, runner, "powershell", cwd, timeout_s, options.Protected,
            (dir, timeout) => ProcessRequest.Raw("powershell.exe", CommandLines.PowerShell(command), dir, timeout),
            ClixmlDecoder.Decode, cancellationToken);

    [McpServerTool(Name = "cmd")]
    [Description("Runs a cmd.exe command line and waits for it to finish. Non-interactive: anything that waits for input fails instead of waiting. On timeout the command and everything it started is killed and the partial output is returned. Output is capped at 64 KiB per stream. Programs this command starts may be stopped when the server restarts; use start_process for apps that must keep running.")]
    public Task<string> Cmd(
        [Description("cmd.exe command line.")] string command,
        [Description("Optional absolute working directory. %VARS% are expanded.")] string? cwd = null,
        [Description("Timeout in seconds, 1 to 600.")] int timeout_s = 60,
        CancellationToken cancellationToken = default)
        => ShellCommand.RunAsync(
            log, runner, "cmd", cwd, timeout_s, options.Protected,
            (dir, timeout) => ProcessRequest.Raw("cmd.exe", CommandLines.Cmd(command), dir, timeout),
            static text => text, cancellationToken);
}
