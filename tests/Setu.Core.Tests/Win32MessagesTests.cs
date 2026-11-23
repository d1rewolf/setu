using Setu.Core.Screen;

namespace Setu.Core.Tests;

public sealed class Win32MessagesTests
{
    [Theory]
    [InlineData(DesktopState.Locked)]
    [InlineData(DesktopState.Disconnected)]
    public void LockedMessage_NamesStateAndSaysFileAndShellToolsWork(DesktopState state)
    {
        var message = WindowsScreenMessages.LockedMessage(state);
        Assert.Contains("locked or the session is disconnected", message);
        Assert.Contains(state.ToString(), message);
        Assert.Contains("file and shell tools still work", message);
    }

    [Fact]
    public void ElevatedMessage_NamesWindowAndExplainsAdministrator()
    {
        var w = new WindowInfo(42, "Administrator: Windows PowerShell", "WindowsTerminal", 8212, 0, 0, 100, 100, false, true);
        var message = WindowsScreenMessages.ElevatedMessage(w);
        Assert.Contains("'Administrator: Windows PowerShell' (WindowsTerminal)", message);
        Assert.Contains("running as administrator", message);
        Assert.Contains("screenshot, list_windows, file and shell tools still work", message);
    }
}
