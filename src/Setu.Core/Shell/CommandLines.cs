using System.Text;

namespace Setu.Core.Shell;

/// <summary>
/// Argument strings for powershell.exe and cmd.exe that force UTF-8 output, and the argument list that runs
/// a zsh command in its own process group.
/// </summary>
public static class CommandLines
{
    private const string PowerShellPrelude =
        "[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false);$ProgressPreference='SilentlyContinue';";

    public static string PowerShell(string command)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(PowerShellPrelude + command));
        return $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}";
    }

    public static string Cmd(string command) => $"/d /s /c \"chcp 65001>nul & {command}\"";

    /// <summary>
    /// Arguments for perl, which puts itself in a new process group and then execs
    /// <c>shellPath -l -c command</c> with the same PID. The shell is the group leader, so its PID is the
    /// group ID, and a timeout can kill every process the command started, even ones reparented to launchd.
    /// </summary>
    public static IReadOnlyList<string> Zsh(string shellPath, string command) =>
        ["-e", "setpgrp(0,0); exec @ARGV or die $!", "--", shellPath, "-l", "-c", command];
}
