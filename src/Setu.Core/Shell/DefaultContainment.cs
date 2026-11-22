using System.Diagnostics;

namespace Setu.Core.Shell;

/// <summary>Kills the attached processes and all their descendants on KillAll (best effort, never throws for
/// processes that cannot be killed). Dispose kills nothing.</summary>
public sealed class DefaultContainment : IProcessContainment
{
    private readonly List<Process> _processes = [];

    public void Attach(Process p)
    {
        lock (_processes) _processes.Add(p);
    }

    public void KillAll()
    {
        Process[] snapshot;
        lock (_processes) snapshot = [.. _processes];
        foreach (var p in snapshot)
        {
            try
            {
                p.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Already exited.
            }
            catch (Exception ex) when (ex is AggregateException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                // Best effort: part of the tree could not be killed (access denied, exiting, protected).
                // Keep going so the remaining attached processes are still killed.
            }
        }
    }

    public void Dispose()
    {
        lock (_processes) _processes.Clear();
    }
}
