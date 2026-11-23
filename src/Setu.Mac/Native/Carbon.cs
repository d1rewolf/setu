using System.Runtime.InteropServices;

namespace Setu.Mac.Native;

internal static partial class Carbon
{
    private const string Lib = "/System/Library/Frameworks/Carbon.framework/Carbon";

    /// <summary>Some process (a password field, Terminal's Secure Keyboard Entry, the lock screen) holds secure input.</summary>
    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool IsSecureEventInputEnabled();
}
