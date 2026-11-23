using System.Runtime.InteropServices;

namespace Setu.Mac.Native;

/// <summary>The IORegistry, as far as Setu reads it. io_object_t handles are mach ports (uint), freed with IOObjectRelease.</summary>
internal static partial class IOKit
{
    private const string Lib = "/System/Library/Frameworks/IOKit.framework/IOKit";

    /// <summary>kIOMainPortDefault (MACH_PORT_NULL).</summary>
    internal const uint kIOMainPortDefault = 0;

    /// <summary>The root of the IOService plane; release with IOObjectRelease. 0 on failure.</summary>
    [LibraryImport(Lib)]
    internal static partial uint IORegistryGetRootEntry(uint mainPort);

    /// <summary>Create: CFRelease the result. Null when the entry has no such property.</summary>
    [LibraryImport(Lib)]
    internal static partial nint IORegistryEntryCreateCFProperty(uint entry, nint key, nint allocator, uint options);

    [LibraryImport(Lib)]
    internal static partial int IOObjectRelease(uint obj);
}
