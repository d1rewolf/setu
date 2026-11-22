using ModelContextProtocol;
using Setu.Core;
using Setu.Core.Screen;
using static Setu.Win32.NativeMethods;

namespace Setu.Win32;

public sealed partial class Win32Desktop
{
    private const string InputBlocked = "Windows blocked the input (an elevated window may have focus)";

    private const ushort VK_RETURN = 0x0D;
    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_MENU = 0x12;
    private const ushort VK_LWIN = 0x5B;

    private static readonly Dictionary<string, ushort> ModifierKeys = new(StringComparer.Ordinal)
    {
        ["ctrl"] = VK_CONTROL,
        ["alt"] = VK_MENU,
        ["shift"] = VK_SHIFT,
        ["win"] = VK_LWIN,
    };

    // Exactly KeySpec's vocabulary: named keys, a-z, 0-9, f1-f24.
    private static readonly Dictionary<string, ushort> NamedKeys = BuildNamedKeys();

    // Keys in the navigation cluster (and the Windows key) need KEYEVENTF_EXTENDEDKEY, or with NumLock off
    // some programs read them as numeric keypad keys.
    private static readonly HashSet<ushort> ExtendedKeys =
    [
        0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, // pageup pagedown end home left up right down
        0x2C, 0x2D, 0x2E,                               // printscreen insert delete
        VK_LWIN,
    ];

    private static Dictionary<string, ushort> BuildNamedKeys()
    {
        var map = new Dictionary<string, ushort>(StringComparer.Ordinal)
        {
            ["enter"] = VK_RETURN,
            ["tab"] = 0x09,
            ["esc"] = 0x1B,
            ["backspace"] = 0x08,
            ["delete"] = 0x2E,
            ["insert"] = 0x2D,
            ["space"] = 0x20,
            ["up"] = 0x26,
            ["down"] = 0x28,
            ["left"] = 0x25,
            ["right"] = 0x27,
            ["home"] = 0x24,
            ["end"] = 0x23,
            ["pageup"] = 0x21,
            ["pagedown"] = 0x22,
            ["printscreen"] = 0x2C,
        };
        for (var c = 'a'; c <= 'z'; c++) map[c.ToString()] = (ushort)char.ToUpperInvariant(c); // VK_A..VK_Z = 'A'..'Z'
        for (var c = '0'; c <= '9'; c++) map[c.ToString()] = c;                                  // VK_0..VK_9 = '0'..'9'
        for (var i = 1; i <= 24; i++) map[$"f{i}"] = (ushort)(0x70 + i - 1);                     // VK_F1 = 0x70
        return map;
    }

    public void Click(int x, int y, MouseButton button, bool doubleClick)
    {
        var (down, up) = button switch
        {
            MouseButton.Left => (MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP),
            MouseButton.Right => (MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP),
            MouseButton.Middle => (MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_MIDDLEUP),
            _ => throw new ToolInputException($"Unknown mouse button {button}"),
        };
        var inputs = new List<INPUT> { MoveTo(x, y), MouseInput(down, 0), MouseInput(up, 0) };
        if (doubleClick)
        {
            inputs.Add(MouseInput(down, 0));
            inputs.Add(MouseInput(up, 0));
        }
        Send(inputs);
    }

    public void Scroll(int x, int y, int amount)
    {
        // Positive = wheel rotated away from the user = scroll up, as ScreenTools documents.
        var delta = (long)amount * WHEEL_DELTA;
        if (delta is > int.MaxValue or < int.MinValue)
            throw new ToolInputException($"amount {amount} is too large");
        Send([MoveTo(x, y), MouseInput(MOUSEEVENTF_WHEEL, unchecked((uint)(int)delta))]);
    }

