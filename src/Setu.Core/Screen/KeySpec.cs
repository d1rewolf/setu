namespace Setu.Core.Screen;

/// <summary>The modifiers and keys one platform's send_keys accepts.</summary>
/// <param name="Name">The platform's name as agents read it, e.g. in focus_window's "macOS refused to bring ...".</param>
/// <param name="Modifiers">Alias to canonical modifier name.</param>
/// <param name="Rejected">Name (modifier or key) to the hint shown when an agent uses it.</param>
public sealed record KeyVocabulary(
    string Name,
    IReadOnlyDictionary<string, string> Modifiers,
    IReadOnlySet<string> Keys,
    IReadOnlyDictionary<string, string> Rejected)
{
    private static readonly string[] NamedKeys =
    [
        "enter", "tab", "esc", "backspace", "delete", "insert", "space",
        "up", "down", "left", "right", "home", "end", "pageup", "pagedown", "printscreen",
    ];

    private static readonly string[] NotOnMac = ["insert", "printscreen"];

    /// <summary>macOS key codes stop at F20 (kVK_F20).</summary>
    private const int MacMaxFunctionKey = 20;

    public static KeyVocabulary Windows { get; } = new(
        "Windows",
        new Dictionary<string, string> { ["ctrl"] = "ctrl", ["alt"] = "alt", ["shift"] = "shift", ["win"] = "win" },
        BuildKeys(NamedKeys, maxFunctionKey: 24),
        new Dictionary<string, string>());

    public static KeyVocabulary Mac { get; } = new(
        "macOS",
        new Dictionary<string, string>
        {
            ["cmd"] = "cmd", ["command"] = "cmd", ["ctrl"] = "ctrl",
            ["option"] = "option", ["alt"] = "option", ["shift"] = "shift",
        },
        BuildKeys(NamedKeys.Except(NotOnMac), MacMaxFunctionKey),
        new Dictionary<string, string>
        {
            ["win"] = "use cmd on macOS",
            ["insert"] = "not on a Mac keyboard",
            ["printscreen"] = "not on a Mac keyboard",
            ["f21"] = "not on a Mac keyboard",
            ["f22"] = "not on a Mac keyboard",
            ["f23"] = "not on a Mac keyboard",
            ["f24"] = "not on a Mac keyboard",
        });

    private static HashSet<string> BuildKeys(IEnumerable<string> named, int maxFunctionKey)
    {
        var set = new HashSet<string>(named);
        for (var c = 'a'; c <= 'z'; c++) set.Add(c.ToString());
        for (var c = '0'; c <= '9'; c++) set.Add(c.ToString());
        for (var i = 1; i <= maxFunctionKey; i++) set.Add($"f{i}");
        return set;
    }

    /// <summary>The highest fN key in <see cref="Keys"/>; both vocabularies have f1 up to it with no gaps.</summary>
    private int MaxFunctionKey()
    {
        var n = 0;
        while (Keys.Contains($"f{n + 1}")) n++;
        return n;
    }

    internal string KnownList() =>
        $"modifiers: {string.Join(" ", Modifiers.Values.Distinct())}; keys: a-z 0-9 f1-f{MaxFunctionKey()} {string.Join(" ", NamedKeys.Where(Keys.Contains))}";
}

public static class KeySpec
{
    public static IReadOnlyList<KeyChord> Parse(string keys, KeyVocabulary vocabulary)
    {
        var tokens = (keys ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) throw new ToolInputException($"keys is empty; known keys: {vocabulary.KnownList()}");

        var chords = new List<KeyChord>();
        foreach (var token in tokens)
        {
            var parts = token.ToLowerInvariant().Split('+');
            var mods = new List<string>();
            for (var i = 0; i < parts.Length - 1; i++)
            {
                if (!vocabulary.Modifiers.TryGetValue(parts[i], out var canonical)) throw Unknown(vocabulary, parts[i]);
                mods.Add(canonical);
            }
            var key = parts[^1];
            if (!vocabulary.Keys.Contains(key)) throw Unknown(vocabulary, key);
            chords.Add(new KeyChord(mods, key));
        }
        return chords;
    }

    private static ToolInputException Unknown(KeyVocabulary vocabulary, string k)
    {
        var hint = vocabulary.Rejected.TryGetValue(k, out var h) ? $" ({h})" : "";
        return new($"Unknown key '{k}'{hint}; known keys: {vocabulary.KnownList()}");
    }
}
