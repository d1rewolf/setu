using System.Runtime.InteropServices;

namespace Setu.Mac.Native;

/// <summary>
/// CoreFoundation, as far as Setu reads it. Ownership follows the CF rule: a result of a function with Create or
/// Copy in its name must be released with CFRelease; a result of a Get function must not.
/// </summary>
internal static unsafe partial class CoreFoundation
{
    private const string Lib = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    internal const uint kCFStringEncodingUTF8 = 0x08000100;
    internal const nint kCFNumberSInt64Type = 4;

    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct CFRange(nint location, nint length)
    {
        public readonly nint Location = location;
        public readonly nint Length = length;
    }

    [LibraryImport(Lib)]
    internal static partial void CFRelease(nint cf);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint CFStringCreateWithCString(nint alloc, string cStr, uint encoding);

    [LibraryImport(Lib)]
    internal static partial nuint CFGetTypeID(nint cf);

    [LibraryImport(Lib)]
    internal static partial nuint CFStringGetTypeID();

    [LibraryImport(Lib)]
    internal static partial nuint CFNumberGetTypeID();

    [LibraryImport(Lib)]
    internal static partial nuint CFBooleanGetTypeID();

    [LibraryImport(Lib)]
    internal static partial nuint CFDictionaryGetTypeID();

    [LibraryImport(Lib)]
    internal static partial nint CFDictionaryGetValue(nint dict, nint key);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool CFNumberGetValue(nint number, nint type, long* value);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool CFBooleanGetValue(nint boolean);

    [LibraryImport(Lib)]
    internal static partial nint CFStringGetLength(nint str);

    [LibraryImport(Lib)]
    internal static partial void CFStringGetCharacters(nint str, CFRange range, char* buffer);

    [LibraryImport(Lib)]
    internal static partial nuint CFArrayGetTypeID();

    [LibraryImport(Lib)]
    internal static partial nint CFArrayGetCount(nint array);

    [LibraryImport(Lib)]
    internal static partial nint CFArrayGetValueAtIndex(nint array, nint index);

    /// <summary>
    /// The value of an exported CF constant such as kCFBooleanTrue. The export is the address of the variable
    /// that holds the reference, so it is read once more. Never released.
    /// </summary>
    internal static nint Constant(string name) =>
        *(nint*)NativeLibrary.GetExport(NativeLibrary.Load(Lib), name);
}
