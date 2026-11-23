using System.Text;
using ModelContextProtocol;
using Setu.Core.Screen;
using Setu.Mac.Native;
using static Setu.Mac.Native.ApplicationServices;
using static Setu.Mac.Native.CoreFoundation;
using static Setu.Mac.Native.CoreGraphics;

namespace Setu.Mac;

/// <summary>
/// IMacSystem over CoreGraphics, CoreFoundation, ApplicationServices, Carbon, IOKit and libproc.
/// Only ever constructed on macOS.
/// </summary>
internal sealed unsafe class MacSystem : IMacSystem
{
    private static readonly nuint StringType = CFStringGetTypeID();
    private static readonly nuint NumberType = CFNumberGetTypeID();
    private static readonly nuint BooleanType = CFBooleanGetTypeID();
    private static readonly nuint DictionaryType = CFDictionaryGetTypeID();
    private static readonly nuint ArrayType = CFArrayGetTypeID();
    private static readonly nint CfTrue = Constant("kCFBooleanTrue");
    private static readonly nint CfFalse = Constant("kCFBooleanFalse");
    private static readonly TimeSpan EventGap = TimeSpan.FromMilliseconds(10);

    public int OwnPid { get; } = Environment.ProcessId;

    // The string values of kCGSessionOnConsoleKey and of the (undocumented) screen-locked key.
    public bool OnConsole() => SessionFlag("kCGSSessionOnConsoleKey");

    /// <summary>The key is present only while the screen is locked.</summary>
    public bool ScreenLocked() => SessionFlag("CGSSessionScreenIsLocked");

    public bool ScreenRecordingPreflight() => CGPreflightScreenCaptureAccess();

    /// <summary>Posting events needs the Accessibility grant; focusing a window also needs AX, so both must hold.</summary>
    public bool PostEventAllowed() => CGPreflightPostEventAccess() && ApplicationServices.AXIsProcessTrusted();

    /// <summary>A missing session dictionary (no GUI session) reads as false, which means "not on console".</summary>
    private static bool SessionFlag(string key)
    {
        var session = CGSessionCopyCurrentDictionary();
        if (session == 0) return false;
        try
        {
            using var k = new CfString(key);
            return ReadBool(session, k.Ref) ?? false;
        }
        finally
        {
            CFRelease(session);
        }
    }

    public IReadOnlyList<MacWindow> Windows(bool onScreenOnly)
    {
        var option = (onScreenOnly ? kCGWindowListOptionOnScreenOnly : kCGWindowListOptionAll) | kCGWindowListExcludeDesktopElements;
        var array = CGWindowListCopyWindowInfo(option, kCGNullWindowID);
        // Null without a window server; nothing to list then.
        if (array == 0) return [];
        try
        {
            using var number = new CfString("kCGWindowNumber");
            using var pid = new CfString("kCGWindowOwnerPID");
            using var owner = new CfString("kCGWindowOwnerName");
            using var name = new CfString("kCGWindowName");
            using var layer = new CfString("kCGWindowLayer");
            using var bounds = new CfString("kCGWindowBounds");
            using var onScreen = new CfString("kCGWindowIsOnscreen");

            var count = CFArrayGetCount(array);
            var result = new List<MacWindow>((int)count);
            for (nint i = 0; i < count; i++)
            {
                var w = CFArrayGetValueAtIndex(array, i);
                if (w == 0 || CFGetTypeID(w) != DictionaryType) continue;
                if (ReadLong(w, number.Ref) is not { } id) continue;
                var rect = ReadRect(w, bounds.Ref);
                result.Add(new MacWindow(
                    (uint)id,
                    (int)(ReadLong(w, pid.Ref) ?? 0),
                    ReadString(w, owner.Ref) ?? "?",
                    ReadString(w, name.Ref),
                    (int)(ReadLong(w, layer.Ref) ?? 0),
                    rect.X, rect.Y, rect.Width, rect.Height,
                    ReadBool(w, onScreen.Ref) ?? false));
            }
            return result;
        }
        finally
        {
            CFRelease(array);
        }
    }

    public (double X, double Y, double W, double H) ScreenUnion()
    {
        uint count;
        var err = CGGetActiveDisplayList(0, null, &count);
        if (err != 0 || count == 0) throw new McpException($"Could not list the displays (CGError {err}, {count} displays)");
        var ids = new uint[count];
        fixed (uint* p = ids) err = CGGetActiveDisplayList(count, p, &count);
        if (err != 0 || count == 0) throw new McpException($"Could not list the displays (CGError {err}, {count} displays)");

        double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
        for (var i = 0; i < count; i++)
        {
            var b = CGDisplayBounds(ids[i]);
            left = Math.Min(left, b.X);
            top = Math.Min(top, b.Y);
            right = Math.Max(right, b.X + b.Width);
            bottom = Math.Max(bottom, b.Y + b.Height);
        }
        return (left, top, right - left, bottom - top);
    }

    public bool SecureInputOn() => Carbon.IsSecureEventInputEnabled();