    public void TypeText(string text)
    {
        var inputs = new List<INPUT>(text.Length * 2);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            // A Unicode '\n' arrives as WM_CHAR 0x0A, which many programs ignore or treat as Ctrl+Enter;
            // press Enter for each line break instead (CRLF counts once).
            if (c == '\r' || c == '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                inputs.Add(KeyInput(VK_RETURN, keyUp: false));
                inputs.Add(KeyInput(VK_RETURN, keyUp: true));
                continue;
            }
            inputs.Add(UnicodeInput(c, keyUp: false));
            inputs.Add(UnicodeInput(c, keyUp: true));
        }
        if (inputs.Count > 0) Send(inputs);
    }

    public void SendKeys(IReadOnlyList<KeyChord> chords)
    {
        var inputs = new List<INPUT>();
        foreach (var chord in chords)
        {
            var modifiers = chord.Modifiers.Select(m => Lookup(ModifierKeys, m)).ToList();
            var key = Lookup(NamedKeys, chord.Key);
            foreach (var m in modifiers) inputs.Add(KeyInput(m, keyUp: false));
            inputs.Add(KeyInput(key, keyUp: false));
            inputs.Add(KeyInput(key, keyUp: true));
            for (var i = modifiers.Count - 1; i >= 0; i--) inputs.Add(KeyInput(modifiers[i], keyUp: true));
        }
        if (inputs.Count > 0) Send(inputs);
    }

    private static ushort Lookup(Dictionary<string, ushort> map, string name) =>
        map.TryGetValue(name.ToLowerInvariant(), out var vk) ? vk : throw new ToolInputException($"Unknown key '{name}'");

    private static void Send(IReadOnlyList<INPUT> inputs)
    {
        if (!TrySend(inputs)) throw new McpException(InputBlocked);
    }

    private static unsafe bool TrySend(IReadOnlyList<INPUT> inputs)
    {
        var array = inputs as INPUT[] ?? [.. inputs];
        fixed (INPUT* p = array)
        {
            return SendInput((uint)array.Length, p, sizeof(INPUT)) == (uint)array.Length;
        }
    }

    /// <summary>
    /// An absolute move to a virtual-screen pixel. Normalised coordinates span 0..65535 over the virtual
    /// desktop and Windows maps them back as floor(n * size / 65536), so n is rounded up to land on the pixel.
    /// </summary>
    private static INPUT MoveTo(int x, int y)
    {
        var vx = GetSystemMetrics(SM_XVIRTUALSCREEN);
        var vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
        var vw = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        var vh = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        if (vw <= 0 || vh <= 0) throw new McpException("Could not read the screen size");
        if (x < vx || x >= vx + vw || y < vy || y >= vy + vh)
            throw new ToolInputException($"Point {x},{y} is outside the screen ({vx},{vy} {vw}x{vh})");
        return MouseInput(
            MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK, 0,
            Normalise(x - vx, vw), Normalise(y - vy, vh));
    }

    private static int Normalise(int offset, int size) =>
        (int)Math.Min(65535L, ((long)offset * 65536 + size - 1) / size);

    private static INPUT MouseInput(uint flags, uint data, int dx = 0, int dy = 0) => new()
    {
        type = INPUT_MOUSE,
        u = new InputUnion { mi = new MOUSEINPUT { dx = dx, dy = dy, mouseData = data, dwFlags = flags } },
    };

    private static INPUT KeyInput(ushort vk, bool keyUp)
    {
        var flags = keyUp ? KEYEVENTF_KEYUP : 0;
        if (ExtendedKeys.Contains(vk)) flags |= KEYEVENTF_EXTENDEDKEY;
        return new INPUT
        {
            type = INPUT_KEYBOARD,
            // The scan code is optional (0 when there is no mapping); programs that read scan codes need it.
            u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = (ushort)MapVirtualKey(vk, MAPVK_VK_TO_VSC), dwFlags = flags } },
        };
    }

    private static INPUT UnicodeInput(char c, bool keyUp) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion
        {
            ki = new KEYBDINPUT { wVk = 0, wScan = c, dwFlags = KEYEVENTF_UNICODE | (keyUp ? KEYEVENTF_KEYUP : 0) },
        },
    };
}
