using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using ModelContextProtocol;
using Setu.Core;
using Setu.Core.Shell;

namespace Setu.Win32;

/// <summary>
/// Starts a program outside the server's job and console so it outlives the server and every command.
/// First CreateProcess with CREATE_BREAKAWAY_FROM_JOB; if the job the server runs in (Task Scheduler)
/// forbids breakaway, Windows says ERROR_ACCESS_DENIED and the program is started through WMI
/// Win32_Process.Create instead, whose parent is WmiPrvSE and outside every job.
/// Console programs get their own visible console window on both paths (CREATE_NEW_CONSOLE here, and
/// Win32_Process.Create does the same), so John can see and use them; GUI programs start normally.
/// </summary>
public sealed class DetachedLauncher(RequestLog log) : IDetachedLauncher
{
    private static readonly TimeSpan WmiTimeout = TimeSpan.FromSeconds(60);

    public DetachedLauncher() : this(RequestLog.Null) { }

    public int Start(string path, string? arguments, string? workingDirectory)
    {
        var commandLine = BuildCommandLine(path, arguments);
        var flags = NativeMethods.CREATE_NEW_PROCESS_GROUP | NativeMethods.CREATE_BREAKAWAY_FROM_JOB;
        if (IsConsoleProgram(Unquote(path))) flags |= NativeMethods.CREATE_NEW_CONSOLE;

        if (TryCreateProcess(commandLine, flags, workingDirectory, out var pid, out var error)) return pid;

        if (error.NativeErrorCode != NativeMethods.ERROR_ACCESS_DENIED)
            throw new McpException($"Could not start {path}: {error.Message}");

        log.Write($"start_process: CreateProcess with breakaway was denied; starting {path} through WMI Win32_Process.Create");
        return StartThroughWmi(path, commandLine, workingDirectory);
    }

    internal static string BuildCommandLine(string path, string? arguments)
    {
        var program = Unquote(path.Trim());
        if (program.Length == 0) throw new ToolInputException("path is required");
        if (program.Contains('"', StringComparison.Ordinal)) throw new ToolInputException("path must not contain a double quote");
        var quoted = $"\"{program}\"";
        return string.IsNullOrWhiteSpace(arguments) ? quoted : $"{quoted} {arguments}";
    }

    private static string Unquote(string path)
    {
        var p = path.Trim();
        return p.Length >= 2 && p[0] == '"' && p[^1] == '"' ? p[1..^1] : p;
    }

    private static unsafe bool TryCreateProcess(
        string commandLine, uint flags, string? workingDirectory, out int pid, out Win32Exception error)
    {
        // CreateProcessW may write into the command line buffer, so it must be a mutable copy.
        var buffer = new char[commandLine.Length + 1];
        commandLine.CopyTo(buffer);
        var startup = new NativeMethods.STARTUPINFOW { cb = sizeof(NativeMethods.STARTUPINFOW) };
        NativeMethods.PROCESS_INFORMATION info;
        bool ok;
        fixed (char* cmd = buffer)
        {
            ok = NativeMethods.CreateProcess(
                null, cmd, 0, 0, bInheritHandles: false, flags, 0, workingDirectory, ref startup, out info);
        }
        if (!ok)
        {
            pid = 0;
            error = new Win32Exception();
            return false;
        }

        // The program is already running; failing to close our copies of its handles would only leak them.
        _ = NativeMethods.CloseHandle(info.hThread);
        _ = NativeMethods.CloseHandle(info.hProcess);
        pid = info.dwProcessId;
        error = null!;
        return true;
    }

