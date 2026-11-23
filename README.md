<p align="center"><img src="docs/assets/setu-logo.svg" width="128" alt="Setu logo: a stone arch bridge over water"></p>

# Setu

Setu (सेतु, Sanskrit for "bridge") is an MCP server that runs on a Windows machine or a Mac, in your logged-on desktop session, and lets an agent on another machine (for example Claude Code on Linux) work on it. The agent can read and edit files, run shell commands (PowerShell and cmd on Windows, zsh on macOS), start programs, and see and drive the screen.

It listens on `127.0.0.1` only and requires a bearer token on every request. You reach it from the agent's machine through an SSH tunnel (`ssh -L`), so nothing is exposed to the network. It needs no admin rights to install. On Windows it starts at logon through a Task Scheduler task; on macOS (15 or later, Apple silicon) it runs as a LaunchAgent, starts at login, and is restarted if it stops.

To install it and connect an agent, follow [docs/connecting.md](docs/connecting.md). It has a section for each platform.

## Tools

Windows has 15 tools and macOS has 14. They are the same except for the shell: Windows has `powershell` and `cmd`, and macOS has `shell` instead.

| Tool | What it does | Platform |
| --- | --- | --- |
| `read_file` | Reads a text file with numbered lines. Returns up to 2000 lines and 256 KiB. | Both |
| `write_file` | Writes a text file, keeping the existing encoding and line endings. | Both |
| `edit_file` | Replaces an exact string in a text file, leaving every other byte alone. | Both |
| `list_dir` | Lists a directory (500 entries by default), with optional pattern and sort. | Both |
| `search` | Searches file contents with a regex (200 matches by default, 2000 at most). | Both |
| `powershell` | Runs a Windows PowerShell command and waits. Timeout 60 s by default, 600 s at most. | Windows only |
| `cmd` | Runs a cmd.exe command line and waits. Same timeouts. | Windows only |
| `shell` | Runs a zsh command as a login shell, so `PATH` matches Terminal, and waits. Same timeouts. | macOS only |
| `start_process` | Starts a program in your desktop session and returns its PID. | Both |
| `list_windows` | Lists the open windows. | Both |
| `screenshot` | Captures the screen or a window as an image. | Both |
| `click` | Clicks at a screen position. | Both |
| `type_text` | Types text into the focused window. | Both |
| `send_keys` | Sends key presses and shortcuts. | Both |
| `scroll` | Scrolls at a screen position. | Both |
| `focus_window` | Brings a window to the front. | Both |

Shell output is capped at 64 KiB per stream. Errors come back to the agent as plain tool errors. On macOS the file tools refuse the folders macOS guards with a privacy prompt (Desktop, Documents, Downloads, iCloud Drive and others), so an agent's call never waits on a prompt nobody is there to answer; [docs/connecting.md](docs/connecting.md#limits-to-know-about-on-macos) lists them.

## Build and test

Needs the .NET 10 SDK (pinned in `global.json`). Everything builds on Linux too.

```
dotnet build
dotnet test
dotnet publish src/Setu -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish/win-x64
dotnet publish src/Setu.MacApp -c Release -r osx-arm64 --self-contained -p:PublishSingleFile=true -o publish/osx-arm64
```

The two publish commands produce `publish/win-x64/setu.exe` for Windows and `publish/osx-arm64/setu` for macOS. Each is a single file with no runtime to install. The Mac needs no .NET SDK: you build on Linux and the macOS install script turns the file into a signed app on the Mac. The tests that need Windows only run on Windows; the macOS behaviour is checked on the Mac with the smoke client ([docs/smoke-checklist-mac.md](docs/smoke-checklist-mac.md)).

## Install, update, uninstall

### Windows

The PowerShell scripts are in `install/` and run as your normal Windows user:

- `install.ps1 -Exe <path>` copies the exe to `%LOCALAPPDATA%\Programs\setu`, registers the logon task, starts it, and checks for OpenSSH Server.
- `update.ps1 -Exe <path>` stops it, swaps in the new exe, and starts it again.
- `uninstall.ps1` removes the task and program. It keeps the token, config and log in `%LOCALAPPDATA%\setu` unless you add `-RemoveData`.

Settings: the port defaults to 47631 and can be changed with `{"port": 12345}` in `%LOCALAPPDATA%\setu\config.json`. The log is `%LOCALAPPDATA%\setu\setu.log`.

### macOS

The zsh scripts are in `install/mac/` and run as your normal Mac user, over SSH, while you are logged in at the Mac:

- `install.sh --binary <path> [--version X.Y.Z]` builds `~/Applications/Setu.app` from the binary, signs it with a certificate it creates on the Mac the first time, registers the LaunchAgent `com.greatworx.setu`, starts it, and tells you which permissions to turn on at the Mac.
- `update.sh --binary <path> [--version X.Y.Z]` signs the new build with the same certificate, swaps it in and restarts Setu. Because the certificate is the same, the permissions you granted carry over.
- `uninstall.sh` stops Setu and removes the app and the LaunchAgent. It keeps the token, config, log and signing certificate unless you add `--remove-data`; after that, a new install needs the permissions granted again.

`--version` only sets the version number the app reports; it is 0.1.0 if you leave it out.

Settings: the port defaults to 47631 and can be changed with `{"port": 12345}` in `~/Library/Application Support/setu/config.json`. The log is `~/Library/Application Support/setu/setu.log`.
