# Smoke test and VM checklist

This is the first real run of setu on the Windows machine. The smoke client checks most things for you. This page covers the rest: things only a person can look at, or that cannot be automated. Work through it top to bottom and fill in the results table at the end.

Replace `user@vm` with your Windows login and address. The commands for the install, token and tunnel are in [connecting.md](connecting.md).

## 1. Prepare

1. Take a snapshot of the VM, so you can roll back.
2. Build, copy and install as in [connecting.md](connecting.md) steps 1 to 3.
3. **Defender and SmartScreen.** After the install, confirm `%LOCALAPPDATA%\Programs\setu\setu.exe` is still there and was not quarantined or blocked. Success: the install script says "setu is installed and running". If Defender or SmartScreen removes or blocks the exe, note it in the table. Code signing then becomes a follow-up.
4. **No console window at logon.** Log off and back on (or restart the task). Success: setu is running (check Task Manager) and no black console window appeared.
5. **Second start exits with code 2.** While it is running, start a second copy by hand:
   ```
   ssh user@vm 'powershell -NoProfile -Command "$p = Start-Process \"$env:LOCALAPPDATA\Programs\setu\setu.exe\" -Wait -PassThru; \"exit=\" + $p.ExitCode"'
   ```
   Success: it returns straight away with `exit=2`, and the log (`%LOCALAPPDATA%\setu\setu.log`) ends with the line `startup failed: setu is already running for this user.` This copy runs in the SSH session, not on your desktop, so it also proves the check works across sessions.
6. Read the token and open the tunnel ([connecting.md](connecting.md) steps 4 and 5). Save the token to a file only you can read:
   ```
   (umask 077; ssh user@vm 'type %LOCALAPPDATA%\setu\token' > /tmp/wmcp.token)
   ```
   Delete it when you are done: `rm /tmp/wmcp.token`.

## 2. Run the smoke client

Be logged on at the VM's desktop and leave it alone while this runs. It opens Notepad and types into it, and it overwrites the VM clipboard (it copies the typed text to check it).

```
dotnet run --project tests/Setu.Smoke -- --url http://127.0.0.1:47631/mcp --token-file /tmp/wmcp.token
```

Success: every line starts with `PASS`, the last line is `All checks passed.`, and the exit code is 0. A failure prints `FAIL <name>: <why>`. To rerun one check, add `--only <name>`.

Then look at the VM:

- Notepad is closed again, and `%TEMP%\wmcp-smoke` is gone. The client cleans up even when a check fails.
- The client checks that the text typed into Notepad was `smoke` by copying it to the clipboard and reading it back, so there is nothing to confirm by eye. The VM clipboard now holds `smoke`.

## 3. Checks that need you

Use any MCP client for these, for example Claude Code connected as in [connecting.md](connecting.md) step 6. Ask it to call the tool, or call it yourself.

**Console window.** Call `start_process` with path `cmd.exe`. Success: a console window appears on the VM desktop and stays open. Close it by hand.

**Tray screenshot.** Call `screenshot` with `target=screen` and look at the system tray. Success: the tray icons for OBS and Sakshi are visible in the image.

**start_process survives a restart.** Call `start_process` for `notepad.exe` and note the PID. Then run the update over SSH (as in "Updating" in [connecting.md](connecting.md)), reconnect, and call `powershell` with `Get-Process -Id <pid>`. Success: Notepad is still running. (The smoke client checks only the 3 second part of this, because it cannot restart the server it is talking to.) Close Notepad afterwards.

**Programs started from powershell may stop on restart.** Call `powershell` with `(Start-Process notepad -PassThru).Id` and note the PID. Run the update over SSH as above, reconnect, and call `powershell` with `Get-Process -Id <pid>`. Record whether Notepad is still alive. Either result is acceptable: the `powershell` and `cmd` tool descriptions already warn that such programs may be stopped and point to `start_process`. Close Notepad if it is still open.

**Restart after a crash.** The scheduled task is set to restart the server up to 999 times, one minute apart, if it fails. Kill it over SSH:
```
ssh user@vm 'powershell -NoProfile -Command "Stop-Process -Name setu -Force"'
```
Then check every 30 seconds or so, for up to about 2 minutes, whether it is back:
```
ssh user@vm 'powershell -NoProfile -Command "Get-Process setu -ErrorAction SilentlyContinue; Get-Content (Join-Path $env:LOCALAPPDATA setu\setu.log) -Tail 3"'
```
Success: a `setu` process is listed again, and the log has a new `listening on http://127.0.0.1:...` line with a time after the kill. Reopen the tunnel if needed and confirm a `list_dir` call works. Record how long it took. If it has not come back after 2 minutes, record FAIL: Task Scheduler does not treat a killed process as a failure to restart, and the follow-up is to add a repeating trigger (for example every 5 minutes; the task already ignores a new start while one is running). Start it again by hand with `ssh user@vm schtasks /run /tn setu`.

