using WindowsMcp.Core.Screen;

namespace WindowsMcp.Core.Tests;

public sealed class KeySpecTests
{
    [Fact]
    public void KeySpec_ParsesChordSequence()
    {
        var chords = KeySpec.Parse("ctrl+shift+s enter");
        Assert.Equal(2, chords.Count);
        Assert.Equal(["ctrl", "shift"], chords[0].Modifiers);
        Assert.Equal("s", chords[0].Key);
        Assert.Empty(chords[1].Modifiers);
        Assert.Equal("enter", chords[1].Key);
    }

    [Fact]
    public void KeySpec_IsCaseInsensitive_AndNormalizesToLower()
    {
        var chords = KeySpec.Parse("CTRL+F4");
        Assert.Equal(["ctrl"], chords[0].Modifiers);
        Assert.Equal("f4", chords[0].Key);
    }

    [Theory]
    [InlineData("ctrl+nope")]
    [InlineData("hyper+a")]
    [InlineData("f25")]
    [InlineData("ctrl+")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ctrl+shift")]
    public void KeySpec_UnknownKey_Error(string spec)
    {
        Assert.Throws<ToolInputException>(() => KeySpec.Parse(spec));
    }

    [Fact]
    public void KeySpec_UnknownKey_MessageNamesKeyAndListsKnown()
    {
        var ex = Assert.Throws<ToolInputException>(() => KeySpec.Parse("ctrl+nope"));
        Assert.Contains("Unknown key 'nope'", ex.Message);
        Assert.Contains("known keys:", ex.Message);
    }
}
