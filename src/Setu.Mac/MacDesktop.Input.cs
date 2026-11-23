using Setu.Core.Screen;

namespace Setu.Mac;

public sealed partial class MacDesktop
{
    /// <summary>Moves the pointer first so the app sees it hover, then presses and releases (twice for a double click).</summary>
    public void Click(int x, int y, MouseButton button, bool doubleClick)
    {
        var events = new List<MacEvent>
        {
            new MouseMoveEvent(x, y),
            new MouseButtonEvent(button, Down: true, x, y, ClickState: 1),
            new MouseButtonEvent(button, Down: false, x, y, ClickState: 1),
        };
        if (doubleClick)
        {
            events.Add(new MouseButtonEvent(button, Down: true, x, y, ClickState: 2));
            events.Add(new MouseButtonEvent(button, Down: false, x, y, ClickState: 2));
        }
        sys.Post(events);
    }

    /// <summary>One down and one up event per chunk of at most 20 UTF-16 units.</summary>
    public void TypeText(string text)
    {
        var events = new List<MacEvent>();
        foreach (var chunk in TextChunker.Chunks(text))
        {
            events.Add(new UnicodeEvent(chunk, Down: true));
            events.Add(new UnicodeEvent(chunk, Down: false));
        }
        if (events.Count > 0) sys.Post(events);
    }

    /// <summary>
    /// Each chord is the key's down and up events with the modifier flags set on both; the modifier keys
    /// themselves are not pressed. Key codes follow the US layout's physical positions (see MacKeyCodes).
    /// </summary>
    public void SendKeys(IReadOnlyList<KeyChord> chords)
    {
        var events = new List<MacEvent>();
        foreach (var chord in chords)
        {
            var code = MacKeyCodes.Key(chord.Key);
            var flags = MacKeyCodes.Flags(chord.Modifiers);
            events.Add(new KeyEvent(code, Down: true, flags));
            events.Add(new KeyEvent(code, Down: false, flags));
        }
        if (events.Count > 0) sys.Post(events);
    }

    /// <summary>Moves the pointer to (x, y), then scrolls <paramref name="amount"/> lines; positive is up, as on Windows.</summary>
    public void Scroll(int x, int y, int amount) =>
        sys.Post([new MouseMoveEvent(x, y), new ScrollEvent(amount)]);
}
