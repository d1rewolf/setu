using System.ComponentModel;
using System.Globalization;
using System.Text;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace WindowsMcp.Core.Screen;

[McpServerToolType]
public sealed class ScreenTools(RequestLog log, IDesktop desktop)
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    [McpServerTool(Name = "list_windows")]
    [Description("Lists visible top-level windows, one per line, tab-separated: handle, pid, process name, x,y,w,h, flags (fg = foreground, min = minimized), title. Use the handle with screenshot (window:<handle>) and focus_window. Works even while the screen is locked.")]
    public string ListWindows()
        => ToolGuard.Run(log, "list_windows", () =>
        {
            var windows = desktop.ListWindows();
            if (windows.Count == 0) return "No windows.";
            var sb = new StringBuilder();
            foreach (var w in windows)
            {
                var flags = string.Join(",", new[] { w.Minimized ? "min" : null, w.Foreground ? "fg" : null }.OfType<string>());
                sb.Append(w.Handle.ToString(Inv)).Append('\t')
                  .Append(w.Pid.ToString(Inv)).Append('\t')
                  .Append(w.ProcessName).Append('\t')
                  .Append(Inv, $"{w.X},{w.Y},{w.Width},{w.Height}").Append('\t')
                  .Append(flags).Append('\t')
                  .Append(w.Title).Append('\n');
            }
            return sb.ToString();
        });

    [McpServerTool(Name = "screenshot")]
    [Description("Captures the screen, one window, or a region and returns a PNG. The text block before the image gives origin, scale and size: screen_x = origin_x + image_x / scale (same for y), so you can turn a position in the image into click coordinates. Fails if the screen is locked or the session is disconnected.")]
    public IEnumerable<ContentBlock> Screenshot(
        [Description("What to capture: \"screen\" (all monitors), \"window:<handle>\" (handle from list_windows), or \"region:x,y,w,h\" in screen coordinates.")] string target = "screen",
        [Description("Optional. Downscale the image so it is at most this many pixels wide (>= 1). Scale in the text block reflects it.")] int? max_width = null)
        => ToolGuard.Run(log, "screenshot", () =>
        {
            RequireAvailable();
            if (max_width is < 1) throw new ToolInputException("max_width must be at least 1");
            var parsed = ParseTarget(target);
            var cap = desktop.Capture(parsed, max_width);
            var header = string.Create(Inv,
                $"origin=({cap.OriginX},{cap.OriginY}) scale={cap.Scale:0.####} size={cap.PixelWidth}x{cap.PixelHeight}; screen_x = origin_x + image_x / scale");
            return (IEnumerable<ContentBlock>)new ContentBlock[]
            {
                new TextContentBlock { Text = header },
                ImageContentBlock.FromBytes(cap.Png, "image/png"),
            };
        });

    [McpServerTool(Name = "click")]
    [Description("Moves the mouse to screen coordinates and clicks. Coordinates are virtual-screen pixels (see the screenshot text block for converting image positions).")]
    public string Click(
        [Description("Screen x coordinate in pixels.")] int x,
        [Description("Screen y coordinate in pixels.")] int y,
        [Description("Mouse button: left, right or middle.")] string button = "left",
        [Description("True for a double click.")] bool double_click = false)
        => ToolGuard.Run(log, "click", () =>
        {
            RequireAvailable();
            if (!Enum.TryParse<MouseButton>(button?.Trim(), ignoreCase: true, out var b) || !Enum.IsDefined(b))
                throw new ToolInputException($"Unknown button '{button}'; use left, right or middle");
            desktop.Click(x, y, b, double_click);
            return "Clicked";
        });

    [McpServerTool(Name = "type_text")]
    [Description("Types text into the focused window as keyboard input, character by character (Unicode is fine). Click or focus the target first.")]
    public string TypeText(
        [Description("The text to type.")] string text)
        => ToolGuard.Run(log, "type_text", () =>
        {
            RequireAvailable();
            desktop.TypeText(text ?? "");
            return "Typed";
        });

    [McpServerTool(Name = "send_keys")]
    [Description("Presses key chords in the focused window. Chords are separated by spaces; the parts of one chord are joined with +, case-insensitive. Modifiers: ctrl alt shift win. Keys: a-z, 0-9, f1-f24, enter tab esc backspace delete insert space up down left right home end pageup pagedown printscreen. Examples: \"ctrl+s\", \"alt+f4\", \"ctrl+a delete\".")]
    public string SendKeys(
        [Description("Space-separated chords, e.g. \"ctrl+shift+s enter\".")] string keys)
        => ToolGuard.Run(log, "send_keys", () =>
        {
            RequireAvailable();
            desktop.SendKeys(KeySpec.Parse(keys));
            return "Sent";
        });

    [McpServerTool(Name = "scroll")]
    [Description("Scrolls the mouse wheel at screen coordinates. Positive amount scrolls up, negative scrolls down; one unit is one wheel notch.")]
    public string Scroll(
        [Description("Screen x coordinate in pixels.")] int x,
        [Description("Screen y coordinate in pixels.")] int y,
        [Description("Wheel notches; positive up, negative down.")] int amount)
        => ToolGuard.Run(log, "scroll", () =>
        {
            RequireAvailable();
            desktop.Scroll(x, y, amount);
            return "Scrolled";
        });

    [McpServerTool(Name = "focus_window")]
    [Description("Brings a window to the foreground, restoring it if minimized. The handle comes from list_windows. Windows can refuse; the error says so.")]
    public string FocusWindow(
        [Description("Window handle from list_windows.")] long handle)
        => ToolGuard.Run(log, "focus_window", () =>
        {
            RequireAvailable();
            if (!desktop.FocusWindow(handle))
                throw new McpException($"Windows refused to bring {handle} to the foreground");
            return "Focused";
        });

    private void RequireAvailable()
    {
        var state = desktop.State();
        if (state != DesktopState.Available)
            throw new McpException($"The screen is locked or the session is disconnected ({state}); file and shell tools still work.");
    }

    private static CaptureTarget ParseTarget(string? target)
    {
        const string hint = "target must be \"screen\", \"window:<handle>\" or \"region:x,y,w,h\"";
        var t = (target ?? "").Trim();
        if (t.Equals("screen", StringComparison.OrdinalIgnoreCase)) return new ScreenTarget();
        if (t.StartsWith("window:", StringComparison.OrdinalIgnoreCase))
        {
            if (!long.TryParse(t["window:".Length..], NumberStyles.Integer, Inv, out var h))
                throw new ToolInputException($"{hint}; window handle must be a number");
            return new WindowTarget(h);
        }
        if (t.StartsWith("region:", StringComparison.OrdinalIgnoreCase))
        {
            var p = t["region:".Length..].Split(',');
            if (p.Length != 4 || !TryInts(p, out var v)) throw new ToolInputException($"{hint}; region needs four integers");
            if (v[2] < 1 || v[3] < 1) throw new ToolInputException("target region width and height must be at least 1");
            return new RegionTarget(v[0], v[1], v[2], v[3]);
        }
        throw new ToolInputException(hint);
    }

    private static bool TryInts(string[] parts, out int[] values)
    {
        values = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
            if (!int.TryParse(parts[i].Trim(), NumberStyles.Integer, Inv, out values[i])) return false;
        return true;
    }
}
