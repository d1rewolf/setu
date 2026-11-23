# Installing setu and connecting an agent

This guide takes you from source code to an agent on Linux using your Windows machine or your Mac. Follow [Windows](#windows) or [macOS](#macos). The steps run in the same order on both (the Mac adds one, for granting permissions), and you can set up both and use them side by side.

## Windows

Replace `user@vm` with your Windows login and the machine's address. This section assumes the Windows machine runs OpenSSH Server; the install script tells you if it doesn't.

Tip: if the Windows machine is a VM, take a snapshot before the first install so you can roll back.

### 1. Build

On the Linux machine, in the repository:

```
dotnet publish src/Setu -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish/win-x64
```

This produces `publish/win-x64/setu.exe`, one file that needs nothing else installed on Windows. Success is the command ending with no errors and that file existing.

### 2. Copy to Windows

```
ssh user@vm mkdir setu
scp publish/win-x64/setu.exe install/*.ps1 user@vm:setu/
```

The first line creates a `setu` folder in your Windows home folder (it prints an error if it already exists; that is fine). The second copies the exe and the scripts into it (`install.ps1`, `update.ps1`, `uninstall.ps1` and `common.ps1`, which the install and update scripts load; keep all four together).

### 3. Install

```
ssh user@vm powershell -NoProfile -ExecutionPolicy Bypass -File setu/install.ps1 -Exe setu/setu.exe
```

This runs as you, with no admin rights. It copies the exe to `%LOCALAPPDATA%\Programs\setu`, registers a Task Scheduler task named `setu` that starts it every time you log on, starts it now, and waits for it to create its token. Success looks like "setu is installed and running" followed by the port (47631 unless you changed it).

If you ran the script at the Windows desktop or over RDP instead of over SSH, it may warn that OpenSSH Server is missing or not running. You need it to reach setu from Linux. Run these once in an Administrator PowerShell on Windows; the script does not run them for you:

```
Add-WindowsCapability -Online -Name OpenSSH.Server~~~~0.0.1.0
Set-Service sshd -StartupType Automatic
Start-Service sshd
```

If the server does not start, the script prints the last lines of `%LOCALAPPDATA%\setu\setu.log`. A startup problem is also logged there:

- "startup failed: setu is already running for this user.": another copy is already running for your Windows user, in any session (a copy started over SSH sees the one on your desktop). The second copy exits with code 2.
- "Port ... is in use": set a different `"port"` in `%LOCALAPPDATA%\setu\config.json`, for example `{"port": 47632}`, then run `update.ps1` again. Use the same port in the tunnel and `.mcp.json` below.

### 4. Read the token

The server created a random token on first start. Read it into an environment variable on the Linux machine, so it is not left in your shell history:

```
export SETU_TOKEN=$(ssh user@vm 'type %LOCALAPPDATA%\setu\token')
```

The single quotes matter: they stop your Linux shell from changing the backslashes. Over OpenSSH on Windows, the remote command runs in cmd by default, so `%LOCALAPPDATA%` expands to the folder on Windows. If your Windows default SSH shell is PowerShell, use `$env:LOCALAPPDATA` instead. The single quotes also stop your Linux shell from expanding `$env`, so nothing needs escaping:

```
export SETU_TOKEN=$(ssh user@vm 'type $env:LOCALAPPDATA\setu\token')
```

Check it worked with `echo ${#SETU_TOKEN}`, which should print 43. Treat the token like a password. Anyone who has it and can reach the port controls your Windows session.

### 5. Open the tunnel

```
ssh -N -L 47631:127.0.0.1:47631 user@vm
```

This forwards port 47631 on your Linux machine to the server on Windows. It prints nothing and keeps running; leave it open in its own terminal (or add `-f` to run it in the background). Change both port numbers if you changed the port in `config.json`.

Quick check, from another terminal. With the tunnel up, a request without the token must be refused:

```
curl -i http://127.0.0.1:47631/mcp
```

A `401` response means the server is reachable and the token is being enforced.

### 6. Tell Claude Code about it

Put this in `.mcp.json` in your project (or add the same server through your agent's MCP settings):

```json
{
  "mcpServers": {
    "windows": {
      "type": "http",
      "url": "http://127.0.0.1:47631/mcp",
      "headers": {
        "Authorization": "Bearer ${SETU_TOKEN}"
      }
    }
  }
}
```

Claude Code fills in `${SETU_TOKEN}` from the environment, so start it from the shell where you ran the `export` in step 4. Run `/mcp` in Claude Code. You should see `windows` connected with 15 tools.

### Updating

Build again (step 1), copy again (step 2), then:

```
ssh user@vm powershell -NoProfile -ExecutionPolicy Bypass -File setu/update.ps1 -Exe setu/setu.exe
```

It stops the running server, replaces the exe, starts it again, and waits until it is listening. The token and config stay as they are. Your tunnel will drop connections briefly; reconnect your agent (`/mcp` in Claude Code) afterwards.

### Uninstalling

```
ssh user@vm powershell -NoProfile -ExecutionPolicy Bypass -File setu/uninstall.ps1
```

This removes the task and the program. It keeps `%LOCALAPPDATA%\setu` (token, config, log). Add `-RemoveData` to delete that folder too.

### Limits to know about

- The screen tools (`screenshot`, `click`, `type_text`, `send_keys`, `scroll`, `focus_window`) need your Windows session to be drawing. If you view the machine over RDP and then disconnect, Windows stops drawing the session and these tools return a locked/disconnected error. The file and shell tools keep working, and so does `list_windows`. Reconnecting over RDP fixes it. If this gets in the way, a later step can hand the session back to the console when you disconnect (`tscon ... /dest:console`).
- While a window running as administrator (for example an "Administrator: Windows PowerShell" terminal) is in front, Windows blocks mouse and keyboard input from setu. The input tools (`click`, `type_text`, `send_keys`, `scroll`, `focus_window`) then return an error saying so; click another window or minimise that one. `screenshot` and `list_windows` keep working.
- Programs started from the `powershell` or `cmd` tools may be stopped when the server restarts (for example during an update). Use `start_process` for apps that must keep running.
- The server runs only while you are logged on, because it is started at logon and needs your desktop. After a reboot, log in once.
- If something fails, read `%LOCALAPPDATA%\setu\setu.log` on Windows.

## macOS

Setu runs on macOS 15 or later on Apple silicon. Replace `<mac>` in the commands with the Mac's address. They log in as `jb`; use your own login on the Mac if it is different.

You need to be logged in at the Mac's desktop while you install, because Setu runs in your desktop session. Sign-in over SSH (Remote Login in System Settings › General › Sharing) must be on. You will also need to sit at the Mac once, in step 4, to grant permissions; after that, everything works over SSH, including updates.

### 1. Build

On the Linux machine, in the repository:

```
dotnet publish src/Setu.MacApp -c Release -r osx-arm64 --self-contained -p:PublishSingleFile=true -o publish/osx-arm64
```

This produces `publish/osx-arm64/setu`, one file that needs nothing else installed on the Mac. The Mac does not need .NET. Success is the command ending with no errors and that file existing.

### 2. Copy to the Mac

```
ssh jb@<mac> 'mkdir -p ~/setu'
scp publish/osx-arm64/setu install/mac/*.sh jb@<mac>:setu/
```

The first line creates a `setu` folder in your home folder on the Mac (it does nothing if the folder already exists). The second copies the binary and the scripts into it (`install.sh`, `update.sh`, `uninstall.sh` and `common.sh`, which the other three load; keep all four together).

### 3. Install

```
ssh jb@<mac> 'zsh ~/setu/install.sh --binary ~/setu/setu --version 0.2.0'
```

This runs as you, with no admin rights. It:

- builds the app `~/Applications/Setu.app` from the binary;
- signs it with a certificate it creates on the Mac the first time ("Creating Setu's signing certificate (once per Mac)..."). The certificate is kept in `~/Library/Keychains/setu-signing.keychain-db` and never leaves the Mac. Every later update is signed with it, which is what lets the permissions from step 4 carry over;
- registers a LaunchAgent named `com.greatworx.setu` that starts Setu every time you log in, and starts it again if it stops;
- starts it now and waits until it is listening.

`--version` only sets the version number the app reports; it is 0.1.0 if you leave it out.

Success looks like "Setu is installed and running on port 47631." (47631 unless you changed the port), followed by how to read the token and the permission steps below. If a permission is missing, the script also opens System Settings on the Mac's screen. macOS shows a "Background Items Added" notification at the Mac; it only tells you Setu will start at login, and needs nothing from you.

If something goes wrong, the script says what:

- "there is no desktop session for jb": nobody is logged in at the Mac. Log in at the Mac's desktop and run the command again.
- "Setu is not listening on port ... after 30 seconds": it prints the last lines of Setu's log, `~/Library/Application Support/setu/setu.log`. "startup failed: setu is already running for this user." means another copy is running already. "Port ... is in use" means you should set a different `"port"` in `~/Library/Application Support/setu/config.json`, for example `{"port": 47633}`, then run the install again. Use the same port in the tunnel and `.mcp.json` below.

### 4. Grant the permissions at the Mac

macOS asks a person at the Mac, not a script, before an app can see the screen or send input. Do this once, sitting at the Mac. Setu asked for each permission when it started, so it is already in the lists and you only turn on switches:

1. In System Settings › Privacy & Security › **Screen & System Audio Recording**, turn on Setu. Setu needs it for `screenshot`, and for `list_windows` to read window titles.
2. In System Settings › Privacy & Security › **Accessibility**, turn on Setu. Setu needs it for `click`, `type_text`, `send_keys`, `scroll` and `focus_window`. If Setu is not in the list, add it with the + button; it is `~/Applications/Setu.app`.
3. **Local Network**: macOS shows a prompt asking whether Setu may find and connect to devices on your local network. Click Allow. If you missed the prompt, turn Setu on in System Settings › Privacy & Security › Local Network. Without it, commands the agent runs cannot reach other machines on your network (see the limits below).

macOS applies the new permissions only after Setu restarts. Restart it, at the Mac in Terminal or from Linux:

```
ssh jb@<mac> 'launchctl kickstart -k gui/$(id -u)/com.greatworx.setu'
```

Stay at the Mac for two more kinds of prompt:

- The first screenshot after the grant brings up "“Setu” is requesting to bypass the system private window picker and directly access your screen and audio". Click Allow. You can trigger it once the agent is connected (step 7) by asking it for a screenshot.
- The first time anything Setu runs touches a folder macOS protects (Desktop, Documents, Downloads, iCloud Drive and others; the full list is under the limits below), macOS asks whether Setu may access it, and the command waits for the answer. Click **Don't Allow**, once per folder. After that, access fails at once with "Operation not permitted" and nothing waits again. Setu's file tools never cause these prompts; they come from shell commands. To get them over with while you are at the Mac, ask the agent to run `ls ~/Documents` through `shell`, and the same for `~/Desktop` and `~/Downloads`.

The permissions stay through updates. They are lost only if you uninstall with `--remove-data`, which deletes the signing certificate.

### 5. Read the token

Setu created a random token on first start. Read it into an environment variable on the Linux machine, so it is not left in your shell history:

```
export SETU_MAC_TOKEN=$(ssh jb@<mac> 'cat ~/Library/Application\ Support/setu/token')
```

The single quotes pass the backslash through to the Mac, where it keeps the space in "Application Support" from splitting the path. The variable is named `SETU_MAC_TOKEN` so it can sit beside the Windows `SETU_TOKEN`; each machine has its own token.

Check it worked with `echo ${#SETU_MAC_TOKEN}`, which should print 43. Treat the token like a password. Anyone who has it and can reach the port controls your Mac session.

### 6. Open the tunnel

```
ssh -N -L 47632:127.0.0.1:47631 jb@<mac>
```

This forwards port 47632 on your Linux machine to Setu on port 47631 on the Mac. The local port is 47632 so that this tunnel and the Windows one (on 47631) can run at the same time. It prints nothing and keeps running; leave it open in its own terminal (or add `-f` to run it in the background). If you changed the port in `config.json`, change the second number.

### 7. Tell Claude Code about it

Put this in `.mcp.json` in your project (or add the same server through your agent's MCP settings). If you also use Windows, add the `mac` entry next to `windows` in the same `mcpServers` block:

```json
{
  "mcpServers": {
    "mac": {
      "type": "http",
      "url": "http://127.0.0.1:47632/mcp",
      "headers": {
        "Authorization": "Bearer ${SETU_MAC_TOKEN}"
      }
    }
  }
}
```

Claude Code fills in `${SETU_MAC_TOKEN}` from the environment, so start it from the shell where you ran the `export` in step 5. Run `/mcp` in Claude Code. You should see `mac` connected with 14 tools.

### Updating

Build again (step 1), copy again (step 2), then:

```
ssh jb@<mac> 'zsh ~/setu/update.sh --binary ~/setu/setu --version 0.2.1'
```

It signs the new build with the same certificate, stops Setu, swaps in the new app, starts it again, and waits until it is listening. Success looks like "Setu is updated to version 0.2.1 and running on port 47631." The token, config and permissions stay as they are, so nobody needs to be at the Mac. Your tunnel will drop connections briefly; reconnect your agent (`/mcp` in Claude Code) afterwards.

### Uninstalling

```
ssh jb@<mac> 'zsh ~/setu/uninstall.sh'
```

This stops Setu and removes the app and the LaunchAgent. It keeps `~/Library/Application Support/setu` (token, config, log) and the signing certificate, so a later install keeps the permissions. To delete those too, run `zsh ~/setu/uninstall.sh --remove-data` the same way. Setu's permissions are then gone, and a new install needs step 4 again. The script also tries to remove Setu from the Screen Recording and Accessibility lists; if macOS does not let it, it tells you to remove Setu from both lists in System Settings by hand.

### Limits to know about on macOS

- **macOS asks again about once a month.** On macOS 15, the "bypass the system private window picker" prompt from step 4 comes back about monthly. Screenshots keep working while it waits, but the prompt can show up inside them, and someone at the Mac must click Allow.
- **After a restart, nothing runs until you log in.** FileVault keeps the disk locked until someone logs in at the Mac, so Setu, and SSH, are unreachable until then. Once you log in, Setu starts by itself.
- **Protected folders are off-limits.** Setu never works in `~/Desktop`, `~/Documents`, `~/Downloads`, iCloud Drive (`~/Library/Mobile Documents`), `~/Library/Mail`, `~/Library/Messages`, `~/Library/Safari`, other apps' data (`~/Library/Containers`, `~/Library/Group Containers`), `~/Library/Calendars`, `~/Library/CloudStorage`, `~/Library/Photos`, `~/Pictures/Photos Library.photoslibrary`, the TV and Music libraries (`~/Movies/TV`, `~/Music/Music`), or removable and network volumes (`/Volumes/*`). The file tools refuse these by name without touching them, and `list_dir` and `search` skip them and say which they skipped. Shell commands that reach one fail with "Operation not permitted" once you have clicked Don't Allow (step 4). Before that, the command waits for the prompt at the Mac until its timeout. A file tool call that is still waiting after 120 seconds stops and says a folder may be waiting on a permission prompt. To work on a file in one of these folders, copy it somewhere else at the Mac first.
- **Closing the lid sleeps the Mac.** Setu is unreachable until the lid is opened again.
- **The display never sleeps from idle while Setu runs.** Setu keeps the Mac and its display awake, so the screen does not lock by itself and the screen tools keep working unattended. `pmset -g assertions` on the Mac shows Setu's two assertions. On battery, this drains it. Locking the screen by hand still works.
- **A locked screen stops the screen tools.** `screenshot`, `click`, `type_text`, `send_keys`, `scroll` and `focus_window` return "The screen is locked or another user is on the console; file and shell tools still work." `list_windows` and the file and shell tools keep working. Unlock at the Mac to use the screen again.
- **Password fields do not block typing.** While a password field or Terminal's Secure Keyboard Entry is active, `type_text` and `send_keys` still type, and add a note naming the app that holds secure keyboard entry.
- **Local Network needs its permission.** Without it (step 4), any shell command that reaches another machine on your network (git, curl or ssh to a LAN address, `.local` names) fails as if the network were down, even though the same command works over plain SSH.
- **Commands run with Setu's permissions.** Everything `shell` runs, and programs `start_process` starts directly, get Setu's macOS permissions, and any permission prompt they cause names Setu. Apps started by name or as a `.app` (for example `TextEdit`) have their own. So if you are testing a command-line program's own permission prompts, running it through Setu does not show what a user would see.
- **Programs started from `shell` may be stopped when Setu restarts** (for example during an update). Use `start_process` for apps that must keep running.
- **Shortcuts use `cmd`.** `send_keys` takes `cmd`, `ctrl`, `option` (or `alt`) and `shift`, for example `cmd+s`, and presses keys by their position on a US keyboard.
- If something fails, read `~/Library/Application Support/setu/setu.log` on the Mac. Output from the LaunchAgent itself goes to `launchd.log` in the same folder.
