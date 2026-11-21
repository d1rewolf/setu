# windows-mcp — design

Date: 2026-11-21. Bahu todo #70 (direction of 2026-11-21). Supersedes the FoxPro-plugin design of 2026-11-01 and the foxpro_mcp path of #66.

## Intent

John wants an agent in an ordinary Linux Bahu session to work on a Windows box: read and edit files, run PowerShell and cmd, see the screen, and click and type. He wants this to be **our own** MCP server, not a third-party one (no Desktop Commander, Windows-MCP, mcp-proxy, or the official filesystem server). FoxPro support is dropped.

The first real job is debugging Sakshi on John's Windows machine. Its transcript says "Sakshi could not find OBS's recording in C:\Users\JB\Videos" (sakshi-net, `TrayHost.FindRecordingAsync`, which picks the newest video by last-write time). The agent needs to list files with times, read `%APPDATA%\Sakshi\state\sakshi.log`, run PowerShell (for example, to restart Sakshi), and see the tray and OBS on screen.

Success: a Bahu session on Linux, given this server, can do that debugging without John touching the Windows box beyond the one-time install.

## Goals

- One self-contained `windows-mcp.exe` that John installs once and that then runs whenever he is logged on.
- The tools listed below, reliable enough that the agent can work unattended: a command can't hang the server, an edit can't mangle a file's encoding, and a failure is an error the agent sees.
- Reachable from Linux only through an SSH tunnel, guarded by a token.

## Non-goals

- Bahu-side wiring (the Windows machine type, tunnel setup in sessions, the guideline, blocking local Read/Edit). That comes next, in bahu-net, as its own piece of work.
- A plugin system. FoxPro, foxpro_mcp, and moving tools from it.
- Headless mode (started per session over plain SSH). Rejected below.
- Clicking controls by name (Windows UI Automation), clipboard, binary file transfer. Easy to add later. Not needed for the first job.
- Multiple simultaneous users or desktops per box.

## Anti-goals (would make this a failure even if it works)

- Listening on anything but loopback, or accepting requests without the token.
- An edit that silently changes a file's encoding, BOM, or line endings.
- A shell command that waits for input, or never exits, and blocks the server or the agent indefinitely.
- Depending on a third-party MCP server, or needing Python or another runtime installed on the box.

## Constraints

- Windows 10/11 x64. John's box is a VM. Its address and whether OpenSSH Server is installed are **still unknown** (needed from John before the end-to-end test, not before building).
- Development happens on Linux (.NET SDK 10.0.114 installed). The Windows-only parts can only be tested on the box.
- Processes started under Windows OpenSSH run in a non-interactive session: they cannot see or draw on the logged-on desktop, and GUI apps they launch are invisible. This is why the server runs in the user's session (below).
- UIPI: a normal-integrity process cannot send input to, or reliably screenshot, elevated windows. The server runs at normal integrity, as John. Clicking UAC prompts and admin windows is out of reach. That is accepted.
- Same conventions as sakshi-net and bahu-net: .NET 10, nullable on, warnings as errors, xUnit v3.

## Approaches considered

**Language.** (John chose .NET.)
- .NET: same toolchain as Bahu and Sakshi, ships as one self-contained exe with nothing to install on the box, and has direct access to Win32 for input and screenshots.
- Python: could reuse foxpro_mcp code and pywinauto, but needs Python and packages on the box and adds a second language to maintain.

**Protocol.** (John chose the SDK.)
- The official MCP C# SDK (`ModelContextProtocol`, `ModelContextProtocol.AspNetCore`, stable since 1.0 on 2026-03-05, semver). It is a library inside our server, not a third-party server. It tracks the MCP spec for us.
- Hand-written JSON-RPC: no outside dependency, but streamable HTTP, sessions, and version negotiation become ours to build and keep current.

