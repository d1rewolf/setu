using System.Runtime.InteropServices;

namespace Setu.Mac.Native;

internal static unsafe partial class CoreGraphics
{
    private const string Lib = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    internal const uint kCGWindowListOptionAll = 0;
    internal const uint kCGWindowListOptionOnScreenOnly = 1 << 0;
    internal const uint kCGWindowListExcludeDesktopElements = 1 << 4;
    internal const uint kCGNullWindowID = 0;

    internal const uint kCGEventLeftMouseDown = 1;
    internal const uint kCGEventLeftMouseUp = 2;
    internal const uint kCGEventRightMouseDown = 3;
    internal const uint kCGEventRightMouseUp = 4;
    internal const uint kCGEventMouseMoved = 5;
    internal const uint kCGEventOtherMouseDown = 25;
    internal const uint kCGEventOtherMouseUp = 26;

    internal const uint kCGMouseButtonLeft = 0;
    internal const uint kCGMouseButtonRight = 1;
    internal const uint kCGMouseButtonCenter = 2;

    /// <summary>CGEventField kCGMouseEventClickState.</summary>
    internal const uint kCGMouseEventClickState = 1;
    internal const uint kCGScrollEventUnitLine = 1;
    internal const uint kCGHIDEventTap = 0;

    [StructLayout(LayoutKind.Sequential)]
    internal struct CGRect
    {
        public double X, Y, Width, Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CGPoint(double x, double y)
    {
        public double X = x, Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CGSize
    {
        public double Width, Height;
    }

    /// <summary>Copy: release the result. Null when there is no window-server session (for example over SSH).</summary>
    [LibraryImport(Lib)]
    internal static partial nint CGSessionCopyCurrentDictionary();

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool CGPreflightScreenCaptureAccess();

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool CGPreflightPostEventAccess();

    /// <summary>Copy: release the result. A CFArray of CFDictionary; front to back with OnScreenOnly.</summary>
    [LibraryImport(Lib)]
    internal static partial nint CGWindowListCopyWindowInfo(uint option, uint relativeToWindow);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool CGRectMakeWithDictionaryRepresentation(nint dict, CGRect* rect);

    /// <summary>Returns a CGError; 0 is success. With a null list it only counts.</summary>
    [LibraryImport(Lib)]
    internal static partial int CGGetActiveDisplayList(uint maxDisplays, uint* activeDisplays, uint* displayCount);

    [LibraryImport(Lib)]
    internal static partial CGRect CGDisplayBounds(uint display);

    /// <summary>Create: release the result. A null source means the default (combined session) state.</summary>
    [LibraryImport(Lib)]
    internal static partial nint CGEventCreateMouseEvent(nint source, uint mouseType, CGPoint mouseCursorPosition, uint mouseButton);

    /// <summary>Create: release the result. Not variadic, unlike CGEventCreateScrollWheelEvent; macOS 13 and later.</summary>
    [LibraryImport(Lib)]
    internal static partial nint CGEventCreateScrollWheelEvent2(nint source, uint units, uint wheelCount, int wheel1, int wheel2, int wheel3);

    /// <summary>Create: release the result.</summary>
    [LibraryImport(Lib)]
    internal static partial nint CGEventCreateKeyboardEvent(nint source, ushort virtualKey, [MarshalAs(UnmanagedType.U1)] bool keyDown);

    [LibraryImport(Lib)]
    internal static partial void CGEventSetFlags(nint ev, ulong flags);

    [LibraryImport(Lib)]
    internal static partial void CGEventSetIntegerValueField(nint ev, uint field, long value);

    /// <summary>Copies the characters; the buffer need not outlive the call.</summary>
    [LibraryImport(Lib)]
    internal static partial void CGEventKeyboardSetUnicodeString(nint ev, nuint stringLength, char* unicodeString);

    [LibraryImport(Lib)]
    internal static partial void CGEventPost(uint tap, nint ev);
}
