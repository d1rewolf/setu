using System.Runtime.InteropServices;

namespace Setu.Mac.Native;

internal static partial class ApplicationServices
{
    private const string Lib = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";

    /// <summary>Whether this process has the Accessibility grant.</summary>
    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool AXIsProcessTrusted();
}
