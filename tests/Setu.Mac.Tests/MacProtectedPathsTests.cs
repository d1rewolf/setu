namespace Setu.Mac.Tests;

public sealed class MacProtectedPathsTests
{
    private const string Home = "/Users/jb";

    private static readonly string[] SpecHomeRoots =
    [
        "Desktop", "Documents", "Downloads", "Library/Mobile Documents", "Library/Mail", "Library/Messages",
        "Library/Safari", "Library/Containers", "Library/Group Containers", "Pictures/Photos Library.photoslibrary",
        "Library/Calendars", "Library/CloudStorage", "Library/Photos",
    ];

    [Fact]
    public void MacProtectedPaths_CoverSpecList()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix paths");
        var prot = MacProtectedPaths.For(Home);

        Assert.Equal(SpecHomeRoots, MacProtectedPaths.HomeRoots);
        foreach (var root in SpecHomeRoots)
        {
            Assert.NotNull(prot.Match($"{Home}/{root}"));
            Assert.NotNull(prot.Match($"{Home}/{root}/x"));
            Assert.NotNull(prot.Match($"/System/Volumes/Data{Home}/{root}/x"));
            Assert.NotNull(prot.Match($"{Home}/{root.ToUpperInvariant()}/x")); // APFS is case-insensitive by default
        }
        Assert.NotNull(prot.Match($"{Home}/Library/CloudStorage/x"));
        Assert.NotNull(prot.Match("/Volumes/USB"));
        Assert.NotNull(prot.Match("/Volumes/USB/x"));
        Assert.NotNull(prot.Match("/System/Volumes/Data/Volumes/USB/x"));

        Assert.Null(prot.Match($"{Home}/Movies/x"));
        Assert.Null(prot.Match($"{Home}/Library/Preferences/x"));
        Assert.Null(prot.Match($"{Home}/DesktopNot/x"));
        Assert.Null(prot.Match("/Volumes"));
        Assert.Null(prot.Match("/tmp/x"));
    }
}
