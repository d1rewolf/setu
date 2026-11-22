namespace Setu.Core.Files;

public static class PathResolver
{
    /// <summary>Expands %VARS%, requires an absolute path, and returns the normalized full path.</summary>
    public static string Resolve(string path)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path);
        if (!Path.IsPathFullyQualified(expanded))
            throw new ToolInputException($"Path must be absolute: {path}");
        return Path.GetFullPath(expanded);
    }
}
