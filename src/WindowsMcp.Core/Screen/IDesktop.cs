namespace WindowsMcp.Core.Screen;

public enum DesktopState { Available, Locked, Disconnected }

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
    DesktopState State();

    /// <summary>
    /// The foreground window when its process runs at a higher integrity level than this one (for example
    /// an Administrator terminal), else null. Windows then drops this process's synthetic input and
    /// refuses to change the foreground, while still reporting success.
    /// </summary>
    WindowInfo? ElevatedForegroundWindow();
    IReadOnlyList<WindowInfo> ListWindows();
    Capture Capture(CaptureTarget target, int? maxWidth);
    void Click(int x, int y, MouseButton button, bool doubleClick);
    void TypeText(string text);
    void SendKeys(IReadOnlyList<KeyChord> chords);
    void Scroll(int x, int y, int amount);
    bool FocusWindow(long handle);
}
