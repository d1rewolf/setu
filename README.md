# windows-mcp

An MCP server that runs on a Windows machine, in your logged-on desktop session, and lets an agent on another machine (for example Claude Code on Linux) work on it. The agent can read and edit files, run PowerShell and cmd commands, start programs, and see and drive the screen.

It listens on `127.0.0.1` only and requires a bearer token on every request. You reach it from the agent's machine through an SSH tunnel (`ssh -L`), so nothing is exposed to the network. It starts at logon through a Task Scheduler task and needs no admin rights to install.

To install it and connect an agent, follow [docs/connecting.md](docs/connecting.md).

## Tools

| Tool | What it does |
| --- | --- |
| `read_file` | Reads a text file with numbered lines. Returns up to 2000 lines and 256 KiB. |
| `write_file` | Writes a text file, keeping the existing encoding and line endings. |
| `edit_file` | Replaces an exact string in a text file, leaving every other byte alone. |
| `list_dir` | Lists a directory (500 entries by default), with optional pattern and sort. |
| `search` | Searches file contents with a regex (200 matches by default, 2000 at most). |
| `powershell` | Runs a Windows PowerShell command and waits. Timeout 60 s by default, 600 s at most. |
| `cmd` | Runs a cmd.exe command line and waits. Same timeouts. |
| `start_process` | Starts a program in your desktop session and returns its PID. |
| `list_windows` | Lists the open windows. |
| `screenshot` | Captures the screen or a window as an image. |
| `click` | Clicks at a screen position. |
| `type_text` | Types text into the focused window. |
| `send_keys` | Sends key presses and shortcuts. |
| `scroll` | Scrolls at a screen position. |
| `focus_window` | Brings a window to the front. |

Shell output is capped at 64 KiB per stream. Errors come back to the agent as plain tool errors.

## Build and test

Needs the .NET 10 SDK (pinned in `global.json`). Everything builds on Linux too.

```
dotnet build
dotnet test
dotnet publish src/WindowsMcp -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish/win-x64
```

The last command produces `publish/win-x64/windows-mcp.exe`, a single file with no runtime to install. The tests that need Windows only run on Windows.

## Install, update, uninstall

The PowerShell scripts are in `install/` and run as your normal Windows user:

- `install.ps1 -Exe <path>` copies the exe to `%LOCALAPPDATA%\Programs\windows-mcp`, registers the logon task, starts it, and checks for OpenSSH Server.
- `update.ps1 -Exe <path>` stops it, swaps in the new exe, and starts it again.
- `uninstall.ps1` removes the task and program. It keeps the token, config and log in `%LOCALAPPDATA%\windows-mcp` unless you add `-RemoveData`.

Settings: the port defaults to 47631 and can be changed with `{"port": 12345}` in `%LOCALAPPDATA%\windows-mcp\config.json`. The log is `%LOCALAPPDATA%\windows-mcp\windows-mcp.log`.
