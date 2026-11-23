namespace Setu.Mac;

/// <summary>
/// Splits text for CGEventKeyboardSetUnicodeString, which carries at most about 20 UTF-16 units per event.
/// A surrogate pair is never split across chunks, so each chunk is valid UTF-16 on its own.
/// </summary>
internal static class TextChunker
{
    public static IEnumerable<string> Chunks(string text, int maxUtf16 = 20)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxUtf16, 2);
        var start = 0;
        while (start < text.Length)
        {
            var length = Math.Min(maxUtf16, text.Length - start);
            // Ending on a high surrogate would cut its pair in two: leave it for the next chunk.
            if (start + length < text.Length && char.IsHighSurrogate(text[start + length - 1])) length--;
            yield return text.Substring(start, length);
            start += length;
        }
    }
}
