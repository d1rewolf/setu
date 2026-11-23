using Setu.Mac;

namespace Setu.Mac.Tests;

public class TextChunkerTests
{
    [Fact]
    public void Chunk_Ascii_20Each()
    {
        var chunks = TextChunker.Chunks(new string('x', 45)).ToList();
        Assert.Equal([20, 20, 5], chunks.Select(c => c.Length));
    }

    [Fact]
    public void Chunk_NeverSplitsSurrogatePair()
    {
        var text = new string('a', 19) + "😀" + "b";
        var chunks = TextChunker.Chunks(text).ToList();
        Assert.Equal(19, chunks[0].Length);
        Assert.All(chunks, AssertValidUtf16);
        Assert.Equal(text, string.Concat(chunks));
    }

    [Fact]
    public void Chunk_Empty_NoChunks()
    {
        Assert.Empty(TextChunker.Chunks(""));
    }

    [Fact]
    public void Chunk_OnlyEmoji_PairsStayWhole()
    {
        var text = string.Concat(Enumerable.Repeat("😀", 15));
        var chunks = TextChunker.Chunks(text).ToList();
        Assert.Equal([20, 10], chunks.Select(c => c.Length));
        Assert.All(chunks, AssertValidUtf16);
    }

    private static void AssertValidUtf16(string s)
    {
        for (var i = 0; i < s.Length; i++)
        {
            if (char.IsHighSurrogate(s[i]))
            {
                Assert.True(i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]), $"lone high surrogate at {i} in chunk");
                i++;
            }
            else
            {
                Assert.False(char.IsLowSurrogate(s[i]), $"lone low surrogate at {i} in chunk");
            }
        }
    }
}