**How it runs.** (John chose desktop only.)
- Desktop only: one process started at logon in John's session, listening on loopback, reached through `ssh -L`. Everything it launches is visible on the desktop, and one startup path to build and test. The cost: no access when nobody is logged on.
- Desktop plus headless: files and shell would also work when nobody is logged on, but that means two startup paths and a tool list that differs by mode. It also rests on #66's untested SSH-session assumption, and GUI apps launched headless would be invisible, which breaks the Sakshi use case.

## Decided design

### Process and startup

- `windows-mcp.exe`, `net10.0-windows`, published self-contained single-file for `win-x64`.
- Started by a Task Scheduler task "at log on of John's user", running as that user, "only when user is logged on" (interactive, so it sees the desktop), normal integrity (not "highest privileges"), and restarted on failure.
- Per-monitor DPI aware (v2), so screenshot pixels and click coordinates are the same physical pixels.
- One instance per user. A second start exits with a clear message.
- Keeps the machine awake: while running, it calls `SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED)` so Windows does not sleep (John, 2026-11-21: "ask windows never to sleep"). The display may still turn off and the screen may still lock.
- Locked or disconnected desktop: the screen tools (`screenshot`, `click`, `type_text`, `send_keys`, `scroll`, `focus_window`) check whether the input desktop is available. If it isn't, they return a plain error ("the screen is locked or the session is disconnected") instead of a black image or a silent no-op. File and shell tools keep working.
- Log file in `%LOCALAPPDATA%\windows-mcp\` (each request's tool, duration, and outcome, but not file contents or command output).

### Transport and auth

- Streamable HTTP via `ModelContextProtocol.AspNetCore` on Kestrel, bound to `127.0.0.1` only, on a fixed port (default to be picked by the builder from the unassigned range, overridable in a config file next to the token).
- Token: on first start, 32 random bytes from a CSPRNG, base64url, written to `%LOCALAPPDATA%\windows-mcp\token` with an ACL that grants only the current user. Every request must carry `Authorization: Bearer <token>`. Compare it in constant time. A missing or wrong token gets 401 and a log line.
- Reaching it from Linux: `ssh -L <local>:127.0.0.1:<port> user@box`, and the token is read once with `ssh user@box type %LOCALAPPDATA%\windows-mcp\token`. Bahu will automate this later. For now it is a documented manual step, plus a Claude Code `.mcp.json` example using `"type": "http"` and an `Authorization` header.

### Tools (first version)

Errors come back as MCP tool errors with a plain message (file not found, timeout after N s, and so on), never as a crash or a hang.

**Files.** All paths are absolute Windows paths. Environment variables like `%APPDATA%` are expanded.
- `read_file(path, offset?, limit?)`: text with line numbers. The result reports the detected encoding (UTF-8, UTF-8 BOM, UTF-16 LE/BE, or the system ANSI code page as a fallback) and line endings (CRLF/LF/mixed). It refuses binary files with a clear message. Output is capped.
- `write_file(path, content, encoding?, line_endings?)`: creates or overwrites a file. For an existing file it keeps that file's encoding and line endings unless told otherwise. For a new file the default is UTF-8 without BOM and CRLF.
- `edit_file(path, old_string, new_string, replace_all?)`: exact find-and-replace. Fails if `old_string` is missing, or appears more than once without `replace_all`. Writes back in the file's original encoding, BOM, and line endings ("byte-safe": bytes outside the edited span are unchanged). The write is atomic (temp file, then replace).
- `list_dir(path, pattern?, sort?, limit?)`: name, type, size, last-write time (local, with offset), sortable by name, time, or size. This is the Sakshi "newest video" check.
- `search(path, name_pattern?, content_regex?, limit?)`: recursive search by file name glob and/or content regex. Returns path and line matches. Skips binary files. Capped.

**Shell.**
- `powershell(command, cwd?, timeout_s?)` and `cmd(command, cwd?, timeout_s?)`: run as John in his session (so GUI apps they start are visible). Stdin is closed, so a prompt fails instead of waiting. Default timeout 60 s, with a maximum (builder's call). On timeout the whole process tree is killed (a Job Object) and the result says so. Returns exit code, stdout, and stderr, each capped with a "truncated" note. PowerShell runs as `powershell.exe -NoProfile -NonInteractive` (Windows PowerShell 5.1, always present).
- *Builder's-check call:* programs a command launches must be able to outlive it (restarting Sakshi is part of the first job). The Job Object is used **only** to kill the tree on timeout. There is no kill-on-close, and leftover children survive a normal exit and a server restart. The result is returned when the main process exits, after a short grace period for draining output. A detached child holding the inherited pipes must not make the command look hung.
- `start_process(path, args?, cwd?)`: launches a program detached (no handle inheritance, not in any job) in John's session and returns its PID. This is the obvious way to start GUI apps such as Sakshi or OBS.

**Screen.**
- `list_windows()`: visible top-level windows with handle, title, process name, PID, rectangle, and whether minimized or foreground.
- `screenshot(target?: "screen" | window handle | region, max_width?)`: PNG returned as MCP image content. It covers the whole virtual screen by default, and is downscaled to `max_width` when given. The result states the scale factor so the agent can map back to click coordinates.
- `click(x, y, button?: left|right|middle, double?)`: screen coordinates in physical pixels, sent with `SendInput`.
- `type_text(text)`: Unicode text sent with `SendInput` (KEYEVENTF_UNICODE) to the foreground window.
- `send_keys(keys)`: key chords such as `ctrl+s`, `alt+f4`, `win+r`, `enter`, `tab`. The builder defines the syntax and documents it in the tool description.
- `scroll(x, y, amount)`: mouse wheel at a point.
- `focus_window(handle)`: bring a window to the foreground before typing. Windows restricts SetForegroundWindow. The builder should use the known workarounds and report failure honestly.

### Install

`install.ps1`, run once by John as himself: copies the exe to `%LOCALAPPDATA%\Programs\windows-mcp\`, registers the logon task, starts it, and prints the port and how to read the token. It also checks for OpenSSH Server and prints how to enable it if missing (and does not enable it silently, since that needs admin). `uninstall.ps1` reverses it.

*Builder's-check call:* `update.ps1` runs over SSH as John with no admin. It stops the logon task (`schtasks /End`), swaps the exe, and starts the task again (`schtasks /Run`). John never has to touch the box to update. The exe is unsigned at first. Whether Defender or SmartScreen objects gets checked at the first install. Code signing is added only if it does.

### Code shape

- `src/WindowsMcp` (the exe: hosting, auth, tool registration), `src/WindowsMcp.Core` (file encoding, edit, list, search, and process running: logic that can be unit-tested on Linux where possible), and Win32 interop (input, windows, screenshots, Job Objects) behind small interfaces.
- Tests: xUnit v3. Encoding detection and byte-safe edit, auth middleware, output capping, and argument validation run on Linux. Win32 and process-tree behaviour get tests that run only on Windows (skipped elsewhere), plus a short manual checklist on the box: the Sakshi scenario end to end.

## Left to the builder

The port number; caps and maximum timeout values; `send_keys` syntax; screenshot implementation (GDI BitBlt vs Windows.Graphics.Capture), but it must capture the whole virtual screen including the tray; how to detect binary files; the exact ACL code; the project and folder layout beyond the outline above; and how the release exe is built and copied to the box.

## Test box

John's Windows machine is a VM, and he can snapshot it. Take a snapshot before the first install and before risky tests. Roll back instead of hand-repairing. The Windows-only tests and the manual checklist run there.

## Open with John

- The VM's address, and whether OpenSSH Server is installed (needed for the end-to-end test).
- How John views the VM. Over RDP, disconnecting stops the session from drawing, and the screen tools then return the locked/disconnected error. For v1 this is accepted (no admin setup). If it gets in the way, add a step that hands the session back to the console when John disconnects (`tscon ... /dest:console`).
