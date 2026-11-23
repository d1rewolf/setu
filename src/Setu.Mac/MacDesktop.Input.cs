using ModelContextProtocol;
using Setu.Core.Screen;

namespace Setu.Mac;

// PLACEHOLDERS: mouse, keyboard and focus arrive in Task 8, which replaces this whole file.
public sealed partial class MacDesktop
{
    private const string NotYet = "not implemented on macOS yet";

    public void Click(int x, int y, MouseButton button, bool doubleClick) => throw new McpException(NotYet);
    public void TypeText(string text) => throw new McpException(NotYet);
    public void SendKeys(IReadOnlyList<KeyChord> chords) => throw new McpException(NotYet);
    public void Scroll(int x, int y, int amount) => throw new McpException(NotYet);
    public bool FocusWindow(long handle) => throw new McpException(NotYet);
}
