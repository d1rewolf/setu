namespace WindowsMcp.Core.Shell;

/// <summary>Starts a program that is not tied to the server or to any command, and returns its PID.</summary>
public interface IDetachedLauncher
{
    int Start(string path, string? arguments, string? workingDirectory);
}
