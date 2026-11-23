using System.Text;
using Setu.Core.Files;

namespace Setu.Core.Shell;

/// <summary>
/// What every shell tool (powershell, cmd, shell) shares: the timeout range, cwd resolution, and the result
/// format, so all of them behave and read the same.
/// </summary>
internal static class ShellCommand
{
    private const int MinTimeoutSeconds = 1;
    private const int MaxTimeoutSeconds = 600;
    private const string TruncatedMarker = "[truncated at 64 KiB]\n";

    /// <summary>
    /// The SDK binds <paramref name="ct"/> to the request (it is not part of the tool's input schema), so a
    /// call the client abandons kills the command and everything it started. <paramref name="cwd"/> is
    /// resolved against <paramref name="protectedPaths"/>, so a protected folder is refused by name before
    /// anything is spawned there.
    /// </summary>
    public static Task<string> RunAsync(
        RequestLog log, IProcessRunner runner, string tool, string? cwd, int timeoutSeconds,
        ProtectedPaths protectedPaths, Func<string?, TimeSpan, ProcessRequest> request,
        Func<string, string> decode, CancellationToken ct)
        => ToolGuard.RunAsync(log, tool, async () =>
        {
            if (timeoutSeconds is < MinTimeoutSeconds or > MaxTimeoutSeconds)
                throw new ToolInputException($"timeout_s must be between {MinTimeoutSeconds} and {MaxTimeoutSeconds}");
            var dir = cwd is null ? null : PathResolver.Resolve(cwd, protectedPaths);
            var result = await runner.RunAsync(request(dir, TimeSpan.FromSeconds(timeoutSeconds)), ct);
            // Decoding after the cap only ever shortens the text, so the 64 KiB bound still holds.
            return Format(result with { Stdout = decode(result.Stdout), Stderr = decode(result.Stderr) });
        });

    private static string Format(ProcessResult r)
    {
        var sb = new StringBuilder();
        var exit = r.ExitCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none";
        sb.Append($"exit_code={exit} timed_out={(r.TimedOut ? "true" : "false")} elapsed_ms={(long)r.Elapsed.TotalMilliseconds}\n");
        AppendStream(sb, "stdout", r.Stdout, r.StdoutTruncated);
        AppendStream(sb, "stderr", r.Stderr, r.StderrTruncated);
        return sb.ToString();
    }

    private static void AppendStream(StringBuilder sb, string name, string text, bool truncated)
    {
        sb.Append($"--- {name} ---\n");
        sb.Append(text);
        if (text.Length > 0 && text[^1] != '\n') sb.Append('\n');
        if (truncated) sb.Append(TruncatedMarker);
    }
}
