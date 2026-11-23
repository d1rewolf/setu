using System.Net;
using System.Net.Sockets;
using Setu.Core.Files;
using Setu.Core.Screen;
using Setu.Core.Server;
using Setu.Core.Shell;

namespace Setu.Core.Tests;

public sealed class SetuStartupTests : IDisposable
{
    private readonly string _data = Path.Combine(Path.GetTempPath(), "setu-startup-" + Guid.NewGuid().ToString("N"));

    public SetuStartupTests() => Directory.CreateDirectory(_data);

    public void Dispose()
    {
        try { Directory.Delete(_data, recursive: true); } catch (IOException) { }
    }

    private sealed class FakeLauncher : IDetachedLauncher
    {
        public int Start(string path, string? arguments, string? workingDirectory) => 1234;
    }

    private sealed class NoProtector : ITokenFileProtector
    {
        public void Protect(string path) { }
    }

    private PlatformParts Parts(Func<IDisposable?>? acquire = null, Action? keepAwake = null, Action<RequestLog>? after = null) =>
        new(_data, acquire ?? (() => new Handle()), new NoProtector(), keepAwake ?? (() => { }),
            () => new DefaultContainment(), new FakeLauncher(), new FakeDesktop(),
            new FileToolsOptions(1252, LineEndings.Crlf, ProtectedPaths.None), KeyVocabulary.Windows,
            ShellFlavor.Windows, null, after);

    private sealed class Handle : IDisposable { public void Dispose() { } }

    private static int FreePort()
    {
        using var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        return ((IPEndPoint)l.LocalEndpoint).Port;
    }

    private string Log => File.ReadAllText(Path.Combine(_data, "setu.log"));

    [Fact]
    public void Run_SecondInstance_Returns2_AndLogs()
    {
        var code = SetuStartup.Run(Parts(acquire: () => null), TestContext.Current.CancellationToken);
        Assert.Equal(2, code);
        Assert.EndsWith("startup failed: setu is already running for this user." + Environment.NewLine, Log);
    }

    [Fact]
    public void Run_PortInUse_Returns3()
    {
        using var holder = new TcpListener(IPAddress.Loopback, 0);
        holder.Start();
        var port = ((IPEndPoint)holder.LocalEndpoint).Port;
        File.WriteAllText(Path.Combine(_data, "config.json"), $"{{\"port\":{port}}}");
        Assert.Equal(3, SetuStartup.Run(Parts(), TestContext.Current.CancellationToken));
        Assert.Contains($"Port {port} is in use; set \"port\" in {Path.Combine(_data, "config.json")}", Log);
    }

    [Fact]
    public async Task Run_KeepAwakeThrows_StillStarts()
    {
        File.WriteAllText(Path.Combine(_data, "config.json"), $"{{\"port\":{FreePort()}}}");
        using var cts = new CancellationTokenSource();
        var started = new TaskCompletionSource();
        var parts = Parts(keepAwake: () => throw new InvalidOperationException("no power"),
            after: _ => { started.SetResult(); cts.Cancel(); });
        var run = Task.Run(() => SetuStartup.Run(parts, cts.Token), TestContext.Current.CancellationToken);

        await started.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.Equal(0, await run.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
        Assert.Contains("keep-awake failed, continuing without it: no power", Log);
        Assert.Contains("/mcp", Log);
    }

    [Fact]
    public async Task Run_AfterStartThrows_IsLoggedAndServerStops()
    {
        File.WriteAllText(Path.Combine(_data, "config.json"), $"{{\"port\":{FreePort()}}}");
        using var cts = new CancellationTokenSource();
        var parts = Parts(after: _ => { cts.CancelAfter(100); throw new InvalidOperationException("boom"); });
        Assert.Equal(0, await Task.Run(() => SetuStartup.Run(parts, cts.Token), TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
        Assert.Contains("after-start failed, continuing: boom", Log);
    }
}
