using Setu.Core.Files;

namespace Setu.Core.Tests;

public sealed class ByteSpliceTests
{
    [Fact]
    public void ByteSplice_Utf16_IgnoresMisalignedMatch()
    {
        // UTF-16LE BOM, then bytes where "AB" (41 00 42 00) appears only at odd offset 3.
        byte[] file = [0xFF, 0xFE, 0x30, 0x41, 0x00, 0x42, 0x00, 0x30];
        byte[] needle = [0x41, 0x00, 0x42, 0x00];
        var result = ByteSplice.Replace(file, needle, [0x43, 0x00], 2, 2, false, out var count);
        Assert.Equal(0, count);
        Assert.Equal(file, result);
    }

    [Fact]
    public void ByteSplice_Utf16_SkipsMisalignedThenFindsAligned()
    {
        // Misaligned "AB" at offset 3, aligned "AB" at offset 8.
        byte[] file = [0xFF, 0xFE, 0x30, 0x41, 0x00, 0x42, 0x00, 0x30, 0x41, 0x00, 0x42, 0x00];
        byte[] needle = [0x41, 0x00, 0x42, 0x00];
        var result = ByteSplice.Replace(file, needle, [0x43, 0x00], 2, 2, false, out var count);
        Assert.Equal(1, count);
        Assert.Equal([0xFF, 0xFE, 0x30, 0x41, 0x00, 0x42, 0x00, 0x30, 0x43, 0x00], result);
    }

    [Fact]
    public void ByteSplice_ReplaceAll_ReplacesNonOverlapping()
    {
        var result = ByteSplice.Replace("aaaa"u8, "aa"u8, "b"u8, 0, 1, true, out var count);
        Assert.Equal(2, count);
        Assert.Equal("bb"u8.ToArray(), result);
    }

    [Fact]
    public void ByteSplice_NotReplaceAll_CountsAllReplacesFirst()
    {
        var result = ByteSplice.Replace("x-x"u8, "x"u8, "y"u8, 0, 1, false, out var count);
        Assert.Equal(2, count);
        Assert.Equal("y-x"u8.ToArray(), result);
    }

    [Fact]
    public void ByteSplice_DoesNotMatchInsidePreamble()
    {
        byte[] file = [0xEF, 0xBB, 0xBF, (byte)'a'];
        var result = ByteSplice.Replace(file, [0xBB], [0x00], 3, 1, true, out var count);
        Assert.Equal(0, count);
        Assert.Equal(file, result);
    }
}
