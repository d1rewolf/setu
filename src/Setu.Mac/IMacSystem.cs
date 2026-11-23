namespace Setu.Mac;

/// <summary>
/// What MacDesktop asks of macOS: the seam the tests fake. <see cref="MacSystem"/> is the P/Invoke implementation.
/// </summary>
internal interface IMacSystem
{
    /// <summary>This login session owns the console (not switched away by fast user switching).</summary>
    bool OnConsole();
    bool ScreenLocked();
    /// <summary>CGPreflightScreenCaptureAccess(). Can keep an old answer inside a running process.</summary>
    bool ScreenRecordingPreflight();
    /// <summary>Posting events and Accessibility are both allowed.</summary>
    bool PostEventAllowed();
    /// <summary>Front to back when <paramref name="onScreenOnly"/>.</summary>
    IReadOnlyList<MacWindow> Windows(bool onScreenOnly);
    /// <summary>The bounding box of all active displays, in points.</summary>
    (double X, double Y, double W, double H) ScreenUnion();
    int OwnPid { get; }
}

/// <summary>One entry of CGWindowListCopyWindowInfo. Bounds are in points; Name is null when macOS hides it.</summary>
internal sealed record MacWindow(
    uint Id, int Pid, string Owner, string? Name, int Layer, double X, double Y, double W, double H, bool OnScreen);

/// <summary>Runs a program to completion.</summary>
internal interface ICommandRunner
{
    (int Exit, string Stderr) Run(string file, IReadOnlyList<string> args, TimeSpan timeout);
}
