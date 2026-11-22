using System.Diagnostics;

namespace WindowsMcp.Core.Shell;

/// <summary>Kills the attached process and all its descendants on KillAll. Dispose kills nothing.</summary>
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
        }
    }

    public void Dispose()
    {
        lock (_processes) _processes.Clear();
    }
}
