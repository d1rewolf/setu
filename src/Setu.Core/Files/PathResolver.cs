namespace Setu.Core.Files;

public static class PathResolver
{
    /// <summary>
    /// Expands %VARS% and, off Windows, a leading ~, requires an absolute path, and returns the normalized full
    /// path. Throws if the path is inside a protected folder.
    /// </summary>
    public static string Resolve(string path, ProtectedPaths protectedPaths)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path);
        if (!OperatingSystem.IsWindows() && (expanded == "~" || expanded.StartsWith("~/", StringComparison.Ordinal)))
            expanded = Home() + expanded[1..];
        if (!Path.IsPathFullyQualified(expanded))
            throw new ToolInputException($"Path must be absolute: {path}");
        var full = Path.GetFullPath(expanded);
        if (protectedPaths.Match(full) is not null)
            throw new ToolInputException(ProtectedPaths.Message(path));
        return full;
    }

    private static string Home()
        => Environment.GetEnvironmentVariable("HOME") is { Length: > 0 } home
            ? home.TrimEnd('/')
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
}
