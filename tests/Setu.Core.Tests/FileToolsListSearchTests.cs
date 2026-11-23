using System.Text;
using System.Text.RegularExpressions;
using ModelContextProtocol;
using Setu.Core.Files;

namespace Setu.Core.Tests;

public sealed class FileToolsListSearchTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("wmcp-ls-").FullName;

    private FileTools Tools() => new(RequestLog.Null, new FileToolsOptions(1252, LineEndings.Crlf, ProtectedPaths.None));

    private string P(string name) => Path.Combine(_dir, name);

    public void Dispose() => Directory.Delete(_dir, true);

    private string Touch(string name, DateTime time, string content = "x")
    {
        var p = P(name);
        File.WriteAllText(p, content);
        File.SetLastWriteTime(p, time);
        return p;
    }

    private static string[] Rows(string result) => result.TrimEnd('\n').Split('\n');

    [Fact]
    public void ListDir_SortByTimeDescending_NewestFirst()
    {
        Touch("old.mkv", new DateTime(2024, 1, 1, 10, 0, 0));
        Touch("newest.mkv", new DateTime(2026, 11, 20, 10, 0, 0));
        Touch("mid.mkv", new DateTime(2025, 6, 1, 10, 0, 0));
        var rows = Rows(Tools().ListDir(_dir, sort: "time", descending: true));
        Assert.EndsWith("\tnewest.mkv", rows[0]);
        Assert.EndsWith("\tmid.mkv", rows[1]);
        Assert.EndsWith("\told.mkv", rows[2]);
        Assert.Equal("3 of 3 entries", rows[^1]);
    }

    [Fact]
    public void ListDir_SortByTimeAscending_OldestFirst()
    {
        Touch("a.mkv", new DateTime(2026, 1, 1));
        Touch("b.mkv", new DateTime(2024, 1, 1));
        Assert.EndsWith("\tb.mkv", Rows(Tools().ListDir(_dir, sort: "time"))[0]);
    }

    [Fact]
    public void ListDir_SortByName_DirectoriesFirst()
    {
        Touch("a.txt", DateTime.Now);
        Directory.CreateDirectory(P("zdir"));
        var rows = Rows(Tools().ListDir(_dir));
        Assert.StartsWith("d\t", rows[0]);
        Assert.EndsWith("\tzdir", rows[0]);
        Assert.StartsWith("f\t", rows[1]);
    }

    [Fact]
    public void ListDir_SortBySize()
    {
        Touch("small.bin", DateTime.Now, "1");
        Touch("big.bin", DateTime.Now, "123456");
        var rows = Rows(Tools().ListDir(_dir, sort: "size", descending: true));
        Assert.EndsWith("\tbig.bin", rows[0]);
        Assert.Contains("\t6\t", rows[0]);
    }

    [Fact]
    public void ListDir_UnicodeAndSpaces_Listed()
    {
        Touch("Bild – é 1.mp4", DateTime.Now);
        Assert.Contains("\tBild – é 1.mp4\n", Tools().ListDir(_dir));
    }

    [Fact]
    public void ListDir_PatternFilters()
    {
        Touch("a.mkv", DateTime.Now);
        Touch("b.mkv", DateTime.Now);
        Touch("c.mp4", DateTime.Now);
        var result = Tools().ListDir(_dir, pattern: "*.mkv");
        Assert.Contains("a.mkv", result);
        Assert.Contains("b.mkv", result);
        Assert.DoesNotContain("c.mp4", result);
        Assert.EndsWith("2 of 2 entries\n", result);
    }

    [Fact]
    public void ListDir_TimeHasOffset()
    {
        Touch("a.txt", DateTime.Now);
        Assert.Matches(@"\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d[+-]\d\d:\d\d", Rows(Tools().ListDir(_dir))[0]);
    }

    [Fact]
    public void ListDir_LimitCaps_ShowsTotal()
    {
        for (var i = 0; i < 5; i++) Touch($"f{i}.txt", DateTime.Now);
        var rows = Rows(Tools().ListDir(_dir, limit: 2));
        Assert.Equal(3, rows.Length);
        Assert.Equal("2 of 5 entries", rows[^1]);
    }

    [Fact]
    public void ListDir_BadSortOrLimit_Error()
    {
        Assert.Throws<McpException>(() => Tools().ListDir(_dir, sort: "color"));
        Assert.Throws<McpException>(() => Tools().ListDir(_dir, limit: 0));
    }

    [Fact]
    public void ListDir_MissingDirectory_Error()
        => Assert.Throws<McpException>(() => Tools().ListDir(P("nope")));

    [Fact]
    public void Search_ContentRegex_ReturnsPathLineText()
    {
        var p = Touch("a.txt", DateTime.Now, "one\ntwo needle here\nthree\n");
        Touch("b.txt", DateTime.Now, "nothing\n");
        var result = Tools().Search(_dir, content_regex: "needl.");
        Assert.Contains($"{p}:2: two needle here", result);
        Assert.DoesNotContain("b.txt", result);
    }

    [Fact]
    public void Search_NamePatternOnly()
    {
        Touch("a.log", DateTime.Now);
        Touch("b.txt", DateTime.Now);
        Directory.CreateDirectory(P("sub"));
        Touch(Path.Combine("sub", "c.log"), DateTime.Now);
        var result = Tools().Search(_dir, name_pattern: "*.log");
        Assert.Contains(P("a.log"), result);
        Assert.Contains(Path.Combine(_dir, "sub", "c.log"), result);
        Assert.DoesNotContain("b.txt", result);
    }

    [Fact]
    public void Search_NoFilters_Error()
        => Assert.Throws<McpException>(() => Tools().Search(_dir));

    [Fact]
    public void Search_LimitReached_Marked()
    {
        Touch("a.txt", DateTime.Now, string.Concat(Enumerable.Repeat("hit\n", 5)));
        var result = Tools().Search(_dir, content_regex: "hit", limit: 3);
        Assert.Equal(3, Regex.Matches(result, ":\\d: hit").Count);
        Assert.EndsWith("[limit reached]\n", result);
    }

    [Fact]
    public void Search_LimitAbove2000_Clamped()
    {
        Touch("a.txt", DateTime.Now, string.Concat(Enumerable.Repeat("hit\n", 2100)));
        var result = Tools().Search(_dir, content_regex: "hit", limit: 99999);
        Assert.Equal(2000, Regex.Matches(result, ": hit").Count);
        Assert.Contains("[limit reached]", result);
    }

    [Fact]
    public void Search_Utf16File_Searchable()
    {
        File.WriteAllBytes(P("u.txt"), [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("hello wörld\r\n")]);
        Assert.Contains("u.txt:1: hello wörld", Tools().Search(_dir, content_regex: "w.rld"));
    }

    [Fact]
    public void Search_BinaryFileSkipped()
    {
        File.WriteAllBytes(P("b.bin"), [0, 1, 2, .. "needle"u8.ToArray()]);
        Assert.DoesNotContain("b.bin", Tools().Search(_dir, content_regex: "needle"));
    }

    [Fact]
    public void Search_LongLineCutAt300()
    {
        Touch("a.txt", DateTime.Now, new string('a', 1000) + "\n");
        var line = Rows(Tools().Search(_dir, content_regex: "a"))[0];
        Assert.EndsWith(":1: " + new string('a', 300), line);
    }

    [Fact]
    public void Search_NameAndContent_BothApply()
    {
        Touch("a.txt", DateTime.Now, "needle\n");
        Touch("b.log", DateTime.Now, "needle\n");
        var result = Tools().Search(_dir, name_pattern: "*.txt", content_regex: "needle");
        Assert.Contains("a.txt", result);
        Assert.DoesNotContain("b.log", result);
    }

    [Fact]
    public void Search_InvalidRegex_Error()
        => Assert.Throws<McpException>(() => Tools().Search(_dir, content_regex: "("));

    [Fact]
    public void Search_RegexTimeout_SkipsFileAndNotes()
    {
        Touch("slow.txt", DateTime.Now, new string('a', 40) + "!\n");
        Touch("ok.txt", DateTime.Now, "(aaa)\n");
        var result = Tools().Search(_dir, content_regex: @"^(a+)+$|\(aaa\)");
        Assert.Contains("ok.txt:1:", result);
        Assert.Contains("regex timed out", result);
        Assert.Contains("slow.txt", result);
    }

    [Fact]
    public void Search_HiddenDirsAndFiles_Included()
    {
        // .NET treats dot-names as Hidden on Unix; on Windows set the attribute explicitly.
        var hiddenDir = Directory.CreateDirectory(P(".appdata"));
        var inHidden = Path.Combine(hiddenDir.FullName, "found.log");
        File.WriteAllText(inHidden, "needle\n");
        var hiddenFile = P(".secret.log");
        File.WriteAllText(hiddenFile, "needle\n");
        if (OperatingSystem.IsWindows())
        {
            hiddenDir.Attributes |= FileAttributes.Hidden;
            File.SetAttributes(hiddenFile, File.GetAttributes(hiddenFile) | FileAttributes.Hidden);
        }
        Assert.True((hiddenDir.Attributes & FileAttributes.Hidden) != 0);

        var byName = Tools().Search(_dir, name_pattern: "*.log");
        Assert.Contains(inHidden, byName);
        Assert.Contains(hiddenFile, byName);
        var byContent = Tools().Search(_dir, content_regex: "needle");
        Assert.Contains($"{inHidden}:1: needle", byContent);
        Assert.Contains($"{hiddenFile}:1: needle", byContent);
    }

    [Fact]
    public void Search_NoMatches_SaysSo()
    {
        Touch("a.txt", DateTime.Now, "x\n");
        Assert.Contains("No matches", Tools().Search(_dir, content_regex: "zzz"));
    }
}
