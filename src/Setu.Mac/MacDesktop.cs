using Setu.Core.Screen;

namespace Setu.Mac;

/// <summary>
/// IDesktop over macOS. Split into partial files: this one (session state and permissions),
/// MacDesktop.Windows.cs (window list), MacDesktop.Capture.cs (screencapture) and MacDesktop.Input.cs (input).
/// Every coordinate is in points, in global display coordinates (origin top-left of the main display).
/// </summary>
public sealed partial class MacDesktop : IDesktop
{
    private readonly IMacSystem sys;
    private readonly ICommandRunner run;
    private readonly string tempDirectory;

    /// <param name="tempDirectory">Where screenshots are written briefly before they are read and deleted.</param>
    public MacDesktop(string tempDirectory) : this(new MacSystem(), new ProcessCommandRunner(), tempDirectory) { }

    internal MacDesktop(IMacSystem sys, ICommandRunner run, string tempDirectory)
    {
        this.sys = sys;
        this.run = run;
        this.tempDirectory = tempDirectory;
    }

    public ScreenCheck? Check(ScreenUse use)
    {
        if (use != ScreenUse.List && (!sys.OnConsole() || sys.ScreenLocked()))
            return new(MacPermissions.LockedMessage, Blocks: true);
        if (use is ScreenUse.See or ScreenUse.List && !MacPermissions.ScreenRecordingGranted(sys))
            return new(MacPermissions.ScreenRecordingMessage, Blocks: true);
        if (use is ScreenUse.Pointer or ScreenUse.Keyboard && !sys.PostEventAllowed())
            return new(MacPermissions.AccessibilityMessage, Blocks: true);
        return null;
    }
}
