using System.Runtime.InteropServices;

namespace Setu.Mac.Native;

/// <summary>Accessibility (HIServices, through the ApplicationServices umbrella). AXError results: 0 is success.</summary>
internal static unsafe partial class ApplicationServices
{
    private const string Lib = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
    private const string HIServicesLib =
        "/System/Library/Frameworks/ApplicationServices.framework/Frameworks/HIServices.framework/HIServices";

    internal const uint kAXValueCGPointType = 1;
    internal const uint kAXValueCGSizeType = 2;

    /// <summary>Whether this process has the Accessibility grant.</summary>
    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool AXIsProcessTrusted();

    /// <summary>Create: release the result.</summary>
    [LibraryImport(Lib)]
    internal static partial nint AXUIElementCreateApplication(int pid);

    /// <summary>How long calls on this element wait for the app before giving up, in seconds.</summary>
    [LibraryImport(Lib)]
    internal static partial int AXUIElementSetMessagingTimeout(nint element, float timeoutInSeconds);

    [LibraryImport(Lib)]
    internal static partial int AXUIElementSetAttributeValue(nint element, nint attribute, nint value);

    /// <summary>Copy: on success, release <paramref name="value"/>.</summary>
    [LibraryImport(Lib)]
    internal static partial int AXUIElementCopyAttributeValue(nint element, nint attribute, nint* value);

    [LibraryImport(Lib)]
    internal static partial int AXUIElementPerformAction(nint element, nint action);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool AXValueGetValue(nint value, uint type, void* valuePtr);

    /// <summary>
    /// The private but long-stable <c>_AXUIElementGetWindow(AXUIElementRef, CGWindowID*)</c>, or null when this
    /// macOS does not export it. Looked up at run time so a missing symbol is a fallback, not a load failure.
    /// </summary>
    internal static readonly delegate* unmanaged<nint, uint*, int> GetWindowId = FindGetWindow();

    private static delegate* unmanaged<nint, uint*, int> FindGetWindow()
    {
        foreach (var lib in new[] { Lib, HIServicesLib })
        {
            if (NativeLibrary.TryLoad(lib, out var handle) && NativeLibrary.TryGetExport(handle, "_AXUIElementGetWindow", out var address))
                return (delegate* unmanaged<nint, uint*, int>)address;
        }
        return null;
    }
}
