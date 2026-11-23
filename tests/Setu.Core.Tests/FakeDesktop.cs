using Setu.Core.Screen;

namespace Setu.Core.Tests;

/// <summary>In-memory IDesktop that records every call. Reused by the HTTP host tests.</summary>
public sealed class FakeDesktop : IDesktop
{
    public Dictionary<ScreenUse, ScreenCheck> Checks { get; } = [];
    public List<WindowInfo> Windows { get; set; } = [];
    public Capture NextCapture { get; set; } = new([1, 2, 3], 0, 0, 100, 50, 1.0);
    public bool FocusResult { get; set; } = true;
    public List<string> Calls { get; } = [];
    public (CaptureTarget Target, int? MaxWidth)? LastCapture { get; private set; }
    public IReadOnlyList<KeyChord>? LastChords { get; private set; }

    public ScreenCheck? Check(ScreenUse use) => Checks.GetValueOrDefault(use);

    public IReadOnlyList<WindowInfo> ListWindows()
    {
        Calls.Add("ListWindows");
        return Windows;
    }

    public Capture Capture(CaptureTarget target, int? maxWidth)
    {
        Calls.Add("Capture");
        LastCapture = (target, maxWidth);
        return NextCapture;
    }

    public void Click(int x, int y, MouseButton button, bool doubleClick) =>
        Calls.Add($"Click {x},{y} {button} {doubleClick}");

    public void TypeText(string text) => Calls.Add($"TypeText {text}");

    public void SendKeys(IReadOnlyList<KeyChord> chords)
    {
        Calls.Add("SendKeys");
        LastChords = chords;
    }

    public void Scroll(int x, int y, int amount) => Calls.Add($"Scroll {x},{y} {amount}");

    public bool FocusWindow(long handle)
    {
        Calls.Add($"FocusWindow {handle}");
        return FocusResult;
    }
}