    /// <summary>
    /// The kCGSSessionSecureInputPID entry of a session in the IORegistry root's IOConsoleUsers property, named
    /// with proc_name. Null when no session has the entry or the process has gone.
    /// </summary>
    public (int Pid, string Name)? SecureInputHolder()
    {
        var root = IOKit.IORegistryGetRootEntry(IOKit.kIOMainPortDefault);
        if (root == 0) return null;
        nint users = 0;
        try
        {
            using var usersKey = new CfString("IOConsoleUsers");
            users = IOKit.IORegistryEntryCreateCFProperty(root, usersKey.Ref, 0, 0);
            if (users == 0 || CFGetTypeID(users) != ArrayType) return null;

            using var pidKey = new CfString("kCGSSessionSecureInputPID");
            var count = CFArrayGetCount(users);
            for (nint i = 0; i < count; i++)
            {
                var session = CFArrayGetValueAtIndex(users, i);
                if (session == 0 || CFGetTypeID(session) != DictionaryType) continue;
                if (ReadLong(session, pidKey.Ref) is not { } pid || pid <= 0 || pid > int.MaxValue) continue;
                return ProcessName((int)pid) is { } name ? ((int)pid, name) : null;
            }
            return null;
        }
        finally
        {
            if (users != 0) CFRelease(users);
            _ = IOKit.IOObjectRelease(root);
        }
    }

    private static string? ProcessName(int pid)
    {
        const int size = 256;
        var buffer = stackalloc byte[size];
        var length = Libc.proc_name(pid, buffer, size);
        return length > 0 ? Encoding.UTF8.GetString(buffer, Math.Min(length, size)) : null;
    }

    public void Post(IReadOnlyList<MacEvent> events)
    {
        for (var i = 0; i < events.Count; i++)
        {
            if (i > 0) Thread.Sleep(EventGap);
            var ev = Create(events[i]);
            if (ev == 0) throw new McpException($"macOS could not create the input event ({events[i].GetType().Name})");
            try
            {
                CGEventPost(kCGHIDEventTap, ev);
            }
            finally
            {
                CFRelease(ev);
            }
        }
    }

    /// <summary>Create: the caller releases the result. Null when CoreGraphics refuses.</summary>
    private static nint Create(MacEvent e)
    {
        switch (e)
        {
            case MouseMoveEvent m:
                return CGEventCreateMouseEvent(0, kCGEventMouseMoved, new CGPoint(m.X, m.Y), kCGMouseButtonLeft);
            case MouseButtonEvent b:
            {
                var (type, button) = (b.Button, b.Down) switch
                {
                    (MouseButton.Left, true) => (kCGEventLeftMouseDown, kCGMouseButtonLeft),
                    (MouseButton.Left, false) => (kCGEventLeftMouseUp, kCGMouseButtonLeft),
                    (MouseButton.Right, true) => (kCGEventRightMouseDown, kCGMouseButtonRight),
                    (MouseButton.Right, false) => (kCGEventRightMouseUp, kCGMouseButtonRight),
                    (MouseButton.Middle, true) => (kCGEventOtherMouseDown, kCGMouseButtonCenter),
                    (MouseButton.Middle, false) => (kCGEventOtherMouseUp, kCGMouseButtonCenter),
                    _ => throw new ArgumentOutOfRangeException(nameof(e), b.Button, "unknown mouse button"),
                };
                var ev = CGEventCreateMouseEvent(0, type, new CGPoint(b.X, b.Y), button);
                if (ev != 0) CGEventSetIntegerValueField(ev, kCGMouseEventClickState, b.ClickState);
                return ev;
            }
            case ScrollEvent s:
                // wheel1 > 0 scrolls up (content moves down), the same direction as a positive Windows wheel delta.
                return CGEventCreateScrollWheelEvent2(0, kCGScrollEventUnitLine, 1, s.Lines, 0, 0);
            case KeyEvent k:
            {
                var ev = CGEventCreateKeyboardEvent(0, k.KeyCode, k.Down);
                if (ev != 0) CGEventSetFlags(ev, k.Flags);
                return ev;
            }
            case UnicodeEvent u:
            {
                // The key code is ignored by apps when the event carries a string; flags are cleared so a
                // modifier held at the Mac does not turn the text into shortcuts.
                var ev = CGEventCreateKeyboardEvent(0, 0, u.Down);
                if (ev == 0) return 0;
                CGEventSetFlags(ev, 0);
                fixed (char* p = u.Text) CGEventKeyboardSetUnicodeString(ev, (nuint)u.Text.Length, p);
                return ev;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(e), e.GetType().Name, "unknown event");
        }
    }