    /// <summary>
    /// True when the program resolves (like CreateProcess would, roughly) to a PE image with the console
    /// subsystem, which then gets a new visible console instead of inheriting the server's (if any).
    /// Anything that cannot be resolved or read counts as not a console program and gets no console flag,
    /// so GUI programs and scripts keep their normal behaviour.
    /// </summary>
    private static unsafe bool IsConsoleProgram(string program)
    {
        const int MaxPath = 32768;
        var buffer = new char[MaxPath];
        uint length;
        fixed (char* p = buffer)
        {
            length = NativeMethods.SearchPath(null, program, ".exe", MaxPath, p, out _);
        }
        if (length == 0 || length >= MaxPath) return false;
        var resolved = new string(buffer, 0, (int)length);

        try
        {
            using var stream = new FileStream(resolved, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new BinaryReader(stream);
            if (stream.Length < 0x40 || reader.ReadUInt16() != 0x5A4D) return false; // "MZ"
            stream.Position = 0x3C;
            var peOffset = reader.ReadInt32();
            // PE signature (4) + COFF header (20) + optional header offset of Subsystem (68).
            if (peOffset <= 0 || peOffset + 4 + 20 + 70 > stream.Length) return false;
            stream.Position = peOffset;
            if (reader.ReadUInt32() != 0x00004550) return false; // "PE\0\0"
            stream.Position = peOffset + 4 + 20 + 68;
            const ushort ImageSubsystemWindowsCui = 3;
            return reader.ReadUInt16() == ImageSubsystemWindowsCui;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private int StartThroughWmi(string path, string commandLine, string? workingDirectory)
    {
        // With redirected stderr, powershell.exe -EncodedCommand writes error and progress records as CLIXML.
        // Progress is silenced and every error is caught and written as its plain message, so only plain text
        // reaches stderr (Explain also strips any CLIXML that still gets through).
        var script = new StringBuilder()
            .AppendLine("$ErrorActionPreference = 'Stop'")
            .AppendLine("$ProgressPreference = 'SilentlyContinue'")
            .AppendLine("try {")
            .Append("  $a = @{ CommandLine = ").Append(PowerShellLiteral(commandLine)).AppendLine(" }");
        if (workingDirectory is not null)
            script.Append("  $a.CurrentDirectory = ").AppendLine(PowerShellLiteral(workingDirectory));
        script
            .AppendLine("  $r = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments $a")
            .AppendLine("  if ($r.ReturnValue -ne 0) { [Console]::Error.WriteLine(\"Win32_Process.Create returned $($r.ReturnValue)\"); exit 1 }")
            .AppendLine("  [Console]::Out.WriteLine($r.ProcessId)")
            .AppendLine("} catch {")
            .AppendLine("  [Console]::Error.WriteLine($_.Exception.Message); exit 1")
            .AppendLine("}");

        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script.ToString()));
        var psi = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
            Arguments = $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        Process? started;
        try
        {
            started = Process.Start(psi);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            throw new McpException($"Could not start {path}: PowerShell could not be started for the WMI fallback ({ex.Message})");
        }
        using var ps = started
            ?? throw new McpException($"Could not start {path}: PowerShell did not start for the WMI fallback");
        var stdout = ps.StandardOutput.ReadToEndAsync();
        var stderr = ps.StandardError.ReadToEndAsync();
        if (!ps.WaitForExit(WmiTimeout))
        {
            try { ps.Kill(entireProcessTree: true); } catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or AggregateException) { }
            throw new McpException($"Could not start {path}: the WMI fallback did not answer within {WmiTimeout.TotalSeconds:0} s");
        }
        ps.WaitForExit(); // flush the redirected streams
        var output = StripClixml(stdout.GetAwaiter().GetResult());
        var errors = StripClixml(stderr.GetAwaiter().GetResult());

        if (ps.ExitCode != 0 || !int.TryParse(LastLine(output), NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid) || pid <= 0)
        {
            var detail = errors.Length > 0 ? errors : output.Length > 0 ? output : $"PowerShell exited with code {ps.ExitCode}";
            throw new McpException($"Could not start {path} (breakaway denied, WMI fallback failed): {Explain(detail)}");
        }

        log.Write($"start_process: WMI started {path} as PID {pid.ToString(CultureInfo.InvariantCulture)}");
        return pid;
    }

    private static string LastLine(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length == 0 ? "" : lines[^1];
    }

    /// <summary>A PowerShell single-quoted literal. PowerShell also treats the typographic single quotes as quote
    /// characters, so every one of them is doubled too.</summary>
    private static string PowerShellLiteral(string value)
    {
        var sb = new StringBuilder(value.Length + 2).Append('\'');
        foreach (var c in value)
        {
            if (c is '\'' or '\u2018' or '\u2019' or '\u201A' or '\u201B') sb.Append(c);
            sb.Append(c);
        }
        return sb.Append('\'').ToString();
    }

    /// <summary>Drops everything from a "#&lt; CLIXML" marker onward (serialized PowerShell records), then trims.</summary>
    private static string StripClixml(string text)
    {
        var marker = text.IndexOf("#< CLIXML", StringComparison.Ordinal);
        return (marker >= 0 ? text[..marker] : text).Trim();
    }

    private static string Explain(string detail) => detail switch
    {
        _ when detail.EndsWith("returned 2", StringComparison.Ordinal) => detail + " (access denied)",
        _ when detail.EndsWith("returned 3", StringComparison.Ordinal) => detail + " (insufficient privilege)",
        _ when detail.EndsWith("returned 9", StringComparison.Ordinal) => detail + " (path not found)",
        _ when detail.EndsWith("returned 21", StringComparison.Ordinal) => detail + " (invalid parameter)",
        _ => detail,
    };
}
