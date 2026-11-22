using ModelContextProtocol;

namespace WindowsMcp.Core.Tests;

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
}
