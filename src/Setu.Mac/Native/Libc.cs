using System.Runtime.InteropServices;

namespace Setu.Mac.Native;

/// <summary>libSystem; libproc's functions live in it.</summary>
internal static unsafe partial class Libc
{
    private const string Lib = "/usr/lib/libSystem.B.dylib";

    /// <summary>Writes the process's short name (no terminator guaranteed) and returns its length; 0 on failure.</summary>
    [LibraryImport(Lib)]
    internal static partial int proc_name(int pid, byte* buffer, uint bufferSize);

    // <spawn.h> and <sys/spawn.h> (Darwin). posix_spawnattr_t and posix_spawn_file_actions_t are opaque
    // pointer-sized handles (typedef void *), initialised and destroyed through these calls. Every function
    // returns 0 or an errno value; it does not set errno.

    internal const short POSIX_SPAWN_SETSIGDEF = 0x0004;
    internal const short POSIX_SPAWN_SETSIGMASK = 0x0008;
    internal const short POSIX_SPAWN_SETSID = 0x0400;
    internal const short POSIX_SPAWN_CLOEXEC_DEFAULT = 0x4000;

    internal const int O_RDONLY = 0x0000;
    internal const int O_RDWR = 0x0002;

    [LibraryImport(Lib)]
    internal static partial int posix_spawn(
        int* pid, byte* path, nint* fileActions, nint* attr, byte** argv, byte** envp);

    [LibraryImport(Lib)]
    internal static partial int posix_spawnattr_init(nint* attr);

    [LibraryImport(Lib)]
    internal static partial int posix_spawnattr_destroy(nint* attr);

    [LibraryImport(Lib)]
    internal static partial int posix_spawnattr_setflags(nint* attr, short flags);

    /// <summary>sigset_t is a 32-bit mask on Darwin; signal n is bit n - 1.</summary>
    [LibraryImport(Lib)]
    internal static partial int posix_spawnattr_setsigdefault(nint* attr, uint* sigdefault);

    [LibraryImport(Lib)]
    internal static partial int posix_spawnattr_setsigmask(nint* attr, uint* sigmask);

    [LibraryImport(Lib)]
    internal static partial int posix_spawn_file_actions_init(nint* fileActions);

    [LibraryImport(Lib)]
    internal static partial int posix_spawn_file_actions_destroy(nint* fileActions);

    /// <summary>mode_t is 16 bits on Darwin.</summary>
    [LibraryImport(Lib)]
    internal static partial int posix_spawn_file_actions_addopen(nint* fileActions, int fd, byte* path, int oflag, ushort mode);

    /// <summary>macOS 10.15 and later.</summary>
    [LibraryImport(Lib)]
    internal static partial int posix_spawn_file_actions_addchdir_np(nint* fileActions, byte* path);

    /// <summary>The address of this process's <c>environ</c> (char ***); a shared library cannot link the variable itself.</summary>
    [LibraryImport(Lib)]
    internal static partial byte*** _NSGetEnviron();

    internal const int EINTR = 4;

    [LibraryImport(Lib, SetLastError = true)]
    internal static partial int waitpid(int pid, int* status, int options);
}
