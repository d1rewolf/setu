using System.Text;
using ModelContextProtocol;
using Setu.Core.Files;

namespace Setu.Core.Tests;

public sealed class ProtectedPathsTests : IDisposable
{
    private readonly string _home = Directory.CreateTempSubdirectory("wmcp-pp-").FullName;

    public void Dispose() => Directory.Delete(_home, true);

    private string P(params string[] parts) => Path.Combine([_home, .. parts]);

    private static FileTools Tools(ProtectedPaths prot, LineEndings le = LineEndings.Crlf, int cp = 1252)
        => new(RequestLog.Null, new FileToolsOptions(cp, le, prot));

    private static ProtectedPaths Roots(params string[] roots) => new(roots, [], false);

    private static string MessageFor(string path) => ProtectedPaths.Message(path);

    [Fact]
    public void Message_IsExact()
        => Assert.Equal(
            "/x is in a folder macOS protects (Desktop, Documents, Downloads, iCloud Drive, other apps' data, removable volumes). Setu has no access to it, on purpose. Work in another folder, or ask someone at the Mac to copy the file out.",
            ProtectedPaths.Message("/x"));

    [Fact]
    public void Tilde_ExpandsToHome_OnUnix()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix only");
        var home = Environment.GetEnvironmentVariable("HOME")!;
        Assert.Equal(Path.Combine(home, "x"), PathResolver.Resolve("~/x", ProtectedPaths.None));
        Assert.Equal(home, PathResolver.Resolve("~", ProtectedPaths.None));
    }

    [Fact]
    public void NewFile_UsesConfiguredLineEndings()
    {
        var path = P("n.txt");
        Tools(ProtectedPaths.None, LineEndings.Lf, 28591).WriteFile(path, "a\nb\n");
        Assert.Equal("a\nb\n"u8.ToArray(), File.ReadAllBytes(path));
    }

    [Fact]
    public void Latin1Fallback_RoundTripsEveryByte()
    {
        var tools = Tools(ProtectedPaths.None, LineEndings.Lf, 28591);
        byte[] high = [.. Enumerable.Range(0x80, 0x80).Select(i => (byte)i)];

        var written = P("w.txt");
        tools.WriteFile(written, "abc " + Encoding.Latin1.GetString(high) + " xyz\n", encoding: "ansi");
        byte[] expectedWritten = [.. "abc "u8.ToArray(), .. high, .. " xyz\n"u8.ToArray()];
        Assert.Equal(expectedWritten, File.ReadAllBytes(written));

        var raw = P("r.txt");
        byte[] original = [.. "abc "u8.ToArray(), 0xC3, 0x28, .. high, .. " xyz\n"u8.ToArray()];
        File.WriteAllBytes(raw, original);
        tools.EditFile(raw, "abc", "ABC");
        var after = File.ReadAllBytes(raw);
        Assert.Equal(original.Length, after.Length);
        Assert.Equal("ABC"u8.ToArray(), after[..3]);
        Assert.Equal(original[3..], after[3..]);
    }

    [Fact]
    public void Protected_DirectRefused()
    {
        Directory.CreateDirectory(P("Documents"));
        var file = P("Documents", "a.txt");
        File.WriteAllText(file, "x");
        var ex = Assert.Throws<McpException>(() => Tools(Roots(P("Documents"))).ReadFile(file));
        Assert.Equal(MessageFor(file), ex.Message);
    }

    [Fact]
    public void Protected_Symlink_Refused()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix symlinks");
        Directory.CreateDirectory(P("Documents"));
        File.WriteAllText(P("Documents", "a.txt"), "x");
        Directory.CreateSymbolicLink(P("link"), P("Documents"));
        var viaLink = P("link", "a.txt");
        var prot = Roots(P("Documents"));
        Assert.Equal(P("Documents"), prot.Match(viaLink));
        Assert.Throws<McpException>(() => Tools(prot).ReadFile(viaLink));
        // A link to a not-yet-existing file below a protected folder.
        Assert.NotNull(prot.Match(P("link", "new", "deeper.txt")));
    }

    [Fact]
    public void Protected_Firmlink_Refused()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix paths");
        var prot = new ProtectedPaths([P("Documents")], [("/System/Volumes/Data", "")], false);
        Assert.NotNull(prot.Match("/System/Volumes/Data" + P("Documents", "a")));
        Assert.Null(prot.Match("/System/Volumes/Data" + P("Other", "a")));
    }

    [Fact]
    public void Protected_Case_Refused()
    {
        var prot = new ProtectedPaths([P("Documents")], [], true);
        Assert.NotNull(prot.Match(P("documents", "a")));
        Assert.Null(Roots(P("Documents")).Match(P("documents", "a")));
        Assert.Null(prot.Match(P("DocumentsX", "a")));
    }

    [Fact]
    public void Protected_VolumesWildcard()
    {
        var volumes = P("Volumes");
        Directory.CreateDirectory(Path.Combine(volumes, "USB"));
        File.WriteAllText(Path.Combine(volumes, "USB", "x"), "x");
        var prot = Roots(volumes + "/*");
        Assert.NotNull(prot.Match(Path.Combine(volumes, "USB", "x")));
        Assert.Null(prot.Match(volumes));
        var listing = Tools(prot).ListDir(volumes);
        Assert.Contains("USB", listing);
        Assert.Throws<McpException>(() => Tools(prot).ReadFile(Path.Combine(volumes, "USB", "x")));
    }

    [Fact]
    public void Search_SkipsProtected_AndSaysSo()
    {
        Directory.CreateDirectory(P("Documents"));
        Directory.CreateDirectory(P("ok"));
        File.WriteAllText(P("Documents", "f"), "needle");
        File.WriteAllText(P("ok", "f"), "needle");
        var result = Tools(Roots(P("Documents"))).Search(_home, content_regex: "needle");
        var lines = result.TrimEnd('\n').Split('\n');
        Assert.Contains(lines, l => l.StartsWith(P("ok", "f") + ":1:"));
        Assert.DoesNotContain("Documents" + Path.DirectorySeparatorChar + "f", string.Join('\n', lines[..^1]));
        Assert.Equal($"skipped protected folders: {P("Documents")}", lines[^1]);
    }

    [Fact]
    public void Search_NoProtected_NoSkippedLine()
    {
        File.WriteAllText(P("f"), "needle");
        var result = Tools(ProtectedPaths.None).Search(_home, content_regex: "needle");
        Assert.DoesNotContain("skipped protected", result);
    }

    [Fact]
    public void ListDir_ShowsProtectedEntryName()
    {
        Directory.CreateDirectory(P("Documents"));
        var result = Tools(Roots(P("Documents"))).ListDir(_home);
        Assert.Contains("\tDocuments\n", result);
        Assert.DoesNotContain("skipped protected", result);
    }

    [Fact]
    public void PermissionDenied_OnUnix_BecomesToolInputException()
    {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root")
        {
            Assert.Skip("needs Unix modes, non-root");
            return;
        }
        var file = P("locked.txt");
        File.WriteAllText(file, "x");
        File.SetUnixFileMode(file, UnixFileMode.None);
        try
        {
            var ex = Assert.Throws<McpException>(() => Tools(ProtectedPaths.None).ReadFile(file));
            Assert.Equal($"{file}: permission denied", ex.Message);
        }
        finally
        {
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    public void FileSymlinkIntoProtected_SearchSkipsIt_ListDirShowsNameOnly()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix symlinks");
        Directory.CreateDirectory(P("Documents"));
        File.WriteAllText(P("Documents", "notes.txt"), "needle");
        File.CreateSymbolicLink(P("notes.txt"), P("Documents", "notes.txt"));
        var tools = Tools(Roots(P("Documents")));

        var search = tools.Search(_home, content_regex: "needle");
        Assert.DoesNotContain(":1:", search);
        var last = search.TrimEnd('\n').Split('\n')[^1];
        Assert.StartsWith("skipped protected folders: ", last);
        Assert.Contains(P("notes.txt"), last);

        var list = tools.ListDir(_home);
        Assert.Contains("f\t0\t", list);
        Assert.Contains("\tnotes.txt\n", list);
    }
}
