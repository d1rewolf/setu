using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Setu.Core.Shell;

/// <summary>
/// For commands that start as the leader of their own process group (see <see cref="CommandLines.Zsh"/>):
/// KillAll sends SIGKILL to the whole group, which also reaches children reparented to launchd that
/// Kill(entireProcessTree) cannot find, then kills the tree as a fallback. Best effort, never throws for a
/// group or process that is already gone. Dispose kills nothing.
/// </summary>
public sealed class ProcessGroupContainment : IProcessContainment
{
    private const int SigKill = 9;

    private readonly object _lock = new();
    private Process? _process;
    private int _pgid;

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int Kill(int pid, int sig);

    /// <summary>The process must be a group leader: its PID is taken as the process group ID.</summary>
    public void Attach(Process p)
    {
        lock (_lock)
        {
            _process = p;
            _pgid = p.Id;
        }
    }

    public void KillAll()
    {
        Process? process;
        int pgid;
        lock (_lock)
        {
            process = _process;
            pgid = _pgid;
        }
        if (process is null || pgid <= 0) return;

        // The result is ignored on purpose: ESRCH just means the group is already gone, and for any other
        // failure the tree kill below still does what it can.
        _ = Kill(-pgid, SigKill);

        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
        catch (Exception ex) when (ex is AggregateException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // Best effort: part of the tree could not be killed.
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _process = null;
            _pgid = 0;
        }
    }
}
