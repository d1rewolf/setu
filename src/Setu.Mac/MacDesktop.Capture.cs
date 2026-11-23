using System.Buffers.Binary;
using System.Globalization;
using ModelContextProtocol;
using Setu.Core.Screen;

namespace Setu.Mac;

public sealed partial class MacDesktop
{
    private const string ScreencapturePath = "/usr/sbin/screencapture";
    private const string SipsPath = "/usr/bin/sips";
    private static readonly TimeSpan ToolTimeout = TimeSpan.FromSeconds(30);

    public Capture Capture(CaptureTarget target, int? maxWidth)
    {
        var window = target is WindowTarget wt ? FindWindow(wt.Handle) : null;
        var union = sys.ScreenUnion();
        var file = Path.Combine(tempDirectory, $"capture-{Guid.NewGuid()}.png");
        // Validates the target (an unknown window throws) before anything runs.
        var args = ScreenCapturePlan.Args(target, union, window, file);
        var (originX, originY, pointWidth) = target switch
        {
            WindowTarget => (window!.X, window.Y, window.W),
            RegionTarget r => ((double)r.X, (double)r.Y, (double)r.Width),
            _ => (union.X, union.Y, union.W),
        };

        try
        {
            var (exit, stderr) = run.Run(ScreencapturePath, args, ToolTimeout);
            if (exit != 0 || !File.Exists(file) || new FileInfo(file).Length == 0)
                throw new McpException($"screencapture failed (exit {exit}): {Detail(stderr)}");

            var (scale, resample) = ScreenCapturePlan.Scale(PngSize(File.ReadAllBytes(file)).Width, pointWidth, maxWidth);
            if (resample is { } width)
            {
                var (sipsExit, sipsErr) = run.Run(SipsPath, ["--resampleWidth", width.ToString(CultureInfo.InvariantCulture), file], ToolTimeout);
                if (sipsExit != 0) throw new McpException($"sips failed (exit {sipsExit}): {Detail(sipsErr)}");
            }

            var png = File.ReadAllBytes(file);
            var (pixelWidth, pixelHeight) = PngSize(png);
            return new Capture(png, (int)Math.Round(originX), (int)Math.Round(originY), pixelWidth, pixelHeight, scale);
        }
        finally
        {
            // Exists never throws, so a missing file or folder cannot hide the error that got us here.
            if (File.Exists(file)) File.Delete(file);
        }
    }

    private MacWindow? FindWindow(long handle) =>
        sys.Windows(onScreenOnly: false).FirstOrDefault(w => w.Id == handle);

    private static string Detail(string stderr) =>
        string.IsNullOrWhiteSpace(stderr) ? "no image was written" : stderr.Trim();

    /// <summary>Width and height from the IHDR chunk, which the PNG format puts first, right after the signature.</summary>
    private static (int Width, int Height) PngSize(ReadOnlySpan<byte> png)
    {
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (png.Length < 24 || !png[..8].SequenceEqual(signature) || !png[12..16].SequenceEqual("IHDR"u8))
            throw new McpException("screencapture failed (exit 0): the image it wrote is not a PNG");
        return (BinaryPrimitives.ReadInt32BigEndian(png[16..]), BinaryPrimitives.ReadInt32BigEndian(png[20..]));
    }
}
