namespace Setu.Mac;

/// <summary>
/// send_keys names to macOS virtual key codes (the kVK_* values of HIToolbox/Events.h) and modifier names to
/// CGEventFlags. Letter and digit codes are the US ANSI layout's (kVK_ANSI_*): they name physical key positions,
/// which is what macOS shortcuts match on. On another layout "cmd+z" presses the key where Z is on a US keyboard.
/// </summary>
internal static class MacKeyCodes
{
    private const ulong kCGEventFlagMaskShift = 0x20000;
    private const ulong kCGEventFlagMaskControl = 0x40000;
    private const ulong kCGEventFlagMaskAlternate = 0x80000;
    private const ulong kCGEventFlagMaskCommand = 0x100000;

    private static readonly Dictionary<string, ushort> Codes = new()
    {
        ["a"] = 0x00, ["s"] = 0x01, ["d"] = 0x02, ["f"] = 0x03, ["h"] = 0x04, ["g"] = 0x05, ["z"] = 0x06,
        ["x"] = 0x07, ["c"] = 0x08, ["v"] = 0x09, ["b"] = 0x0B, ["q"] = 0x0C, ["w"] = 0x0D, ["e"] = 0x0E,
        ["r"] = 0x0F, ["y"] = 0x10, ["t"] = 0x11, ["o"] = 0x1F, ["u"] = 0x20, ["i"] = 0x22, ["p"] = 0x23,
        ["l"] = 0x25, ["j"] = 0x26, ["k"] = 0x28, ["n"] = 0x2D, ["m"] = 0x2E,

        ["1"] = 0x12, ["2"] = 0x13, ["3"] = 0x14, ["4"] = 0x15, ["6"] = 0x16, ["5"] = 0x17, ["9"] = 0x19,
        ["7"] = 0x1A, ["8"] = 0x1C, ["0"] = 0x1D,

        ["enter"] = 0x24,     // kVK_Return
        ["tab"] = 0x30,
        ["space"] = 0x31,
        ["backspace"] = 0x33, // kVK_Delete: the Mac's "delete" key, which deletes backwards
        ["esc"] = 0x35,
        ["delete"] = 0x75,    // kVK_ForwardDelete
        ["home"] = 0x73,
        ["pageup"] = 0x74,
        ["end"] = 0x77,
        ["pagedown"] = 0x79,
        ["left"] = 0x7B,
        ["right"] = 0x7C,
        ["down"] = 0x7D,
        ["up"] = 0x7E,

        ["f1"] = 0x7A, ["f2"] = 0x78, ["f3"] = 0x63, ["f4"] = 0x76, ["f5"] = 0x60, ["f6"] = 0x61, ["f7"] = 0x62,
        ["f8"] = 0x64, ["f9"] = 0x65, ["f10"] = 0x6D, ["f11"] = 0x67, ["f12"] = 0x6F, ["f13"] = 0x69,
        ["f14"] = 0x6B, ["f15"] = 0x71, ["f16"] = 0x6A, ["f17"] = 0x40, ["f18"] = 0x4F, ["f19"] = 0x50,
        ["f20"] = 0x5A,
    };

    /// <summary>The key code for a key name from the Mac key vocabulary. Throws for any other name.</summary>
    public static ushort Key(string key) =>
        Codes.TryGetValue(key, out var code) ? code : throw new ArgumentException($"No macOS key code for '{key}'", nameof(key));

    /// <summary>
    /// The modifier keys of a chord in press order (cmd, ctrl, option, shift), each once however often it is
    /// named, as (kVK_* key code, CGEventFlags bit). Throws for an unknown modifier name.
    /// </summary>
    public static IReadOnlyList<(ushort KeyCode, ulong Flag)> ModifierKeys(IReadOnlyList<string> modifiers)
    {
        var flags = Flags(modifiers);
        var keys = new List<(ushort, ulong)>();
        foreach (var (code, flag) in ModifierOrder)
            if ((flags & flag) != 0) keys.Add((code, flag));
        return keys;
    }

    private static readonly (ushort KeyCode, ulong Flag)[] ModifierOrder =
    [
        (0x37, kCGEventFlagMaskCommand),   // kVK_Command
        (0x3B, kCGEventFlagMaskControl),   // kVK_Control
        (0x3A, kCGEventFlagMaskAlternate), // kVK_Option
        (0x38, kCGEventFlagMaskShift),     // kVK_Shift
    ];

    /// <summary>The CGEventFlags for canonical Mac modifier names (cmd ctrl option shift). A repeated name counts once.</summary>
    public static ulong Flags(IReadOnlyList<string> modifiers)
    {
        ulong flags = 0;
        foreach (var m in modifiers)
        {
            flags |= m switch
            {
                "cmd" => kCGEventFlagMaskCommand,
                "shift" => kCGEventFlagMaskShift,
                "ctrl" => kCGEventFlagMaskControl,
                "option" => kCGEventFlagMaskAlternate,
                _ => throw new ArgumentException($"Unknown macOS modifier '{m}'", nameof(modifiers)),
            };
        }
        return flags;
    }
}
