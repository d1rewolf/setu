namespace Setu.Core.Screen;

public enum DesktopState { Available, Locked, Disconnected }

public enum ScreenUse { See, List, Pointer, Keyboard }

/// <summary>
/// Blocks=true: the tool throws McpException(Message) before touching the desktop.
/// Blocks=false: the tool runs and appends "\nnote: " + Message to its text result.
/// </summary>
public sealed record ScreenCheck(string Message, bool Blocks);

public enum MouseButton { Left, Right, Middle }

public sealed record WindowInfo(
    long Handle, string Title, string ProcessName, int Pid,
    int X, int Y, int Width, int Height, bool Minimized, bool Foreground);

public abstract record CaptureTarget;

public sealed record ScreenTarget : CaptureTarget;

public sealed record WindowTarget(long Handle) : CaptureTarget;

public sealed record RegionTarget(int X, int Y, int Width, int Height) : CaptureTarget;

/// <summary>A PNG plus where it sits on the virtual screen. screen_x = OriginX + image_x / Scale.</summary>
public sealed record Capture(byte[] Png, int OriginX, int OriginY, int PixelWidth, int PixelHeight, double Scale);

public sealed record KeyChord(IReadOnlyList<string> Modifiers, string Key);

/// <summary>The OS-facing surface the screen tools run against. Implemented with Win32 elsewhere.</summary>
public interface IDesktop
{
    /// <summary>
    /// Whether this desktop can serve the given use right now, or null when nothing stands in the way.
    /// A blocking check stops the tool before it touches the desktop; a non-blocking one is a note on the result.
    /// </summary>
    ScreenCheck? Check(ScreenUse use);
    IReadOnlyList<WindowInfo> ListWindows();
    Capture Capture(CaptureTarget target, int? maxWidth);
    void Click(int x, int y, MouseButton button, bool doubleClick);
    void TypeText(string text);
    void SendKeys(IReadOnlyList<KeyChord> chords);
    void Scroll(int x, int y, int amount);
    bool FocusWindow(long handle);
}
