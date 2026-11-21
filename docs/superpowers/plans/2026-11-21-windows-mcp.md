# windows-mcp Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A self-contained `windows-mcp.exe` that runs in John's logged-on Windows session and gives a Linux agent file, shell, and screen tools over MCP (streamable HTTP on loopback, bearer token, reached through `ssh -L`).

**Architecture:** Everything that can be tested on Linux lives in `WindowsMcp.Core` (net10.0): the tools, the HTTP host, auth, file encoding, and the process runner. The tools reach the OS through small interfaces. `WindowsMcp.Win32` (net10.0-windows) implements those interfaces with P/Invoke and is exercised only on the VM. The exe project wires the two together. A smoke client in `tests/WindowsMcp.Smoke` drives the real server from Linux through the tunnel. That is the end-to-end test.

**Tech Stack:** .NET 10, `ModelContextProtocol.AspNetCore` 2.4.0 (official MCP C# SDK), ASP.NET Core Kestrel, `System.Drawing.Common` (Win32 project only, for PNG encoding and resizing), xUnit v3 4.0.3, PowerShell 5.1 scripts for install.

**Spec:** `docs/superpowers/specs/2026-11-21-windows-mcp-design.md`

## Global Constraints

- `net10.0` for Core and tests, `net10.0-windows` for Win32 and the exe. `<EnableWindowsTargeting>true</EnableWindowsTargeting>` so everything builds on Linux.
- Nullable on, ImplicitUsings on, `TreatWarningsAsErrors` true, `WarningsNotAsErrors` NU1901–NU1904 (copied from sakshi-net `Directory.Build.props`). `global.json` pins SDK 10.0.100 with `rollForward: latestFeature`, like sakshi-net.
- The exe is published as `dotnet publish src/WindowsMcp -c Release -r win-x64 --self-contained -p:PublishSingleFile=true`.
- Listen on `127.0.0.1` only. Default port **47631**, overridable by `"port"` in `%LOCALAPPDATA%\windows-mcp\config.json`.
- Token: 32 CSPRNG bytes, base64url, at `%LOCALAPPDATA%\windows-mcp\token`, with an ACL for the current user only. `Authorization: Bearer <token>`, compared in constant time. Anything else gets 401.
- Every tool error reaches the agent as an MCP tool error with a plain message. The SDK hides the message of any exception that is not an `McpException`, so every tool body runs inside `ToolGuard` (Task 1).
- Caps: `read_file` returns at most 2000 lines by default and 256 KiB in total. Shell output is capped at 64 KiB per stream. The shell timeout defaults to 60 s, with a maximum of 600 s. `list_dir` returns at most 500 entries by default. `search` returns at most 200 matches by default, with a maximum of 2000. The regex match timeout is 2 s.
- No third-party MCP servers. No runtime other than the self-contained exe on the box.
- SDK API names in this plan (`[McpServerToolType]`, `[McpServerTool]`, `ImageContentBlock.FromBytes`, `TextContentBlock`, `WithHttpTransport`, `HttpServerSessionMode.Stateless`, `MapMcp`, `McpClient`, `HttpClientTransport`) come from the SDK docs. Before using one, confirm it against v2.4.0 with context7 `/modelcontextprotocol/csharp-sdk`.

## Review Focus

1. **The file being read is open for writing by another process** (Sakshi holds `sakshi.log` open). A person expects the read to work. Pinned in Task 3 (`ReadFile_FileOpenForWritingElsewhere_StillReads`).
2. **A command that leaves a child holding its output pipe** (for example, `Start-Process` or `cmd /c start`). A person expects the call to return when the command exits, not at the timeout. Pinned in Task 6 (`Run_BackgroundChildHoldsStdout_ReturnsWhenMainExits`).
3. **The agent sends an `old_string` with LF for a CRLF file.** A person expects the edit to match and the file to stay CRLF. Pinned in Task 4 (`Edit_LfOldStringOnCrlfFile_MatchesAndKeepsCrlf`).
4. **Non-ASCII in command output and file names** (accented names in Videos, `é` in PowerShell output). A person expects the characters to arrive intact. Pinned in Task 5 (`ListDir_UnicodeAndSpaces_Listed`) and in the Task 12 smoke run (`powershell_unicode_output`).
5. **The screen is locked or the RDP session is disconnected.** A person expects a plain "locked or disconnected" error from the screen tools, and file and shell tools that keep working. Pinned in Task 7 (`ScreenTools_DesktopUnavailable_ThrowsPlainError`) and in the Task 12 smoke checklist.

---

## File Structure

```
Directory.Build.props, global.json, .gitignore, WindowsMcp.slnx, README.md
src/WindowsMcp.Core/
  ToolGuard.cs                 error → McpException, per-call log line
  RequestLog.cs                append-only file log
  Files/PathResolver.cs        expand %VARS%, require fully-qualified path
  Files/TextFormat.cs          encoding / BOM / line-ending / binary detection
  Files/AtomicWriter.cs        temp file + replace
  Files/FileTools.cs           read_file, write_file, edit_file, list_dir, search
  Files/ByteSplice.cs          byte-level find/replace for edit_file
  Shell/ProcessRunner.cs       run, cap, timeout, tree-kill, return-on-exit
  Shell/IProcessContainment.cs + DefaultContainment.cs
  Shell/CommandLines.cs        powershell -EncodedCommand / cmd /d /s /c
  Shell/ShellTools.cs          powershell, cmd, start_process
  Shell/IDetachedLauncher.cs
  Screen/IDesktop.cs           OS-facing screen interface + records
  Screen/KeySpec.cs            send_keys parser
  Screen/ScreenTools.cs        list_windows, screenshot, click, type_text, send_keys, scroll, focus_window
  Server/ServerOptions.cs      port, data dir
  Server/TokenStore.cs         create/read token, via ITokenFileProtector
  Server/BearerAuth.cs         middleware
  Server/ServerHost.cs         Build(ServerHostSettings) → WebApplication
src/WindowsMcp.Win32/
  Win32Desktop.cs              IDesktop
  JobObjectContainment.cs      IProcessContainment
  DetachedLauncher.cs          IDetachedLauncher
  TokenFileAcl.cs              ITokenFileProtector
  KeepAwake.cs, AnsiCodePage.cs, NativeMethods.cs
src/WindowsMcp/
  Program.cs, app.manifest, WindowsMcp.csproj
install/install.ps1, uninstall.ps1, update.ps1
docs/connecting.md
tests/WindowsMcp.Core.Tests/   (one test file per Core file under test)
tests/WindowsMcp.Smoke/        console app: end-to-end checks against a live server
```

---

### Task 1: Solution scaffold and ToolGuard

**Files:**
- Create: `Directory.Build.props`, `global.json`, `.gitignore`, `WindowsMcp.slnx`, `src/WindowsMcp.Core/WindowsMcp.Core.csproj`, `src/WindowsMcp.Core/ToolGuard.cs`, `src/WindowsMcp.Core/RequestLog.cs`, `tests/WindowsMcp.Core.Tests/WindowsMcp.Core.Tests.csproj`, `tests/WindowsMcp.Core.Tests/ToolGuardTests.cs`

**Interfaces:**
- Produces: `sealed class RequestLog(string path)` with `void Write(string line)` (thread-safe, prefixes ISO-8601 local time). `RequestLog.Null` is a no-op instance for tests.
- Produces: `static class ToolGuard` with `T Run<T>(RequestLog log, string tool, Func<T> body)` and `Task<T> RunAsync<T>(RequestLog log, string tool, Func<Task<T>> body)`. These rethrow `McpException` as is. They map `FileNotFoundException`/`DirectoryNotFoundException` to `McpException("Not found: <path>")`, `UnauthorizedAccessException` to `McpException("Access denied: <message>")`, `ToolInputException` to its message, and anything else to `McpException("<Type>: <message>")`. Each call logs one line: `tool`, duration in ms, and `ok` or `error: <message>`.
- Produces: `sealed class ToolInputException(string message) : Exception`, which tools throw for bad arguments.

- [ ] **Step 1:** Create the props, global.json, and csproj files. Core references `ModelContextProtocol.AspNetCore` 2.4.0 and `<FrameworkReference Include="Microsoft.AspNetCore.App" />`. Tests reference `xunit.v3` 4.0.3 and Core. `.gitignore` covers `bin/ obj/ publish/`.
- [ ] **Step 2: Write failing tests** in `ToolGuardTests`:
  - `Run_ToolInputException_BecomesMcpExceptionWithSameMessage`: throws `ToolInputException("limit must be positive")`, asserts `McpException.Message == "limit must be positive"`.
  - `Run_FileNotFound_SaysNotFound`: asserts that the message starts with `"Not found: "`.
  - `Run_Success_LogsOkLine`: a log in a temp file has one line containing `read_file` and `ok`.
  - `Run_Failure_LogsErrorLine`: the line contains `error: `.
- [ ] **Step 3:** Run `dotnet test`. Expected: compile failure (types missing).
- [ ] **Step 4:** Implement `RequestLog`, `ToolGuard`, and `ToolInputException`.
- [ ] **Step 5:** Run `dotnet test`. Expected: 4 passed.
- [ ] **Step 6:** Commit with `feat: scaffold solution and ToolGuard`.

### Task 2: Paths and text format detection

**Files:**
- Create: `src/WindowsMcp.Core/Files/PathResolver.cs`, `src/WindowsMcp.Core/Files/TextFormat.cs`, `tests/.../PathResolverTests.cs`, `tests/.../TextFormatTests.cs`

**Interfaces:**
- Produces: `static string PathResolver.Resolve(string path)`. It applies `Environment.ExpandEnvironmentVariables`, then requires `Path.IsPathFullyQualified` (otherwise `ToolInputException("Path must be absolute: <path>")`), then returns `Path.GetFullPath`.
- Produces: `enum FileEncodingKind { Utf8, Utf8Bom, Utf16Le, Utf16Be, Ansi }`, `enum LineEndings { Crlf, Lf, Mixed, None }`, and `sealed record TextFormat(FileEncodingKind Kind, Encoding Encoding, int PreambleLength, LineEndings LineEndings)` with:
  - `static TextFormat Detect(ReadOnlySpan<byte> bytes, int ansiCodePage)`. BOM first. Then a strict UTF-8 decode (`new UTF8Encoding(false, true)`): if it succeeds, the kind is `Utf8`, otherwise `Ansi` with `Encoding.GetEncoding(ansiCodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)`. `CodePagesEncodingProvider` is registered once in a static constructor.
  - `static bool LooksBinary(ReadOnlySpan<byte> bytes)`: true if any NUL byte appears in the first 8192 bytes, unless the data has a UTF-16 BOM.
  - `static LineEndings DetectLineEndings(string text)`.
- The ANSI code page is a parameter. Core never calls Win32. The exe passes `AnsiCodePage.Current` (Task 9), and tests pass 1252.

- [ ] **Step 1: Write failing tests:**
  - `Resolve_ExpandsEnvVar`: sets a variable `WMCP_T` to a temp dir and asserts that `Resolve("%WMCP_T%/a.txt")` ends with `a.txt` and is rooted in the temp dir.
  - `Resolve_Relative_Throws`: `Resolve("a.txt")` throws `ToolInputException`.
  - `Detect_Utf8Bom`, `Detect_Utf16Le`, `Detect_Utf16Be`: check the kind and that `PreambleLength` is 3 or 2.
  - `Detect_PlainUtf8`: `"héllo"u8` gives `Utf8`.
  - `Detect_InvalidUtf8_FallsBackToAnsi1252`: bytes `{0x68, 0xE9, 0x6C}` give `Ansi`, and decoding them gives `"hél"`.
  - `LooksBinary_NulByte_True`; `LooksBinary_Utf16Text_False`.
  - `DetectLineEndings`: `"a\r\nb\r\n"`→Crlf, `"a\nb"`→Lf, `"a\r\nb\n"`→Mixed, `"ab"`→None.
- [ ] **Step 2:** Run `dotnet test --filter "FullyQualifiedName~PathResolver|FullyQualifiedName~TextFormat"`. Expected: fails to compile.
- [ ] **Step 3:** Implement the code above.
- [ ] **Step 4:** Run the same command. Expected: all pass.
- [ ] **Step 5:** Commit with `feat: path resolution and text format detection`.

### Task 3: read_file and write_file

**Files:**
- Create: `src/WindowsMcp.Core/Files/AtomicWriter.cs`, `src/WindowsMcp.Core/Files/FileTools.cs`, `tests/.../FileToolsReadWriteTests.cs`

**Interfaces:**
- Consumes: `PathResolver`, `TextFormat`, `ToolGuard`, `RequestLog`.
- Produces: `static void AtomicWriter.Write(string path, ReadOnlySpan<byte> bytes)`. It writes `<path>.wmcp-<guid>.tmp` in the same directory, then `File.Replace(tmp, path, null)` if the target exists (this keeps its ACL and attributes), else `File.Move(tmp, path)`. The temp file is deleted on failure.
- Produces: `[McpServerToolType] sealed class FileTools(RequestLog log, FileToolsOptions options)`, where `sealed record FileToolsOptions(int AnsiCodePage)`. The tools are instance methods with `[McpServerTool(Name = "...")]` and a `[Description]` on the method and on every parameter:
  - `string ReadFile(string path, int offset = 1, int limit = 2000)`. The first line is a header: `encoding=<Kind> line_endings=<LineEndings> total_lines=<n>`. Then lines in `cat -n` style (`{n,6}\t{text}`), stopping at 256 KiB with a final line `[truncated at 256 KiB; use offset to continue]`. Binary files fail with `"Binary file; read_file only reads text: <path>"`. The file opens with `FileShare.ReadWrite | FileShare.Delete`.
  - `string WriteFile(string path, string content, string? encoding = null, string? line_endings = null)`. `encoding` accepts `utf8|utf8bom|utf16le|utf16be|ansi`, `line_endings` accepts `crlf|lf`. For an existing file, the defaults are the file's own encoding and line endings (`Mixed`/`None` mean CRLF). For a new file, the defaults are UTF-8 without BOM and CRLF. The content's newlines are normalised to the chosen style. The parent directory must exist. Returns `Wrote <n> bytes to <path> (<Kind>, <LineEndings>)`.

- [ ] **Step 1: Write failing tests:**
  - `ReadFile_ReportsEncodingAndNumbersLines`: a UTF-8 BOM CRLF file `"a\r\nb\r\n"`. The header contains `encoding=Utf8Bom line_endings=Crlf total_lines=2`, and the body has `"     1\ta"` and `"     2\tb"`.
  - `ReadFile_OffsetAndLimit`: a 10-line file with offset 4 and limit 2 returns exactly lines 4 and 5.
  - `ReadFile_Binary_Refuses`: the message contains `Binary file`.
  - `ReadFile_FileOpenForWritingElsewhere_StillReads`: a `FileStream` is held open with `FileAccess.Write, FileShare.ReadWrite`, and the read still returns its content. (Review Focus 1)
  - `ReadFile_Huge_TruncatesAt256KiB`.
  - `WriteFile_ExistingUtf16Crlf_KeepsFormat`: write `"x\ny"` and assert the bytes are `FF FE` + UTF-16LE of `"x\r\ny"`.
  - `WriteFile_NewFile_DefaultsUtf8NoBomCrlf`.
  - `WriteFile_AnsiUnrepresentableChar_FailsWithoutWriting`: writing `"→"` into an ANSI 1252 file fails, and the file bytes are unchanged.
- [ ] **Step 2:** Run the filtered tests. Expected: they fail.
- [ ] **Step 3:** Implement `AtomicWriter`, `ReadFile`, and `WriteFile`, each body wrapped in `ToolGuard.Run`.
- [ ] **Step 4:** Run the filtered tests. Expected: all pass.
- [ ] **Step 5:** Commit with `feat: read_file and write_file`.

### Task 4: edit_file (byte-safe)

**Files:**
- Create: `src/WindowsMcp.Core/Files/ByteSplice.cs`, `tests/.../ByteSpliceTests.cs`, `tests/.../FileToolsEditTests.cs`
- Modify: `src/WindowsMcp.Core/Files/FileTools.cs` (add `EditFile`)

**Interfaces:**
- Produces: `static byte[] ByteSplice.Replace(ReadOnlySpan<byte> file, ReadOnlySpan<byte> oldBytes, ReadOnlySpan<byte> newBytes, int start, int alignment, bool replaceAll, out int count)`. It searches from `start` (the preamble length) and only accepts matches at offsets where `(offset - start) % alignment == 0` (2 for UTF-16, 1 otherwise). The count is 0 when there is no match.
- Produces: `string FileTools.EditFile(string path, string old_string, string new_string, bool replace_all = false)`. The algorithm is fixed, because this is what makes it byte-safe:
  1. Read the bytes and `Detect` the format. Binary files are refused.
  2. If the file's line endings are `Crlf`, convert lone `\n` in `old_string` and `new_string` to `\r\n`. For `Lf`/`Mixed`/`None`, use them as given.
  3. Encode both strings with the file's encoding, without preamble and with exception fallback. An unrepresentable character gives `ToolInputException("Text cannot be represented in <Kind>")`.
  4. Splice. A count of 0 gives `"old_string not found in <path>"`. A count above 1 without `replace_all` gives `"old_string found <n> times; pass more context or replace_all=true"`.
  5. `AtomicWriter.Write`. Returns `Replaced <n> occurrence(s) in <path>`.
  An empty `old_string` gives `ToolInputException`.

- [ ] **Step 1: Write failing tests:**
  - `ByteSplice_Utf16_IgnoresMisalignedMatch`: build bytes where the encoded needle appears at an odd offset only, and assert the count is 0.
  - `Edit_LfOldStringOnCrlfFile_MatchesAndKeepsCrlf`: the file is `"one\r\ntwo\r\nthree\r\n"`, `old="one\ntwo"`, `new="1\n2"`, and the result bytes are `"1\r\n2\r\nthree\r\n"`. (Review Focus 3)
  - `Edit_PreservesBomAndUntouchedBytes`: a UTF-8 BOM file containing an invalid-in-context but untouched region (for example, a trailing `0x1A` byte) keeps every byte outside the replaced span.
  - `Edit_AnsiFile_StaysAnsi`: a 1252 file containing `é` (`0xE9`). Replace `"caf"` with `"CAF"`, and the bytes become `43 41 46 E9`.
  - `Edit_NotFound_Error`; `Edit_Ambiguous_Error`; `Edit_ReplaceAll_ReplacesEvery`.
- [ ] **Step 2:** Run the tests. Expected: they fail.
- [ ] **Step 3:** Implement `ByteSplice` and `EditFile`.
- [ ] **Step 4:** Run the tests. Expected: all pass.
- [ ] **Step 5:** Commit with `feat: byte-safe edit_file`.

### Task 5: list_dir and search

**Files:**
- Modify: `src/WindowsMcp.Core/Files/FileTools.cs`
- Create: `tests/.../FileToolsListSearchTests.cs`

**Interfaces:**
- Produces: `string FileTools.ListDir(string path, string? pattern = null, string sort = "name", bool descending = false, int limit = 500)`.
  - `pattern` is a Windows wildcard matched with `FileSystemName.MatchesSimpleExpression`.
  - `sort` is one of `name|time|size`. Directories come first only when sorting by name.
  - Each row is tab-separated: `type(d|f)  size  last_write(ISO-8601 local with offset, "yyyy-MM-ddTHH:mm:sszzz")  name`. The last line is `<shown> of <total> entries`.
- Produces: `string FileTools.Search(string path, string? name_pattern = null, string? content_regex = null, int limit = 200)`. At least one of the two filters is required. It recurses with `EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }`. The regex is compiled with a `TimeSpan.FromSeconds(2)` match timeout, and a timeout on one file skips that file and notes it. Binary files are skipped for content search. Output is `path` for a name-only match, or `path:line: text` (text cut at 300 chars). A `limit` above 2000 is clamped. When the limit is hit, it ends with `[limit reached]`.

- [ ] **Step 1: Write failing tests:**
  - `ListDir_SortByTimeDescending_NewestFirst`: three files with explicit `File.SetLastWriteTime` values. The first row is the newest. (This is the Sakshi check.)
  - `ListDir_UnicodeAndSpaces_Listed`: `"Bild – é 1.mp4"` appears verbatim. (Review Focus 4)
  - `ListDir_PatternFilters`: `*.mkv` returns only the mkv files.
  - `ListDir_TimeHasOffset`: the row matches the regex `\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d[+-]\d\d:\d\d`.
  - `Search_ContentRegex_ReturnsPathLineText`; `Search_NamePatternOnly`; `Search_NoFilters_Error`; `Search_LimitReached_Marked`.
- [ ] **Step 2:** Run the tests. Expected: they fail.
- [ ] **Step 3:** Implement `ListDir` and `Search`.
- [ ] **Step 4:** Run the tests. Expected: all pass.
- [ ] **Step 5:** Commit with `feat: list_dir and search`.

### Task 6: Process runner and shell tools

**Files:**
- Create: `src/WindowsMcp.Core/Shell/ProcessRunner.cs`, `IProcessContainment.cs`, `DefaultContainment.cs`, `CommandLines.cs`, `IDetachedLauncher.cs`, `ShellTools.cs`, `tests/.../ProcessRunnerTests.cs`, `tests/.../CommandLinesTests.cs`, `tests/.../ShellToolsTests.cs`

**Interfaces:**
- Produces: `sealed record ProcessRequest(string FileName, string Arguments, string? WorkingDirectory, TimeSpan Timeout)`.
- Produces: `sealed record ProcessResult(int? ExitCode, string Stdout, string Stderr, bool TimedOut, bool StdoutTruncated, bool StderrTruncated, TimeSpan Elapsed)`.
- Produces: `interface IProcessContainment : IDisposable { void Attach(Process p); void KillAll(); }` and `Func<IProcessContainment>` as the factory. `DefaultContainment` does `Kill(entireProcessTree: true)` on the attached process.
- Produces: `sealed class ProcessRunner(Func<IProcessContainment> containment)` with `Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct)`. The behaviour is fixed:
  - `RedirectStandardInput = true`, and stdin is closed immediately (so a prompt sees EOF). stdout and stderr are redirected and decoded as UTF-8, and read asynchronously into buffers capped at 64 KiB each (excess is dropped, and the truncated flag is set).
  - Wait on the **`Exited` event or a `WaitForExit(int)` poll**, never on `WaitForExitAsync()`, because that waits for the pipes to close (Review Focus 2). After the main process exits, keep draining for a 2 s grace period, then stop reading and return.
  - On timeout, call `KillAll()`, return `TimedOut = true` and `ExitCode = null`, plus whatever output arrived.
  - Never kill on dispose. Leftover children survive a normal exit.
- Produces: `static class CommandLines` with `static string PowerShell(string command)` and `static string Cmd(string command)`.
  - `PowerShell` returns `-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand <base64 UTF-16LE of "[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false);$ProgressPreference='SilentlyContinue';" + command>`.
  - `Cmd` returns `/d /s /c "chcp 65001>nul & <command>"`.
- Produces: `interface IDetachedLauncher { int Start(string path, string? arguments, string? workingDirectory); }`.
- Produces: `[McpServerToolType] sealed class ShellTools(RequestLog log, ProcessRunner runner, IDetachedLauncher launcher)` with:
  - `Task<string> PowerShell(string command, string? cwd = null, int timeout_s = 60)` runs `powershell.exe`.
  - `Task<string> Cmd(string command, string? cwd = null, int timeout_s = 60)` runs `cmd.exe`.
  - `string StartProcess(string path, string? arguments = null, string? cwd = null)` returns `Started <path> as PID <n>`. Its description tells the agent to use this, not `powershell Start-Process`, for GUI apps it wants to keep running.
  - `timeout_s` outside 1–600 gives `ToolInputException`. `cwd` goes through `PathResolver`.
  - The result text is `exit_code=<n|none> timed_out=<bool> elapsed_ms=<n>`, then `--- stdout ---`, then the output, then `--- stderr ---`, then the output, with `[truncated at 64 KiB]` markers where truncation happened. A timed-out command is a normal result, not an error, so the agent sees the partial output.

- [ ] **Step 1: Write failing tests.** The runner tests use `/bin/sh -c` and skip themselves on Windows (`Assert.SkipWhen(OperatingSystem.IsWindows(), "uses /bin/sh")`, xUnit v3). The Windows behaviour is covered by the Task 12 smoke run.
  - `Run_EchoesStdoutAndExitCode`: `echo hi; exit 3` gives `ExitCode 3` and `Stdout "hi\n"`.
  - `Run_StdinClosed_ReadDoesNotHang`: `read x; echo got:$x` finishes in under 5 s.
  - `Run_Timeout_KillsTree`: `sleep 30 & sleep 30` with a 1 s timeout returns `TimedOut` in under 4 s, and `pgrep -f "sleep 30"` (scoped by a unique marker argument, for example `sleep 30.123`) finds nothing afterwards.
  - `Run_BackgroundChildHoldsStdout_ReturnsWhenMainExits`: `(sleep 30.456 &) ; echo done` returns in under 4 s with `"done"`, and the background `sleep` is still alive (kill it in cleanup). (Review Focus 2)
  - `Run_OutputCapped`: `head -c 200000 /dev/zero | tr '\0' a` gives a stdout of 65536 chars and `StdoutTruncated`.
  - `CommandLines_PowerShell_EncodesUtf16LeBase64`: decode the argument and assert it ends with the command and contains `OutputEncoding`.
  - `CommandLines_Cmd_Shape`: equals `/d /s /c "chcp 65001>nul & dir"`.
  - `ShellTools_TimeoutOutOfRange_Error`; `ShellTools_FormatsResult` (with a fake runner).
- [ ] **Step 2:** Run the tests. Expected: they fail.
- [ ] **Step 3:** Implement the code above.
- [ ] **Step 4:** Run the tests. Expected: all pass, and the whole suite stays under 30 s.
- [ ] **Step 5:** Commit with `feat: process runner and shell tools`.

### Task 7: Screen tools (Core side)

**Files:**
- Create: `src/WindowsMcp.Core/Screen/IDesktop.cs`, `KeySpec.cs`, `ScreenTools.cs`, `tests/.../KeySpecTests.cs`, `tests/.../ScreenToolsTests.cs`, `tests/.../FakeDesktop.cs`

**Interfaces:**
- Produces, in `IDesktop.cs`:
  - `sealed record WindowInfo(long Handle, string Title, string ProcessName, int Pid, int X, int Y, int Width, int Height, bool Minimized, bool Foreground)`
  - `abstract record CaptureTarget`, with `ScreenTarget`, `WindowTarget(long Handle)`, and `RegionTarget(int X, int Y, int Width, int Height)`
  - `sealed record Capture(byte[] Png, int OriginX, int OriginY, int PixelWidth, int PixelHeight, double Scale)`
  - `enum MouseButton { Left, Right, Middle }`
  - `sealed record KeyChord(IReadOnlyList<string> Modifiers, string Key)`
  - `interface IDesktop` with these members:
    - `DesktopState State()`, where `enum DesktopState { Available, Locked, Disconnected }`
    - `IReadOnlyList<WindowInfo> ListWindows()`
    - `Capture Capture(CaptureTarget target, int? maxWidth)`
    - `void Click(int x, int y, MouseButton button, bool doubleClick)`
    - `void TypeText(string text)`
    - `void SendKeys(IReadOnlyList<KeyChord> chords)`
    - `void Scroll(int x, int y, int amount)`
    - `bool FocusWindow(long handle)`
- Produces: `static IReadOnlyList<KeyChord> KeySpec.Parse(string keys)`. Chords are separated by spaces, and the parts of a chord are joined with `+`, case-insensitive. Modifiers: `ctrl alt shift win`. Keys: `a`–`z`, `0`–`9`, `f1`–`f24`, `enter tab esc backspace delete insert space up down left right home end pageup pagedown printscreen`. An unknown name gives `ToolInputException("Unknown key '<k>'; known keys: ...")`. The tool description documents the syntax, with the example `"ctrl+s"` and `"alt+f4"` or `"ctrl+a delete"`.
- Produces: `[McpServerToolType] sealed class ScreenTools(RequestLog log, IDesktop desktop)`.
  - The tools are `list_windows`, `screenshot(string target = "screen", int? max_width = null)`, `click(int x, int y, string button = "left", bool double_click = false)`, `type_text(string text)`, `send_keys(string keys)`, `scroll(int x, int y, int amount)`, and `focus_window(long handle)`.
  - The `screenshot` target is `"screen"`, `"window:<handle>"`, or `"region:x,y,w,h"`. It returns `IEnumerable<ContentBlock>`: a `TextContentBlock` with `origin=(<x>,<y>) scale=<s> size=<w>x<h>; screen_x = origin_x + image_x / scale`, then `ImageContentBlock.FromBytes(png, "image/png")`.
  - Every tool except `list_windows` first checks `desktop.State()`. If it isn't `Available`, it throws `McpException("The screen is locked or the session is disconnected (<state>); file and shell tools still work.")`.
  - `focus_window` returns `Focused` or throws `"Windows refused to bring <handle> to the foreground"`.

- [ ] **Step 1: Write failing tests:**
  - `KeySpec_ParsesChordSequence`: `"ctrl+shift+s enter"` gives 2 chords, the first with modifiers `[ctrl, shift]` and key `s`.
  - `KeySpec_UnknownKey_Error`.
  - `ScreenTools_DesktopUnavailable_ThrowsPlainError`: a `FakeDesktop` in the `Locked` state makes `click` fail with a message containing `locked or the session is disconnected`. (Review Focus 5)
  - `Screenshot_ReturnsOriginScaleTextThenPng`.
  - `Screenshot_BadTarget_Error`: `"window:abc"`.
  - `Click_BadButton_Error`.
- [ ] **Step 2:** Run the tests. Expected: they fail.
- [ ] **Step 3:** Implement the code above, plus `FakeDesktop` in the tests (it records calls).
- [ ] **Step 4:** Run the tests. Expected: all pass.
- [ ] **Step 5:** Commit with `feat: screen tools over IDesktop`.

### Task 8: HTTP host, token, and auth

**Files:**
- Create: `src/WindowsMcp.Core/Server/ServerOptions.cs`, `TokenStore.cs`, `BearerAuth.cs`, `ServerHost.cs`, `tests/.../ServerHostTests.cs`, `tests/.../TokenStoreTests.cs`

**Interfaces:**
- Produces: `sealed record ServerOptions(string DataDirectory, int Port)`, with `static ServerOptions Load(string dataDirectory)`. It reads `config.json` `{ "port": n }` if present, otherwise uses port 47631. An invalid port gives an exception with a message naming the file.
- Produces: `interface ITokenFileProtector { void Protect(string path); }`. `UnixTokenFileProtector` does `File.SetUnixFileMode(path, UserRead | UserWrite)`. The Win32 one comes in Task 9.
- Produces: `sealed class TokenStore(string dataDirectory, ITokenFileProtector protector)` with `string GetOrCreate()`. A missing file means 32 bytes from `RandomNumberGenerator`, encoded with `Base64Url.EncodeToString`, written, then `Protect`. The token is read back trimmed.
- Produces: `static class BearerAuth` with `IApplicationBuilder UseBearerAuth(this IApplicationBuilder app, string token, RequestLog log)`. It compares the `"Bearer "` prefix case-insensitively. The token is compared with `CryptographicOperations.FixedTimeEquals` on UTF-8 bytes (length mismatch means fail). On failure it returns 401 and logs `401 <method> <path>`.
- Produces: `static class ServerHost` with `WebApplication Build(ServerHostSettings settings)`, where `sealed record ServerHostSettings(IPAddress Address, int Port, string Token, RequestLog Log, FileToolsOptions FileOptions, Func<IProcessContainment> Containment, IDetachedLauncher Launcher, IDesktop Desktop)`. It uses Kestrel `Listen(settings.Address, settings.Port)`, registers each of these as a singleton, calls `AddMcpServer().WithHttpTransport(o => o.SessionMode = HttpServerSessionMode.Stateless).WithTools<FileTools>().WithTools<ShellTools>().WithTools<ScreenTools>()`, then `UseBearerAuth`, then `MapMcp("/mcp")`. ASP.NET's own console logging is reduced to warnings.
- Consumes: `FileTools` (Tasks 3–5), `ShellTools`/`ProcessRunner` (Task 6), `IDesktop`/`ScreenTools` (Task 7, with `FakeDesktop` from its tests).

- [ ] **Step 1: Write failing tests.** Each test starts the real host on `IPAddress.Loopback` port 0 (read the bound port from `app.Urls`), with a fake `IDesktop` and a temp dir:
  - `NoToken_401`, `WrongToken_401`, `WrongLengthToken_401`.
  - `RightToken_ListsAllTools`: connect `McpClient` with `HttpClientTransport` at `http://127.0.0.1:<port>/mcp` and an `Authorization` header. The tool names equal the set `{read_file, write_file, edit_file, list_dir, search, powershell, cmd, start_process, list_windows, screenshot, click, type_text, send_keys, scroll, focus_window}`.
  - `ToolError_ReachesClientWithPlainMessage`: `read_file` on a missing path gives `IsError == true`, and the text starts with `Not found:`.
  - `TokenStore_CreatesOnceAndReuses`; `TokenStore_FileIsUserOnly` (Unix mode check); `ServerOptions_DefaultPort47631`; `ServerOptions_ConfigOverridesPort`.
- [ ] **Step 2:** Run the tests. Expected: they fail.
- [ ] **Step 3:** Implement the code above.
- [ ] **Step 4:** Run the tests. Expected: all pass.
- [ ] **Step 5:** Commit with `feat: loopback MCP host with bearer auth`.

### Task 9: Win32 implementations

**Files:**
- Create: `src/WindowsMcp.Win32/WindowsMcp.Win32.csproj` (net10.0-windows, `AllowUnsafeBlocks`, `System.Drawing.Common` 10.x), `NativeMethods.cs`, `Win32Desktop.cs`, `JobObjectContainment.cs`, `DetachedLauncher.cs`, `TokenFileAcl.cs`, `KeepAwake.cs`, `AnsiCodePage.cs`

**Interfaces:**
- Consumes: `IDesktop` (Task 7), `IProcessContainment`/`IDetachedLauncher` (Task 6), `ITokenFileProtector` (Task 8).
- Produces: `Win32Desktop : IDesktop`, `JobObjectContainment : IProcessContainment`, `DetachedLauncher : IDetachedLauncher`, `TokenFileAcl : ITokenFileProtector`, `static class KeepAwake { void Start(); }`, and `static class AnsiCodePage { int Current { get; } }` (`GetACP()`).
- Implementation decisions (these are not obvious from the signatures):
  - **State:** `WTSQuerySessionInformation(WTS_CURRENT_SESSION, WTSConnectState)` not `WTSActive` means `Disconnected`. Otherwise `OpenInputDesktop`, and if `GetUserObjectInformation(UOI_NAME)` is not `"Default"` (or the open fails), the state is `Locked`.
  - **ListWindows:** `EnumWindows`, keeping windows that are `IsWindowVisible` and not DWM-cloaked (`DWMWA_CLOAKED`) and have a non-empty title. The process name comes from `GetWindowThreadProcessId` → `Process.GetProcessById`, with `"?"` on failure.
  - **Capture:**
    - For the screen, GDI `BitBlt` with `SRCCOPY | CAPTUREBLT` over the virtual screen (`SM_XVIRTUALSCREEN`…`SM_CYVIRTUALSCREEN`), which includes the taskbar and tray.
    - For a window, `PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT = 2)`, with a crop of the screen capture at `GetWindowRect` as fallback when that returns 0.
    - For a region, crop the screen capture.
    - When `maxWidth` is smaller than the width, downscale with `HighQualityBicubic`. `Scale` is new width / original width. Encode to PNG.
  - **Input:** `SendInput`. A click moves with `MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK`, normalising to 0–65535 over the virtual screen. Text uses a `KEYEVENTF_UNICODE` down/up pair per UTF-16 unit. Chords press the modifiers, then the key, then release in reverse order. A `SendInput` return value smaller than the number of inputs throws `McpException("Windows blocked the input (an elevated window may have focus)")`.
  - **FocusWindow:** `ShowWindow(SW_RESTORE)` if iconic, then `AllowSetForegroundWindow(ASFW_ANY)`, then a synthetic Alt down/up, then `SetForegroundWindow`. Returns `GetForegroundWindow() == hwnd`.
  - **JobObjectContainment:** `CreateJobObject` with **no** `KILL_ON_JOB_CLOSE` and `AssignProcessToJobObject` right after start (the small race is accepted). `KillAll` is `TerminateJobObject(job, 1)`, and `Dispose` closes the handle.
  - **DetachedLauncher:** `CreateProcess` with `bInheritHandles = false` and `CREATE_NEW_PROCESS_GROUP | CREATE_BREAKAWAY_FROM_JOB | DETACHED_PROCESS` for console apps. If that fails with `ERROR_ACCESS_DENIED` (breakaway not allowed by the Task Scheduler job), fall back to WMI `Win32_Process.Create` through PowerShell `Invoke-CimMethod`. The result's process parent is WmiPrvSE, outside every job. Which path works is checked on the VM in Task 12.
  - **TokenFileAcl:** `FileSecurity` with inheritance off, one rule granting `FullControl` to `WindowsIdentity.GetCurrent().User`, and nothing else.
  - **KeepAwake:** `SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED)`, called on a dedicated thread that lives as long as the process (the state is per-thread).
- [ ] **Step 1:** Implement all of the above. This code can't be unit-tested on Linux, and it is verified by the Task 12 smoke run.
- [ ] **Step 2:** Run `dotnet build src/WindowsMcp.Win32 -c Release`. Expected: `Build succeeded`, 0 warnings.
- [ ] **Step 3:** Commit with `feat: Win32 desktop, job, launcher, ACL, keep-awake`.

### Task 10: The exe

**Files:**
- Create: `src/WindowsMcp/WindowsMcp.csproj` (Exe, net10.0-windows, `AssemblyName=windows-mcp`, `ApplicationManifest=app.manifest`, `RuntimeIdentifier=win-x64`, `SelfContained`, `PublishSingleFile`, `IncludeNativeLibrariesForSelfExtract`), `app.manifest`, `Program.cs`

**Interfaces:**
- Consumes: `ServerHost.Build`, `ServerOptions.Load`, `TokenStore`, `RequestLog`, all Win32 types.
- `app.manifest`: `<dpiAwareness>PerMonitorV2</dpiAwareness>`, `<dpiAware>true/pm</dpiAware>`, and `requestedExecutionLevel asInvoker`.
- `Program.cs`:
  1. Set the data directory to `%LOCALAPPDATA%\windows-mcp` and create it.
  2. Use a single named `Mutex("Local\\windows-mcp")`. If it's already owned, write `windows-mcp is already running for this user.` to stderr and exit with code 2.
  3. Create `RequestLog(<data>\windows-mcp.log)`.
  4. `ServerOptions.Load`, then `TokenStore(..., new TokenFileAcl()).GetOrCreate()`.
  5. `KeepAwake.Start()`.
  6. `ServerHost.Build(new(IPAddress.Loopback, port, token, log, new(AnsiCodePage.Current), () => new JobObjectContainment(), new DetachedLauncher(), new Win32Desktop()))`, then `Run()`.
  7. If the port is in use, log and stderr `Port <p> is in use; set "port" in <data>\config.json` and exit with code 3.
- [ ] **Step 1:** Implement the above.
- [ ] **Step 2:** Run `dotnet publish src/WindowsMcp -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish/win-x64`. Expected: `publish/win-x64/windows-mcp.exe` exists, and `file` reports `PE32+ executable ... x86-64`.
- [ ] **Step 3:** Commit with `feat: windows-mcp.exe entry point`.

### Task 11: Install, update, uninstall, and connection docs

**Files:**
- Create: `install/install.ps1`, `install/update.ps1`, `install/uninstall.ps1`, `docs/connecting.md`, `README.md`

**Decisions:**
- `install.ps1 -Exe <path>` runs as John, with no admin:
  - Copy the exe to `$env:LOCALAPPDATA\Programs\windows-mcp\windows-mcp.exe`.
  - `Register-ScheduledTask -TaskName windows-mcp` with:
    - trigger `New-ScheduledTaskTrigger -AtLogOn -User $env:USERDOMAIN\$env:USERNAME`
    - principal `-LogonType Interactive -RunLevel Limited`
    - settings `-ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) -MultipleInstances IgnoreNew -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable`. **`ExecutionTimeLimit` zero is required**: the default of 72 h would kill the server.
  - `Start-ScheduledTask`.
  - Wait until the token file exists (max 15 s).
  - Print the port and the token-read command.
  - Check `Get-Service sshd`. If it's missing or stopped, print the admin commands to enable it (`Add-WindowsCapability -Online -Name OpenSSH.Server~~~~0.0.1.0`, `Set-Service sshd -StartupType Automatic`, `Start-Service sshd`), and don't run them.
- `update.ps1 -Exe <path>`: `Stop-ScheduledTask windows-mcp`, wait for the process to exit (max 10 s), copy over the exe, `Start-ScheduledTask`, and wait until it's listening (`Test-NetConnection 127.0.0.1 -Port <p>`, max 15 s).
- `uninstall.ps1`: stop and unregister the task, remove the program folder, and keep `%LOCALAPPDATA%\windows-mcp` unless `-RemoveData` is passed.
- `docs/connecting.md`:
  1. Build: the publish command.
  2. Copy: `scp publish/win-x64/windows-mcp.exe install/*.ps1 user@vm:windows-mcp/`.
  3. Install: `ssh user@vm powershell -NoProfile -ExecutionPolicy Bypass -File windows-mcp\install.ps1 -Exe windows-mcp\windows-mcp.exe`.
  4. Read the token: `ssh user@vm type %LOCALAPPDATA%\windows-mcp\token`.
  5. Tunnel: `ssh -N -L 47631:127.0.0.1:47631 user@vm`.
  6. A Claude Code `.mcp.json` example: `{"mcpServers":{"windows":{"type":"http","url":"http://127.0.0.1:47631/mcp","headers":{"Authorization":"Bearer ${WINDOWS_MCP_TOKEN}"}}}}`.
  7. Update: the same as copying, then `update.ps1`.
  8. A note that disconnecting RDP stops the screen tools (from the spec's "Open with John").
- [ ] **Step 1:** Write the scripts and docs.
- [ ] **Step 2:** If `pwsh` is installed locally, run `pwsh -NoProfile -Command "foreach($f in Get-ChildItem install/*.ps1){ $null=[System.Management.Automation.Language.Parser]::ParseFile($f.FullName,[ref]$null,[ref]$e); if($e){$e; exit 1} }"`. Expected: no output. If `pwsh` isn't installed, skip this step and say so in the task report.
- [ ] **Step 3:** Commit with `feat: install, update, uninstall scripts and connection guide`.

### Task 12: Smoke client and VM run (needs the VM address and OpenSSH from John)

**Files:**
- Create: `tests/WindowsMcp.Smoke/WindowsMcp.Smoke.csproj` (net10.0 console, references `ModelContextProtocol` 2.4.0), `Program.cs`, `docs/smoke-checklist.md`

**Interfaces:**
- Run it as `dotnet run --project tests/WindowsMcp.Smoke -- --url http://127.0.0.1:47631/mcp --token-file <path>`. Each check prints `PASS <name>` or `FAIL <name>: <why>`, and the program exits non-zero if any check fails. A scratch folder `%TEMP%\wmcp-smoke` on the box is created through `powershell` and removed at the end.
- Automated checks:
  - `auth_rejects_bad_token`
  - `list_tools_complete` (the 15 names from Task 8)
  - `write_read_edit_roundtrip_crlf`
  - `read_locked_file`: PowerShell holds a file open for writing in a background job while `read_file` reads it.
  - `list_dir_newest_first`
  - `search_content`
  - `powershell_unicode_output`: `Write-Output 'héllo→'` comes back intact.
  - `cmd_exit_code`: `exit /b 7` gives `exit_code=7`.
  - `powershell_prompt_does_not_hang`: `Read-Host 'x'` returns in under 10 s.
  - `powershell_timeout_kills_tree`: `Start-Sleep 120` with `timeout_s=2` gives `timed_out=true`, and no `powershell` child with the marker survives.
  - `start_process_survives`: `start_process notepad.exe`, its PID still exists after 3 s and after `update.ps1`-style `Stop/Start-ScheduledTask`, and the PID is killed in cleanup.
  - `list_windows_shows_notepad`
  - `screenshot_png_nonempty`: the image has the PNG signature and a width above 0.
  - `click_type_into_notepad`: `focus_window` on notepad, screenshot it, `type_text smoke`, screenshot it again, and assert that the two PNGs differ. A human confirms the text in the checklist.
- `docs/smoke-checklist.md` covers the checks a human has to look at or a state that can't be automated:
  - Snapshot the VM first.
  - Run the install over SSH and confirm Defender/SmartScreen doesn't quarantine the exe (if it does, note it; code signing becomes a follow-up).
  - Run the smoke client.
  - Lock the screen (`rundll32 user32.dll,LockWorkStation` over the `powershell` tool), confirm `screenshot` gives the locked error and `list_dir` still works, then unlock by hand.
  - Disconnect RDP if that's how John views the VM, and confirm the same error.
  - Leave the VM idle past its sleep timeout and confirm it stays reachable.
  - **The Sakshi scenario:** `list_dir C:\Users\JB\Videos sort=time descending=true`, `read_file %APPDATA%\Sakshi\state\sakshi.log`, restart Sakshi with `start_process`, and take a screenshot of the tray.
  - Record which `DetachedLauncher` path worked (breakaway or WMI).
- [ ] **Step 1:** Write the smoke client and the checklist. Running `dotnet build tests/WindowsMcp.Smoke` should give `Build succeeded`.
- [ ] **Step 2:** Commit with `test: end-to-end smoke client and VM checklist`.
- [ ] **Step 3 (blocked on John):** Snapshot the VM, deploy per `docs/connecting.md`, and run the smoke client and the checklist. Record the results in `docs/smoke-checklist.md` (date, PASS/FAIL per item, and the launcher path), and fix failures as separate commits, each with a failing check first.
