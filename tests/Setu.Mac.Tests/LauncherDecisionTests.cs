using ModelContextProtocol;
using Setu.Core;
using Setu.Core.Files;

namespace Setu.Mac.Tests;

public sealed class LauncherDecisionTests
{
    private static bool Never(string _) => false;

    [Fact]
    public void Classify_AppBundle_IsApp()
    {
        Assert.Equal(LaunchKind.App, MacDetachedLauncher.Classify("/Applications/TextEdit.app", _ => true));
        Assert.Equal(LaunchKind.App, MacDetachedLauncher.Classify("/Applications/TextEdit.app/", _ => true));
        Assert.Equal(LaunchKind.App, MacDetachedLauncher.Classify("TextEdit.app", _ => true));
        Assert.Equal(LaunchKind.App, MacDetachedLauncher.Classify("Foo.APP", _ => true));
    }

    [Fact]
    public void Classify_BareNameNotOnPath_IsApp()
        => Assert.Equal(LaunchKind.App, MacDetachedLauncher.Classify("TextEdit", Never));

    [Fact]
    public void Classify_ExecutableOnPath_IsExecutable()
        => Assert.Equal(LaunchKind.Executable, MacDetachedLauncher.Classify("sleep", n => n == "sleep"));

    [Fact]
    public void Classify_AbsoluteBinary_IsExecutable()
    {
        Assert.Equal(LaunchKind.Executable, MacDetachedLauncher.Classify("/bin/sleep", Never));
        Assert.Equal(LaunchKind.Executable, MacDetachedLauncher.Classify("./run.sh", Never));
        Assert.Equal(LaunchKind.Executable, MacDetachedLauncher.Classify("~/bin/tool", Never));
    }

    [Fact]
    public void SplitArgs_QuotesAndBackslash()
        => Assert.Equal(["a", "b c", "d\"e", "f g"], MacDetachedLauncher.SplitArgs("""a 'b c' "d\"e" f\ g"""));

    [Fact]
    public void SplitArgs_NullOrBlank_IsEmpty()
    {
        Assert.Empty(MacDetachedLauncher.SplitArgs(null));
        Assert.Empty(MacDetachedLauncher.SplitArgs(" \t\n "));
    }

    [Fact]
    public void SplitArgs_EmptyQuotes_GiveEmptyArgument()
        => Assert.Equal(["", "x", ""], MacDetachedLauncher.SplitArgs("""'' x "" """));

    [Fact]
    public void SplitArgs_AdjacentPartsJoin()
        => Assert.Equal(["abc d"], MacDetachedLauncher.SplitArgs("""a"b"'c d'"""));

    [Fact]
    public void SplitArgs_BackslashRules()
    {
        // In single quotes a backslash is literal; in double quotes it escapes only $ ` " \ and newline.
        Assert.Equal(["a\\b"], MacDetachedLauncher.SplitArgs("""'a\b'"""));
        Assert.Equal(["a\\b", "$`\\"], MacDetachedLauncher.SplitArgs("""
            "a\b" "\$\`\\"
            """));
        // Outside quotes a backslash escapes any character, and backslash-newline joins lines.
        Assert.Equal(["'x", "ab"], MacDetachedLauncher.SplitArgs("\\'x a\\\nb"));
    }

    [Fact]
    public void SplitArgs_NoExpansion()
        => Assert.Equal(["$HOME", "*", "~"], MacDetachedLauncher.SplitArgs("$HOME * ~"));

    [Theory]
    [InlineData("'abc")]
    [InlineData("\"abc")]
    [InlineData("abc\\")]
    public void SplitArgs_Unfinished_Throws(string text)
        => Assert.Throws<ToolInputException>(() => MacDetachedLauncher.SplitArgs(text));

    [Fact]
    public void OpenArgs_DocumentsThenArgs_NeverNewInstance()
    {
        var args = MacDetachedLauncher.OpenArgs("TextEdit", ["/tmp/a.txt", "-x", "/no/such", "y"], a => a == "/tmp/a.txt");
        Assert.Equal(["-a", "TextEdit", "/tmp/a.txt", "--args", "-x", "/no/such", "y"], args);
        Assert.Equal(["-a", "TextEdit"], MacDetachedLauncher.OpenArgs("TextEdit", [], Never));
        Assert.Equal(["-a", "TextEdit", "/tmp/a.txt"], MacDetachedLauncher.OpenArgs("TextEdit", ["/tmp/a.txt"], _ => true));
    }

