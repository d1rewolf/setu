using ModelContextProtocol;
using Setu.Core.Screen;
using Setu.Mac;

namespace Setu.Mac.Tests;

public class MacDesktopInputTests
{
    private static MacDesktop Desktop(FakeMacSystem sys) => new(sys, new FakeRunner(), Path.GetTempPath());

    private static MacWindow Window(uint id, int pid = 200, bool onScreen = true) =>
        new(id, pid, "TextEdit", $"Window {id}", 0, 10, 20, 300, 200, onScreen);

    [Fact]
    public void Click_MovesThenDownUp()
    {
        var sys = new FakeMacSystem();
        Desktop(sys).Click(100, 50, MouseButton.Left, doubleClick: false);
        Assert.Equal<MacEvent>(
            [
                new MouseMoveEvent(100, 50),
                new MouseButtonEvent(MouseButton.Left, Down: true, 100, 50, ClickState: 1),
                new MouseButtonEvent(MouseButton.Left, Down: false, 100, 50, ClickState: 1),
            ],
            sys.Posted);
    }

    [Fact]
    public void DoubleClick_SecondPairHasClickState2()
    {
        var sys = new FakeMacSystem();
        Desktop(sys).Click(7, 8, MouseButton.Right, doubleClick: true);
        Assert.Equal<MacEvent>(
            [
                new MouseMoveEvent(7, 8),
                new MouseButtonEvent(MouseButton.Right, true, 7, 8, 1),
                new MouseButtonEvent(MouseButton.Right, false, 7, 8, 1),
                new MouseButtonEvent(MouseButton.Right, true, 7, 8, 2),
                new MouseButtonEvent(MouseButton.Right, false, 7, 8, 2),
            ],
            sys.Posted);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(-2)]
    public void Scroll_MovesThenScrollsLines_PositiveIsUp(int amount)
    {
        var sys = new FakeMacSystem();
        Desktop(sys).Scroll(40, 60, amount);
        Assert.Equal<MacEvent>([new MouseMoveEvent(40, 60), new ScrollEvent(amount)], sys.Posted);
    }

    [Fact]
    public void TypeText_DownAndUpPerChunk()
    {
        var sys = new FakeMacSystem();
        var text = new string('x', 25);
        Desktop(sys).TypeText(text);
        Assert.Equal<MacEvent>(
            [
                new UnicodeEvent(new string('x', 20), Down: true),
                new UnicodeEvent(new string('x', 20), Down: false),
                new UnicodeEvent("xxxxx", Down: true),
                new UnicodeEvent("xxxxx", Down: false),
            ],
            sys.Posted);
    }

    [Fact]
    public void TypeText_Empty_PostsNothing()
    {
        var sys = new FakeMacSystem();
        Desktop(sys).TypeText("");
        Assert.Empty(sys.Posted);
    }

    [Fact]
    public void SendKeys_FlagsOnTheKeysOwnEvents()
    {
        var sys = new FakeMacSystem();
        Desktop(sys).SendKeys([new KeyChord(["cmd", "shift"], "s"), new KeyChord([], "enter")]);
        Assert.Equal<MacEvent>(
            [
                new KeyEvent(0x01, Down: true, 0x120000),
                new KeyEvent(0x01, Down: false, 0x120000),
                new KeyEvent(0x24, Down: true, 0),
                new KeyEvent(0x24, Down: false, 0),
            ],
            sys.Posted);
    }

    [Fact]
    public void SendKeys_ParsedFromMacVocabulary_DuplicateModifiersHarmless()
    {
        var sys = new FakeMacSystem();
        Desktop(sys).SendKeys(KeySpec.Parse("cmd+command+s", KeyVocabulary.Mac));
        Assert.Equal<MacEvent>([new KeyEvent(0x01, true, 0x100000), new KeyEvent(0x01, false, 0x100000)], sys.Posted);
    }

    [Fact]
    public void FocusWindow_RaisedToFront_True()
    {
        var target = Window(5, onScreen: false);
        var sys = new FakeMacSystem { AllWindows = [Window(4), target], OnScreenWindows = [Window(4)] };
        Assert.True(Desktop(sys).FocusWindow(5));
        Assert.Equal([target], sys.Raised);
    }

    [Fact]
    public void FocusWindow_AlreadyInFront_NoRaise()
    {
        var sys = new FakeMacSystem { AllWindows = [Window(5)], OnScreenWindows = [Window(5)] };
        Assert.True(Desktop(sys).FocusWindow(5));
        Assert.Empty(sys.Raised);
    }

    [Fact]
    public void FocusWindow_StaysBehind_FalseAfterWaiting()
    {
        var sys = new FakeMacSystem { AllWindows = [Window(4), Window(5)], OnScreenWindows = [Window(4), Window(5)], RaiseWorks = false };
        var started = System.Diagnostics.Stopwatch.StartNew();
        Assert.False(Desktop(sys).FocusWindow(5));
        Assert.InRange(started.ElapsedMilliseconds, 450, 5000);
    }

    [Fact]
    public void FocusWindow_UnknownHandle_Throws()
    {
        var sys = new FakeMacSystem { AllWindows = [Window(4)] };
        var ex = Assert.Throws<McpException>(() => Desktop(sys).FocusWindow(99));
        Assert.Equal("No window with handle 99; get a current handle from list_windows", ex.Message);
        Assert.Empty(sys.Raised);
    }
}
