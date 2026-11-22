namespace WindowsMcp.Core.Screen;

public static class KeySpec
{
    private static readonly string[] ModifierNames = ["ctrl", "alt", "shift", "win"];

    private static readonly string[] NamedKeys =
    [
        "enter", "tab", "esc", "backspace", "delete", "insert", "space",
        "up", "down", "left", "right", "home", "end", "pageup", "pagedown", "printscreen",
    ];

    private static readonly HashSet<string> Modifiers = new(ModifierNames);
    private static readonly HashSet<string> Keys = BuildKeys();

    private static HashSet<string> BuildKeys()
    {
        var set = new HashSet<string>(NamedKeys);
        for (var c = 'a'; c <= 'z'; c++) set.Add(c.ToString());
        for (var c = '0'; c <= '9'; c++) set.Add(c.ToString());
        for (var i = 1; i <= 24; i++) set.Add($"f{i}");
        return set;
    }

    private static string KnownList() =>
        $"modifiers: {string.Join(" ", ModifierNames)}; keys: a-z 0-9 f1-f24 {string.Join(" ", NamedKeys)}";

    public static IReadOnlyList<KeyChord> Parse(string keys)
    {
        var tokens = (keys ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) throw new ToolInputException($"keys is empty; known keys: {KnownList()}");

        var chords = new List<KeyChord>();
        foreach (var token in tokens)
        {
            var parts = token.ToLowerInvariant().Split('+');
            var mods = new List<string>();
            for (var i = 0; i < parts.Length - 1; i++)
            {
                if (!Modifiers.Contains(parts[i])) throw Unknown(parts[i]);
                mods.Add(parts[i]);
            }
            var key = parts[^1];
            if (!Keys.Contains(key)) throw Unknown(key);
            chords.Add(new KeyChord(mods, key));
        }
        return chords;
    }

    private static ToolInputException Unknown(string k) =>
        new($"Unknown key '{k}'; known keys: {KnownList()}");
}
