using System.Runtime.InteropServices;

namespace Setu.Mac.Native;

internal static unsafe partial class CoreGraphics
{
    private const string Lib = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    internal const uint kCGWindowListOptionAll = 0;
    internal const uint kCGWindowListOptionOnScreenOnly = 1 << 0;
    internal const uint kCGWindowListExcludeDesktopElements = 1 << 4;
    internal const uint kCGNullWindowID = 0;

    [StructLayout(LayoutKind.Sequential)]
    internal struct CGRect
    {
        public double X, Y, Width, Height;
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
}
