using System.Text;

namespace WindowsMcp.Core.Shell;

/// <summary>Argument strings for powershell.exe and cmd.exe that force UTF-8 output.</summary>
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
}
