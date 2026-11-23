using Setu.Core.Screen;
using Setu.Mac;

namespace Setu.Mac.Tests;

public class MacKeyCodesTests
{
    [Theory]
    [InlineData("a", 0x00)]
    [InlineData("s", 0x01)]
    [InlineData("enter", 0x24)]
    [InlineData("tab", 0x30)]
    [InlineData("space", 0x31)]
    [InlineData("backspace", 0x33)]
    [InlineData("esc", 0x35)]
    [InlineData("delete", 0x75)]
    [InlineData("left", 0x7B)]
    [InlineData("right", 0x7C)]
    [InlineData("down", 0x7D)]
    [InlineData("up", 0x7E)]
    [InlineData("home", 0x73)]
    [InlineData("end", 0x77)]
    [InlineData("pageup", 0x74)]
    [InlineData("pagedown", 0x79)]
    [InlineData("f1", 0x7A)]
    // A spread of the rest of HIToolbox/Events.h, which is not in key order.
    [InlineData("z", 0x06)]
    [InlineData("q", 0x0C)]
    [InlineData("m", 0x2E)]
    [InlineData("0", 0x1D)]
    [InlineData("1", 0x12)]
    [InlineData("5", 0x17)]
    [InlineData("9", 0x19)]
    [InlineData("f5", 0x60)]
    [InlineData("f12", 0x6F)]
    [InlineData("f20", 0x5A)]
    public void Keys_MatchHIToolbox(string key, int code)
    {
        Assert.Equal((ushort)code, MacKeyCodes.Key(key));
    }

    [Fact]
    public void EveryMacVocabularyKey_HasACode_AndCodesAreDistinct()
    {
        var codes = KeyVocabulary.Mac.Keys.ToDictionary(k => k, MacKeyCodes.Key);
        Assert.Equal(codes.Count, codes.Values.Distinct().Count());
    }

    [Fact]
    public void UnknownKey_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() => MacKeyCodes.Key("f21"));
    }

    [Fact]
    public void Flags_Combine()
    {
        Assert.Equal(0x120000UL, MacKeyCodes.Flags(["cmd", "shift"]));
        Assert.Equal(0x40000UL | 0x80000UL, MacKeyCodes.Flags(["ctrl", "option"]));
        Assert.Equal(0UL, MacKeyCodes.Flags([]));
    }

    [Fact]
    public void Flags_DuplicateModifier_SameAsOnce()
    {
        Assert.Equal(0x100000UL, MacKeyCodes.Flags(["cmd", "cmd"]));
    }

    [Fact]
    public void ModifierKeys_FixedOrderEachOnce()
    {
        Assert.Equal(
            [((ushort)0x37, 0x100000UL), ((ushort)0x3B, 0x40000UL), ((ushort)0x3A, 0x80000UL), ((ushort)0x38, 0x20000UL)],
            MacKeyCodes.ModifierKeys(["shift", "option", "cmd", "ctrl", "cmd"]));
        Assert.Empty(MacKeyCodes.ModifierKeys([]));
    }

    [Fact]
    public void ModifierKeys_UnknownModifier_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() => MacKeyCodes.ModifierKeys(["hyper"]));
    }
}
