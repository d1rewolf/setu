using System.Text;
using Setu.Core.Files;

namespace Setu.Core.Tests;

public class TextFormatTests
{
    [Fact]
    public void Detect_Utf8Bom()
    {
        var f = TextFormat.Detect([0xEF, 0xBB, 0xBF, 0x61], 1252);
        Assert.Equal(FileEncodingKind.Utf8Bom, f.Kind);
        Assert.Equal(3, f.PreambleLength);
    }

    [Fact]
    public void Detect_Utf16Le()
    {
        var f = TextFormat.Detect([0xFF, 0xFE, 0x61, 0x00], 1252);
        Assert.Equal(FileEncodingKind.Utf16Le, f.Kind);
        Assert.Equal(2, f.PreambleLength);
    }

    [Fact]
    public void Detect_Utf16Be()
    {
        var f = TextFormat.Detect([0xFE, 0xFF, 0x00, 0x61], 1252);
        Assert.Equal(FileEncodingKind.Utf16Be, f.Kind);
        Assert.Equal(2, f.PreambleLength);
    }

    [Fact]
    public void Detect_PlainUtf8()
    {
        var f = TextFormat.Detect("héllo"u8, 1252);
        Assert.Equal(FileEncodingKind.Utf8, f.Kind);
        Assert.Equal(0, f.PreambleLength);
    }

    [Fact]
    public void Detect_InvalidUtf8_FallsBackToAnsi1252()
    {
        byte[] bytes = [0x68, 0xE9, 0x6C];
        var f = TextFormat.Detect(bytes, 1252);
        Assert.Equal(FileEncodingKind.Ansi, f.Kind);
        Assert.Equal("hél", f.Encoding.GetString(bytes));
    }

    [Fact]
    public void LooksBinary_NulByte_True() => Assert.True(TextFormat.LooksBinary([0x61, 0x00, 0x62]));

    [Fact]
    public void LooksBinary_Utf16Text_False() => Assert.False(TextFormat.LooksBinary([0xFF, 0xFE, 0x61, 0x00]));

    [Theory]
    [InlineData("a\r\nb\r\n", LineEndings.Crlf)]
    [InlineData("a\nb", LineEndings.Lf)]
    [InlineData("a\r\nb\n", LineEndings.Mixed)]
    [InlineData("ab", LineEndings.None)]
    public void DetectLineEndings(string text, LineEndings expected) =>
        Assert.Equal(expected, TextFormat.DetectLineEndings(text));

    [Fact]
    public void Detect_Utf8Crlf_ReportsCrlf() =>
        Assert.Equal(LineEndings.Crlf, TextFormat.Detect("a\r\nb\r\n"u8, 1252).LineEndings);

    [Fact]
    public void Detect_Utf16LeBomLf_ReportsLf()
    {
        var bytes = new byte[] { 0xFF, 0xFE }.Concat(Encoding.Unicode.GetBytes("a\nb")).ToArray();
        Assert.Equal(LineEndings.Lf, TextFormat.Detect(bytes, 1252).LineEndings);
    }

    [Fact]
    public void Detect_Ansi1252Crlf_ReportsCrlf()
    {
        byte[] bytes = [0x68, 0xE9, 0x0D, 0x0A, 0x6C, 0x0D, 0x0A];
        var f = TextFormat.Detect(bytes, 1252);
        Assert.Equal(FileEncodingKind.Ansi, f.Kind);
        Assert.Equal(LineEndings.Crlf, f.LineEndings);
    }
}
