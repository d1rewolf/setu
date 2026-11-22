using ModelContextProtocol;

namespace Setu.Core.Tests;

public class ToolGuardTests
{
    [Fact]
    public void Run_ToolInputException_BecomesMcpExceptionWithSameMessage()
    {
        var ex = Assert.Throws<McpException>(() =>
            ToolGuard.Run<int>(RequestLog.Null, "t", () => throw new ToolInputException("limit must be positive")));
        Assert.Equal("limit must be positive", ex.Message);
    }

    [Fact]
    public void Run_FileNotFound_SaysNotFound()
    {
        var ex = Assert.Throws<McpException>(() =>
            ToolGuard.Run<int>(RequestLog.Null, "t", () => throw new FileNotFoundException("nope", "C:\\x.txt")));
        Assert.StartsWith("Not found: ", ex.Message);
    }

    [Fact]
    public void Run_Success_LogsOkLine()
    {
        var path = Path.GetTempFileName();
        try
        {
            var r = ToolGuard.Run(new RequestLog(path), "read_file", () => 42);
            Assert.Equal(42, r);
            var lines = File.ReadAllLines(path);
            var line = Assert.Single(lines);
            Assert.Contains("read_file", line);
            Assert.Contains("ok", line);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Run_Failure_LogsErrorLine()
    {
        var path = Path.GetTempFileName();
        try
        {
            Assert.Throws<McpException>(() =>
                ToolGuard.Run<int>(new RequestLog(path), "read_file", () => throw new InvalidOperationException("boom")));
            var line = Assert.Single(File.ReadAllLines(path));
            Assert.Contains("error: ", line);
        }
        finally { File.Delete(path); }
    }

    private static RequestLog UnwritableLog() =>
        new(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing-dir", "log.txt"));

    [Fact]
    public void Run_UnwritableLog_SuccessResultUnchanged()
    {
        Assert.Equal(42, ToolGuard.Run(UnwritableLog(), "t", () => 42));
    }

    [Fact]
    public async Task RunAsync_UnwritableLog_SuccessResultUnchanged()
    {
        Assert.Equal(42, await ToolGuard.RunAsync(UnwritableLog(), "t", () => Task.FromResult(42)));
    }

    [Fact]
    public void Run_UnwritableLog_FailureMessageUnchanged()
    {
        var ex = Assert.Throws<McpException>(() =>
            ToolGuard.Run<int>(UnwritableLog(), "t", () => throw new ToolInputException("bad arg")));
        Assert.Equal("bad arg", ex.Message);
    }

    [Fact]
    public async Task RunAsync_UnwritableLog_FailureMessageUnchanged()
    {
        var ex = await Assert.ThrowsAsync<McpException>(() =>
            ToolGuard.RunAsync<int>(UnwritableLog(), "t", () => throw new ToolInputException("bad arg")));
        Assert.Equal("bad arg", ex.Message);
    }

    [Fact]
    public void Run_InvalidLogPath_OutcomesUnchanged()
    {
        var log = new RequestLog("bad\0path");
        Assert.Equal(42, ToolGuard.Run(log, "t", () => 42));
        var ex = Assert.Throws<McpException>(() =>
            ToolGuard.Run<int>(log, "t", () => throw new ToolInputException("bad arg")));
        Assert.Equal("bad arg", ex.Message);
    }
}
