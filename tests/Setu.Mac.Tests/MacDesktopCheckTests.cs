using Setu.Core.Screen;
using Setu.Mac;

namespace Setu.Mac.Tests;

public class MacDesktopCheckTests
{
    private const string Kickstart =
        " If you just granted it, restart Setu: launchctl kickstart -k gui/$(id -u)/com.greatworx.setu";

    private const string LockedMessage =
        "The screen is locked or another user is on the console; file and shell tools still work.";

    private const string ScreenRecordingMessage =
        "Setu needs Screen Recording permission. At the Mac: System Settings › Privacy & Security › " +
        "Screen & System Audio Recording › turn on Setu (or click Allow on the monthly prompt). " +
        "File and shell tools still work." + Kickstart;

    private const string AccessibilityMessage =
        "Setu needs Accessibility permission. At the Mac: System Settings › Privacy & Security › " +
        "Accessibility › turn on Setu. File and shell tools still work." + Kickstart;

    private static readonly ScreenUse[] AllUses = [ScreenUse.See, ScreenUse.List, ScreenUse.Pointer, ScreenUse.Keyboard];

    private static MacDesktop Desktop(FakeMacSystem sys) => new(sys, new FakeRunner(), Path.GetTempPath());

    private static MacWindow Foreign(string? name, int pid = 200, int layer = 0) =>
        new(1, pid, "TextEdit", name, layer, 0, 0, 100, 100, true);

    [Fact]
    public void Locked_BlocksAllButList()
    {
        var desktop = Desktop(new FakeMacSystem { Locked = true });
        foreach (var use in new[] { ScreenUse.See, ScreenUse.Pointer, ScreenUse.Keyboard })
            Assert.Equal(new ScreenCheck(LockedMessage, Blocks: true), desktop.Check(use));
        Assert.Null(desktop.Check(ScreenUse.List));
    }

    [Fact]
    public void NotOnConsole_BlocksAllButList()
    {
        var desktop = Desktop(new FakeMacSystem { Console = false });
        Assert.Equal(new ScreenCheck(LockedMessage, Blocks: true), desktop.Check(ScreenUse.Keyboard));
        Assert.Null(desktop.Check(ScreenUse.List));
    }

    [Fact]
    public void Locked_ComesBeforeMissingGrants()
    {
        var desktop = Desktop(new FakeMacSystem { Locked = true, Preflight = false, PostEvents = false });
        Assert.Equal(LockedMessage, desktop.Check(ScreenUse.See)?.Message);
        Assert.Equal(LockedMessage, desktop.Check(ScreenUse.Pointer)?.Message);
    }

    [Fact]
    public void NoScreenRecording_BlocksSeeAndList_WithKickstartHint()
    {
        // A foreign window is on screen but its name is hidden: the grant really is missing.
        var desktop = Desktop(new FakeMacSystem { Preflight = false, OnScreenWindows = [Foreign(null)] });
        Assert.Equal(new ScreenCheck(ScreenRecordingMessage, Blocks: true), desktop.Check(ScreenUse.See));
        Assert.Equal(new ScreenCheck(ScreenRecordingMessage, Blocks: true), desktop.Check(ScreenUse.List));
        Assert.Null(desktop.Check(ScreenUse.Pointer));
        Assert.Null(desktop.Check(ScreenUse.Keyboard));
    }

    [Fact]
    public void PreflightFalse_ButForeignWindowNamed_CountsAsGranted()
    {
        var desktop = Desktop(new FakeMacSystem { Preflight = false, OnScreenWindows = [Foreign(null), Foreign("Untitled")] });
        Assert.Null(desktop.Check(ScreenUse.See));
        Assert.Null(desktop.Check(ScreenUse.List));
    }

    [Fact]
    public void PreflightFalse_OnlyOwnOrNonZeroLayerWindowsNamed_NotGranted()
    {
        var desktop = Desktop(new FakeMacSystem
        {
            Preflight = false,
            OwnPid = 100,
            OnScreenWindows = [Foreign("Setu's own", pid: 100), Foreign("Menu bar", layer: 25), Foreign(null)],
        });
        Assert.Equal(ScreenRecordingMessage, desktop.Check(ScreenUse.See)?.Message);
    }

    [Fact]
    public void PreflightFalse_NoForeignWindows_TrustsPreflight()
    {
        var desktop = Desktop(new FakeMacSystem { Preflight = false, OnScreenWindows = [] });
        Assert.Equal(ScreenRecordingMessage, desktop.Check(ScreenUse.See)?.Message);
    }

    [Fact]
    public void NoAccessibility_BlocksPointerAndKeyboard()
    {
        var desktop = Desktop(new FakeMacSystem { PostEvents = false });
        Assert.Equal(new ScreenCheck(AccessibilityMessage, Blocks: true), desktop.Check(ScreenUse.Pointer));
        Assert.Equal(new ScreenCheck(AccessibilityMessage, Blocks: true), desktop.Check(ScreenUse.Keyboard));
        Assert.Null(desktop.Check(ScreenUse.See));
        Assert.Null(desktop.Check(ScreenUse.List));
    }

    [Fact]
    public void AllGranted_NoCheck()
    {
        var desktop = Desktop(new FakeMacSystem());
        foreach (var use in AllUses) Assert.Null(desktop.Check(use));
    }
}
