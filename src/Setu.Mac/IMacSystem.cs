using Setu.Core.Screen;

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
    /// <summary>IsSecureEventInputEnabled(): some process holds secure keyboard entry.</summary>
    bool SecureInputOn();
    /// <summary>The process holding secure keyboard entry, or null when it cannot be found.</summary>
    (int Pid, string Name)? SecureInputHolder();
    /// <summary>Posts the events at the HID event tap, in order, 10 ms apart.</summary>
    void Post(IReadOnlyList<MacEvent> events);
    /// <summary>
    /// Asks the window's app, through Accessibility, to come to the front, then un-minimizes, raises and makes
    /// main that window. Best effort and returns nothing: the caller judges the outcome from the window list.
    /// </summary>
    void Raise(MacWindow window);
}

/// <summary>One entry of CGWindowListCopyWindowInfo. Bounds are in points; Name is null when macOS hides it.</summary>
internal sealed record MacWindow(
    uint Id, int Pid, string Owner, string? Name, int Layer, double X, double Y, double W, double H, bool OnScreen);

/// <summary>One synthetic input event for <see cref="IMacSystem.Post"/>. Coordinates are global points.</summary>
internal abstract record MacEvent;

internal sealed record MouseMoveEvent(double X, double Y) : MacEvent;

/// <param name="ClickState">kCGMouseEventClickState: 1 for a single click, 2 for the second click of a double.</param>
internal sealed record MouseButtonEvent(MouseButton Button, bool Down, double X, double Y, int ClickState) : MacEvent;

/// <summary>Whole wheel lines; positive scrolls up, as on Windows.</summary>
internal sealed record ScrollEvent(int Lines) : MacEvent;

internal sealed record KeyEvent(ushort KeyCode, bool Down, ulong Flags) : MacEvent;

/// <summary>A keyboard event that carries text instead of a key, with no modifier flags.</summary>
internal sealed record UnicodeEvent(string Text, bool Down) : MacEvent;

/// <summary>Runs a program to completion.</summary>
internal interface ICommandRunner
{
    /// <summary>Discards stdout.</summary>
    (int Exit, string Stderr) Run(string file, IReadOnlyList<string> args, TimeSpan timeout);

    /// <summary>Also returns stdout.</summary>
    (int Exit, string Stdout, string Stderr) RunCapture(string file, IReadOnlyList<string> args, TimeSpan timeout);
}
