using System.Diagnostics;
using ModelContextProtocol;

namespace Setu.Mac;

public sealed partial class MacDesktop
{
    private static readonly TimeSpan FocusWait = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan FocusPoll = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// True once the window's app is frontmost and the window is that app's foreground window (see
    /// <see cref="ForegroundWindowId"/>: the app's own untitled popovers do not count). macOS may refuse or be slow;
    /// after <see cref="FocusWait"/> without it in front this returns false.
    /// </summary>
    public bool FocusWindow(long handle)
    {
        var window = FindWindow(handle)
            ?? throw new McpException($"No window with handle {handle}; get a current handle from list_windows");

        // Already in front: do nothing more, as on Windows.
        if (ForegroundWindowId() == window.Id) return true;

        sys.Raise(window);
        var waited = Stopwatch.StartNew();
        while (true)
        {
            if (ForegroundWindowId() == window.Id) return true;
            if (waited.Elapsed >= FocusWait) return false;
            Thread.Sleep(FocusPoll);
        }
    }
}
