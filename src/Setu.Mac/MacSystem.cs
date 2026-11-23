using ModelContextProtocol;
using Setu.Mac.Native;
using static Setu.Mac.Native.CoreFoundation;
using static Setu.Mac.Native.CoreGraphics;

namespace Setu.Mac;

/// <summary>IMacSystem over CoreGraphics, CoreFoundation and ApplicationServices. Only ever constructed on macOS.</summary>
internal sealed unsafe class MacSystem : IMacSystem
{
    private static readonly nuint StringType = CFStringGetTypeID();
    private static readonly nuint NumberType = CFNumberGetTypeID();
    private static readonly nuint BooleanType = CFBooleanGetTypeID();
    private static readonly nuint DictionaryType = CFDictionaryGetTypeID();

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

    private static string? ReadString(nint dict, nint key)
    {
        var v = CFDictionaryGetValue(dict, key);
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
