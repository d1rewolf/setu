using Setu.Core.Files;

namespace Setu.Mac;

/// <summary>
/// The folders macOS guards with a privacy prompt (spec, Protected folders): the file tools refuse them up front,
/// so an agent's call is never the first touch that would wait on a prompt nobody answers.
/// </summary>
public static class MacProtectedPaths
{
    /// <summary>Relative to the home folder.</summary>
    internal static readonly string[] HomeRoots =
    [
        "Desktop", "Documents", "Downloads",
        "Library/Mobile Documents", // iCloud Drive
        "Library/Mail", "Library/Messages", "Library/Safari",
        "Library/Containers", "Library/Group Containers", // other apps' data
        "Pictures/Photos Library.photoslibrary",
        "Library/Calendars",
        "Library/CloudStorage", // third-party cloud providers
    ];

    /// <summary>Removable and network volumes: every child of /Volumes, not /Volumes itself.</summary>
    internal const string Volumes = "/Volumes/*";

    /// <summary>
    /// The list for <paramref name="home"/>. /System/Volumes/Data is a firmlink to /, which resolving symlinks
    /// does not catch, so it is stripped first. Matching ignores case, as APFS does by default.
    /// </summary>
    public static ProtectedPaths For(string home) => new(
        [.. HomeRoots.Select(r => Path.Combine(home, r)), Volumes],
        [("/System/Volumes/Data", "")],
        ignoreCase: true);
}
