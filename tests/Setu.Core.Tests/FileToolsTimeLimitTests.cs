using ModelContextProtocol;
using Setu.Core.Files;

namespace Setu.Core.Tests;

public sealed class FileToolsTimeLimitTests
{
    private static FileTools Tools(TimeSpan? limit)
        => new(RequestLog.Null, new FileToolsOptions(1252, LineEndings.Crlf, ProtectedPaths.None, limit));

    [Fact]
    public void CallTimeLimit_DefaultsToNone()
        => Assert.Null(new FileToolsOptions(1252, LineEndings.Crlf, ProtectedPaths.None).CallTimeLimit);

    [Fact]
    public void SlowCall_PastTheLimit_ReturnsTimeLimitMessage()
    {
        using var release = new ManualResetEventSlim();
        try
        {
            var ex = Assert.Throws<McpException>(() =>
                Tools(TimeSpan.FromMilliseconds(100)).Run("search", "/x", () => { release.Wait(TimeSpan.FromSeconds(5)); return "done"; }));
            Assert.Equal(
                "search did not finish within 0 seconds. A folder it touched may be waiting on a macOS permission prompt at the Mac, or the folder is very large. Narrow the path, or ask John to check the Mac's screen.",
                ex.Message);
        }
        finally
        {
            release.Set(); // let the abandoned work finish
        }
    }

    [Fact]
    public void TimeLimitMessage_UsesWholeSeconds()
        => Assert.StartsWith("search did not finish within 120 seconds.", FileTools.TimeLimitMessage("search", TimeSpan.FromSeconds(120)));

    [Fact]
    public void SlowCall_WithoutLimit_Completes()
        => Assert.Equal("done", Tools(null).Run("search", "/x", () => { Thread.Sleep(300); return "done"; }));

    [Fact]
    public void FastCall_WithLimit_IsUnaffected()
        => Assert.Equal("done", Tools(TimeSpan.FromSeconds(5)).Run("search", "/x", () => "done"));

    [Fact]
    public void ErrorInsideLimit_IsSurfacedAsBefore()
    {
        var ex = Assert.Throws<McpException>(() =>
            Tools(TimeSpan.FromSeconds(5)).Run<string>("read_file", "/x", () => throw new ToolInputException("boom")));
        Assert.Equal("boom", ex.Message);
    }

    [Fact]
    public void RealTool_WithLimit_StillWorks()
    {
        var dir = Directory.CreateTempSubdirectory("wmcp-tl-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "a.txt"), "hello\n");
            var output = Tools(TimeSpan.FromSeconds(5)).ReadFile(Path.Combine(dir, "a.txt"));
            Assert.Contains("hello", output);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
