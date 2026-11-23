using System.Globalization;
using Setu.Core;
using Setu.Core.Screen;

namespace Setu.Mac;

/// <summary>How a capture target becomes a /usr/sbin/screencapture command line, and how big the result is.</summary>
internal static class ScreenCapturePlan
{
    /// <summary>
    /// The arguments for /usr/sbin/screencapture. Every rectangle is in points (global display coordinates);
    /// -x silences the shutter sound. A window capture (-l) uses -o to leave out the shadow, so the image is the
    /// window's frame and its origin is the window's position.
    /// </summary>
    public static IReadOnlyList<string> Args(
        CaptureTarget t, (double X, double Y, double W, double H) screenUnion, MacWindow? window, string outFile) =>
        t switch
        {
            ScreenTarget => ["-x", "-t", "png", "-R", Rect(screenUnion.X, screenUnion.Y, screenUnion.W, screenUnion.H), outFile],
            WindowTarget w => window is not null
                ? ["-x", "-t", "png", "-o", "-l", window.Id.ToString(CultureInfo.InvariantCulture), outFile]
                : throw new ToolInputException($"No window with handle {w.Handle}; get a current handle from list_windows"),
            RegionTarget r => ["-x", "-t", "png", "-R", Rect(r.X, r.Y, r.Width, r.Height), outFile],
            _ => throw new ToolInputException($"Unsupported capture target {t.GetType().Name}"),
        };

    /// <summary>
    /// Image pixels per point, and the width to resample to when <paramref name="maxWidth"/> is smaller than
    /// the PNG (null when the image is kept as captured).
    /// </summary>
    public static (double Scale, int? ResampleWidth) Scale(int pngWidth, double pointWidth, int? maxWidth) =>
        maxWidth is { } max && max < pngWidth
            ? (max / pointWidth, max)
            : (pngWidth / pointWidth, null);

    private static string Rect(double x, double y, double w, double h) =>
        string.Join(',', new[] { x, y, w, h }.Select(v => ((long)Math.Round(v)).ToString(CultureInfo.InvariantCulture)));
}
