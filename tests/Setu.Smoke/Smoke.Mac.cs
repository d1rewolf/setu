using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

// The macOS check set. Chosen when list_tools shows a "shell" tool. See Smoke.RunAsync.

sealed partial class Smoke
{
    private static readonly string[] MacExpectedTools =
    [
        "read_file", "write_file", "edit_file", "list_dir", "search",
        "shell", "start_process",
        "list_windows", "screenshot", "click", "type_text", "send_keys", "scroll", "focus_window",
    ];

    private const string MacScratch = "/tmp/setu-smoke";

    private bool _textEditStarted;
    private bool _textEditWasRunning;
    private int? _sleepPid;

    private List<(string Name, Func<Task> Run)> MacChecks() =>
    [
        ("auth_rejects_bad_token", AuthRejectsBadToken),
        ("list_tools_complete", ListToolsComplete),
        ("write_read_edit_roundtrip_lf", WriteReadEditRoundtripLf),
        ("list_dir_newest_first", MacListDirNewestFirst),
        ("search_content", MacSearchContent),
        ("protected_folders_refused", ProtectedFoldersRefused),
        ("shell_unicode_output", ShellUnicodeOutput),
        ("shell_error_plain", ShellErrorPlain),
        ("shell_exit_code", ShellExitCode),
        ("shell_prompt_does_not_hang", ShellPromptDoesNotHang),
        ("shell_timeout_kills_tree", ShellTimeoutKillsTree),
        ("start_process_survives", MacStartProcessSurvives),
        ("list_windows_shows_textedit", ListWindowsShowsTextEdit),
        ("screenshot_png_nonempty", MacScreenshotPngNonempty),
        ("click_type_into_textedit", ClickTypeIntoTextEdit),
    ];

    // ---- plumbing -------------------------------------------------------------------------

    private static string ShQuote(string s) => "'" + s.Replace("'", "'\\''") + "'";

    private static int ExitCodeOf(string text)
    {
        var m = Regex.Match(text, @"^exit_code=(-?\d+) ", RegexOptions.Multiline);
        Require(m.Success, $"no exit_code in: {text.Split('\n')[0]}");
        return int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    /// <summary>Runs a zsh command and returns its stdout. Fails on a non-zero exit code.</summary>
    private async Task<string> Sh(string script, int timeoutSeconds = 60)
    {
        var r = await Ok("shell", ("command", script), ("timeout_s", timeoutSeconds));
        Require(r.Text.Contains("exit_code=0 "), $"shell failed: {r.Text}");
        return Section(r.Text, "stdout").Trim();
    }

    private async Task<string> MacSub(string name)
    {
        var dir = $"{MacScratch}/{name}";
        await Sh($"mkdir -p {ShQuote(dir)}");
        return dir;
    }

    private async Task EnsureTextEdit()
    {
        if (_textEditStarted) return;
        _textEditWasRunning = await Sh("pgrep -x TextEdit >/dev/null && echo yes || echo no") == "yes";
        var dir = await MacSub("textedit");
        var file = dir + "/smoke.txt";
        await Ok("write_file", ("path", file), ("content", "smoke test\n"));
        _textEditStarted = true;
        await Ok("start_process", ("path", "TextEdit"), ("arguments", ShQuote(file)));
    }

    private async Task MacCleanupAsync()
    {
        try
        {
            if (_textEditStarted)
            {
                // The smoke document always goes, unsaved, even when TextEdit was already running (for example
                // left open by an earlier failed run); otherwise it would stay open on a deleted file. The app
                // itself quits only when this run started it.
                var script = new List<string>
                {
                    "if application \"TextEdit\" is running then",
                    "tell application \"TextEdit\"",
                    "repeat 20 times",
                    "if not (exists document \"smoke.txt\") then exit repeat",
                    "close document \"smoke.txt\" saving no",
                    "end repeat",
                };
                if (!_textEditWasRunning) script.AddRange(["close every document saving no", "quit"]);
                script.AddRange(["end tell", "end if"]);
                var command = "osascript " + string.Join(' ', script.Select(line => "-e " + ShQuote(line)));
                var r = await Call("shell", ("command", command), ("timeout_s", 30));
                if (r.IsError || !r.Text.Contains("exit_code=0 "))
                    Console.WriteLine($"WARN cleanup could not close the TextEdit smoke document: {r.Text.Trim().ReplaceLineEndings(" | ")}");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"WARN cleanup failed: {Describe(e)}");
        }

        try
        {
            var sb = new StringBuilder();
            if (_sleepPid is { } p) sb.Append($"kill {p} 2>/dev/null; ");
            sb.Append("rm -rf /tmp/setu-smoke");
            await _client!.CallToolAsync("shell", new Dictionary<string, object?> { ["command"] = sb.ToString(), ["timeout_s"] = 30 });
        }
        catch (Exception e)
        {
            Console.WriteLine($"WARN cleanup failed: {Describe(e)}");
        }
    }

    // ---- checks ---------------------------------------------------------------------------

    private async Task WriteReadEditRoundtripLf()
    {
        var dir = await MacSub("rw");
        var file = dir + "/lf.txt";
        // CR shows as R and LF as N, so the line endings are visible in the output.
        string Visible() => $"tr '\\r' 'R' < {ShQuote(file)} | tr '\\n' 'N'";
        await Ok("write_file", ("path", file), ("content", "alpha\nbeta\ngamma\n"));
        var written = await Sh(Visible());
        Require(written == "alphaNbetaNgammaN", $"new file is not LF: {written}");

        var read = await Ok("read_file", ("path", file));
        Require(Regex.IsMatch(read.Text, "line_endings=lf", RegexOptions.IgnoreCase), $"read_file header: {read.Text.Split('\n')[0]}");
        Require(read.Text.Contains("total_lines=3"), $"read_file header: {read.Text.Split('\n')[0]}");
        Require(read.Text.Contains("\tbeta"), "read_file output lacks 'beta'");

        await Ok("edit_file", ("path", file), ("old_string", "beta\ngamma"), ("new_string", "BETA\ndelta"));
        var edited = await Sh(Visible());
        Require(edited == "alphaNBETANdeltaN", $"after edit: {edited}");
    }

    private async Task MacListDirNewestFirst()
    {
        var dir = await MacSub("list");
        await Sh($"cd {ShQuote(dir)} && for n in mid.txt new.txt old.txt; do echo $n > $n; done; " +
                 "touch -t 202001010000 old.txt; touch -t 202101010000 mid.txt; touch new.txt");
        var r = await Ok("list_dir", ("path", dir), ("sort", "time"), ("descending", true));
        var names = r.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Split('\t'))
            .Where(p => p.Length >= 4)
            .Select(p => p[3])
            .ToList();
        Require(names.SequenceEqual(["new.txt", "mid.txt", "old.txt"]), $"order was: {string.Join(", ", names)}");
    }

