using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

// End-to-end smoke client for setu. Runs on Linux against the ssh tunnel and
// exercises every tool on the real Windows box. See docs/smoke-checklist.md.

const string Usage = """
    Usage: Setu.Smoke --token-file <path> [--url <mcp-url>] [--only <check-name>]

      --token-file  File holding the bearer token (required).
      --url         MCP endpoint. Default: http://127.0.0.1:47631/mcp
      --only        Run a single check by name, then exit. "selftest" checks the
                    output parser only and needs no server.

    Each check prints PASS <name> or FAIL <name>: <why>. Exit code 0 means every
    check passed, 1 means a check failed, 2 means bad usage.
    """;

string? url = "http://127.0.0.1:47631/mcp", tokenFile = null, only = null;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--url" when i + 1 < args.Length: url = args[++i]; break;
        case "--token-file" when i + 1 < args.Length: tokenFile = args[++i]; break;
        case "--only" when i + 1 < args.Length: only = args[++i]; break;
        default:
            Console.Error.WriteLine(args[i] is "--help" or "-h" ? Usage : $"Unknown or incomplete option: {args[i]}\n\n{Usage}");
            return 2;
    }
}
if (only == "selftest")
{
    try { Smoke.SelfTest(); Console.WriteLine("PASS selftest"); return 0; }
    catch (Exception e) { Console.WriteLine($"FAIL selftest: {e.Message}"); return 1; }
}
if (tokenFile is null)
{
    Console.Error.WriteLine(Usage);
    return 2;
}

string token;
try { token = File.ReadAllText(tokenFile).Trim(); }
catch (Exception e) { Console.Error.WriteLine($"Cannot read token file: {e.Message}"); return 2; }

var smoke = new Smoke(new Uri(url), token);
if (only is not null && !smoke.Names.Contains(only))
{
    Console.Error.WriteLine($"No check named '{only}'. Checks: {string.Join(", ", smoke.Names)}");
    return 2;
}
return await smoke.RunAsync(only);

sealed record ToolReply(bool IsError, string Text, IList<ContentBlock> Blocks);

sealed class Smoke
{
    private static readonly string[] ExpectedTools =
    [
        "read_file", "write_file", "edit_file", "list_dir", "search",
        "powershell", "cmd", "start_process",
        "list_windows", "screenshot", "click", "type_text", "send_keys", "scroll", "focus_window",
    ];

    private readonly Uri _url;
    private readonly string _token;
    private McpClient? _client;
    private string? _scratch;
    private HashSet<int>? _notepadBefore;
    private int? _notepadPid;
    private int? _holderPid;

    private readonly List<(string Name, Func<Task> Run)> _checks;
    public IReadOnlyList<string> Names => _checks.Select(c => c.Name).ToList();

    public Smoke(Uri url, string token)
    {
        _url = url;
        _token = token;
        _checks =
        [
            ("auth_rejects_bad_token", AuthRejectsBadToken),
            ("list_tools_complete", ListToolsComplete),
            ("write_read_edit_roundtrip_crlf", WriteReadEditRoundtripCrlf),
            ("read_locked_file", ReadLockedFile),
            ("list_dir_newest_first", ListDirNewestFirst),
            ("search_content", SearchContent),
            ("powershell_unicode_output", PowerShellUnicodeOutput),
            ("powershell_error_plain", PowerShellErrorPlain),
            ("cmd_exit_code", CmdExitCode),
            ("powershell_prompt_does_not_hang", PowerShellPromptDoesNotHang),
            ("powershell_timeout_kills_tree", PowerShellTimeoutKillsTree),
            ("start_process_survives", StartProcessSurvives),
            ("list_windows_shows_notepad", ListWindowsShowsNotepad),
            ("screenshot_png_nonempty", ScreenshotPngNonempty),
            ("click_type_into_notepad", ClickTypeIntoNotepad),
        ];
    }