**Locked screen.**
1. Call `powershell` with `rundll32 user32.dll,LockWorkStation`.
2. Call `screenshot`. Success: a tool error saying the screen is locked or the session is disconnected.
3. Call `list_dir` on `C:\Users`. Success: a normal listing.
4. Unlock the VM by hand and call `screenshot` again. Success: an image.

**RDP disconnect** (only if you view the VM over RDP). Disconnect the RDP window without logging off, then call `screenshot`. Success: the same locked/disconnected error, while `list_dir` still works. Reconnect and confirm `screenshot` works again.

**Sleep.** Leave the VM idle past its sleep timeout (or longer than an hour if it never sleeps). Success: a `list_dir` call afterwards still works, with no need to log on again. If the tunnel dropped, reopen it and reconnect; that is not a server failure.

**The Sakshi scenario.**
1. `list_dir` with `path=C:\Users\JB\Videos`, `sort=time`, `descending=true`. Success: the newest recording is the first line.
2. `read_file` with `path=%APPDATA%\Sakshi\state\sakshi.log`. Success: numbered log lines, even while Sakshi is running and writing to it.
3. Stop Sakshi, then call `start_process` with Sakshi's exe path. Success: it returns a PID, and the Sakshi tray icon comes back.
4. `screenshot` the screen and look at the tray. Success: the Sakshi icon is there.

## 4. Which launcher path was used

`start_process` first tries CreateProcess with breakaway, and falls back to WMI if Windows denies that. Every call logs a `start_process NNms ok` line, but only the fallback mentions WMI. On the VM:

```
ssh user@vm 'findstr /c:"WMI" "%LOCALAPPDATA%\setu\setu.log"'
```

The single quotes keep the quotes and backslashes intact on the way through your Linux shell, so the path works even if it contains spaces.

- Lines saying "CreateProcess with breakaway was denied; starting ... through WMI Win32_Process.Create" and "WMI started ... as PID" mean the **WMI** path was used.
- No output at all, after you have started processes, means plain **CreateProcess** worked.

Record which one in the table.

## 5. Results

Fill this in after the run. Use PASS or FAIL, and a short note for anything that failed.

| | |
|---|---|
| Date | |
| VM / Windows version | |
| Launcher path used (CreateProcess or WMI) | |

| Item | Result | Notes |
|---|---|---|
| Snapshot taken | | |
| Install: exe not quarantined by Defender/SmartScreen | | |
| No console window at logon | | |
| Second manual start exits with code 2 | | |
| Smoke: auth_rejects_bad_token | | |
| Smoke: list_tools_complete | | |
| Smoke: write_read_edit_roundtrip_crlf | | |
| Smoke: read_locked_file | | |
| Smoke: list_dir_newest_first | | |
| Smoke: search_content | | |
| Smoke: powershell_unicode_output | | |
| Smoke: powershell_error_plain | | |
| Smoke: cmd_exit_code | | |
| Smoke: powershell_prompt_does_not_hang | | |
| Smoke: powershell_timeout_kills_tree | | |
| Smoke: start_process_survives | | |
| Smoke: list_windows_shows_notepad | | |
| Smoke: screenshot_png_nonempty | | |
| Smoke: click_type_into_notepad | | |
| Smoke: scratch folder and Notepad cleaned up | | |
| start_process cmd.exe shows a visible console window | | |
| Screenshot of the tray shows OBS and Sakshi | | |
| start_process survives update.ps1 (PID still alive) | | |
| powershell Start-Process notepad: alive after update.ps1? (record yes/no) | | |
| Server restarts after Stop-Process (time to come back) | | |
| Locked screen: screenshot errors, list_dir works | | |
| RDP disconnect: screenshot errors, list_dir works | | |
| Idle past sleep timeout: still reachable | | |
| Sakshi: list_dir newest-first on Videos | | |
| Sakshi: read_file on sakshi.log | | |
| Sakshi: restart with start_process, tray icon back | | |
