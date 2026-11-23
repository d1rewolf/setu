using System.Runtime.InteropServices;

namespace Setu.Mac.Native;

/// <summary>libSystem; libproc's functions live in it.</summary>
internal static unsafe partial class Libc
{
    private const string Lib = "/usr/lib/libSystem.B.dylib";

    /// <summary>Writes the process's short name (no terminator guaranteed) and returns its length; 0 on failure.</summary>
    [LibraryImport(Lib)]
    internal static partial int proc_name(int pid, byte* buffer, uint bufferSize);
}
