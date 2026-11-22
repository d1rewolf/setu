namespace WindowsMcp.Core.Tests;

public sealed class RequestLogTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("wmcp-log-").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void Write_LineBreaksAndControlChars_StayOnOneLine()
    {
        var path = Path.Combine(_dir, "log.txt");
        var log = new RequestLog(path);
        log.Write("first\r\nsecond\nthird\tfourth\u0001fifth");
        log.Write("next");
        var lines = File.ReadAllLines(path);
        Assert.Equal(2, lines.Length);
        Assert.EndsWith(" first second third fourth fifth", lines[0]);
        Assert.EndsWith(" next", lines[1]);
    }
}
