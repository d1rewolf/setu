using System.Text;

namespace Setu.Core.Files;

public enum FileEncodingKind { Utf8, Utf8Bom, Utf16Le, Utf16Be, Ansi }

public enum LineEndings { Crlf, Lf, Mixed, None }

public sealed record TextFormat(FileEncodingKind Kind, Encoding Encoding, int PreambleLength, LineEndings LineEndings)
{
    private const int BinarySniffLength = 8192;

    static TextFormat() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>
    /// Detects encoding and line endings. Callers must pass the whole file: line endings are
    /// computed from the full content after the preamble.
    /// </summary>
    public static TextFormat Detect(ReadOnlySpan<byte> bytes, int ansiCodePage)
    {
        FileEncodingKind kind;
        Encoding encoding;
        Encoding lenient;
        int preamble;

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            (kind, encoding, lenient, preamble) = (FileEncodingKind.Utf8Bom, new UTF8Encoding(true), new UTF8Encoding(false), 3);
        }
        else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            (kind, encoding, lenient, preamble) = (FileEncodingKind.Utf16Le, new UnicodeEncoding(false, true), new UnicodeEncoding(false, false), 2);
        }
        else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            (kind, encoding, lenient, preamble) = (FileEncodingKind.Utf16Be, new UnicodeEncoding(true, true), new UnicodeEncoding(true, false), 2);
        }
        else
        {
            preamble = 0;
            try
            {
                _ = new UTF8Encoding(false, true).GetCharCount(bytes);
                (kind, encoding, lenient) = (FileEncodingKind.Utf8, new UTF8Encoding(false), new UTF8Encoding(false));
            }
            catch (DecoderFallbackException)
            {
                kind = FileEncodingKind.Ansi;
                encoding = Encoding.GetEncoding(ansiCodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
                lenient = Encoding.GetEncoding(ansiCodePage);
            }
        }

        var text = lenient.GetString(bytes[preamble..]);
        return new(kind, encoding, preamble, DetectLineEndings(text));
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
