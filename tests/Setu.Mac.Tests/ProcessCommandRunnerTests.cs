using System.Diagnostics;

namespace Setu.Mac.Tests;

public class ProcessCommandRunnerTests
{
    [Fact]
    public void Timeout_KillsAndReturnsPromptly()
    {
        var clock = Stopwatch.StartNew();
        var (exit, stdout, stderr) = new ProcessCommandRunner().RunCapture("sleep", ["30"], TimeSpan.FromMilliseconds(200));
        Assert.Equal(-1, exit);
        Assert.Equal("", stdout);
        Assert.Equal("sleep did not finish within 0 s", stderr);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"took {clock.Elapsed}");
    }

    [Fact]
    public void Finishing_ReturnsExitAndOutput()
    {
        var (exit, stdout, _) = new ProcessCommandRunner().RunCapture("sh", ["-c", "echo hi; exit 3"], TimeSpan.FromSeconds(10));
        Assert.Equal(3, exit);
        Assert.Equal("hi\n", stdout);
    }
}
