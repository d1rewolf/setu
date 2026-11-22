using System.Text;

namespace WindowsMcp.Core.Files;

public enum FileEncodingKind { Utf8, Utf8Bom, Utf16Le, Utf16Be, Ansi }

public enum LineEndings { Crlf, Lf, Mixed, None }

public sealed record TextFormat(FileEncodingKind Kind, Encoding Encoding, int PreambleLength, LineEndings LineEndings)
{
    private const int BinarySniffLength = 8192;

    static TextFormat() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static TextFormat Detect(ReadOnlySpan<byte> bytes, int ansiCodePage)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return new(FileEncodingKind.Utf8Bom, new UTF8Encoding(true), 3, LineEndings.None);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return new(FileEncodingKind.Utf16Le, new UnicodeEncoding(false, true), 2, LineEndings.None);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return new(FileEncodingKind.Utf16Be, new UnicodeEncoding(true, true), 2, LineEndings.None);

        try
        {
            _ = new UTF8Encoding(false, true).GetCharCount(bytes);
            return new(FileEncodingKind.Utf8, new UTF8Encoding(false), 0, LineEndings.None);
        }
        catch (DecoderFallbackException)
        {
            var ansi = Encoding.GetEncoding(ansiCodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            return new(FileEncodingKind.Ansi, ansi, 0, LineEndings.None);
        }
    }

    public static bool LooksBinary(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF)))
            return false;
        return bytes[..Math.Min(bytes.Length, BinarySniffLength)].Contains((byte)0);
    }

    public static LineEndings DetectLineEndings(string text)
    {
        int crlf = 0, lf = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n') continue;
            if (i > 0 && text[i - 1] == '\r') crlf++; else lf++;
        }
        return (crlf, lf) switch
        {
            (0, 0) => LineEndings.None,
            (_, 0) => LineEndings.Crlf,
            (0, _) => LineEndings.Lf,
            _ => LineEndings.Mixed,
        };
    }
}
