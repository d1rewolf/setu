using Microsoft.Extensions.Hosting;
using System.Net;
using System.Net.Sockets;
using Setu.Core.Files;
using Setu.Core.Screen;
using Setu.Core.Shell;

namespace Setu.Core.Server;

/// <param name="AcquireSingleInstance">Returns a handle held for the whole run, or null when another copy holds it.</param>
/// <param name="AfterStart">Runs once the server is listening. Its exceptions are logged and do not end the server.</param>
public sealed record PlatformParts(
    string DataDirectory,
    Func<IDisposable?> AcquireSingleInstance,
    ITokenFileProtector TokenProtector,
    Action StartKeepAwake,
    Func<IProcessContainment> Containment,
    IDetachedLauncher Launcher,
    IDesktop Desktop,
    FileToolsOptions FileOptions,
    KeyVocabulary Keys,
    ShellFlavor Shell,
    ZshSettings? Zsh,
    Action<RequestLog>? AfterStart = null);

/// <summary>Startup shared by every platform: single instance, data directory, token, port, host, exit codes.</summary>
public static class SetuStartup
{
    public static int Run(PlatformParts parts) => Run(parts, CancellationToken.None);

    /// <summary>Cancelling <paramref name="stop"/> stops the app gracefully and <c>Run</c> returns 0.</summary>
    public static int Run(PlatformParts parts, CancellationToken stop) => Run(parts, null, stop);

    /// <summary>
    /// Builds the parts inside startup's failure handling, so a part whose constructor throws ends in a logged
    /// exit code 1, and hands <paramref name="build"/> the one <see cref="RequestLog"/> the whole run writes to
    /// (two RequestLog instances on one file would not serialize their writes). The parts must use
    /// <paramref name="dataDirectory"/>.
    /// </summary>
    public static int Run(string dataDirectory, Func<RequestLog, PlatformParts> build) =>
        Run(dataDirectory, build, CancellationToken.None);

    /// <inheritdoc cref="Run(string, Func{RequestLog, PlatformParts})"/>
    public static int Run(string dataDirectory, Func<RequestLog, PlatformParts> build, CancellationToken stop)
    {
        var log = new RequestLog(Path.Combine(dataDirectory, "setu.log"));
        PlatformParts parts;
        try
        {
            parts = build(log);
            if (parts.DataDirectory != dataDirectory)
                throw new ArgumentException($"The parts use {parts.DataDirectory}, not {dataDirectory}.", nameof(build));
        }
        catch (Exception ex)
        {
            // Best effort, as for a second instance: the log may not be writable; RequestLog swallows failures.
            try { Directory.CreateDirectory(dataDirectory); } catch (Exception) { }
            return Fail(log, 1, ex.Message.ReplaceLineEndings(" "));
        }
        return Run(parts, log, stop);
    }

    private static int Fail(RequestLog log, int code, string message)
    {
        log.Write($"startup failed: {message}");
        Console.Error.WriteLine(message);
        return code;
    }

    private static int Run(PlatformParts parts, RequestLog? shared, CancellationToken stop)
    {
        var data = parts.DataDirectory;
        var log = RequestLog.Null;
        RequestLog OpenLog() => shared ?? new RequestLog(Path.Combine(data, "setu.log"));
        int Fail(int code, string message) => SetuStartup.Fail(log, code, message);

        IDisposable? acquired;
        try
        {
            acquired = parts.AcquireSingleInstance();
        }
        catch (Exception ex)
        {
            try { Directory.CreateDirectory(data); } catch (Exception) { }
            log = OpenLog();
            return Fail(1, ex.Message.ReplaceLineEndings(" "));
        }
        using var instance = acquired;
        if (instance is null)
        {
            // Best effort: the log may not exist or be writable; RequestLog swallows failures.
            try { Directory.CreateDirectory(data); } catch (Exception) { }
            log = OpenLog();
            return Fail(2, "setu is already running for this user.");
        }

        var port = ServerOptions.DefaultPort;
        try
        {
            Directory.CreateDirectory(data);
            log = OpenLog();
            var options = ServerOptions.Load(data);
            port = options.Port;
            var token = new TokenStore(data, parts.TokenProtector).GetOrCreate();
            try
            {
                parts.StartKeepAwake();
            }
            catch (Exception ex)
            {
                // Sleep prevention is a convenience: the server still works without it.
                log.Write($"keep-awake failed, continuing without it: {ex.Message}");
            }

            var app = ServerHost.Build(new ServerHostSettings(
                IPAddress.Loopback, port, token, log, parts.FileOptions, parts.Containment, parts.Launcher,
                parts.Desktop, parts.Keys, parts.Shell, parts.Zsh));
            var started = log;
            app.Lifetime.ApplicationStarted.Register(() =>
            {
                started.Write($"listening on http://127.0.0.1:{port}/mcp");
                try
                {
                    parts.AfterStart?.Invoke(started);
                }
                catch (Exception ex)
                {
                    started.Write($"after-start failed, continuing: {ex.Message}");
                }
            });
            ((IHost)app).RunAsync(stop).GetAwaiter().GetResult();
            return 0;
        }
        catch (Exception ex) when (IsAddressInUse(ex))
        {
            return Fail(3, $"Port {port} is in use; set \"port\" in {Path.Combine(data, "config.json")}");
        }
        catch (Exception ex)
        {
            return Fail(1, ex.Message.ReplaceLineEndings(" "));
        }
    }

    private static bool IsAddressInUse(Exception? ex)
    {
        for (; ex is not null; ex = ex.InnerException)
            if (ex is SocketException { SocketErrorCode: SocketError.AddressAlreadyInUse }) return true;
        return false;
    }
}
