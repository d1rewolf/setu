namespace Setu.Core.Files;

public static class ByteSplice
{
    /// <summary>
    /// Replaces <paramref name="oldBytes"/> with <paramref name="newBytes"/> in <paramref name="file"/>,
    /// searching from <paramref name="start"/> (the preamble length). A match counts only at offsets
    /// where (offset - start) % alignment == 0, so UTF-16 matches never straddle code units.
    /// <paramref name="count"/> is the number of non-overlapping aligned matches; with
    /// <paramref name="replaceAll"/> false only the first is replaced. Bytes outside replaced spans
    /// are copied unchanged.
    /// </summary>
    public static byte[] Replace(ReadOnlySpan<byte> file, ReadOnlySpan<byte> oldBytes, ReadOnlySpan<byte> newBytes,
        int start, int alignment, bool replaceAll, out int count)
    {
        ArgumentOutOfRangeException.ThrowIfZero(oldBytes.Length, nameof(oldBytes));
        ArgumentOutOfRangeException.ThrowIfLessThan(alignment, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(start, file.Length);

        var output = new List<byte>(file.Length);
        count = 0;
        var copied = 0;
        var pos = start;
        while (pos <= file.Length - oldBytes.Length)
        {
            var rel = file[pos..].IndexOf(oldBytes);
            if (rel < 0) break;
            var at = pos + rel;
            if ((at - start) % alignment != 0)
            {
                pos = at + 1;
                continue;
            }
            count++;
            if (count == 1 || replaceAll)
            {
                output.AddRange(file[copied..at]);
                output.AddRange(newBytes);
                copied = at + oldBytes.Length;
            }
            pos = at + oldBytes.Length;
        }
        output.AddRange(file[copied..]);
        return [.. output];
    }
}
