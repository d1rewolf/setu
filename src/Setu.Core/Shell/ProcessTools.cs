using System.ComponentModel;
using ModelContextProtocol.Server;
using Setu.Core.Files;

namespace Setu.Core.Shell;

[McpServerToolType]
public sealed class ProcessTools(RequestLog log, IDetachedLauncher launcher, FileToolsOptions options)
{
    [McpServerTool(Name = "start_process")]
    [Description("Starts a program in the user's desktop session and returns its PID without waiting for it. Use this, not a shell command's own background launch, for GUI apps or anything you want to keep running after the call returns. On macOS, a path ending in .app or an app name that is not a program on PATH (e.g. TextEdit) starts through LaunchServices; if that app is already running it is brought to the front and its existing PID is returned.")]
    public string StartProcess(
        [Description("Program path or name on PATH, e.g. notepad.exe.")] string path,
        [Description("Optional command-line arguments as one string. On Windows it is passed to the program as written. On macOS it is split into arguments like a POSIX shell does (single quotes, double quotes and backslash), with no variable, ~ or wildcard expansion; for an app, absolute paths of existing files are opened as documents and the rest are passed to the app.")] string? arguments = null,
        [Description("Optional absolute working directory. %VARS% are expanded.")] string? cwd = null)
        => ToolGuard.Run(log, "start_process", () =>
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ToolInputException("path is required");
            var dir = cwd is null ? null : PathResolver.Resolve(cwd, options.Protected);
            var pid = launcher.Start(path, arguments, dir);
            return $"Started {path} as PID {pid}";
        });
}
