using System.Diagnostics;
using System.Runtime.InteropServices;
using ModelContextProtocol;
using Setu.Core.Files;
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

    private static readonly FileToolsOptions NoProtection = new(1252, LineEndings.Crlf, ProtectedPaths.None);

    /// <summary>Linux stands in for the Mac: /bin/sh instead of zsh, same perl wrapper.</summary>
    private static readonly ZshSettings TestZsh = new(ShellPath: "/bin/sh");

    private static WindowsShellTools Tools(IProcessRunner runner, FileToolsOptions? options = null) =>
        new(RequestLog.Null, runner, options ?? NoProtection);

    private static ZshShellTools Zsh(IProcessRunner runner, FileToolsOptions? options = null) =>
        new(RequestLog.Null, runner, options ?? NoProtection, TestZsh);

    private static WindowsProcessTools Processes(IDetachedLauncher launcher, FileToolsOptions? options = null) =>
        new(RequestLog.Null, launcher, options ?? NoProtection);

    private static ZshShellTools RealZsh() =>
        Zsh(new ProcessRunner(() => new ProcessGroupContainment()));

    private static string AbsDir => Path.GetTempPath();

    /// <summary>Options whose only protected root is a made-up folder under the temp directory.</summary>
    private static (FileToolsOptions Options, string Inside) Protecting()
    {
        var root = Path.Combine(Path.GetTempPath(), "setu-prot-" + Guid.NewGuid().ToString("N"));
        var options = new FileToolsOptions(1252, LineEndings.Lf, new ProtectedPaths([root], [], false));
        return (options, Path.Combine(root, "sub"));
    }

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
        var sh = await Assert.ThrowsAsync<McpException>(() => Zsh(runner).Shell("ls", timeout_s: timeout, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(ps.Message, sh.Message);
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
    public async Task Shell_FormatsLikePowerShell()
    {
        var runner = new FakeRunner(new ProcessResult(null, "partial", "bad\n", true, true, false, TimeSpan.FromSeconds(5)));
        var text = await Zsh(runner).Shell("x", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(
            "exit_code=none timed_out=true elapsed_ms=5000\n--- stdout ---\npartial\n[truncated at 64 KiB]\n--- stderr ---\nbad\n",
            text);
    }

    [Fact]
    public async Task ShellTools_PowerShell_BuildsRequest()
    {
        var runner = new FakeRunner(Ok);
        await Tools(runner).PowerShell("Get-Date", cwd: AbsDir, timeout_s: 90, cancellationToken: TestContext.Current.CancellationToken);
        var req = Assert.Single(runner.Requests);
        Assert.Equal("powershell.exe", req.FileName);
        Assert.Equal(CommandLines.PowerShell("Get-Date"), req.RawArguments);
        Assert.Empty(req.Arguments);
        Assert.Null(req.Environment);
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
        Assert.Equal(CommandLines.Cmd("dir"), req.RawArguments);
        Assert.Empty(req.Arguments);
        Assert.Null(req.WorkingDirectory);
        Assert.Equal(TimeSpan.FromSeconds(60), req.Timeout);
    }

    [Fact]
    public async Task Shell_BuildsRequest()
    {
        var runner = new FakeRunner(Ok);
        await Zsh(runner).Shell("echo hi", cwd: AbsDir, timeout_s: 90, cancellationToken: TestContext.Current.CancellationToken);
        var req = Assert.Single(runner.Requests);
        Assert.Equal("/usr/bin/perl", req.FileName);
        Assert.Equal(CommandLines.Zsh("/bin/sh", "echo hi"), req.Arguments);
        Assert.Null(req.RawArguments);
        Assert.Equal(Path.GetFullPath(AbsDir), req.WorkingDirectory);
        Assert.Equal(TimeSpan.FromSeconds(90), req.Timeout);
    }

    [Fact]
    public async Task Shell_DefaultTimeoutIs60()
    {
        var runner = new FakeRunner(Ok);
        await Zsh(runner).Shell("true", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(TimeSpan.FromSeconds(60), Assert.Single(runner.Requests).Timeout);
    }

    [Fact]
    public void Shell_Environment_SetsUtf8LocaleOnlyWhenUnset()
    {
        var none = ZshShellTools.LocaleEnvironment(_ => null);
        Assert.Equal(new Dictionary<string, string> { ["LANG"] = "en_US.UTF-8", ["LC_ALL"] = "en_US.UTF-8" }, none);

        var empty = ZshShellTools.LocaleEnvironment(_ => "");
        Assert.Equal(none, empty);

        var langSet = ZshShellTools.LocaleEnvironment(n => n == "LANG" ? "de_DE.UTF-8" : null);
        Assert.Equal(new Dictionary<string, string> { ["LC_ALL"] = "en_US.UTF-8" }, langSet);

        Assert.Null(ZshShellTools.LocaleEnvironment(_ => "C"));
    }

    [Fact]
    public async Task ShellTools_RelativeCwd_Error()
    {
        var runner = new FakeRunner(Ok);
        var ex = await Assert.ThrowsAsync<McpException>(() => Tools(runner).Cmd("dir", cwd: "relative\\dir", cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("absolute", ex.Message);
        var sh = await Assert.ThrowsAsync<McpException>(() => Zsh(runner).Shell("ls", cwd: "relative/dir", cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("absolute", sh.Message);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task ShellTools_ProtectedCwd_RefusedByName()
    {
        var (options, inside) = Protecting();
        var runner = new FakeRunner(Ok);
        var sh = await Assert.ThrowsAsync<McpException>(() => Zsh(runner, options).Shell("ls", cwd: inside, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("macOS protects", sh.Message);
        var ps = await Assert.ThrowsAsync<McpException>(() => Tools(runner, options).PowerShell("dir", cwd: inside, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("macOS protects", ps.Message);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public void StartProcess_ProtectedCwd_RefusedByName()
    {
        var (options, inside) = Protecting();
        var launcher = new FakeLauncher();
        var ex = Assert.Throws<McpException>(() => Processes(launcher, options).StartProcess("TextEdit", null, inside));
        Assert.Contains("macOS protects", ex.Message);
        Assert.Null(launcher.Last);
    }

    [Fact]
    public async Task ShellTools_CancellationToken_FlowsToRunner()
    {
        var runner = new FakeRunner(Ok);
        using var cts = new CancellationTokenSource();
        await Tools(runner).PowerShell("x", cancellationToken: cts.Token);
        await Tools(runner).Cmd("x", cancellationToken: cts.Token);
        await Zsh(runner).Shell("x", cancellationToken: cts.Token);
        Assert.Equal(3, runner.Tokens.Count);
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
        var text = Processes(launcher).StartProcess("notepad.exe", "a.txt", AbsDir);
        Assert.Equal("Started notepad.exe as PID 4242", text);
        Assert.Equal(("notepad.exe", "a.txt", Path.GetFullPath(AbsDir)), launcher.Last);
    }

    // --- Real processes: Linux stands in for the Mac (perl wrapper, /bin/sh as the shell). ---

    [Fact]
    public async Task Shell_EchoUnicode()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "uses /usr/bin/perl and /bin/sh");
        var text = await RealZsh().Shell("printf 'héllo ✓'", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains("héllo ✓", text);
        Assert.StartsWith("exit_code=0 ", text);
    }

    [Fact]
    public async Task Shell_ReadPrompt_DoesNotHang()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "uses /usr/bin/perl and /bin/sh");
        var sw = Stopwatch.StartNew();
        var text = await RealZsh().Shell("read x; echo got:$x", timeout_s: 10, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"took {sw.Elapsed}");
        Assert.Contains("got:", text);
        Assert.Contains("timed_out=false", text);
    }

    [Fact]
    public async Task Shell_RunsInItsOwnProcessGroup()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "uses /usr/bin/perl and /bin/sh");
        var text = await RealZsh().Shell("echo $$; ps -o pgid= -p $$", cancellationToken: TestContext.Current.CancellationToken);
        var lines = Stdout(text).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(lines[0], lines[1]);
    }

    [Fact]
    public async Task ProcessGroupContainment_KillsBackgroundedChild()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "uses kill(2), /usr/bin/perl and /bin/sh");
        var text = await RealZsh().Shell("sleep 300 & echo $! ; wait", timeout_s: 2, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains("timed_out=true", text);
        await AssertGone(int.Parse(Stdout(text).Trim(), System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task ProcessGroupContainment_KillsReparentedChild()
    {
        // The inner sh exits at once, so the sleep is reparented away from the command's tree, where
        // Kill(entireProcessTree) cannot find it. It is still in the process group.
        Assert.SkipWhen(OperatingSystem.IsWindows(), "uses kill(2), /usr/bin/perl and /bin/sh");
        var text = await RealZsh().Shell("sh -c 'sleep 300 & echo $!'; sleep 30", timeout_s: 1, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains("timed_out=true", text);
        await AssertGone(int.Parse(Stdout(text).Trim(), System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ProcessGroupContainment_KillAll_WithoutAttach_DoesNotThrow()
    {
        using var c = new ProcessGroupContainment();
        c.KillAll();
    }

    [Fact]
    public async Task ProcessGroupContainment_KillAll_AfterExit_DoesNotThrow()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "uses kill(2) and /bin/sh");
        using var p = Process.Start(new ProcessStartInfo("/bin/sh", ["-c", "true"]))!;
        await p.WaitForExitAsync(TestContext.Current.CancellationToken);
        using var c = new ProcessGroupContainment();
        c.Attach(p);
        c.KillAll();
    }

    private static string Stdout(string formatted)
    {
        const string start = "--- stdout ---\n";
        var from = formatted.IndexOf(start, StringComparison.Ordinal) + start.Length;
        var to = formatted.IndexOf("--- stderr ---\n", from, StringComparison.Ordinal);
        return formatted[from..to];
    }

    private const int Esrch = 3;

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int Kill(int pid, int sig);

    /// <summary>Waits up to 2 s for kill(pid, 0) to report ESRCH; kills the process if the test fails.</summary>
    private static async Task AssertGone(int pid)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(2))
        {
            if (Kill(pid, 0) != 0 && Marshal.GetLastPInvokeError() == Esrch) return;
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
        Kill(pid, 9);
        Assert.Fail($"PID {pid} is still running 2 s after the timeout");
    }
}