    [Fact]
    public void NewestPid_TakesTheHighest()
    {
        Assert.Equal(456, MacDetachedLauncher.NewestPid("\"pid\"=123\n\"pid\"=456\n"));
        Assert.Equal(77, MacDetachedLauncher.NewestPid("\"pid\" = 77"));
        Assert.Null(MacDetachedLauncher.NewestPid(""));
        Assert.Null(MacDetachedLauncher.NewestPid("\"pid\"=[ NULL ]"));
    }

    [Fact]
    public void FindOnPath_FindsOnlyExecutableFiles()
    {
        if (OperatingSystem.IsWindows()) { Assert.Skip("Unix file modes"); return; }
        var dir = Directory.CreateTempSubdirectory("setu-path-").FullName;
        try
        {
            var tool = Path.Combine(dir, "tool");
            File.WriteAllText(tool, "#!/bin/sh\n");
            File.SetUnixFileMode(tool, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            File.WriteAllText(Path.Combine(dir, "data"), "x");
            Directory.CreateDirectory(Path.Combine(dir, "folder"));
            var pathVar = $"::/no/such/dir:{dir}";

            Assert.Equal(tool, MacDetachedLauncher.FindOnPath("tool", pathVar));
            Assert.Null(MacDetachedLauncher.FindOnPath("data", pathVar));
            Assert.Null(MacDetachedLauncher.FindOnPath("folder", pathVar));
            Assert.Null(MacDetachedLauncher.FindOnPath("missing", pathVar));
            Assert.Null(MacDetachedLauncher.FindOnPath("tool", null));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ----- The app path, with open, plutil and lsappinfo faked -----

    private sealed class ScriptedRunner(Func<string, IReadOnlyList<string>, (int, string, string)> answer) : ICommandRunner
    {
        public List<(string File, List<string> Args)> Calls { get; } = [];

        public (int Exit, string Stdout, string Stderr) RunCapture(string file, IReadOnlyList<string> args, TimeSpan timeout)
        {
            Calls.Add((file, args.ToList()));
            return answer(file, args);
        }

        public (int Exit, string Stderr) Run(string file, IReadOnlyList<string> args, TimeSpan timeout)
        {
            var (exit, _, stderr) = RunCapture(file, args, timeout);
            return (exit, stderr);
        }
    }

    private static MacDetachedLauncher Launcher(ScriptedRunner runner, ProtectedPaths? prot = null) =>
        new(RequestLog.Null, prot ?? ProtectedPaths.None, runner, _ => null,
            TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(10));

    [Fact]
    public void StartApp_OpensWithoutNewInstance_AndReturnsPidFromLsappinfo()
    {
        var doc = Path.GetTempFileName();
        try
        {
            var runner = new ScriptedRunner((file, _) => file switch
            {
                "/usr/bin/lsappinfo" => (0, "\"pid\"=4321\n", ""),
                _ => (0, "", ""),
            });
            Assert.Equal(4321, Launcher(runner).Start("TextEdit", $"'{doc}' --flag", null));

            Assert.Equal("/usr/bin/open", runner.Calls[0].File);
            Assert.Equal(["-a", "TextEdit", doc, "--args", "--flag"], runner.Calls[0].Args);
            Assert.DoesNotContain("-n", runner.Calls[0].Args);
            Assert.Equal("/usr/bin/lsappinfo", runner.Calls[1].File);
            Assert.Equal(["info", "-only", "pid", "TextEdit"], runner.Calls[1].Args);
        }
        finally
        {
            File.Delete(doc);
        }
    }

    [Fact]
    public void StartApp_PollsUntilThePidShows()
    {
        var asked = 0;
        var runner = new ScriptedRunner((file, _) => file == "/usr/bin/lsappinfo"
            ? (0, ++asked < 3 ? "" : "\"pid\"=99", "")
            : (0, "", ""));
        Assert.Equal(99, Launcher(runner).Start("TextEdit", null, null));
        Assert.Equal(3, asked);
    }

    [Fact]
    public void StartApp_NoPid_Throws()
    {
        var runner = new ScriptedRunner((_, _) => (0, "", ""));
        var ex = Assert.Throws<McpException>(() => Launcher(runner).Start("TextEdit", null, null));
        Assert.Equal("Started TextEdit but could not find its PID", ex.Message);
    }

    [Fact]
    public void StartApp_UnknownApp_PlainError()
    {
        var runner = new ScriptedRunner((_, _) => (1, "", "Unable to find application named 'Foo'\n"));
        var ex = Assert.Throws<McpException>(() => Launcher(runner).Start("Foo", null, null));
        Assert.Equal("No application named 'Foo'", ex.Message);
        Assert.Single(runner.Calls);
    }

    [Fact]
    public void StartApp_OtherOpenFailure_CarriesStderr()
    {
        var runner = new ScriptedRunner((_, _) => (1, "", "LSOpenURLsWithRole() failed with error -10810\n"));
        var ex = Assert.Throws<McpException>(() => Launcher(runner).Start("/Applications/Foo.app", null, null));
        Assert.Equal("Could not start /Applications/Foo.app: LSOpenURLsWithRole() failed with error -10810", ex.Message);
    }

    [Fact]
    public void StartApp_BundlePath_LooksUpPidByBundleId()
    {
        var runner = new ScriptedRunner((file, _) => file switch
        {
            "/usr/bin/plutil" => (0, "com.apple.TextEdit\n", ""),
            "/usr/bin/lsappinfo" => (0, "\"pid\"=7\n", ""),
            _ => (0, "", ""),
        });
        Assert.Equal(7, Launcher(runner).Start("/System/Applications/TextEdit.app", null, null));
        Assert.Equal(["-a", "/System/Applications/TextEdit.app"], runner.Calls[0].Args);
        Assert.Equal(
            ["-extract", "CFBundleIdentifier", "raw", "-o", "-", "/System/Applications/TextEdit.app/Contents/Info.plist"],
            runner.Calls[1].Args);
        Assert.Equal(["info", "-only", "pid", "com.apple.TextEdit"], runner.Calls[2].Args);
    }

    [Fact]
    public void StartApp_BundleIdUnreadable_FallsBackToName()
    {
        var runner = new ScriptedRunner((file, _) => file switch
        {
            "/usr/bin/plutil" => (1, "", "no such file"),
            "/usr/bin/lsappinfo" => (0, "\"pid\"=8\n", ""),
            _ => (0, "", ""),
        });
        Assert.Equal(8, Launcher(runner).Start("/Applications/Visual Studio Code.app/", null, null));
        Assert.Equal(["info", "-only", "pid", "Visual Studio Code"], runner.Calls[2].Args);
    }

    [Fact]
    public void StartApp_DocumentInProtectedFolder_IsRefusedBeforeOpen()
    {
        var home = Directory.CreateTempSubdirectory("setu-home-").FullName;
        try
        {
            var prot = new ProtectedPaths([Path.Combine(home, "Documents")], [], ignoreCase: true);
            var runner = new ScriptedRunner((_, _) => (0, "", ""));
            var doc = Path.Combine(home, "Documents", "x.txt");
            var ex = Assert.Throws<ToolInputException>(() => Launcher(runner, prot).Start("TextEdit", $"'{doc}'", null));
            Assert.Equal(ProtectedPaths.Message(doc), ex.Message);
            Assert.Empty(runner.Calls);
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    public void StartExecutable_ProtectedOrMissing_IsRefusedBeforeSpawn()
    {
        var home = Directory.CreateTempSubdirectory("setu-home-").FullName;
        try
        {
            var prot = new ProtectedPaths([Path.Combine(home, "Desktop")], [], ignoreCase: true);
            var runner = new ScriptedRunner((_, _) => (0, "", ""));
            var inDesktop = Path.Combine(home, "Desktop", "tool");
            Assert.Equal(ProtectedPaths.Message(inDesktop),
                Assert.Throws<ToolInputException>(() => Launcher(runner, prot).Start(inDesktop, null, null)).Message);

            var missing = Path.Combine(home, "nope");
            Assert.Equal($"No such program: {missing}",
                Assert.Throws<McpException>(() => Launcher(runner, prot).Start(missing, null, null)).Message);

            var plain = Path.Combine(home, "plain");
            File.WriteAllText(plain, "x");
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(plain, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            Assert.Equal($"{plain} is not executable",
                Assert.Throws<McpException>(() => Launcher(runner, prot).Start(plain, null, null)).Message);
            Assert.Empty(runner.Calls);
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }
}
