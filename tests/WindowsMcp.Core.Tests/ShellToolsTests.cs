using ModelContextProtocol;
using WindowsMcp.Core.Shell;

namespace WindowsMcp.Core.Tests;

public sealed class ShellToolsTests
{
    private sealed class FakeRunner(ProcessResult result) : IProcessRunner
    {
        public List<ProcessRequest> Requests { get; } = [];

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct)
        {
            Requests.Add(request);
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
        var ps = await Assert.ThrowsAsync<McpException>(() => Tools(runner).PowerShell("dir", timeout_s: timeout));
        Assert.Contains("timeout_s", ps.Message);
        var cmd = await Assert.ThrowsAsync<McpException>(() => Tools(runner).Cmd("dir", timeout_s: timeout));
        Assert.Contains("timeout_s", cmd.Message);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task ShellTools_FormatsResult()
    {
        var runner = new FakeRunner(new ProcessResult(2, "hello\n", "bad\n", false, false, false, TimeSpan.FromMilliseconds(1234.7)));
        var text = await Tools(runner).Cmd("dir");
        Assert.Equal("exit_code=2 timed_out=false elapsed_ms=1234\n--- stdout ---\nhello\n--- stderr ---\nbad\n", text);
    }

    [Fact]
    public async Task ShellTools_FormatsTimeoutAndTruncation()
    {
        var runner = new FakeRunner(new ProcessResult(null, "partial", "", true, true, true, TimeSpan.FromSeconds(5)));
        var text = await Tools(runner).PowerShell("x");
        Assert.Equal(
            "exit_code=none timed_out=true elapsed_ms=5000\n--- stdout ---\npartial\n[truncated at 64 KiB]\n--- stderr ---\n[truncated at 64 KiB]\n",
            text);
    }

    [Fact]
    public async Task ShellTools_PowerShell_BuildsRequest()
    {
        var runner = new FakeRunner(Ok);
        await Tools(runner).PowerShell("Get-Date", cwd: AbsDir, timeout_s: 90);
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
        await Tools(runner).Cmd("dir");
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
        var ex = await Assert.ThrowsAsync<McpException>(() => Tools(runner).Cmd("dir", cwd: "relative\\dir"));
        Assert.Contains("absolute", ex.Message);
        Assert.Empty(runner.Requests);
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
