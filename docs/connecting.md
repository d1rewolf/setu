# Installing windows-mcp and connecting an agent

This guide takes you from source code to an agent on Linux using your Windows machine. Replace `user@vm` with your Windows login and the machine's address. It assumes the Windows machine runs OpenSSH Server; the install script tells you if it doesn't.

Tip: if the Windows machine is a VM, take a snapshot before the first install so you can roll back.

## 1. Build

On the Linux machine, in the repository:

```
dotnet publish src/WindowsMcp -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish/win-x64
```

This produces `publish/win-x64/windows-mcp.exe`, one file that needs nothing else installed on Windows. Success is the command ending with no errors and that file existing.

## 2. Copy to Windows

```
ssh user@vm mkdir windows-mcp
scp publish/win-x64/windows-mcp.exe install/*.ps1 user@vm:windows-mcp/
```

The first line creates a `windows-mcp` folder in your Windows home folder (it prints an error if it already exists; that is fine). The second copies the exe and the three scripts into it.

## 3. Install

```
ssh user@vm powershell -NoProfile -ExecutionPolicy Bypass -File windows-mcp\install.ps1 -Exe windows-mcp\windows-mcp.exe
```

This runs as you, with no admin rights. It copies the exe to `%LOCALAPPDATA%\Programs\windows-mcp`, registers a Task Scheduler task named `windows-mcp` that starts it every time you log on, starts it now, and waits for it to create its token. Success looks like "windows-mcp is installed and running" followed by the port (47631 unless you changed it).

If it warns that OpenSSH Server is missing or not running, you are already connected over SSH to something, but on a fresh machine run these once in an Administrator PowerShell on Windows. The script does not run them for you:

```
Add-WindowsCapability -Online -Name OpenSSH.Server~~~~0.0.1.0
Set-Service sshd -StartupType Automatic
Start-Service sshd
```

If the server does not start, the script prints the last lines of `%LOCALAPPDATA%\windows-mcp\windows-mcp.log`. A startup problem is also logged there:

- "already running": another copy is running for your user.
- "Port ... is in use": set a different `"port"` in `%LOCALAPPDATA%\windows-mcp\config.json`, for example `{"port": 47632}`, then run `update.ps1` again. Use the same port in the tunnel and `.mcp.json` below.

## 4. Read the token

The server created a random token on first start. Read it into an environment variable on the Linux machine, so it is not left in your shell history:

```
export WINDOWS_MCP_TOKEN=$(ssh user@vm type %LOCALAPPDATA%\\windows-mcp\\token)
```

Over OpenSSH on Windows, the remote command runs in cmd by default, so `%LOCALAPPDATA%` expands to the folder on Windows. If your Windows default SSH shell is PowerShell, use `$env:LOCALAPPDATA` instead, with the dollar sign escaped for your local shell:

```
export WINDOWS_MCP_TOKEN=$(ssh user@vm 'type $env:LOCALAPPDATA\windows-mcp\token')
```

Check it worked with `echo ${#WINDOWS_MCP_TOKEN}`, which should print 43. Treat the token like a password. Anyone who has it and can reach the port controls your Windows session.

## 5. Open the tunnel

```
ssh -N -L 47631:127.0.0.1:47631 user@vm
```

This forwards port 47631 on your Linux machine to the server on Windows. It prints nothing and keeps running; leave it open in its own terminal (or add `-f` to run it in the background). Change both port numbers if you changed the port in `config.json`.

Quick check, from another terminal. With the tunnel up, a request without the token must be refused:

```
curl -i http://127.0.0.1:47631/mcp
```

A `401` response means the server is reachable and the token is being enforced.

## 6. Tell Claude Code about it

Put this in `.mcp.json` in your project (or add the same server through your agent's MCP settings):

```json
{
  "mcpServers": {
    "windows": {
      "type": "http",
      "url": "http://127.0.0.1:47631/mcp",
      "headers": {
        "Authorization": "Bearer ${WINDOWS_MCP_TOKEN}"
      }
    }
  }
}
```

Claude Code fills in `${WINDOWS_MCP_TOKEN}` from the environment, so start it from the shell where you ran the `export` in step 4. Run `/mcp` in Claude Code. You should see `windows` connected with 15 tools.

## Updating

Build again (step 1), copy again (step 2), then:

```
ssh user@vm powershell -NoProfile -ExecutionPolicy Bypass -File windows-mcp\update.ps1 -Exe windows-mcp\windows-mcp.exe
```

It stops the running server, replaces the exe, starts it again, and waits until it is listening. The token and config stay as they are. Your tunnel will drop connections briefly; reconnect your agent (`/mcp` in Claude Code) afterwards.

## Uninstalling

```
ssh user@vm powershell -NoProfile -ExecutionPolicy Bypass -File windows-mcp\uninstall.ps1
```

This removes the task and the program. It keeps `%LOCALAPPDATA%\windows-mcp` (token, config, log). Add `-RemoveData` to delete that folder too.

## Limits to know about

- The screen tools (`screenshot`, `click`, `type_text`, `send_keys`, `scroll`, `focus_window`, `list_windows`) need your Windows session to be drawing. If you view the machine over RDP and then disconnect, Windows stops drawing the session and these tools return a locked/disconnected error. The file and shell tools keep working. Reconnecting over RDP fixes it. If this gets in the way, a later step can hand the session back to the console when you disconnect (`tscon ... /dest:console`).
- The server runs only while you are logged on, because it is started at logon and needs your desktop. After a reboot, log in once.
- If something fails, read `%LOCALAPPDATA%\windows-mcp\windows-mcp.log` on Windows.
