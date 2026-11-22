using System.Text;
using ModelContextProtocol;
using WindowsMcp.Core.Files;

namespace WindowsMcp.Core.Tests;

public sealed class FileToolsEditTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("wmcp-edit-").FullName;

    private FileTools Tools() => new(RequestLog.Null, new FileToolsOptions(1252));

    private string P(string name) => Path.Combine(_dir, name);

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void Edit_LfOldStringOnCrlfFile_MatchesAndKeepsCrlf()
    {
        var path = P("crlf.txt");
        File.WriteAllBytes(path, "one\r\ntwo\r\nthree\r\n"u8.ToArray());
        var result = Tools().EditFile(path, "one\ntwo", "1\n2");
        Assert.Equal($"Replaced 1 occurrence(s) in {path}", result);
        Assert.Equal("1\r\n2\r\nthree\r\n"u8.ToArray(), File.ReadAllBytes(path));
    }

    [Fact]
    public void Edit_PreservesBomAndUntouchedBytes()
    {
        var path = P("bom.txt");
        // UTF-8 BOM file with an invalid-UTF-8 0xFF byte outside the replaced span.
        byte[] before = [0xEF, 0xBB, 0xBF, .. "head\n"u8.ToArray(), 0xFF, .. "\nold tail\n"u8.ToArray()];
        File.WriteAllBytes(path, before);
        Tools().EditFile(path, "old", "new");
        byte[] expected = [0xEF, 0xBB, 0xBF, .. "head\n"u8.ToArray(), 0xFF, .. "\nnew tail\n"u8.ToArray()];
        Assert.Equal(expected, File.ReadAllBytes(path));
    }

    [Fact]
    public void Edit_AnsiFile_StaysAnsi()
    {
        var path = P("ansi.txt");
        File.WriteAllBytes(path, [0x63, 0x61, 0x66, 0xE9]);
        Tools().EditFile(path, "caf", "CAF");
        Assert.Equal([0x43, 0x41, 0x46, 0xE9], File.ReadAllBytes(path));
    }

    [Fact]
    public void Edit_Utf16Le_EditsAndKeepsEncoding()
    {
        var path = P("u16.txt");
        var enc = new UnicodeEncoding(false, true);
        File.WriteAllBytes(path, [.. enc.GetPreamble(), .. enc.GetBytes("alpha\r\nbeta\r\n")]);
        Tools().EditFile(path, "beta", "gamma");
        Assert.Equal([.. enc.GetPreamble(), .. enc.GetBytes("alpha\r\ngamma\r\n")], File.ReadAllBytes(path));
    }

    [Fact]
    public void Edit_NotFound_Error()
    {
        var path = P("nf.txt");
        File.WriteAllText(path, "hello\n");
        var ex = Assert.Throws<McpException>(() => Tools().EditFile(path, "absent", "x"));
        Assert.Equal($"old_string not found in {path}", ex.Message);
        Assert.Equal("hello\n", File.ReadAllText(path));
    }

    [Fact]
    public void Edit_Ambiguous_Error()
    {
        var path = P("amb.txt");
        File.WriteAllText(path, "x x x\n");
        var ex = Assert.Throws<McpException>(() => Tools().EditFile(path, "x", "y"));
        Assert.Equal("old_string found 3 times; pass more context or replace_all=true", ex.Message);
        Assert.Equal("x x x\n", File.ReadAllText(path));
    }

    [Fact]
    public void Edit_ReplaceAll_ReplacesEvery()
    {
        var path = P("all.txt");
        File.WriteAllText(path, "x x x\n");
        var result = Tools().EditFile(path, "x", "yy", replace_all: true);
        Assert.Equal($"Replaced 3 occurrence(s) in {path}", result);
        Assert.Equal("yy yy yy\n", File.ReadAllText(path));
    }

    [Fact]
    public void Edit_EmptyOldString_Error()
    {
        var path = P("empty.txt");
        File.WriteAllText(path, "hello\n");
        var ex = Assert.Throws<McpException>(() => Tools().EditFile(path, "", "x"));
        Assert.Contains("old_string", ex.Message);
    }

    [Fact]
    public void Edit_Binary_Refuses()
    {
        var path = P("b.bin");
        File.WriteAllBytes(path, [0x61, 0x00, 0x62]);
        var ex = Assert.Throws<McpException>(() => Tools().EditFile(path, "a", "c"));
        Assert.Contains("Binary file", ex.Message);
        Assert.Equal([0x61, 0x00, 0x62], File.ReadAllBytes(path));
    }

    [Fact]
    public void Edit_Unrepresentable_Error()
    {
        var path = P("ansi2.txt");
        File.WriteAllBytes(path, [0x63, 0x61, 0x66, 0xE9]);
        var ex = Assert.Throws<McpException>(() => Tools().EditFile(path, "caf", "中"));
        Assert.Equal("Text cannot be represented in Ansi", ex.Message);
    }
}
