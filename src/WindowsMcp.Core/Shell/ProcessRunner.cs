using System.Diagnostics;
using System.Text;

namespace WindowsMcp.Core.Shell;

public sealed record ProcessRequest(string FileName, string Arguments, string? WorkingDirectory, TimeSpan Timeout);

public sealed record ProcessResult(
    int? ExitCode, string Stdout, string Stderr, bool TimedOut, bool StdoutTruncated, bool StderrTruncated, TimeSpan Elapsed);

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct);
}

/// <summary>
/// Runs a command to completion without ever hanging: stdin is closed at once, a timeout kills the
/// whole tree, and the result comes back shortly after the main process exits even if a background
/// child still holds the output pipes. Nothing is killed on a normal exit.
/// </summary>
public sealed class ProcessRunner(Func<IProcessContainment> containment) : IProcessRunner
{
    public const int MaxStreamChars = 64 * 1024;
    private static readonly TimeSpan DrainGrace = TimeSpan.FromSeconds(2);
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(request.FileName, request.Arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8,
            WorkingDirectory = request.WorkingDirectory ?? "",
        };

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        process.Exited += (_, _) => exited.TrySetResult();

        using var contained = containment();
        var sw = Stopwatch.StartNew();
        try
        {
            process.Start();
        }
        catch
        {
            process.Dispose();
            throw;
        }
        contained.Attach(process);

        // A prompt must see EOF instead of waiting for input that never comes.
        try { process.StandardInput.Close(); } catch (IOException) { }

        var stdout = new CappedBuffer();
        var stderr = new CappedBuffer();
        var pumps = Task.WhenAll(Pump(process.StandardOutput, stdout), Pump(process.StandardError, stderr));

        // Never WaitForExitAsync(): it also waits for the pipes to close, which a background
        // child can hold open indefinitely. The Exited event fires on main-process exit alone.
        if (process.HasExited) exited.TrySetResult();

        var timedOut = false;
        using (var waitCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            var timeout = Task.Delay(request.Timeout, waitCts.Token);
            var first = await Task.WhenAny(exited.Task, timeout).ConfigureAwait(false);
            waitCts.Cancel();
            if (first != exited.Task)
            {
                contained.KillAll();
                ct.ThrowIfCancellationRequested();
                timedOut = true;
            }
        }

        // Main process is gone (or killed): give the readers a short grace period, then return
        // whatever arrived. Readers that never finish keep draining in the background.
        await Task.WhenAny(pumps, Task.Delay(DrainGrace, CancellationToken.None)).ConfigureAwait(false);
        var (outText, outTrunc) = stdout.Freeze();
        var (errText, errTrunc) = stderr.Freeze();

        int? exitCode = null;
        if (!timedOut)
        {
            process.WaitForExit(0); // ensures ExitCode is populated
            exitCode = process.ExitCode;
        }

        // Dispose only once the pipes are closed, so a lingering child never gets a broken pipe.
        _ = pumps.ContinueWith(_ => process.Dispose(), TaskScheduler.Default);

        return new ProcessResult(exitCode, outText, errText, timedOut, outTrunc, errTrunc, sw.Elapsed);
    }

    private static async Task Pump(StreamReader reader, CappedBuffer buffer)
    {
        var chunk = new char[4096];
        try
        {
            int n;
            while ((n = await reader.ReadAsync(chunk.AsMemory()).ConfigureAwait(false)) > 0)
                buffer.Append(chunk.AsSpan(0, n));
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // Pipe torn down: keep what we have.
        }
    }

    private sealed class CappedBuffer
    {
        private readonly StringBuilder _sb = new();
        private bool _truncated;
        private bool _frozen;

        public void Append(ReadOnlySpan<char> chars)
        {
            lock (_sb)
            {
                if (_frozen) return;
                var room = MaxStreamChars - _sb.Length;
                if (chars.Length > room)
                {
                    _truncated = true;
                    chars = chars[..Math.Max(room, 0)];
                }
                _sb.Append(chars);
            }
        }

        public (string Text, bool Truncated) Freeze()
        {
            lock (_sb)
            {
                _frozen = true;
                return (_sb.ToString(), _truncated);
            }
        }
    }
}
