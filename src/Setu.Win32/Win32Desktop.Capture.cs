using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using ModelContextProtocol;
using Setu.Core;
using Setu.Core.Screen;

namespace Setu.Win32;

public sealed partial class Win32Desktop
{
    private const string CaptureBlocked =
        "Screen capture failed; the screen may be locked or showing a secure prompt";

    public Capture Capture(CaptureTarget target, int? maxWidth)
    {
        var (bitmap, originX, originY) = target switch
        {
            ScreenTarget => CaptureScreen(VirtualScreen()),
            WindowTarget w => CaptureWindow(w.Handle),
            RegionTarget r => CaptureRegion(r),
            _ => throw new ToolInputException($"Unsupported capture target {target.GetType().Name}"),
        };

        using (bitmap)
        {
            var originalWidth = bitmap.Width;
            if (maxWidth is { } max && max < originalWidth)
            {
                var height = Math.Max(1, (int)Math.Round(bitmap.Height * (double)max / originalWidth));
                using var scaled = Downscale(bitmap, max, height);
                return new Capture(Png(scaled), originX, originY, scaled.Width, scaled.Height, (double)max / originalWidth);
            }
            return new Capture(Png(bitmap), originX, originY, bitmap.Width, bitmap.Height, 1.0);
        }
    }

    private static Rectangle VirtualScreen() => new(
        NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN),
        NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN),
        NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN),
        NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN));

    private static (Bitmap, int, int) CaptureRegion(RegionTarget r)
    {
        var area = Rectangle.Intersect(new Rectangle(r.X, r.Y, r.Width, r.Height), VirtualScreen());
        if (area.Width <= 0 || area.Height <= 0)
            throw new ToolInputException($"Region {r.X},{r.Y},{r.Width},{r.Height} is outside the screen");
        return CaptureScreen(area);
    }

    private static (Bitmap, int, int) CaptureWindow(long handle)
    {
        var hwnd = RequireWindow(handle);
        if (NativeMethods.IsIconic(hwnd))
            throw new McpException($"Window {handle} is minimized; focus_window it first");
        if (!NativeMethods.GetWindowRect(hwnd, out var r))
            throw new McpException($"Could not get the position of window {handle}; it may have closed");
        var rect = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        if (rect.Width <= 0 || rect.Height <= 0)
            throw new McpException($"Window {handle} has no visible area");

        var printed = PrintWindow(hwnd, rect.Width, rect.Height);
        if (printed is not null) return (printed, rect.Left, rect.Top);

        // PrintWindow refused: fall back to what is on screen where the window is.
        var onScreen = Rectangle.Intersect(rect, VirtualScreen());
        if (onScreen.Width <= 0 || onScreen.Height <= 0)
            throw new McpException($"Window {handle} could not be rendered and is not on screen");
        return CaptureScreen(onScreen);
    }

    /// <summary>BitBlt of a rectangle of the virtual screen (taskbar, tray and layered windows included).</summary>
    private static (Bitmap, int, int) CaptureScreen(Rectangle area)
    {
        if (area.Width <= 0 || area.Height <= 0) throw new McpException(CaptureBlocked);
        var screenDc = NativeMethods.GetDC(0);
        if (screenDc == 0) throw new McpException(CaptureBlocked);
        try
        {
            var bitmap = WithMemoryBitmap(screenDc, area.Width, area.Height, memDc =>
                NativeMethods.BitBlt(memDc, 0, 0, area.Width, area.Height, screenDc, area.X, area.Y,
                    NativeMethods.SRCCOPY | NativeMethods.CAPTUREBLT));
            return (bitmap ?? throw new McpException(CaptureBlocked), area.X, area.Y);
        }
        finally
        {
            _ = NativeMethods.ReleaseDC(0, screenDc);
        }
    }

    /// <summary>The window rendered by itself (PW_RENDERFULLCONTENT), or null when PrintWindow fails.</summary>
    private static Bitmap? PrintWindow(nint hwnd, int width, int height)
    {
        var screenDc = NativeMethods.GetDC(0);
        if (screenDc == 0) throw new McpException(CaptureBlocked);
        try
        {
            return WithMemoryBitmap(screenDc, width, height, memDc =>
                NativeMethods.PrintWindow(hwnd, memDc, NativeMethods.PW_RENDERFULLCONTENT));
        }
        finally
        {
            _ = NativeMethods.ReleaseDC(0, screenDc);
        }
    }

    /// <summary>
    /// Creates a memory DC and a bitmap compatible with <paramref name="referenceDc"/>, lets
    /// <paramref name="draw"/> paint into it, and copies the result into a managed Bitmap. Returns null when
    /// draw returns false. Every GDI object is released whatever happens.
    /// </summary>
    private static Bitmap? WithMemoryBitmap(nint referenceDc, int width, int height, Func<nint, bool> draw)
    {
        var memDc = NativeMethods.CreateCompatibleDC(referenceDc);
        if (memDc == 0) throw new McpException(CaptureBlocked);
        try
        {
            var hBitmap = NativeMethods.CreateCompatibleBitmap(referenceDc, width, height);
            if (hBitmap == 0) throw new McpException($"Could not allocate a {width}x{height} capture bitmap");
            try
            {
                var old = NativeMethods.SelectObject(memDc, hBitmap);
                if (old == 0 || old == NativeMethods.HGDI_ERROR) throw new McpException(CaptureBlocked);
                bool drawn;
                try
                {
                    drawn = draw(memDc);
                }
                finally
                {
                    // Deselect so the bitmap can be read and deleted.
                    _ = NativeMethods.SelectObject(memDc, old);
                }
                return drawn ? Image.FromHbitmap(hBitmap) : null;
            }
            finally
            {
                _ = NativeMethods.DeleteObject(hBitmap);
            }
        }
        finally
        {
            _ = NativeMethods.DeleteDC(memDc);
        }
    }

    private static Bitmap Downscale(Bitmap source, int width, int height)
    {
        var scaled = new Bitmap(width, height, PixelFormat.Format32bppRgb);
        try
        {
            using var g = Graphics.FromImage(scaled);
            g.CompositingMode = CompositingMode.SourceCopy;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.SmoothingMode = SmoothingMode.HighQuality;
            // TileFlipXY stops the bicubic filter from blending a dark fringe in at the edges.
            using var attributes = new ImageAttributes();
            attributes.SetWrapMode(WrapMode.TileFlipXY);
            g.DrawImage(source, new Rectangle(0, 0, width, height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
            return scaled;
        }
        catch
        {
            scaled.Dispose();
            throw;
        }
    }

    private static byte[] Png(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }
}
