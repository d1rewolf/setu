using System.Text;
using ModelContextProtocol;
using Setu.Core.Files;

namespace Setu.Core.Tests;

public sealed class FileToolsReadWriteTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("wmcp-rw-").FullName;

    private FileTools Tools() => new(RequestLog.Null, new FileToolsOptions(1252));

    private string P(string name) => Path.Combine(_dir, name);

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void ReadFile_ReportsEncodingAndNumbersLines()
    {
        var path = P("a.txt");
        File.WriteAllBytes(path, [0xEF, 0xBB, 0xBF, .. "a\r\nb\r\n"u8.ToArray()]);
        var result = Tools().ReadFile(path);
        Assert.Contains("encoding=Utf8Bom line_endings=Crlf total_lines=2", result);
        Assert.Contains("     1\ta", result);
        Assert.Contains("     2\tb", result);
    }

    [Fact]
    public void ReadFile_OffsetAndLimit()
    {
        var path = P("ten.txt");
        File.WriteAllText(path, string.Concat(Enumerable.Range(1, 10).Select(i => $"line{i}\n")));
        var result = Tools().ReadFile(path, offset: 4, limit: 2);
        Assert.Contains("line4", result);
        Assert.Contains("line5", result);
        Assert.DoesNotContain("line3", result);
        Assert.DoesNotContain("line6", result);
    }

    [Fact]
    public void ReadFile_Binary_Refuses()
    {
        var path = P("b.bin");
        File.WriteAllBytes(path, [1, 2, 0, 3]);
        var ex = Assert.Throws<McpException>(() => Tools().ReadFile(path));
        Assert.Contains("Binary file", ex.Message);
    }

    [Fact]
    public void ReadFile_FileOpenForWritingElsewhere_StillReads()
    {
        var path = P("busy.txt");
        File.WriteAllText(path, "hello");
        using var held = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
        Assert.Contains("hello", Tools().ReadFile(path));
    }

    [Fact]
    public void ReadFile_Huge_TruncatesAt256KiB()
    {
        var path = P("huge.txt");
        var line = new string('x', 200);
        File.WriteAllText(path, string.Concat(Enumerable.Repeat(line + "\n", 5000)));
        var result = Tools().ReadFile(path, limit: 5000);
        Assert.EndsWith("[truncated at 256 KiB; use offset to continue]", result.TrimEnd());
        Assert.True(Encoding.UTF8.GetByteCount(result) < 256 * 1024 + 512);
    }

    [Fact]
    public void WriteFile_ExistingUtf16Crlf_KeepsFormat()
    {
        var path = P("u16.txt");
        File.WriteAllBytes(path, [0xFF, 0xFE, .. Encoding.Unicode.GetBytes("a\r\nb")]);
        Tools().WriteFile(path, "x\ny");
        byte[] expected = [0xFF, 0xFE, .. Encoding.Unicode.GetBytes("x\r\ny")];
        Assert.Equal(expected, File.ReadAllBytes(path));
    }

    [Fact]
    public void WriteFile_NewFile_DefaultsUtf8NoBomCrlf()
    {
        var path = P("new.txt");
        var result = Tools().WriteFile(path, "x\ny");
        Assert.Equal("x\r\ny"u8.ToArray(), File.ReadAllBytes(path));
        Assert.Equal($"Wrote 4 bytes to {path} (Utf8, Crlf)", result);
    }

    [Fact]
    public void WriteFile_AnsiUnrepresentableChar_FailsWithoutWriting()
    {
        var path = P("ansi.txt");
        byte[] original = [0x63, 0x61, 0x66, 0xE9, 0x0D, 0x0A];
        File.WriteAllBytes(path, original);
        var ex = Assert.Throws<McpException>(() => Tools().WriteFile(path, "→"));
        Assert.Contains("cannot be represented", ex.Message);
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public void WriteFile_ExistingBinary_WithoutEncoding_FailsWithoutWriting()
    {
        var path = P("bin.dat");
        byte[] original = [1, 2, 0, 3];
        File.WriteAllBytes(path, original);
        var ex = Assert.Throws<McpException>(() => Tools().WriteFile(path, "text"));
        Assert.Contains("looks binary", ex.Message);
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void WriteFile_ExistingBinary_WithExplicitEncoding_Succeeds()
    {
        var path = P("bin2.dat");
        File.WriteAllBytes(path, [1, 2, 0, 3]);
        Tools().WriteFile(path, "text", encoding: "utf8");
        Assert.Equal("text"u8.ToArray(), File.ReadAllBytes(path));
    }
}
