using Setu.Core.Screen;

namespace Setu.Core.Tests;

public sealed class KeySpecTests
{
    [Fact]
    public void KeySpec_ParsesChordSequence()
    {
        var chords = KeySpec.Parse("ctrl+shift+s enter", KeyVocabulary.Windows);
        Assert.Equal(2, chords.Count);
        Assert.Equal(["ctrl", "shift"], chords[0].Modifiers);
        Assert.Equal("s", chords[0].Key);
        Assert.Empty(chords[1].Modifiers);
        Assert.Equal("enter", chords[1].Key);
    }

    [Fact]
    public void KeySpec_IsCaseInsensitive_AndNormalizesToLower()
    {
        var chords = KeySpec.Parse("CTRL+F4", KeyVocabulary.Windows);
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
        Assert.Throws<ToolInputException>(() => KeySpec.Parse(spec, KeyVocabulary.Windows));
    }

    [Fact]
    public void KeySpec_UnknownKey_MessageNamesKeyAndListsKnown()
    {
        var ex = Assert.Throws<ToolInputException>(() => KeySpec.Parse("ctrl+nope", KeyVocabulary.Windows));
        Assert.Contains("Unknown key 'nope'", ex.Message);
        Assert.Contains("known keys:", ex.Message);
    }

    [Fact]
    public void Mac_AliasesCanonicalise()
    {
        var chords = KeySpec.Parse("Command+alt+s", KeyVocabulary.Mac);
        Assert.Equal(["cmd", "option"], chords[0].Modifiers);
        Assert.Equal("s", chords[0].Key);
    }

    [Fact]
    public void Mac_Win_RejectedWithHint()
    {
        var ex = Assert.Throws<ToolInputException>(() => KeySpec.Parse("win+r", KeyVocabulary.Mac));
        Assert.Contains("use cmd on macOS", ex.Message);
    }

    [Theory]
    [InlineData("printscreen")]
    [InlineData("insert")]
    public void Mac_KeysNotOnMacKeyboard_Rejected(string key)
    {
        var ex = Assert.Throws<ToolInputException>(() => KeySpec.Parse(key, KeyVocabulary.Mac));
        Assert.Contains("not on a Mac keyboard", ex.Message);
    }

    [Fact]
    public void Windows_Cmd_Unknown()
    {
        var ex = Assert.Throws<ToolInputException>(() => KeySpec.Parse("cmd+s", KeyVocabulary.Windows));
        Assert.Contains("Unknown key 'cmd'", ex.Message);
    }

    [Theory]
    [InlineData("f21")]
    [InlineData("f24")]
    public void Mac_F21ToF24_RejectedAsNotOnMacKeyboard(string key)
    {
        var ex = Assert.Throws<ToolInputException>(() => KeySpec.Parse(key, KeyVocabulary.Mac));
        Assert.Contains("not on a Mac keyboard", ex.Message);
        Assert.Contains("f1-f20", ex.Message);
    }

    [Fact]
    public void Mac_F20_Accepted()
    {
        Assert.Equal("f20", KeySpec.Parse("f20", KeyVocabulary.Mac)[0].Key);
    }

    [Fact]
    public void Windows_KnownList_Unchanged()
    {
        Assert.Equal(
            "modifiers: ctrl alt shift win; keys: a-z 0-9 f1-f24 enter tab esc backspace delete insert space up down left right home end pageup pagedown printscreen",
            KeyVocabulary.Windows.KnownList());
        Assert.True(KeyVocabulary.Windows.Keys.Contains("f24"));
    }

    [Fact]
    public void Mac_KnownList_SaysF1ToF20()
    {
        Assert.Equal(
            "modifiers: cmd ctrl option shift; keys: a-z 0-9 f1-f20 enter tab esc backspace delete space up down left right home end pageup pagedown",
            KeyVocabulary.Mac.KnownList());
    }
}