    public void Raise(MacWindow window)
    {
        var app = AXUIElementCreateApplication(window.Pid);
        if (app == 0) return;
        nint windows = 0;
        try
        {
            // A hung app would otherwise hold every AX call for the default 6 s.
            _ = AXUIElementSetMessagingTimeout(app, 1.0f);
            using var frontmost = new CfString("AXFrontmost");
            _ = AXUIElementSetAttributeValue(app, frontmost.Ref, CfTrue);

            using var windowsKey = new CfString("AXWindows");
            if (AXUIElementCopyAttributeValue(app, windowsKey.Ref, &windows) != 0) windows = 0;
            if (windows == 0 || CFGetTypeID(windows) != ArrayType) return;

            var element = FindAxWindow(windows, window);
            if (element == 0) return;

            using var minimized = new CfString("AXMinimized");
            using var raise = new CfString("AXRaise");
            using var main = new CfString("AXMain");
            _ = AXUIElementSetAttributeValue(element, minimized.Ref, CfFalse);
            _ = AXUIElementPerformAction(element, raise.Ref);
            _ = AXUIElementSetAttributeValue(element, main.Ref, CfTrue);
        }
        finally
        {
            if (windows != 0) CFRelease(windows);
            CFRelease(app);
        }
    }

    /// <summary>
    /// The element of <paramref name="windows"/> (an AXWindows array; elements are borrowed from it) that is
    /// <paramref name="target"/>: by CGWindowID through _AXUIElementGetWindow, or when that is not exported,
    /// by title and frame. 0 when none matches.
    /// </summary>
    private static nint FindAxWindow(nint windows, MacWindow target)
    {
        var count = CFArrayGetCount(windows);
        for (nint i = 0; i < count; i++)
        {
            var element = CFArrayGetValueAtIndex(windows, i);
            if (element == 0) continue;
            if (GetWindowId != null)
            {
                uint id;
                if (GetWindowId(element, &id) == 0 && id == target.Id) return element;
            }
            else if (SameTitleAndFrame(element, target))
            {
                return element;
            }
        }
        return 0;
    }

    private static bool SameTitleAndFrame(nint element, MacWindow target)
    {
        using var titleKey = new CfString("AXTitle");
        using var positionKey = new CfString("AXPosition");
        using var sizeKey = new CfString("AXSize");

        var title = CopyAttribute(element, titleKey.Ref);
        try
        {
            if (StringValue(title) != target.Name) return false;
        }
        finally
        {
            if (title != 0) CFRelease(title);
        }

        CGPoint position = default;
        CGSize size = default;
        if (!ReadAxValue(element, positionKey.Ref, kAXValueCGPointType, &position)) return false;
        if (!ReadAxValue(element, sizeKey.Ref, kAXValueCGSizeType, &size)) return false;
        return Math.Abs(position.X - target.X) < 1 && Math.Abs(position.Y - target.Y) < 1
            && Math.Abs(size.Width - target.W) < 1 && Math.Abs(size.Height - target.H) < 1;
    }

    /// <summary>Copy: release a non-zero result.</summary>
    private static nint CopyAttribute(nint element, nint attribute)
    {
        nint value;
        return AXUIElementCopyAttributeValue(element, attribute, &value) == 0 ? value : 0;
    }

    private static bool ReadAxValue(nint element, nint attribute, uint type, void* result)
    {
        var value = CopyAttribute(element, attribute);
        if (value == 0) return false;
        try
        {
            return AXValueGetValue(value, type, result);
        }
        finally
        {
            CFRelease(value);
        }
    }

    private static bool? ReadBool(nint dict, nint key)
    {
        var v = CFDictionaryGetValue(dict, key);
        if (v == 0) return null;
        var type = CFGetTypeID(v);
        if (type == BooleanType) return CFBooleanGetValue(v);
        if (type == NumberType) return ReadNumber(v) is { } n ? n != 0 : null;
        return null;
    }

    private static long? ReadLong(nint dict, nint key)
    {
        var v = CFDictionaryGetValue(dict, key);
        return v != 0 && CFGetTypeID(v) == NumberType ? ReadNumber(v) : null;
    }

    private static long? ReadNumber(nint number)
    {
        long value;
        return CFNumberGetValue(number, kCFNumberSInt64Type, &value) ? value : null;
    }

    private static string? ReadString(nint dict, nint key) => StringValue(CFDictionaryGetValue(dict, key));

    /// <summary>The text of a CFString, or null when <paramref name="v"/> is null or not a CFString.</summary>
    private static string? StringValue(nint v)
    {
        if (v == 0 || CFGetTypeID(v) != StringType) return null;
        var length = CFStringGetLength(v);
        if (length <= 0) return "";
        // UTF-16 straight out of the CFString: no encoding step and no size guess, whatever the title's length.
        var buffer = new char[length];
        fixed (char* p = buffer) CFStringGetCharacters(v, new CFRange(0, length), p);
        return new string(buffer);
    }

    private static CGRect ReadRect(nint dict, nint key)
    {
        var v = CFDictionaryGetValue(dict, key);
        CGRect rect = default;
        if (v != 0 && CFGetTypeID(v) == DictionaryType && CGRectMakeWithDictionaryRepresentation(v, &rect)) return rect;
        return default;
    }

    /// <summary>A CFString created for use as a dictionary key, released on Dispose.</summary>
    private readonly struct CfString : IDisposable
    {
        public nint Ref { get; }

        public CfString(string value)
        {
            Ref = CFStringCreateWithCString(0, value, kCFStringEncodingUTF8);
            if (Ref == 0) throw new McpException($"Could not create the CFString '{value}'");
        }

        public void Dispose() => CFRelease(Ref);
    }
}
