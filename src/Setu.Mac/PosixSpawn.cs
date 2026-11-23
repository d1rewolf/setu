using System.Runtime.InteropServices;
using ModelContextProtocol;
using static Setu.Mac.Native.Libc;

namespace Setu.Mac;

/// <summary>
/// Starts a program detached from Setu with posix_spawn: its own session (POSIX_SPAWN_SETSID, so it is outside
/// Setu's process group and survives a restart), no inherited file descriptors (POSIX_SPAWN_CLOEXEC_DEFAULT)
/// except stdin, stdout and stderr on /dev/null, the default disposition for every signal (.NET ignores
/// SIGPIPE, and an ignored signal would otherwise stay ignored across exec) and an empty signal mask.
/// The environment is Setu's own. Only ever called on macOS.
/// </summary>
internal static unsafe class PosixSpawn
{
    private const int SigKill = 9, SigStop = 17;

    /// <param name="path">Absolute path of the program.</param>
    /// <param name="argv">The argument vector, argv[0] first.</param>
    /// <returns>The PID. A thread reaps the child when it exits, so it never lingers as a zombie.</returns>
    internal static int Spawn(string path, IReadOnlyList<string> argv, string? workingDirectory)
    {
        var strings = new List<nint>();
        byte* Utf8(string s)
        {
            var p = Marshal.StringToCoTaskMemUTF8(s);
            strings.Add(p);
            return (byte*)p;
        }

        nint attr = 0, actions = 0;
        bool attrReady = false, actionsReady = false;
        var args = (byte**)NativeMemory.AllocZeroed((nuint)(argv.Count + 1), (nuint)sizeof(nint));
        try
        {
            Check(posix_spawnattr_init(&attr), "posix_spawnattr_init");
            attrReady = true;
            Check(posix_spawnattr_setflags(&attr,
                (short)(POSIX_SPAWN_SETSID | POSIX_SPAWN_CLOEXEC_DEFAULT | POSIX_SPAWN_SETSIGDEF | POSIX_SPAWN_SETSIGMASK)),
                "posix_spawnattr_setflags");
            // Signals 1 to 31, except SIGKILL and SIGSTOP, whose disposition cannot be changed.
            uint defaults = ~0u & ~(1u << (SigKill - 1)) & ~(1u << (SigStop - 1)) & ~(1u << 31);
            uint mask = 0;
            Check(posix_spawnattr_setsigdefault(&attr, &defaults), "posix_spawnattr_setsigdefault");
            Check(posix_spawnattr_setsigmask(&attr, &mask), "posix_spawnattr_setsigmask");

            Check(posix_spawn_file_actions_init(&actions), "posix_spawn_file_actions_init");
            actionsReady = true;
            var devNull = Utf8("/dev/null");
            Check(posix_spawn_file_actions_addopen(&actions, 0, devNull, O_RDONLY, 0), "posix_spawn_file_actions_addopen");
            Check(posix_spawn_file_actions_addopen(&actions, 1, devNull, O_RDWR, 0), "posix_spawn_file_actions_addopen");
            Check(posix_spawn_file_actions_addopen(&actions, 2, devNull, O_RDWR, 0), "posix_spawn_file_actions_addopen");
            if (workingDirectory is not null)
                Check(posix_spawn_file_actions_addchdir_np(&actions, Utf8(workingDirectory)), "posix_spawn_file_actions_addchdir_np");

            for (var i = 0; i < argv.Count; i++) args[i] = Utf8(argv[i]);

            int pid;
            var error = posix_spawn(&pid, Utf8(path), &actions, &attr, args, *_NSGetEnviron());
            if (error != 0)
                throw new McpException($"Could not start {argv[0]}: {Marshal.GetPInvokeErrorMessage(error)}");
            Reap(pid);
            return pid;
        }
        finally
        {
            if (actionsReady) _ = posix_spawn_file_actions_destroy(&actions);
            if (attrReady) _ = posix_spawnattr_destroy(&attr);
            NativeMemory.Free(args);
            foreach (var p in strings) Marshal.FreeCoTaskMem(p);
        }
    }

    private static void Check(int error, string call)
    {
        if (error != 0) throw new InvalidOperationException($"{call} failed: {Marshal.GetPInvokeErrorMessage(error)}");
    }

    /// <summary>
    /// The child is Setu's child until Setu exits (then launchd adopts it). .NET reaps only the children it
    /// started itself, so this waits for it on a small background thread.
    /// </summary>
    private static void Reap(int pid)
    {
        var thread = new Thread(() =>
        {
            int status;
            while (waitpid(pid, &status, 0) == -1 && Marshal.GetLastPInvokeError() == EINTR) { }
        }, maxStackSize: 256 * 1024)
        {
            IsBackground = true,
            Name = $"reap {pid}",
        };
        thread.Start();
    }
}
