using ModelContextProtocol;
using Setu.Core.Shell;

namespace Setu.Core.Tests;

public sealed class ShellToolsTests
{
    private sealed class FakeRunner(ProcessResult result) : IProcessRunner
    {
        public List<ProcessRequest> Requests { get; } = [];
        public List<CancellationToken> Tokens { get; } = [];

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            Tokens.Add(ct);
            return Task.FromResult(result);
        }
    }

    private sealed class FakeLauncher : IDetachedLauncher
    {
        public (string Path, string? Arguments, string? Cwd)? Last { get; private set; }

        public int Start(string path, string? arguments, string? workingDirectory)
        {
            Last = (path, arguments, workingDirectory);
            return 4242;
        }
    }

    private static readonly ProcessResult Ok =
        new(0, "out\n", "", false, false, false, TimeSpan.FromMilliseconds(12));

    private static ShellTools Tools(IProcessRunner runner, IDetachedLauncher? launcher = null) =>
        new(RequestLog.Null, runner, launcher ?? new FakeLauncher());

    private static string AbsDir => Path.GetTempPath();

    [Theory]
    [InlineData(0)]
    [InlineData(601)]
    [InlineData(-5)]
    public async Task ShellTools_TimeoutOutOfRange_Error(int timeout)
    {
        var runner = new FakeRunner(Ok);
        var ps = await Assert.ThrowsAsync<McpException>(() => Tools(runner).PowerShell("dir", timeout_s: timeout, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("timeout_s", ps.Message);
        var cmd = await Assert.ThrowsAsync<McpException>(() => Tools(runner).Cmd("dir", timeout_s: timeout, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("timeout_s", cmd.Message);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task ShellTools_FormatsResult()
    {
        var runner = new FakeRunner(new ProcessResult(2, "hello\n", "bad\n", false, false, false, TimeSpan.FromMilliseconds(1234.7)));
        var text = await Tools(runner).Cmd("dir", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("exit_code=2 timed_out=false elapsed_ms=1234\n--- stdout ---\nhello\n--- stderr ---\nbad\n", text);
    }

    [Fact]
    public async Task ShellTools_FormatsTimeoutAndTruncation()
    {
        var runner = new FakeRunner(new ProcessResult(null, "partial", "", true, true, true, TimeSpan.FromSeconds(5)));
        var text = await Tools(runner).PowerShell("x", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(
            "exit_code=none timed_out=true elapsed_ms=5000\n--- stdout ---\npartial\n[truncated at 64 KiB]\n--- stderr ---\n[truncated at 64 KiB]\n",
            text);
    }

    [Fact]
    public async Task ShellTools_PowerShell_BuildsRequest()
    {
        var runner = new FakeRunner(Ok);
        await Tools(runner).PowerShell("Get-Date", cwd: AbsDir, timeout_s: 90, cancellationToken: TestContext.Current.CancellationToken);
        var req = Assert.Single(runner.Requests);
        Assert.Equal("powershell.exe", req.FileName);
        Assert.Equal(CommandLines.PowerShell("Get-Date"), req.Arguments);
        Assert.Equal(Path.GetFullPath(AbsDir), req.WorkingDirectory);
        Assert.Equal(TimeSpan.FromSeconds(90), req.Timeout);
    }

    [Fact]
    public async Task ShellTools_Cmd_BuildsRequestWithDefaults()
    {
        var runner = new FakeRunner(Ok);
        await Tools(runner).Cmd("dir", cancellationToken: TestContext.Current.CancellationToken);
        var req = Assert.Single(runner.Requests);
        Assert.Equal("cmd.exe", req.FileName);
        Assert.Equal(CommandLines.Cmd("dir"), req.Arguments);
        Assert.Null(req.WorkingDirectory);
        Assert.Equal(TimeSpan.FromSeconds(60), req.Timeout);
    }

    [Fact]
    public async Task ShellTools_RelativeCwd_Error()
    {
        var runner = new FakeRunner(Ok);
        var ex = await Assert.ThrowsAsync<McpException>(() => Tools(runner).Cmd("dir", cwd: "relative\\dir", cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("absolute", ex.Message);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task ShellTools_CancellationToken_FlowsToRunner()
    {
        var runner = new FakeRunner(Ok);
        using var cts = new CancellationTokenSource();
        await Tools(runner).PowerShell("x", cancellationToken: cts.Token);
        await Tools(runner).Cmd("x", cancellationToken: cts.Token);
        Assert.Equal(2, runner.Tokens.Count);
        Assert.All(runner.Tokens, t => Assert.Equal(cts.Token, t));
    }

    private const string ClixmlError =
        "#< CLIXML\r\n<Objs Version=\"1.1.0.1\" xmlns=\"http://schemas.microsoft.com/powershell/2004/04\"><S S=\"Error\">Write-Error boom : boom_x000D__x000A_</S></Objs>";

    [Fact]
    public async Task ShellTools_PowerShell_DecodesClixmlStreams()
    {
        var runner = new FakeRunner(new ProcessResult(1, ClixmlError, ClixmlError, false, false, false, TimeSpan.Zero));
        var text = await Tools(runner).PowerShell("Write-Error boom", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(
            "exit_code=1 timed_out=false elapsed_ms=0\n--- stdout ---\nWrite-Error boom : boom\r\n--- stderr ---\nWrite-Error boom : boom\r\n",
            text);
    }

    [Fact]
    public async Task ShellTools_Cmd_LeavesClixmlAlone()
    {
        var runner = new FakeRunner(new ProcessResult(1, "", ClixmlError, false, false, false, TimeSpan.Zero));
        var text = await Tools(runner).Cmd("x", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains("#< CLIXML", text);
    }

    [Fact]
    public void ShellTools_StartProcess_ReturnsPid()
    {
        var launcher = new FakeLauncher();
        var text = Tools(new FakeRunner(Ok), launcher).StartProcess("notepad.exe", "a.txt", AbsDir);
        Assert.Equal("Started notepad.exe as PID 4242", text);
        Assert.Equal(("notepad.exe", "a.txt", Path.GetFullPath(AbsDir)), launcher.Last);
    }
}
