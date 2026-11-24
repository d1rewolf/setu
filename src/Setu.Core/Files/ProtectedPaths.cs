namespace Setu.Core.Files;

/// <summary>
/// Folders the OS guards with a prompt that blocks the calling thread until someone answers it (macOS: Desktop,
/// Documents, Downloads, iCloud Drive, removable volumes). Matching is by name, so a protected folder is refused
/// without being opened. <see cref="Match"/> never stats anything below a protected root.
/// </summary>
public sealed class ProtectedPaths
{
    private const int MaxLinkHops = 40;

    public static ProtectedPaths None { get; } = new([], [], false);

    private readonly (string Original, string Base, bool Wildcard)[] _roots;
    private readonly (string From, string To)[] _rewrites;
    private readonly StringComparison _cmp;

    /// <param name="roots">Absolute paths. A root ending in "/*" matches every child of that folder, and below.</param>
    /// <param name="prefixRewrites">Prefix replacements applied before matching, such as the macOS firmlink /System/Volumes/Data to "".</param>
    public ProtectedPaths(IEnumerable<string> roots, IEnumerable<(string From, string To)> prefixRewrites, bool ignoreCase)
    {
        _cmp = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        _rewrites = [.. prefixRewrites];
        _roots = [.. roots.Select(r =>
        {
            var wildcard = r.EndsWith("/*", StringComparison.Ordinal) || r.EndsWith("\\*", StringComparison.Ordinal);
            var path = wildcard ? r[..^2] : r;
            // Only the root and its ancestors are stat'ed here, never anything inside it.
            return (r, Walk(Path.GetFullPath(path)).Path.TrimEnd(Path.DirectorySeparatorChar), wildcard);
        })];
    }

    public bool IsEmpty => _roots.Length == 0;

    public static string Message(string path)
        => $"{path} is in a folder macOS protects (Desktop, Documents, Downloads, iCloud Drive, other apps' data, removable volumes). Setu has no access to it, on purpose. Work in another folder, or ask someone at the Mac to copy the file out.";

    /// <summary>
    /// Rewrites prefixes, resolves symlinks of the parts that exist (case and links cannot hide a protected
    /// folder), and returns the matching root as given, or null.
    /// </summary>
    public string? Match(string fullPath) => _roots.Length == 0 ? null : Walk(Path.GetFullPath(fullPath), checkRoots: true).Match;

    private (string Path, string? Match) Walk(string path, bool checkRoots = false)
    {
        for (var hops = 0; ; hops++)
        {
            path = Rewrite(path);
            // Checking the whole path first means the walk below never stats inside a protected folder.
            if (checkRoots && Find(path) is { } hit) return (path, hit);

            var root = Path.GetPathRoot(path) ?? "";
            var parts = path[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            var current = root;
            var relinked = false;
            for (var i = 0; i < parts.Length && !relinked; i++)
            {
                var next = Path.Combine(current, parts[i]);
                var target = LinkTarget(next);
                if (target is null) { current = next; continue; }
                if (hops >= MaxLinkHops) return (path, null);
                path = Path.GetFullPath(Path.Combine([current, target, .. parts[(i + 1)..]]));
                relinked = true;
            }
            if (!relinked) return (path, null);
        }
    }

    private static string? LinkTarget(string path)
    {
        try { return new FileInfo(path).LinkTarget; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private string Rewrite(string path)
    {
        foreach (var (from, to) in _rewrites)
            if (IsAtOrBelow(path, from))
            {
                var rewritten = to + path[from.Length..];
                return rewritten.Length > 0 ? rewritten : Path.DirectorySeparatorChar.ToString();
            }
        return path;
    }

    private bool IsAtOrBelow(string path, string root)
        => path.StartsWith(root, _cmp) && (path.Length == root.Length || path[root.Length] == Path.DirectorySeparatorChar);

    private string? Find(string path)
    {
        foreach (var (original, baseDir, wildcard) in _roots)
        {
            if (!wildcard)
            {
                if (IsAtOrBelow(path, baseDir)) return original;
            }
            else if (path.StartsWith(baseDir + Path.DirectorySeparatorChar, _cmp)
                     && path.Length > baseDir.Length + 1)
            {
                return original;
            }
        }
        return null;
    }
}
