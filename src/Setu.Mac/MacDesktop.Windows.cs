using Setu.Core.Screen;

namespace Setu.Mac;

public sealed partial class MacDesktop
{
    /// <summary>
    /// Layer-0 (normal app) windows, on screen or not; Minimized means "not on screen" (minimized, hidden, or
    /// on another Space). Windows without a title or with no area are left out, as on Windows.
    /// </summary>
    public IReadOnlyList<WindowInfo> ListWindows()
    {
        var foreground = ForegroundWindowId();
        var result = new List<WindowInfo>();
        foreach (var w in sys.Windows(onScreenOnly: false))
        {
            if (w.Layer != 0 || string.IsNullOrEmpty(w.Name) || w.W <= 0 || w.H <= 0) continue;
            result.Add(new WindowInfo(
                w.Id, w.Name, w.Owner, w.Pid,
                Round(w.X), Round(w.Y), Round(w.W), Round(w.H),
                Minimized: !w.OnScreen, Foreground: w.Id == foreground));
        }
        return result;
    }

    /// <summary>
    /// The window in front. The frontmost app is the owner of the first layer-0, on-screen window that is not
    /// Setu's own (the on-screen list is ordered front to back). Its foreground window is its first layer-0
    /// on-screen window with a title, so an app's untitled popover, sheet or panel listed above its document
    /// window does not count; when none of its windows has a title (macOS hides them without Screen Recording),
    /// its first layer-0 window.
    /// </summary>
    private uint? ForegroundWindowId()
    {
        var own = sys.OwnPid;
        var appWindows = sys.Windows(onScreenOnly: true).Where(w => w.Layer == 0 && w.OnScreen).ToList();
        var first = appWindows.FirstOrDefault(w => w.Pid != own);
        if (first is null) return null;
        var frontApp = appWindows.Where(w => w.Pid == first.Pid).ToList();
        return (frontApp.FirstOrDefault(w => !string.IsNullOrEmpty(w.Name)) ?? frontApp[0]).Id;
    }

    private static int Round(double points) => (int)Math.Round(points);
}
