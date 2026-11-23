namespace Setu.Mac;

/// <summary>The blocking messages of the Mac desktop's checks and the rule for when Screen Recording counts as granted.</summary>
internal static class MacPermissions
{
    internal const string RestartHint =
        " If you just granted it, restart Setu: launchctl kickstart -k gui/$(id -u)/com.greatworx.setu";

    internal const string LockedMessage =
        "The screen is locked or another user is on the console; file and shell tools still work.";

    internal const string ScreenRecordingMessage =
        "Setu needs Screen Recording permission. At the Mac: System Settings › Privacy & Security › " +
        "Screen & System Audio Recording › turn on Setu (or click Allow on the monthly prompt). " +
        "File and shell tools still work." + RestartHint;

    internal const string AccessibilityMessage =
        "Setu needs Accessibility permission. At the Mac: System Settings › Privacy & Security › " +
        "Accessibility › turn on Setu. File and shell tools still work." + RestartHint;

    /// <summary>The note on type_text and send_keys while some process holds secure keyboard entry.</summary>
    internal static string SecureInputNote((int Pid, string Name)? holder) =>
        holder is { } h ? $"secure keyboard entry is on, held by '{h.Name}' (PID {h.Pid})" : "secure keyboard entry is on";

    /// <summary>
    /// Granted when the preflight says so, or when macOS shows the name of at least one other process's
    /// on-screen, layer-0 window (it hides those names without the grant). The preflight can keep a stale
    /// "no" inside a running process; a named foreign window overrides it. With no foreign window on screen
    /// the name probe cannot tell, so the preflight answer stands.
    /// </summary>
    internal static bool ScreenRecordingGranted(IMacSystem sys)
    {
        if (sys.ScreenRecordingPreflight()) return true;
        var own = sys.OwnPid;
        return sys.Windows(onScreenOnly: true).Any(w => w.Pid != own && w.Layer == 0 && w.OnScreen && w.Name is not null);
    }
}
