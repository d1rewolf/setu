using Setu.Core.Screen;
using Setu.Mac;

namespace Setu.Mac.Tests;

public class MacDesktopWindowsTests
{
    private static MacDesktop Desktop(FakeMacSystem sys) => new(sys, new FakeRunner(), Path.GetTempPath());

    [Fact]
    public void ListWindows_KeepsLayer0Titled_MapsFields()
    {
        var sys = new FakeMacSystem
        {
            AllWindows =
            [
                new(10, 200, "TextEdit", "notes.txt", 0, 10.4, 20.6, 300, 400, true),
                new(11, 201, "Dock", "Dock", 20, 0, 0, 1512, 982, true),
                new(12, 202, "Finder", null, 0, 0, 0, 0, 0, false),
                new(13, 203, "Safari", "Apple", 0, 0, 0, 800, 600, false),
            ],
        };
        var windows = Desktop(sys).ListWindows();
        Assert.Equal(
            [
                new WindowInfo(10, "notes.txt", "TextEdit", 200, 10, 21, 300, 400, Minimized: false, Foreground: false),
                new WindowInfo(13, "Apple", "Safari", 203, 0, 0, 800, 600, Minimized: true, Foreground: false),
            ],
            windows);
    }

    [Fact]
    public void ListWindows_ForegroundIsFrontmostForeignLayer0OnScreen()
    {
        var a = new MacWindow(10, 200, "TextEdit", "a", 0, 0, 0, 100, 100, true);
        var b = new MacWindow(11, 201, "Safari", "b", 0, 0, 0, 100, 100, true);
        var sys = new FakeMacSystem
        {
            OwnPid = 100,
            AllWindows = [a, b],
            OnScreenWindows =
            [
                new(1, 300, "Window Server", "Menubar", 24, 0, 0, 1512, 30, true),
                new(2, 100, "setu", "own", 0, 0, 0, 10, 10, true),
                b,
                a,
            ],
        };
        var windows = Desktop(sys).ListWindows();
        Assert.False(windows.Single(w => w.Handle == 10).Foreground);
        Assert.True(windows.Single(w => w.Handle == 11).Foreground);
    }
}
