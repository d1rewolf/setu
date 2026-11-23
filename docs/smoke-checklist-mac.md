# Smoke test and Mac checklist

This is the real-world check of Setu on the Mac. The smoke client checks most things for you. This page covers the rest: things only a person can look at, or that cannot be automated. Work through it top to bottom and fill in the results table at the end. The Windows version is [smoke-checklist.md](smoke-checklist.md).

Replace `<mac>` with the Mac's address. The commands for the install, permissions, token and tunnel are in [connecting.md › macOS](connecting.md#macos).

## 1. Prepare

1. **Before the permissions are granted.** On a Mac where Setu has never had its permissions:
   - Call `screenshot`. Success: a tool error starting "Setu needs Screen Recording permission".
   - Call `click` anywhere. Success: a tool error starting "Setu needs Accessibility permission".
   - Call `list_dir` on `~`. Success: a normal listing.

   On the test Mac this baseline was covered by the spike that came before the real install, so it is not repeated here: see [the spike results](superpowers/specs/2026-11-22-setu-mac-spike-results.md), "Before the grants". Run it again only on a fresh Mac.
2. Build, copy and install as in [connecting.md › macOS](connecting.md#macos) steps 1 to 3. Success: the install script says "Setu is installed and running on port 47631."
3. Grant the three permissions at the Mac and restart Setu, as in step 4 there. Click Allow on the "bypass the system private window picker" prompt when it appears, and Don't Allow on each protected-folder prompt.
4. **No Dock icon and no menu bar.** Look at the Mac's screen. Success: Setu has no icon in the Dock and no menu bar of its own; it runs in the background only.
5. **Second start exits with code 2.** While Setu is running, start a second copy by hand over SSH:
   ```
   ssh jb@<mac> '~/Applications/Setu.app/Contents/MacOS/setu; echo exit=$?'
   ```
   Success: it returns straight away with `exit=2`, and the log (`~/Library/Application Support/setu/setu.log`) ends with the line `startup failed: setu is already running for this user.` The running copy is not affected.
6. Read the token and open the tunnel (steps 5 and 6 in connecting.md). Save the token to a file only you can read:
   ```
   (umask 077; ssh jb@<mac> 'cat ~/Library/Application\ Support/setu/token' > /tmp/setu-mac.token)
   ```
   Delete it when you are done: `rm /tmp/setu-mac.token`.

## 2. Run the smoke client

Be logged in at the Mac's desktop, with the screen unlocked, and leave it alone while this runs. It opens TextEdit and types into it, and it overwrites the Mac's clipboard (it copies the typed text to check it).

```
dotnet run --project tests/Setu.Smoke -- --url http://127.0.0.1:47632/mcp --token-file /tmp/setu-mac.token
```

The client sees `shell` in the tool list and runs the 15 Mac checks. Success: every line starts with `PASS`, the last line is `All checks passed.`, and the exit code is 0. A failure prints `FAIL <name>: <why>`. To rerun one check, add `--only <name>`.

Then look at the Mac:

- The smoke document `smoke.txt` is closed in TextEdit (and TextEdit has quit, if it was not running before), and `/tmp/setu-smoke` is gone. The client cleans up even when a check fails.
- No protected-folder prompt appeared. `protected_folders_refused` tries `~/Documents` and `~/Downloads` and searches your whole home folder; Setu must refuse or skip those without touching them.
- The clipboard now holds `smoke`. The client checked it already, so there is nothing to confirm by eye.

## 3. Checks that need you

Use any MCP client for these, for example Claude Code connected as in connecting.md step 7. Ask it to call the tool, or call it yourself.

**The permissions survive updates.** Build twice, with a change between the builds (or just different `--version` numbers), and run the update from each, as in "Updating" in connecting.md. Nobody goes to the Mac. Then call `screenshot`, and rerun the smoke client with `--only click_type_into_textedit`. Success: the screenshot shows other apps' windows (not just the wallpaper), the check passes, and no new permission prompt appeared at the Mac.

**Restart after a crash.** The LaunchAgent is set to restart Setu whenever it stops. Kill it hard over SSH:
```
ssh jb@<mac> 'pkill -9 -x setu'
```
Then check whether it is back:
```
ssh jb@<mac> 'pgrep -x setu; tail -n 3 ~/Library/Application\ Support/setu/setu.log'
```
Success: within about 15 seconds a new PID is listed, and the log has a new `listening on http://127.0.0.1:...` line with a time after the kill. Reopen the tunnel if needed and confirm a `list_dir` call works. Record how long it took.

**start_process survives an update.** Call `start_process` with path `/bin/sleep` and arguments `600`, and note the PID. Run the update as above, reconnect, and call `shell` with `kill -0 <pid>`. Success: exit code 0, so the program is still running. (The smoke client checks only that it outlives the call, because it cannot restart the server it is talking to.) You can repeat this with an app, for example `TextEdit`.

**Locked screen.**
1. Make sure the Mac asks for a password immediately when the display turns off (System Settings › Lock Screen). Then call `shell` with `pmset displaysleepnow`.
2. Call `screenshot`. Success: a tool error saying "The screen is locked or another user is on the console; file and shell tools still work."
3. Call `list_dir` on `~`. Success: a normal listing. `list_windows` also still works.
4. Unlock the Mac by hand and call `screenshot` again. Success: an image.

**Secure input.** Click into a password field (for example a login form in Safari), or turn on Terminal › Secure Keyboard Entry and click into Terminal. Call `type_text` with some text. Success: the call works, the text arrives, and the result ends with a note like "secure keyboard entry is on, held by '<app>' (PID n)". Call `click` somewhere else. Success: it works. Record whether the text really arrived. Turn Secure Keyboard Entry off again afterwards.

**Local Network.** Call `shell` with `nc -z 172.16.1.4 22` (or another machine and port on your network that accepts connections). Success: exit code 0. Run it again after an update. Success: still exit code 0, with no new prompt at the Mac.

**Protected folder through shell.** Call `shell` with `ls ~/Documents`. Success: it fails at once (well under a second) with "Operation not permitted", and no prompt appears at the Mac. If it waits instead, nobody has clicked Don't Allow for Documents yet; do that at the Mac and record it.

**Keep-awake.**
1. Call `shell` with `pmset -g assertions`. Success: Setu holds both `PreventUserIdleSystemSleep` and `PreventUserIdleDisplaySleep`.
2. With the lid open, leave the Mac alone for longer than its display-sleep and sleep times in System Settings. Then call `screenshot`. Success: the display is still on, the screen is not locked, and you get an image.

**Monthly prompt.** macOS 15 asks again about the "bypass the system private window picker" permission about once a month. It cannot be brought on without changing the Mac's clock, which we will not do. When it first shows up, record the date, and check that `screenshot` still returns an image while the prompt waits (the prompt may be in the picture). Click Allow at the Mac.

## 4. Results

Fill this in after the run. Use PASS or FAIL, and a short note for anything that failed. Leave "pending" for a check that has not been run yet.

| | |
|---|---|
| Date | 2026-11-23 |
| Mac / macOS version | macOS 15.8, arm64 |

| Item | Result | Notes |
|---|---|---|
| Before the grants: screenshot and click errors, list_dir works | partly | The spike showed every permission check false before the grants ([spike results](superpowers/specs/2026-11-22-setu-mac-spike-results.md)); the exact tool error text was not seen, because the real build came after the grants |
| Grants given at the Mac, then the smoke client passes | PASS | 15 of 15 |
| Smoke: auth_rejects_bad_token | PASS | |
| Smoke: list_tools_complete | PASS | |
| Smoke: write_read_edit_roundtrip_lf | PASS | |
| Smoke: list_dir_newest_first | PASS | |
| Smoke: search_content | PASS | |
| Smoke: protected_folders_refused | PASS | |
| Smoke: shell_unicode_output | PASS | |
| Smoke: shell_error_plain | PASS | |
| Smoke: shell_exit_code | PASS | |
| Smoke: shell_prompt_does_not_hang | PASS | |
| Smoke: shell_timeout_kills_tree | PASS | |
| Smoke: start_process_survives | PASS | |
| Smoke: list_windows_shows_textedit | PASS | |
| Smoke: screenshot_png_nonempty | PASS | |
| Smoke: click_type_into_textedit | PASS | |
| No Dock icon and no menu bar | PASS | |
| Second manual start exits with code 2 | PASS | |
| Permissions survive updates (no one at the Mac) | PASS | Several updates, including one after stopping Setu |
| Restarts after kill -9 (time to come back) | PASS | Back and listening within 12 s |
| start_process program survives an update | PASS | A `/bin/sleep` kept running across an update |
| Locked screen: screenshot errors, list_dir works | PASS | |
| Locked screen: screenshot works again after unlocking | PASS | Locked with `send_keys ctrl+cmd+q`; after unlocking, all 15 smoke checks passed |
| Secure input: typing works and the result has the note | PASS | A note, not a refusal; the typed text got through |
| Local Network: `nc -z` to a LAN host through `shell` | PASS | 172.16.1.4:22 |
| Protected folder through `shell`: `ls ~/Documents` fails at once, no prompt | PASS | "Operation not permitted" in 0.04 s |
| Keep-awake: both assertions in `pmset -g assertions` | PASS | |
| Keep-awake: still awake and unlocked after idling past the timeouts | pending | Not run: the Mac was locked by hand or in use all day |
| `type_text` line breaks: "a\r\nb\nc" gives three lines | PASS | Each break pressed Return once; CRLF counted once |
| `send_keys cmd+tab` switches apps | PASS | Failed before modifier keys were pressed for real (fixed in 5b9a04f); now toggles TextEdit and Finder every time |
| Monthly prompt: date seen, screenshot works while it waits | pending | Cannot be forced; record it when it happens |
