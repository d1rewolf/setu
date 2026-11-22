using System.ComponentModel;

namespace WindowsMcp.Win32;

/// <summary>
/// Keeps the machine from sleeping while the server runs. SetThreadExecutionState is per thread, so it is
/// called on a dedicated background thread that never exits; the state ends with the process.
/// The display is still allowed to turn off.
/// </summary>
public static class KeepAwake
{
    private static readonly Lock Gate = new();
    private static bool _started;

    /// <summary>Idempotent. Throws Win32Exception if Windows rejects the request.</summary>
    public static void Start()
    {
        lock (Gate)
        {
            if (_started) return;

            int? error = null;
            using var ready = new ManualResetEventSlim();
            var thread = new Thread(() =>
            {
                var previous = NativeMethods.SetThreadExecutionState(
                    NativeMethods.ES_CONTINUOUS | NativeMethods.ES_SYSTEM_REQUIRED);
                if (previous == 0) error = System.Runtime.InteropServices.Marshal.GetLastPInvokeError();
                var failed = previous == 0;
                ready.Set();
                if (failed) return;
                Thread.Sleep(Timeout.Infinite);
            })
            {
                IsBackground = true,
                Name = "windows-mcp keep-awake",
            };
            thread.Start();
            ready.Wait();

            if (error is { } code) throw new Win32Exception(code, "Could not keep the machine awake (SetThreadExecutionState failed)");
            _started = true;
        }
    }
}
