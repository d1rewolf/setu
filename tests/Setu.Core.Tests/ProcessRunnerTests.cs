using System.Diagnostics;
using Setu.Core.Shell;

namespace Setu.Core.Tests;

public sealed class ProcessRunnerTests : IDisposable
{
    // Unique marker durations so cleanup only touches processes these tests started.
    private const string TreeMarker = "sleep 30.123";
    private const string BackgroundMarker = "sleep 30.456";
    private const string ThrowingKillMarker = "sleep 30.789";

    private static readonly ProcessRunner Runner = new(() => new DefaultContainment());

    public void Dispose()
    {
        if (OperatingSystem.IsWindows()) return;
        Pkill(TreeMarker);
        Pkill(BackgroundMarker);
        Pkill(ThrowingKillMarker);
    }

    /// <summary>Kills the tree like DefaultContainment, then throws the way Kill(entireProcessTree) can.</summary>
    private sealed class ThrowingContainment : IProcessContainment
    {
        private Process? _process;

        public void Attach(Process p) => _process = p;

        public void KillAll()
        {
            _process?.Kill(entireProcessTree: true);
            throw new AggregateException("Not all processes in the process tree could be terminated.",
                new System.ComponentModel.Win32Exception(5, "Access is denied."));
        }

        public void Dispose() { }
    }

    private static Task<ProcessResult> Sh(string script, double timeoutSeconds = 10) =>
        Runner.RunAsync(
            new ProcessRequest("/bin/sh", ["-c", script], null, TimeSpan.FromSeconds(timeoutSeconds)),
            TestContext.Current.CancellationToken);

    private static bool Pgrep(string pattern)
    {
        using var p = Process.Start(new ProcessStartInfo("pgrep", ["-f", pattern]) { RedirectStandardOutput = true })!;
        p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode == 0;
    }

    private static void Pkill(string pattern)
    {
        using var p = Process.Start(new ProcessStartInfo("pkill", ["-f", pattern]))!;
        p.WaitForExit();
    }

    [Fact]
    public async Task Run_EchoesStdoutAndExitCode()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "uses /bin/sh");
        var r = await Sh("echo hi; exit 3");
        Assert.Equal(3, r.ExitCode);
        Assert.Equal("hi\n", r.Stdout);
        Assert.False(r.TimedOut);
        Assert.False(r.StdoutTruncated);
    }

    [Fact]
    public async Task Run_CapturesStderr()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "uses /bin/sh");
        var r = await Sh("echo oops >&2");
        Assert.Equal(0, r.ExitCode);
        Assert.Equal("oops\n", r.Stderr);
        Assert.Equal("", r.Stdout);
    }

    [Fact]
    public async Task Run_StdinClosed_ReadDoesNotHang()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "uses /bin/sh");
        var sw = Stopwatch.StartNew();
        var r = await Sh("read x; echo got:$x");
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"took {sw.Elapsed}");
        Assert.False(r.TimedOut);
        Assert.StartsWith("got:", r.Stdout);
    }

    [Fact]
    public async Task Run_Timeout_KillsTree()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "uses /bin/sh");
        var sw = Stopwatch.StartNew();
        var r = await Sh($"echo started; {TreeMarker} & {TreeMarker}", timeoutSeconds: 1);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(4), $"took {sw.Elapsed}");
        Assert.True(r.TimedOut);
        Assert.Null(r.ExitCode);
        Assert.Equal("started\n", r.Stdout);

        // Kill is asynchronous at the OS level; give it a moment to land.
        var deadline = Stopwatch.StartNew();
        while (Pgrep(TreeMarker) && deadline.Elapsed < TimeSpan.FromSeconds(2))
            await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(Pgrep(TreeMarker), "a sleep from the timed-out tree is still running");
    }

    [Fact]
    public async Task Run_BackgroundChildHoldsStdout_ReturnsWhenMainExits()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "uses /bin/sh");
        var sw = Stopwatch.StartNew();
        var r = await Sh($"({BackgroundMarker} &) ; echo done");
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(4), $"took {sw.Elapsed}");
        Assert.False(r.TimedOut);
        Assert.Equal(0, r.ExitCode);
        Assert.Equal("done\n", r.Stdout);
        Assert.True(Pgrep(BackgroundMarker), "background child should outlive the command");
    }

    [Fact]
    public async Task Run_OutputCapped()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "uses /bin/sh");
        var r = await Sh(@"head -c 200000 /dev/zero | tr '\0' a");
        Assert.Equal(0, r.ExitCode);
        Assert.Equal(65536, r.Stdout.Length);
        Assert.True(r.StdoutTruncated);
        Assert.False(r.StderrTruncated);
    }

    [Fact]
    public async Task Run_WorkingDirectory_IsUsed()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "uses /bin/sh");
        var dir = Directory.CreateTempSubdirectory("wmcp-run-").FullName;
        try
        {
            var r = await Runner.RunAsync(
                new ProcessRequest("/bin/sh", ["-c", "pwd"], dir, TimeSpan.FromSeconds(10)),
                TestContext.Current.CancellationToken);
            Assert.Equal(dir + "\n", r.Stdout);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Run_Timeout_KillAllThrows_StillReturnsTimedOutWithPartialOutput()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "uses /bin/sh");
        var runner = new ProcessRunner(() => new ThrowingContainment());
        var sw = Stopwatch.StartNew();
        var r = await runner.RunAsync(
            new ProcessRequest("/bin/sh", ["-c", $"echo partial; {ThrowingKillMarker}"], null, TimeSpan.FromSeconds(1)),
            TestContext.Current.CancellationToken);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(4), $"took {sw.Elapsed}");
        Assert.True(r.TimedOut);
        Assert.Null(r.ExitCode);
        Assert.Equal("partial\n", r.Stdout);
    }

    [Fact]
    public async Task Run_ArgumentList_EachArgumentPassedWhole()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "uses /bin/sh");
        var r = await Runner.RunAsync(
            new ProcessRequest("/bin/sh", ["-c", "printf '%s|' \"$@\"", "sh", "a b", "\"q\"", ""], null, TimeSpan.FromSeconds(10)),
            TestContext.Current.CancellationToken);
        Assert.Equal("a b|\"q\"||", r.Stdout);
    }

    [Fact]
    public async Task Run_Raw_UsesArgumentsStringAsIs()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "uses /bin/sh");
        var request = ProcessRequest.Raw("/bin/sh", "-c \"echo 'a  b'\"", null, TimeSpan.FromSeconds(10));
        Assert.Equal("-c \"echo 'a  b'\"", request.RawArguments);
        Assert.Empty(request.Arguments);
        var r = await Runner.RunAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal("a  b\n", r.Stdout);
    }

    [Fact]
    public async Task Run_Environment_AddsToInheritedEnvironment()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "uses /bin/sh");
        var r = await Runner.RunAsync(
            new ProcessRequest("/bin/sh", ["-c", "echo \"$SETU_TEST_VAR:${PATH:+path}\""], null, TimeSpan.FromSeconds(10),
                new Dictionary<string, string> { ["SETU_TEST_VAR"] = "héllo" }),
            TestContext.Current.CancellationToken);
        Assert.Equal("héllo:path\n", r.Stdout);
    }
}
