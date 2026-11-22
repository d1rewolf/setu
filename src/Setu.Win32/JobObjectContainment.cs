using System.ComponentModel;
using System.Diagnostics;
using Setu.Core.Shell;

namespace Setu.Win32;

/// <summary>
/// Puts a command's process into a fresh job object so a timeout kills the whole tree, including children
/// started after the main process exits. The job has no KILL_ON_JOB_CLOSE: Dispose closes the handle and
/// leaves programs the command launched running. Children inherit the job as soon as the process is
/// assigned; anything it starts in the moment between Process.Start and Attach is not contained (accepted).
/// </summary>
public sealed class JobObjectContainment : IProcessContainment
{
    private readonly SafeJobHandle _job;
    // Processes that could not be assigned to the job; KillAll falls back to killing their trees.
    private readonly List<Process> _unassigned = [];
    private bool _disposed;

    public JobObjectContainment()
    {
        _job = NativeMethods.CreateJobObject(0, null);
        if (_job.IsInvalid)
        {
            var error = new Win32Exception();
            _job.Dispose();
            throw new Win32Exception(error.NativeErrorCode, $"Could not create a job object: {error.Message}");
        }
    }

    public void Attach(Process p)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (NativeMethods.AssignProcessToJobObject(_job, p.SafeHandle)) return;

        // A fast command may already have exited (assignment then fails); that needs no containment.
        // Otherwise keep the process so KillAll can still kill its tree the slow way.
        if (p.HasExited) return;
        lock (_unassigned) _unassigned.Add(p);
    }

    public void KillAll()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var terminated = NativeMethods.TerminateJobObject(_job, 1);
        var error = terminated ? null : new Win32Exception();

        Process[] fallback;
        lock (_unassigned) fallback = [.. _unassigned];
        foreach (var p in fallback)
        {
            try
            {
                p.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or AggregateException or Win32Exception or NotSupportedException)
            {
                // Best effort: already exited, or part of the tree could not be killed.
            }
        }

        if (error is not null)
            throw new Win32Exception(error.NativeErrorCode, $"Could not terminate the command's job object: {error.Message}");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_unassigned) _unassigned.Clear();
        _job.Dispose();
    }
}
