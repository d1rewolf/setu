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

    /// <summary>The frontmost layer-0 window that is not Setu's own; the on-screen list is ordered front to back.</summary>
    private uint? ForegroundWindowId()
    {
        var own = sys.OwnPid;
        return sys.Windows(onScreenOnly: true).FirstOrDefault(w => w.Layer == 0 && w.Pid != own)?.Id;
    }

    private static int Round(double points) => (int)Math.Round(points);
}