    public async Task<int> RunAsync(string? only)
    {
        var failures = 0;
        try { SelfTest(); }
        catch (Exception e)
        {
            Console.WriteLine($"FAIL selftest: {e.Message}");
            return 1;
        }
        try
        {
            _client = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = _url,
                AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + _token },
            }));
        }
        catch (Exception e)
        {
            Console.WriteLine($"FAIL connect: {Describe(e)}");
            return 1;
        }

        try
        {
            foreach (var (name, run) in _checks)
            {
                if (only is not null && name != only) continue;
                try
                {
                    await run();
                    Console.WriteLine($"PASS {name}");
                }
                catch (Exception e)
                {
                    failures++;
                    Console.WriteLine($"FAIL {name}: {Describe(e)}");
                }
            }
        }
        finally
        {
            await CleanupAsync();
            await _client.DisposeAsync();
        }
        Console.WriteLine(failures == 0 ? "All checks passed." : $"{failures} check(s) failed.");
        return failures == 0 ? 0 : 1;
    }

    private static string Describe(Exception e) => e.Message.ReplaceLineEndings(" | ");

    private static void Require(bool ok, string why)
    {
        if (!ok) throw new CheckFailed(why);
    }

    private sealed class CheckFailed(string message) : Exception(message);

    // ---- plumbing -------------------------------------------------------------------------

    private async Task<ToolReply> Call(string tool, params (string Key, object? Value)[] arguments)
    {
        var dict = arguments.ToDictionary(a => a.Key, a => a.Value);
        var r = await _client!.CallToolAsync(tool, dict);
        var text = string.Join("\n", r.Content.OfType<TextContentBlock>().Select(t => t.Text));
        return new ToolReply(r.IsError == true, text, r.Content);
    }

    private async Task<ToolReply> Ok(string tool, params (string Key, object? Value)[] arguments)
    {
        var r = await Call(tool, arguments);
        Require(!r.IsError, $"{tool} returned a tool error: {r.Text}");
        return r;
    }

    /// <summary>Runs a PowerShell script and returns its stdout. Fails on a non-zero exit code.</summary>
    private async Task<string> PS(string script, int timeoutSeconds = 60)
    {
        var r = await Ok("powershell", ("command", script), ("timeout_s", timeoutSeconds));
        Require(r.Text.Contains("exit_code=0 "), $"powershell failed: {r.Text}");
        return Section(r.Text, "stdout").Trim();
    }

    private const string TruncatedMarker = "[truncated at 64 KiB]\n";

    /// <summary>Extracts one stream from the shell tools' "--- stdout ---/--- stderr ---" output.</summary>
    internal static string Section(string text, string name)
    {
        var header = $"--- {name} ---\n";
        var start = text.IndexOf(header, StringComparison.Ordinal);
        if (start < 0) return "";
        start += header.Length;
        var end = text.Length;
        if (name == "stdout")
        {
            var stderr = text.IndexOf("--- stderr ---\n", start, StringComparison.Ordinal);
            if (stderr >= 0) end = stderr;
        }
        var body = text[start..end];
        return body.EndsWith(TruncatedMarker, StringComparison.Ordinal) ? body[..^TruncatedMarker.Length] : body;
    }

    public static void SelfTest()
    {
        const string head = "exit_code=0 timed_out=false elapsed_ms=5\n";
        void Eq(string expected, string actual, string what)
        {
            if (expected != actual) throw new CheckFailed($"{what}: expected '{expected}', got '{actual}'");
        }
        var emptyOut = head + "--- stdout ---\n--- stderr ---\nboom\n";
        Eq("", Section(emptyOut, "stdout"), "empty stdout");
        Eq("boom\n", Section(emptyOut, "stderr"), "stderr after empty stdout");
        var emptyErr = head + "--- stdout ---\nhello\n--- stderr ---\n";
        Eq("hello\n", Section(emptyErr, "stdout"), "stdout with empty stderr");
        Eq("", Section(emptyErr, "stderr"), "empty stderr");
        var both = head + "--- stdout ---\na\nb\n--- stderr ---\nc\n";
        Eq("a\nb\n", Section(both, "stdout"), "both: stdout");
        Eq("c\n", Section(both, "stderr"), "both: stderr");
        var trunc = head + "--- stdout ---\nxx\n" + TruncatedMarker + "--- stderr ---\n";
        Eq("xx\n", Section(trunc, "stdout"), "truncated stdout");
        Eq("", Section(head + "--- stdout ---\n--- stderr ---\n", "stdout"), "both empty");
    }

    private static string PsQuote(string s) => "'" + s.Replace("'", "''") + "'";

    private async Task<string> Scratch()
    {
        if (_scratch is not null) return _scratch;
        _scratch = await PS("$d = Join-Path $env:TEMP 'wmcp-smoke'; New-Item -ItemType Directory -Force $d | Out-Null; $d");
        return _scratch;
    }

    private async Task<string> Sub(string name)
    {
        var dir = await Scratch() + "\\" + name;
        await PS($"New-Item -ItemType Directory -Force {PsQuote(dir)} | Out-Null");
        return dir;
    }

    private async Task<int> EnsureNotepad()
    {
        if (_notepadPid is { } pid) return pid;
        var before = await PS("(Get-Process notepad -ErrorAction SilentlyContinue | ForEach-Object { $_.Id }) -join ','");
        _notepadBefore = before.Length == 0
            ? []
            : before.Split(',').Select(int.Parse).ToHashSet();
        var r = await Ok("start_process", ("path", "notepad.exe"));
        var m = Regex.Match(r.Text, @"PID (\d+)");
        Require(m.Success, $"start_process did not report a PID: {r.Text}");
        _notepadPid = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        await Task.Delay(TimeSpan.FromSeconds(2));
        return _notepadPid.Value;
    }

    private Task<string> FileAsVisible(string path) =>
        PS($"[IO.File]::ReadAllText({PsQuote(path)}).Replace(\"`r\",'<CR>').Replace(\"`n\",'<LF>')");

    private async Task CleanupAsync()
    {
        try
        {
            var kill = new StringBuilder();
            if (_holderPid is { } h) kill.Append($"Stop-Process -Id {h} -Force -ErrorAction SilentlyContinue; ");
            if (_notepadPid is { } n) kill.Append($"Stop-Process -Id {n} -Force -ErrorAction SilentlyContinue; ");
            if (_notepadBefore is { } before)
            {
                var keep = before.Count == 0 ? "@()" : "@(" + string.Join(",", before) + ")";
                kill.Append($"Get-Process notepad -ErrorAction SilentlyContinue | Where-Object {{ {keep} -notcontains $_.Id }} | Stop-Process -Force -ErrorAction SilentlyContinue; ");
            }
            // The scratch folder is removed even if this run failed before Scratch() resolved it.
            kill.Append("Start-Sleep -Milliseconds 500; Remove-Item -Recurse -Force -ErrorAction SilentlyContinue (Join-Path $env:TEMP 'wmcp-smoke')");
            await _client!.CallToolAsync("powershell", new Dictionary<string, object?> { ["command"] = kill.ToString(), ["timeout_s"] = 30 });
        }
        catch (Exception e)
        {
            Console.WriteLine($"WARN cleanup failed: {Describe(e)}");
        }
    }

    // ---- checks ---------------------------------------------------------------------------

    private async Task AuthRejectsBadToken()
    {
        const string body = """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"smoke","version":"1"}}}""";
        // The case names, never the header itself: the first case contains the real token.
        foreach (var (name, header) in new (string, string?)[]
                 {
                     ("real token with an extra character", "Bearer " + _token + "x"),
                     ("wrong token", "Bearer wrong-token"),
                     ("no Authorization header", null),
                 })
        {
            using var http = new HttpClient();
            using var req = new HttpRequestMessage(HttpMethod.Post, _url) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            if (header is not null) req.Headers.TryAddWithoutValidation("Authorization", header);
            using var res = await http.SendAsync(req);
            Require(res.StatusCode == HttpStatusCode.Unauthorized,
                $"expected 401 for {name}, got {(int)res.StatusCode}");
        }
    }

    private async Task ListToolsComplete()
    {
        var tools = await _client!.ListToolsAsync();
        var names = tools.Select(t => t.Name).ToHashSet();
        var missing = ExpectedTools.Where(n => !names.Contains(n)).ToList();
        var extra = names.Where(n => !ExpectedTools.Contains(n)).ToList();
        Require(missing.Count == 0, $"missing tools: {string.Join(", ", missing)}");
        Require(extra.Count == 0, $"unexpected tools: {string.Join(", ", extra)}");
    }

    private async Task WriteReadEditRoundtripCrlf()
    {
        var dir = await Sub("rw");
        var file = dir + "\\crlf.txt";
        await Ok("write_file", ("path", file), ("content", "alpha\nbeta\ngamma\n"));
        var written = await FileAsVisible(file);
        Require(written == "alpha<CR><LF>beta<CR><LF>gamma<CR><LF>", $"new file is not CRLF: {written}");

        var read = await Ok("read_file", ("path", file));
        Require(Regex.IsMatch(read.Text, "line_endings=crlf", RegexOptions.IgnoreCase), $"read_file header: {read.Text.Split('\n')[0]}");
        Require(read.Text.Contains("total_lines=3"), $"read_file header: {read.Text.Split('\n')[0]}");
        Require(read.Text.Contains("\tbeta"), "read_file output lacks 'beta'");

        await Ok("edit_file", ("path", file), ("old_string", "beta\ngamma"), ("new_string", "BETA\ndelta"));
        var edited = await FileAsVisible(file);
        Require(edited == "alpha<CR><LF>BETA<CR><LF>delta<CR><LF>", $"after edit: {edited}");
    }

    private async Task ReadLockedFile()
    {
        var dir = await Sub("lock");
        var file = dir + "\\locked.txt";
        var ready = dir + "\\holder.ready";
        await Ok("write_file", ("path", file), ("content", "locked content line\n"));

        var holder = $"$f = [IO.File]::Open({PsQuote(file)}, 'Open', 'Write', 'ReadWrite'); " +
                     $"[IO.File]::WriteAllText({PsQuote(ready)}, [string]$PID); Start-Sleep -Seconds 20; $f.Close()";
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(holder));
        await PS($"Start-Process powershell -WindowStyle Hidden -ArgumentList '-NoProfile','-NonInteractive','-EncodedCommand','{encoded}'");

        string pidText = "";
        for (var i = 0; i < 40 && pidText.Length == 0; i++)
        {
            await Task.Delay(250);
            pidText = await PS($"if (Test-Path {PsQuote(ready)}) {{ Get-Content -Raw {PsQuote(ready)} }}");
        }
        Require(pidText.Length > 0, "the lock holder did not start within 10 s");
        _holderPid = int.Parse(pidText, CultureInfo.InvariantCulture);

        // Prove the lock is real: a plain exclusive open must fail while the holder runs.
        var exclusive = await Ok("powershell", ("command",
            $"try {{ $x = [IO.File]::Open({PsQuote(file)}, 'Open', 'Read', 'None'); $x.Close(); 'opened' }} catch {{ 'locked' }}"));
        Require(Section(exclusive.Text, "stdout").Trim() == "locked", "test setup is wrong: the file was not locked");

        var read = await Ok("read_file", ("path", file));
        Require(read.Text.Contains("locked content line"), $"read_file did not return the content: {read.Text}");
    }

    private async Task ListDirNewestFirst()
    {
        var dir = await Sub("list");
        await PS($$"""
            $d = {{PsQuote(dir)}}
            foreach ($n in 'mid.txt','new.txt','old.txt') { Set-Content -Path (Join-Path $d $n) -Value $n }
            (Get-Item (Join-Path $d 'old.txt')).LastWriteTime = (Get-Date).AddDays(-2)
            (Get-Item (Join-Path $d 'mid.txt')).LastWriteTime = (Get-Date).AddDays(-1)
            (Get-Item (Join-Path $d 'new.txt')).LastWriteTime = Get-Date
            """);
        var r = await Ok("list_dir", ("path", dir), ("sort", "time"), ("descending", true));
        var names = r.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Split('\t'))
            .Where(p => p.Length >= 4)
            .Select(p => p[3])
            .ToList();
        Require(names.SequenceEqual(["new.txt", "mid.txt", "old.txt"]), $"order was: {string.Join(", ", names)}");
    }

    private async Task SearchContent()
    {
        var dir = await Sub("search");
        var needle = "needle-" + Guid.NewGuid().ToString("N");
        await Ok("write_file", ("path", dir + "\\one.txt"), ("content", $"first\nsecond {needle} here\nthird\n"));
        await Ok("write_file", ("path", dir + "\\two.log"), ("content", "nothing to see\n"));
        var r = await Ok("search", ("path", dir), ("content_regex", needle));
        Require(r.Text.Contains("one.txt:2:"), $"expected a match at one.txt:2: in: {r.Text}");
        Require(!r.Text.Contains("two.log"), "two.log must not match");
        var byName = await Ok("search", ("path", dir), ("name_pattern", "*.log"));
        Require(byName.Text.Contains("two.log") && !byName.Text.Contains("one.txt"), $"name search returned: {byName.Text}");
    }

    private async Task PowerShellUnicodeOutput()
    {
        var r = await Ok("powershell", ("command", "Write-Output 'héllo→'"));
        Require(Section(r.Text, "stdout").Contains("héllo→"), $"stdout was: {Section(r.Text, "stdout").Trim()}");
    }

    private async Task PowerShellErrorPlain()
    {
        var r = await Ok("powershell", ("command", "Write-Error boom"));
        var stderr = Section(r.Text, "stderr");
        Require(stderr.Contains("boom"), $"stderr was: {stderr.Trim()}");
        Require(!stderr.Contains("#< CLIXML"), $"stderr is still CLIXML: {stderr.Trim()}");
    }

    private async Task CmdExitCode()
    {
        var r = await Ok("cmd", ("command", "exit /b 7"));
        Require(r.Text.Contains("exit_code=7 "), $"result: {r.Text.Split('\n')[0]}");
    }

    private async Task PowerShellPromptDoesNotHang()
    {
        var sw = Stopwatch.StartNew();
        var r = await Call("powershell", ("command", "Read-Host 'x'"), ("timeout_s", 30));
        sw.Stop();
        Require(sw.Elapsed < TimeSpan.FromSeconds(10), $"took {sw.Elapsed.TotalSeconds:0.0} s");
        Require(!r.Text.Contains("timed_out=true"), "the prompt waited until the timeout");
    }

    private async Task PowerShellTimeoutKillsTree()
    {
        var marker = "wmcpkill" + Guid.NewGuid().ToString("N");
        var r = await Ok("powershell", ("command", $"& powershell.exe -NoProfile -Command \"Start-Sleep -Seconds 120 # {marker}\""), ("timeout_s", 2));
        Require(r.Text.Contains("timed_out=true"), $"expected timed_out=true: {r.Text.Split('\n')[0]}");
        await Task.Delay(TimeSpan.FromSeconds(2));
        // The pattern is split so this query's own command line does not contain the marker.
        var half = marker.Length / 2;
        var survivors = await PS($"$p = '*' + {PsQuote(marker[..half])} + {PsQuote(marker[half..])} + '*'; " +
                                 "(Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like $p -and $_.ProcessId -ne $PID } | ForEach-Object { $_.ProcessId }) -join ','");
        Require(survivors.Length == 0, $"processes with the marker survived: PID {survivors}");
    }

    private async Task StartProcessSurvives()
    {
        // Windows 11 Notepad may hand off to the packaged app: the launched PID can exit while a
        // Notepad process that did not exist before the run keeps going. Either counts.
        var pid = await EnsureNotepad();
        await Task.Delay(TimeSpan.FromSeconds(3));
        var launched = await PS($"if (Get-Process -Id {pid} -ErrorAction SilentlyContinue) {{ 'alive' }} else {{ 'gone' }}");
        if (launched == "alive") return;
        var current = await NotepadPids();
        Require(current.Any(p => !_notepadBefore!.Contains(p)),
            $"launched PID {pid} is gone and no new notepad process is running");
    }

    private async Task<HashSet<int>> NotepadPids()
    {
        var text = await PS("(Get-Process notepad -ErrorAction SilentlyContinue | ForEach-Object { $_.Id }) -join ','");
        return text.Length == 0 ? [] : text.Split(',').Select(int.Parse).ToHashSet();
    }

    private async Task ListWindowsShowsNotepad()
    {
        await EnsureNotepad();
        var rows = await NotepadWindows();
        Require(rows.Count > 0, "no window with process name notepad");
    }

    private sealed record Win(long Handle, int Pid);

    private async Task<List<Win>> NotepadWindows()
    {
        var r = await Ok("list_windows");
        return r.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Split('\t'))
            .Where(p => p.Length >= 3 && p[2].StartsWith("notepad", StringComparison.OrdinalIgnoreCase))
            .Select(p => new Win(long.Parse(p[0], CultureInfo.InvariantCulture), int.Parse(p[1], CultureInfo.InvariantCulture)))
            .ToList();
    }

    private static byte[] PngOf(ToolReply r)
    {
        var img = r.Blocks.OfType<ImageContentBlock>().FirstOrDefault();
        Require(img is not null, "no image block in the screenshot result");
        return img!.DecodedData.ToArray();
    }

    private static int PngWidth(byte[] png)
    {
        byte[] sig = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        Require(png.Length >= 24 && png.AsSpan(0, 8).SequenceEqual(sig), "image does not have the PNG signature");
        return (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
    }

    private async Task ScreenshotPngNonempty()
    {
        var png = PngOf(await Ok("screenshot"));
        var width = PngWidth(png);
        Require(width > 0, $"PNG width is {width}");
    }

    private async Task ClickTypeIntoNotepad()
    {
        var pid = await EnsureNotepad();
        var wins = await NotepadWindows();
        var win = wins.FirstOrDefault(w => w.Pid == pid)
                  ?? wins.FirstOrDefault(w => !_notepadBefore!.Contains(w.Pid))
                  ?? wins.FirstOrDefault();
        Require(win is not null, "no notepad window to focus");
        await Ok("focus_window", ("handle", win!.Handle));
        await Task.Delay(500);
        var before = PngOf(await Ok("screenshot", ("target", $"window:{win.Handle}")));

        // The VM clipboard is overwritten here (see docs/smoke-checklist.md).
        await PS(ClipboardRetry("Set-Clipboard -Value 'wmcp-before'"));
        await Ok("type_text", ("text", "smoke"));
        await Task.Delay(500);
        var after = PngOf(await Ok("screenshot", ("target", $"window:{win.Handle}")));
        await Ok("send_keys", ("keys", "ctrl+a ctrl+c"));
        await Task.Delay(500);
        var clip = await PS(ClipboardRetry("Get-Clipboard -Raw"));
        Require(clip == "smoke", $"clipboard after ctrl+a ctrl+c was '{clip}', expected 'smoke'");

        PngWidth(before);
        PngWidth(after);
        Require(!before.AsSpan().SequenceEqual(after), "typed text reached the window (clipboard matched) but the screenshots are identical");
    }

    /// <summary>
    /// Another program (Notepad as it activates, clipboard history) can hold the clipboard open for a moment,
    /// and then Set/Get-Clipboard fail with "Requested Clipboard operation did not succeed". Retry for ~3 s.
    /// </summary>
    static string ClipboardRetry(string command) =>
        "$i=0; while ($true) { try { " + command + " -ErrorAction Stop; break } " +
        "catch { if (++$i -ge 10) { throw }; Start-Sleep -Milliseconds 300 } }";
}