    private async Task MacSearchContent()
    {
        var dir = await MacSub("search");
        var needle = "needle-" + Guid.NewGuid().ToString("N");
        await Ok("write_file", ("path", dir + "/one.txt"), ("content", $"first\nsecond {needle} here\nthird\n"));
        await Ok("write_file", ("path", dir + "/two.log"), ("content", "nothing to see\n"));
        var r = await Ok("search", ("path", dir), ("content_regex", needle));
        Require(r.Text.Contains("one.txt:2:"), $"expected a match at one.txt:2: in: {r.Text}");
        Require(!r.Text.Contains("two.log"), "two.log must not match");
        var byName = await Ok("search", ("path", dir), ("name_pattern", "*.log"));
        Require(byName.Text.Contains("two.log") && !byName.Text.Contains("one.txt"), $"name search returned: {byName.Text}");
    }

    private async Task ProtectedFoldersRefused()
    {
        const string tail = " is in a folder macOS protects (Desktop, Documents, Downloads, iCloud Drive, other apps' data, removable volumes). Setu has no access to it, on purpose. Work in another folder, or ask John to copy the file out.";
        foreach (var (tool, path) in new[] { ("read_file", "~/Documents/x"), ("list_dir", "~/Downloads") })
        {
            var r = await Call(tool, ("path", path));
            Require(r.IsError, $"{tool} {path} was not refused: {r.Text}");
            Require(r.Text == path + tail, $"{tool} {path} gave: {r.Text}");
        }
        // A whole-home walk can be slow; nothing matches, so the answer is only the skipped list.
        var s = await Ok("search", ("path", "~"), ("name_pattern", "nonexistent-*"));
        var last = s.Text.TrimEnd().Split('\n').Last();
        Require(last.StartsWith("skipped protected folders:", StringComparison.Ordinal), $"search ~ ended with: {last}");
    }

    private async Task ShellUnicodeOutput()
    {
        // The bytes are spelled out so the check does not depend on this file's encoding.
        var r = await Ok("shell", ("command", "printf 'h\\303\\251llo\\342\\206\\222\\n'"));
        Require(Section(r.Text, "stdout").Contains("héllo→"), $"stdout was: {Section(r.Text, "stdout").Trim()}");
    }

    private async Task ShellErrorPlain()
    {
        var r = await Ok("shell", ("command", "ls /nonexistent"));
        var code = ExitCodeOf(r.Text);
        Require(code is 1 or 2, $"exit code was {code}");
        var stderr = Section(r.Text, "stderr");
        Require(stderr.Contains("No such file"), $"stderr was: {stderr.Trim()}");
    }

    private async Task ShellExitCode()
    {
        var r = await Ok("shell", ("command", "exit 7"));
        Require(r.Text.Contains("exit_code=7 "), $"result: {r.Text.Split('\n')[0]}");
    }

