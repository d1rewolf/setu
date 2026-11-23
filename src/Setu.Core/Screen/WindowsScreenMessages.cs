namespace Setu.Core.Screen;

/// <summary>The user-facing text of the Windows desktop's blocking checks, kept here so it can be tested on any OS.</summary>
internal static class WindowsScreenMessages
{
    internal static string LockedMessage(DesktopState state) =>
        $"The screen is locked or the session is disconnected ({state}); file and shell tools still work.";

    /// <summary>
    /// Windows silently drops input from a normal process while an elevated window is in front,
    /// so a "Clicked" would be a lie.
    /// </summary>
    internal static string ElevatedMessage(WindowInfo w) =>
        $"The window in front, '{ScreenTools.OneField(w.Title)}' ({ScreenTools.OneField(w.ProcessName)}), is running as administrator, " +
        "so Windows blocks mouse and keyboard input from setu. Ask the user to switch to another window " +
        "or minimise it; screenshot, list_windows, file and shell tools still work.";
}
