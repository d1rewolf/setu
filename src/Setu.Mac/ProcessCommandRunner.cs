using System.Diagnostics;

namespace Setu.Mac;

/// <summary>Runs a program with stdin closed and stdout and stderr collected; kills it on timeout.</summary>
internal sealed class ProcessCommandRunner : ICommandRunner
{
    public (int Exit, string Stderr) Run(string file, IReadOnlyList<string> args, TimeSpan timeout)
    {
        var (exit, _, stderr) = RunCapture(file, args, timeout);
        return (exit, stderr);
    }

    public (int Exit, string Stdout, string Stderr) RunCapture(string file, IReadOnlyList<string> args, TimeSpan timeout)
    {
        var start = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args) start.ArgumentList.Add(a);

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {file}");
        process.StandardInput.Close();
        // Both pipes are drained concurrently so a chatty program cannot block on a full pipe.
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeout))
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            process.WaitForExit();
            return (-1, "", $"{Path.GetFileName(file)} did not finish within {timeout.TotalSeconds:0} s");
        }
        process.WaitForExit(); // let the async readers reach end of stream
        return (process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
    }
}