    private async Task ShellPromptDoesNotHang()
    {
        var sw = Stopwatch.StartNew();
        var r = await Call("shell", ("command", "read -r x; echo got:$x"), ("timeout_s", 30));
        sw.Stop();
        Require(sw.Elapsed < TimeSpan.FromSeconds(10), $"took {sw.Elapsed.TotalSeconds:0.0} s");
        Require(!r.Text.Contains("timed_out=true"), "the prompt waited until the timeout");
    }

    private async Task ShellTimeoutKillsTree()
    {
        var r = await Ok("shell", ("command", "sleep 120 & echo $!; wait"), ("timeout_s", 2));
        Require(r.Text.Contains("timed_out=true"), $"expected timed_out=true: {r.Text.Split('\n')[0]}");
        var m = Regex.Match(Section(r.Text, "stdout"), @"^\d+", RegexOptions.Multiline);
        Require(m.Success, $"no background PID in the partial output: {r.Text}");
        var pid = int.Parse(m.Value, CultureInfo.InvariantCulture);
        _sleepPid = pid; // cleanup kills it if it survived
        await Task.Delay(TimeSpan.FromSeconds(2));
        var probe = await Ok("shell", ("command", $"kill -0 {pid}"));
        Require(ExitCodeOf(probe.Text) != 0, $"the background sleep (PID {pid}) survived the timeout");
        _sleepPid = null;
    }

    private async Task MacStartProcessSurvives()
    {
        var r = await Ok("start_process", ("path", "/bin/sleep"), ("arguments", "600"));
        var m = Regex.Match(r.Text, @"PID (\d+)");
        Require(m.Success, $"start_process did not report a PID: {r.Text}");
        var pid = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        _sleepPid = pid;
        await Task.Delay(TimeSpan.FromSeconds(3));
        var probe = await Ok("shell", ("command", $"kill -0 {pid}"));
        Require(ExitCodeOf(probe.Text) == 0, $"the launched sleep (PID {pid}) is gone after 3 s");
    }

    private sealed record MacWin(long Handle, int Pid, int X, int Y, int W, int H);

    private async Task<MacWin?> TextEditWindow()
    {
        var r = await Ok("list_windows");
        foreach (var p in r.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Split('\t')))
        {
            if (p.Length < 6 || p[2] != "TextEdit" || !p[5].Contains("smoke.txt")) continue;
            var rect = p[3].Split(',').Select(v => int.Parse(v, CultureInfo.InvariantCulture)).ToArray();
            if (rect.Length != 4) continue;
            return new MacWin(long.Parse(p[0], CultureInfo.InvariantCulture), int.Parse(p[1], CultureInfo.InvariantCulture),
                rect[0], rect[1], rect[2], rect[3]);
        }
        return null;
    }

    private async Task<MacWin> WaitForTextEditWindow()
    {
        await EnsureTextEdit();
        for (var i = 0; i < 40; i++)
        {
            if (await TextEditWindow() is { } w) return w;
            await Task.Delay(250);
        }
        throw new CheckFailed("no TextEdit window titled smoke.txt appeared within 10 s");
    }

    private async Task ListWindowsShowsTextEdit() => _ = await WaitForTextEditWindow();

    private async Task MacScreenshotPngNonempty()
    {
        var r = await Ok("screenshot");
        var width = PngWidth(PngOf(r));
        Require(width > 100, $"PNG width is {width}");
        Require(r.Text.Contains("scale=2"), $"screenshot header lacks scale=2: {r.Text.Split('\n')[0]}");
    }

    private async Task ClickTypeIntoTextEdit()
    {
        var win = await WaitForTextEditWindow();
        await Ok("focus_window", ("handle", win.Handle));
        await Task.Delay(500);
        await Ok("click", ("x", win.X + win.W / 2), ("y", win.Y + win.H / 2));
        await Task.Delay(300);

        // The Mac's clipboard is overwritten here (see docs/smoke-checklist.md).
        const string sentinel = "setu-before";
        await Sh($"printf {sentinel} | pbcopy");
        await Ok("send_keys", ("keys", "cmd+a"));
        await Ok("type_text", ("text", "smoke"));
        await Task.Delay(500);
        await Ok("send_keys", ("keys", "cmd+a cmd+c"));
        // TextEdit puts the copy on the pasteboard a moment after cmd+c, so wait for it to change.
        var clip = await Sh("pbpaste");
        var waited = Stopwatch.StartNew();
        while (clip == sentinel && waited.Elapsed < TimeSpan.FromSeconds(3))
        {
            await Task.Delay(100);
            clip = await Sh("pbpaste");
        }
        // Case-insensitive because TextEdit auto-capitalises the first word: the document holds "Smoke".
        Require(string.Equals(clip, "smoke", StringComparison.OrdinalIgnoreCase),
            $"clipboard after cmd+a cmd+c was '{clip}', expected 'smoke' (any case)");
    }
}
