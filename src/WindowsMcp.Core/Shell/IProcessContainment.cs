using System.Diagnostics;

namespace WindowsMcp.Core.Shell;

/// <summary>
/// Tracks the processes one command starts so a timeout can kill all of them.
/// Disposing must not kill anything: programs a command launches may outlive it.
/// </summary>
public interface IProcessContainment : IDisposable
{
    void Attach(Process p);

    void KillAll();
}
