# Setu on macOS — design

Date: 2026-11-22. Bahu todo #170. Builds on the Windows design of 2026-11-21 (`2026-11-21-windows-mcp-design.md`); everything said there about transport, auth, tool behaviour and error style still holds unless this document says otherwise.

## Intent

John wants agents to do Mac work and testing themselves, the way they already do on Windows through Setu: read and edit files, run shell commands, start programs, and see and drive the screen. After a one-time install and a one-time grant of two macOS permissions at the Mac, John should not need to touch the Mac, except for the monthly Screen Recording re-approval described below.

Success: an agent on Linux, through an SSH tunnel to the Mac, passes the Mac smoke client and the Mac checklist, and a Setu update installed over SSH keeps both permissions without anyone at the Mac.

## Goals

- The same Setu server on macOS (arm64), with the Mac-specific parts behind the existing seams (`IDesktop`, `IDetachedLauncher`, `IProcessContainment`, `ITokenFileProtector`).
- A `shell` tool that runs zsh, in place of `powershell` and `cmd` on the Mac.
- A LaunchAgent that starts Setu at login and restarts it after a crash.
- `install.sh`, `update.sh`, `uninstall.sh`, run over SSH as John, with no admin rights.
- One stable self-signed signing certificate kept in a keychain on the Mac, so the Screen Recording and Accessibility grants survive updates.
- A token file readable only by its owner, and keep-awake.
- Clear errors, never silent no-ops, when input is blocked by secure keyboard entry, when the screen is locked, and when a permission is missing or needs re-approval.

## Non-goals

- Intel Macs, macOS before 15, and multiple displays beyond "it should work". Only the test Mac (one built-in display) gets tested.
- Notarization, the hardened runtime, Developer ID, or distribution to other people.
- Bahu-side wiring (machine type, automatic tunnels). That stays separate work, as on Windows.
- Clicking controls by name (the macOS Accessibility tree), clipboard tools, and binary file transfer.
- Folders macOS protects separately, such as Desktop, Documents, Downloads, iCloud Drive and other apps' data. Setu gets no Full Disk Access or Files & Folders grant (John, 2026-11-22: "leave em protected for now"). See *Protected folders*.
- Changing how the Windows build behaves. The Windows refactors below must keep every Windows tool, message and test result the same.

## Anti-goals (would make this a failure even if it works)

- Listening on anything but loopback, or accepting a request without the token.
- An update that silently drops a permission, so screenshots come back showing only the wallpaper, or clicks do nothing.
- A screen tool that reports success when macOS dropped the input or blanked the image.
- Needing a .NET runtime, Homebrew, Xcode or the Command Line Tools on the Mac.
- Storing John's login password anywhere, or needing it for installs and updates.

## Constraints (found on the Mac, 2026-11-22)

- macOS 15.8.3, arm64, MacBook Pro with a built-in Retina display (3024 × 1964 pixels). John is logged on at the console. His `gui/501` launchd domain is reachable over SSH, so `launchctl bootstrap` works remotely.
- **No developer tools.** `swift` and `clang` are only stubs that offer to install the Command Line Tools. `codesign`, `security`, `screencapture`, `sips`, `openssl` (LibreSSL), `lsappinfo`, `open`, `pmset` and `ioreg` are part of macOS and work. The binary is built on Linux with `dotnet publish -r osx-arm64` and signed on the Mac.
- **The login keychain is locked in an SSH session**, and unlocking it needs John's password. Signing therefore uses a separate keychain (below).
- The macOS firewall is off on this Mac (checked 2026-11-22), so a listen on loopback brings up no prompt. jb is an admin user, but nothing here uses admin rights.
- The Mac currently locks 300 s after the display sleeps, and the display sleeps after 10 minutes. Keeping the display awake (below) avoids both.
- **The monthly Screen Recording prompt.** Since macOS 15, macOS asks about once a month whether an app may keep recording the screen, and someone must click Allow at the Mac. Apple gives developers no way around it. The only switch is an MDM setting (`forceBypassScreenCaptureAlert`), and this Mac is not enrolled in MDM (John, 2026-11-22). This is accepted. Setu must report the state clearly, never return a blank image as if it worked.
- **macOS keys permissions to the code signature.** TCC (the macOS permission system) stores a grant against the app's designated requirement. With ad-hoc signing that requirement changes on every build and the grant is lost. With one self-signed certificate it stays `identifier "com.greatworx.setu" and certificate leaf = H"…"` for every build. Bundle id and install path must also stay fixed.
- **Reboots.** FileVault is on and there is no automatic login (checked 2026-11-22). After any restart, including macOS updates, Setu stays down until John logs in at the Mac. The connecting doc says so, and so does the hint install and update print when the GUI session is missing.
- **No developer tools** means no `git`, `make` or usable `python3` on the Mac either. Mac work that needs them will hit this. Installing the Command Line Tools is John's call, outside this design.
- The Mac is a laptop. Keep-awake prevents idle sleep, but closing the lid still sleeps it.
- Coordinates: macOS events use points (top-left origin of the main display, y down), and a Retina screenshot has two pixels per point. The existing `Capture.Scale` formula (`screen_x = origin_x + image_x / scale`) already covers this.

## Approaches considered (John chose A, 2026-11-22)

- **A. A `Setu.app` bundle, with screenshots taken by `/usr/sbin/screencapture`.** A small app bundle in `~/Applications` with a fixed bundle id. System Settings shows it as "Setu", and `tccutil reset … com.greatworx.setu` works for testing. Screenshots come from Apple's own tool, which inherits Setu's Screen Recording grant because Setu starts it. It costs about 150 ms per screenshot and a few lines of bundle packaging. There is no capture code of our own.
- **B. The same bundle, with screenshots from ScreenCaptureKit called from .NET.** No extra process per shot, but ScreenCaptureKit is an Objective-C API that reports back through callbacks. With no Xcode, those would have to be built by hand through `objc_msgSend`. That is a lot of fragile interop that can only be tested on the Mac.
- **C. A bare signed binary with no bundle.** The simplest packaging. But permissions are then keyed partly to a file path, System Settings shows a raw path, `tccutil` cannot reset it by bundle id, and grants are more fragile when the file is replaced.

Rejected outright: a Swift helper (no compiler on the Mac, and a second language), and AppleScript or `osascript` for input (slow, and it needs its own Automation grants).

## Decided design

### Packaging, signing and locations

| What | Where |
| --- | --- |
| App bundle | `~/Applications/Setu.app` (`Contents/MacOS/setu`, `Contents/Info.plist`) |
| LaunchAgent | `~/Library/LaunchAgents/com.greatworx.setu.plist`, label `com.greatworx.setu` |
| Data (token, `config.json`, `setu.log`) | `~/Library/Application Support/setu/` |
| Signing keychain | `~/Library/Keychains/setu-signing.keychain-db` |
| Its password | `~/Library/Application Support/setu/signing-keychain-password`, mode 0600 |

- `Info.plist` also carries `NSLocalNetworkUsageDescription` and `NSAppleEventsUsageDescription`, so the Local Network and Automation (`osascript`) prompts can appear instead of being denied silently.
- `Info.plist`: `CFBundleIdentifier` `com.greatworx.setu`, `CFBundleName` and `CFBundleDisplayName` `Setu`, `CFBundleExecutable` `setu`, `CFBundleShortVersionString` and `CFBundleVersion` from `Directory.Build.props`, `LSUIElement` true (no Dock icon or menu bar), and `LSMinimumSystemVersion` 15.0.
- Build: `dotnet publish src/Setu.MacApp -c Release -r osx-arm64 --self-contained -p:PublishSingleFile=true -o publish/osx-arm64`, on Linux. The SDK already gives the output an ad-hoc signature covering the bundled files (checked with SDK 10.0.114, 2026-11-22). `codesign --force` replaces it with the Setu signature on the Mac.
- **Fallback if on-Mac signing fails in the spike:** sign on Linux with `rcodesign` (apple-codesign), which can make a self-signed certificate and sign bundles off the Mac. The cost is that the key then lives on the Linux machine, and the project gains a tool. This comes before asking John to trust the certificate at the Mac.
- **Certificate.** The first `install.sh` creates it on the Mac if the signing keychain does not exist yet, and it never leaves the Mac:
  1. Generate a random keychain password and write it 0600.
  2. Create the keychain and unlock it with that password (`security create-keychain`, `unlock-keychain`). Turn off its auto-lock timeout (`security set-keychain-settings` without `-l`/`-t`). Add it to the user's keychain search list while keeping the existing entries (`security list-keychains -d user -s <existing…> <setu keychain>`). `man codesign` says the identity must be in a keychain on that list; `--keychain` only narrows the lookup. `uninstall.sh --remove-data` takes it off the list again.
  3. Make an RSA 2048 self-signed certificate with `/usr/bin/openssl`. CN "Setu Code Signing". Extended key usage `codeSigning`, key usage `digitalSignature`, critical basic constraints `CA:false`. Valid 20 years, so it never expires in practice.
  4. Import it as a PKCS#12, with a non-empty temporary password, into the signing keychain with `-T /usr/bin/codesign`. Then run `security set-key-partition-list -S apple-tool:,apple:,codesign: -k <pw>`, so `codesign` never shows a GUI prompt.
  5. Delete the temporary key and certificate files.
- **Signing.** Every install and update first unlocks the signing keychain with the stored password, then signs the whole bundle:
  `codesign --force --sign <certificate SHA-1> --keychain <signing keychain> --identifier com.greatworx.setu --timestamp=none Setu.app`.
  - The identity is named by its SHA-1 hash, not its common name. A lookup by name only matches *trusted* code-signing identities, and making the certificate trusted (`security add-trusted-cert`) brings up an authorization dialog at the Mac. Signing by hash with an untrusted self-signed certificate is expected to work. The spike proves it over SSH. If it does not, trusting the certificate becomes one extra step John does at the Mac during the first install.
  - There is no `--options runtime`. The hardened runtime would need JIT entitlements for .NET, and buys nothing without notarization.
  - Then `codesign --verify --strict`. A `CSSMERR_TP_NOT_TRUSTED` result is expected for an untrusted self-signed signer and is not a failure; any other error is. Then `codesign -d -r-`: the script fails unless the designated requirement names `com.greatworx.setu` and the certificate's hash. It checks those two values, not the exact text, because for a self-signed certificate codesign may write `certificate leaf = H"…"`, `certificate root = H"…"` or `anchor H"…"`. The spike records which form appears.
  - It also fails if that leaf hash differs from the one saved at install in `~/Library/Application Support/setu/signing-leaf-sha1`. A changed certificate would silently drop both grants, so it must be loud.
- `uninstall.sh` leaves the signing keychain and the data folder unless given `--remove-data`. Keeping the certificate is what lets a reinstall keep the grants.

### LaunchAgent

```
Label                  com.greatworx.setu
Program                ~/Applications/Setu.app/Contents/MacOS/setu (absolute path written by install.sh)
RunAtLoad              true
KeepAlive              true            # restart after a crash or kill; launchd throttles to one start per 10 s
ProcessType            Interactive     # not throttled by App Nap
LimitLoadToSessionType Aqua            # only in John's GUI login session
StandardOutPath/StandardErrorPath  ~/Library/Application Support/setu/launchd.log
```

- `AbandonProcessGroup` is not set. Programs from `start_process` already live outside Setu's process group, through `open` or `POSIX_SPAWN_SETSID`, so they survive a restart. `shell` commands still running when Setu restarts are stopped, like `powershell` on Windows.
- Expect a one-time "Background Items Added" notification at the Mac on first install.
- Install and update use `launchctl bootstrap gui/$(id -u) <plist>` and `launchctl bootout gui/$(id -u)/com.greatworx.setu`. Both work from SSH while John is logged on. If he is not logged on, the scripts say so (the same hint as `$sessionHint` on Windows).
- One instance per user: Setu takes an exclusive lock on `~/Library/Application Support/setu/setu.lock` for its whole lifetime. A second copy (for example one started by hand over SSH) exits with code 2 and logs `startup failed: setu is already running for this user.`, as on Windows. Exit codes 1, 2 and 3 keep their Windows meanings.

### Permissions

macOS needs two grants for Setu, given once by John at the Mac:

- **Screen Recording** ("Screen & System Audio Recording"): needed by `screenshot`, and by `list_windows` to read window titles.
- **Accessibility**: needed by `click`, `type_text`, `send_keys`, `scroll` and `focus_window`.

A third grant is needed in normal use: **Local Network** (macOS 15). Apple's TN3179 says the launchd-daemon exception does not cover launchd agents, and that a spawned helper's access is checked against the app that started it. Without this grant, any `shell` command that reaches a LAN address (git, curl, ssh to 172.16.1.x, `.local` names) fails as if the network were down, while the same command over plain SSH works. TN3179 also warns that tracking is only reliable with an Apple-issued identity, so the spike checks whether the grant sticks with the self-signed one. If it does not, the fallback is the macOS 15.5+ setting that allows a subnet without a grant: `sudo defaults write com.apple.network.local-network AllowedEthernetLocalNetworkAddresses -array 172.16.1.0/24` (and the Wi-Fi variant), then a restart. That needs John's admin password once, and it is the only use of admin rights in this design. Setu triggers the Local Network alert on start, while the grant is undetermined, by connecting a UDP socket to a LAN address as TN3179 describes.

How they get granted:

- On start, if a grant is missing, Setu calls `CGRequestScreenCaptureAccess()` and `AXIsProcessTrustedWithOptions({kAXTrustedCheckOptionPrompt: true})`, once each. That adds "Setu" to both lists in System Settings, so John only flips two switches.
- `install.sh` prints exactly where those switches are. It also opens both panes on the Mac's screen (`open "x-apple.systempreferences:com.apple.preference.security?Privacy_ScreenCapture"` and `?Privacy_Accessibility`).
- Setu checks the grants on every screen call (`CGPreflightScreenCaptureAccess()`, `AXIsProcessTrusted()`), not once at start.
- A Screen Recording grant usually takes effect only after the app restarts; System Settings offers "Quit & Reopen". `CGPreflightScreenCaptureAccess()` can keep its old answer inside a running process. So the result of a real capture wins over the in-process check:
  - When the check says "not granted", `screenshot` still runs `screencapture`. If the image shows other apps' windows, it is returned.
  - The test for "granted" is the standard window-name probe. `CGWindowListCopyWindowInfo` returns `kCGWindowName` for other processes' windows only when Screen Recording is granted. If other processes have layer-0 windows on screen and none of them has a name, Setu treats Screen Recording as not granted, and so does a failed `screencapture`. Then the Screen Recording error is returned. Nothing is judged from pixels.
  - Every permission error ends with how to restart Setu remotely: `launchctl kickstart -k gui/$(id -u)/com.greatworx.setu`. It runs over SSH, and so can an agent through `shell`.
  - The builder confirms this behaviour in the spike. It also covers the monthly-prompt detection under *Unverified* below.

### Code shape

- **`src/Setu.Mac`** (new, `net10.0` library): `MacDesktop : IDesktop`, `MacDetachedLauncher`, `MacKeepAwake`, the `screencapture` and `sips` wrappers, and P/Invoke declarations for CoreGraphics, CoreFoundation, ApplicationServices (Accessibility), IOKit and Carbon (`IsSecureEventInputEnabled`). Plain C APIs only, with no Objective-C runtime calls, except where noted below.
- **`src/Setu.MacApp`** (new, exe, `AssemblyName` `setu`, `RuntimeIdentifier` `osx-arm64`): the Mac `Program.cs`. It is the Mac counterpart of `src/Setu`, which stays the Windows exe. Each is kept thin: the shared startup moves into Core (next bullet).
- **Setu.Core changes**, all platform-neutral:
  - **Shared startup** (`Setu.Core/Server/SetuStartup.cs`, or similar). The common part of today's `Program.cs` moves here: data folder, log, options, token, keep-awake best effort, building and running the host, and the exit codes. Each platform's `Program.cs` supplies its parts: the data folder, the single-instance guard, the token protector, keep-awake, the containment factory, the launcher, the desktop, the file defaults and the shell flavour.
  - **`IDesktop`**: `State()` and `ElevatedForegroundWindow()` are replaced by one method, `string? BlockedReason(ScreenUse use)`. `ScreenUse` is `See` (screenshot), `List` (list_windows), `Pointer` (click, scroll, focus_window) or `Keyboard` (type_text, send_keys). The method returns null when the call may go ahead, or the complete message for the agent. The Windows messages move from `ScreenTools` into `Win32Desktop` word for word. On Windows, `Pointer` and `Keyboard` behave the same (both check locked, then elevated), and `List` always returns null, as today. `ScreenTools` just throws `McpException(reason)`.
  - **Shell tools** split into `WindowsShellTools` (`powershell`, `cmd`), `ZshShellTools` (`shell`) and `ProcessTools` (`start_process`). `ServerHostSettings` gets a `ShellFlavor` (`Windows` or `Zsh`) that picks which shell class is registered. The tool lists are:
    - Windows: the same 15 tools as today.
    - Mac: 14 tools. The 5 file tools, `shell`, `start_process` and the 7 screen tools.
  - **`KeySpec`** takes a key vocabulary.
    - Windows: unchanged, with modifiers `ctrl alt shift win`.
    - Mac: modifiers `cmd` (alias `command`), `ctrl`, `option` (alias `alt`) and `shift`. `win` is rejected with "use cmd on macOS". The keys are the same, except `insert` and `printscreen`, which the Mac lacks and which are rejected by name.
    - The tool description lists both vocabularies, labelled by OS.
  - **`FileToolsOptions`** gains `DefaultLineEndings` (CRLF on Windows, LF on Mac) and the fallback encoding for files that are not valid UTF-8 or UTF-16. Windows keeps the ANSI code page. The Mac uses ISO-8859-1, because it maps every byte to a character and back, so edits stay byte-safe.
  - **`PathResolver`**: on non-Windows, a leading `~` or `~/` expands to the home folder. `%VARS%` expansion stays.
  - **Tool descriptions** become platform-neutral where they say "Windows". For example: "Absolute path. On Windows %VARS% are expanded; on macOS a leading ~ is your home folder." One text, valid on both, so no per-platform description machinery is needed.

### Screen tools on the Mac

- **Blocked reasons**, checked in this order, each a plain message that names what to do:
  1. **Session not on the console, or screen locked** (`CGSessionCopyCurrentDictionary`: `kCGSessionOnConsoleKey`, `CGSSessionScreenIsLocked`). Applies to all four uses except `List`. "The screen is locked or another user is on the console; file and shell tools still work."
  2. **Screen Recording not granted or awaiting re-approval** (`CGPreflightScreenCaptureAccess()` false). Applies to `See` and `List`. "Setu needs Screen Recording permission. At the Mac: System Settings › Privacy & Security › Screen & System Audio Recording › turn on Setu (or click Allow on the monthly prompt). File and shell tools still work."
  3. **Accessibility not granted** (`CGPreflightPostEventAccess()` or `AXIsProcessTrusted()` false). Applies to `Pointer` and `Keyboard`. The message has the same shape, naming Accessibility.
  4. **Secure keyboard entry on** (`IsSecureEventInputEnabled()`). Applies to `Keyboard` only, and **only if the spike proves macOS really drops posted keyboard events while it is on.** Secure input is meant to stop other processes *reading* keystrokes. Password managers type into password fields with posted events. The flag is also known to get stuck on, often wrongly attributed to `loginwindow`. Refusing on the flag alone could therefore disable typing until someone visits the Mac. If events do get through, Setu does not refuse; `type_text` and `send_keys` add one line to their result instead ("note: secure keyboard entry is on, held by …"). The rest of this item describes the refusal case. Mouse events are not affected, so the agent can still click out of the password field. The message names the holding process when it can be found (the `kCGSSessionSecureInputPID` entry in the IORegistry `IOConsoleUsers` property, mapped to a process name). For example: "A password field or secure keyboard entry is active in 'loginwindow' (PID 123), so macOS blocks typed input from setu. Click elsewhere or ask the user to close it; Terminal's 'Secure Keyboard Entry' setting also causes this. click, screenshot, file and shell tools still work."
- **Unverified, to check on the Mac:**
  - Whether `CGPreflightScreenCaptureAccess()` returns false while the monthly prompt is waiting. If it does not, `screenshot` must also detect a capture that shows no other app's windows while the window list says there are some, and report the same Screen Recording message rather than return the image. The builder verifies this on the Mac and picks one of the two.
  - Whether CGEvent keyboard input really is dropped under secure input. This decides between refusing and adding a note (item 4 above).
- **`screenshot`.** Runs `/usr/sbin/screencapture -x -t png` into a temp file in Setu's data folder, which is deleted afterwards:
  - `screen` uses `-R` over the union of all displays in points. With one display that is just the display.
  - `window:<id>` uses `-l <CGWindowID> -o` (no shadow).
  - `region:x,y,w,h` uses `-R x,y,w,h`.
  - `max_width` is applied with `/usr/bin/sips --resampleWidth`.
  - `Scale` is the PNG width in pixels divided by the captured width in points (2.0 on this Retina screen, before any downscale).
  - The origin is in points.
  - A non-zero exit or an empty file is a tool error that includes `screencapture`'s stderr.
- **`list_windows`.** `CGWindowListCopyWindowInfo(kCGWindowListOptionAll | kCGWindowListExcludeDesktopElements)`, keeping layer-0 windows only.
  - `Handle` is the `CGWindowID`, and `ProcessName` comes from `kCGWindowOwnerName`.
  - The `min` flag means "not on screen" (minimized, hidden, or on another Space), and the tool description says so.
  - `fg` marks the first layer-0 window in the on-screen list, which `kCGWindowListOptionOnScreenOnly` orders front to back. It needs no Accessibility.
  - Windows with no title and zero size are dropped, as on Windows.
- **`click` and `scroll`.** `CGEventCreateMouseEvent` and `CGEventCreateScrollWheelEvent2` (line units) posted with `CGEventPost(kCGHIDEventTap)`. A double click sets `kCGMouseEventClickState` to 2 on the second pair. Positive scroll is up, as on Windows.
- **`type_text`.** Key-down and key-up events carrying `CGEventKeyboardSetUnicodeString`, in chunks of at most 20 UTF-16 units, without splitting a surrogate pair.
- **`send_keys`.** Virtual key codes for the US ANSI layout (`kVK_ANSI_*`, `kVK_Return`, …), with modifier flags on each event. Shortcuts follow physical key positions, which is what macOS shortcuts expect. This is fine on the test Mac's layout, and is stated in the code.
- **`focus_window`.** Find the owning PID, then:
  - Set `kAXFrontmostAttribute` on the app.
  - Find the AX window whose `CGWindowID` matches, through the private but long-stable `_AXUIElementGetWindow` (used by Hammerspoon, yabai and Rectangle). If that symbol is missing, fall back to matching title and frame.
  - Un-minimize it if needed, then `AXRaise` it and set it as main.
  - Then confirm that the frontmost PID is the target. If it is not, the tool returns the same "refused" error as Windows, with macOS wording.

### Shell and processes on the Mac

- **`shell(command, cwd?, timeout_s?)`** runs `/bin/zsh -l -c <command>`. The login shell runs `/etc/zprofile` (`path_helper`) and the user's `~/.zprofile`, so `PATH` matches a Terminal. Without it a LaunchAgent gets only `/usr/bin:/bin:/usr/sbin:/sbin`. It is not interactive (no `-i`), so `.zshrc` is not read.
  - stdin is closed.
  - `LANG` and `LC_ALL` are set to `en_US.UTF-8` if they are unset, so output is UTF-8.
  - Timeout, caps (64 KiB per stream), output format and the cancel-on-abandon behaviour are the same as `powershell`. zsh starts in its own process group, and a timeout kills the whole group. `Process.Kill(entireProcessTree)` would miss children that have been reparented to launchd. Programs that call `setsid` themselves survive, which is the same accepted limit as on Windows.
  - Everything `shell` and `posix_spawn` start runs under Setu's macOS identity: it gets Setu's grants, and its prompts name "Setu". Apps started through `open` have their own identity. The tool descriptions say this, so an agent testing a command-line program's own permission behaviour is not misled.
  - The description warns that the command must not wait for input and points GUI apps to `start_process`.
- **`start_process(path, arguments?, cwd?)`**:
  - A path ending in `.app`, or a bare name that is not an executable on `PATH` (for example `TextEdit`), is launched through LaunchServices: `open -n -a <app> [--args …]`. When `arguments` are given and the app takes documents, the builder decides whether they go through `--args` or as files to open. The smoke test needs "open TextEdit with this file".
    - The PID is then read with `lsappinfo` (newest instance of that bundle).
    - Apps launched this way belong to launchd, not to Setu, so they survive anything that happens to Setu.
  - Anything else is a plain executable. It is started with `posix_spawn` using `POSIX_SPAWN_SETSID` and `POSIX_SPAWN_CLOEXEC_DEFAULT` (its own session, no inherited file descriptors), stdin, stdout and stderr on `/dev/null`, and returns its PID.
  - Errors are plain ("No application named 'Foo'", "not executable").

### Protected folders

macOS asks the user before an app first touches certain folders. Nobody is at the Mac to answer, so Setu must never cause that prompt from a file tool, and must never wait on one.

- **The file tools refuse protected paths up front.** They check before any file system call, using the path after `~` expansion, `GetFullPath`, and resolving symlinks in the parts that exist.
  - The paths: `~/Desktop`, `~/Documents`, `~/Downloads`, `~/Library/Mobile Documents` (iCloud Drive), `~/Library/Mail`, `~/Library/Messages`, `~/Library/Safari`, `~/Library/Containers` and `~/Library/Group Containers` (other apps' data), `~/Pictures/Photos Library.photoslibrary`, `~/Library/Calendars`, `~/Library/CloudStorage` (third-party cloud providers), `~/Library/Photos`, `~/Movies/TV` and `~/Music/Music` (the TV and Music apps' media libraries), `/Volumes/*` (removable and network volumes), and anything under them.
  - The error: "<path> is in a folder macOS protects (Desktop, Documents, Downloads, iCloud Drive, other apps' data, removable volumes). Setu has no access to it, on purpose. Work in another folder, or ask John to copy the file out."
  - `list_dir` and `search` also skip these folders when walking a parent (for example `search` over `~`), and say which ones they skipped.
- The list lives in one place in `Setu.Mac`, as data that `PathResolver` consults through `FileToolsOptions`. Windows passes an empty list.
- **Anything else that still runs into one returns an error, not a hang.** That covers `shell` commands an agent runs, and a protected location that isn't on the list.
  - `EPERM` and `UnauthorizedAccessException` from a file tool become the same plain error.
  - `shell` already has its timeout.
  - The `shell` tool description tells the agent the folders are off-limits.
- **Recorded denials, set up during the grant visit.** `shell` (`find ~`, `du`, `ls ~/Documents`) is the likely trigger, and the file tools' list cannot stop it. So during John's visit, Setu touches each prompting location on purpose: Desktop, Documents, Downloads, iCloud Drive, a removable or network volume if present, and another app's container. John clicks **Don't Allow** on each prompt. A recorded denial makes every later access fail at once with `EPERM` and no prompt. That keeps the folders protected and removes any hang.
- **The list also covers** `~/Library/CloudStorage` (third-party cloud providers), and the same paths reached through `/System/Volumes/Data` (a firmlink that resolving symlinks does not catch), which are normalised first. Places only Full Disk Access opens, such as `~/.Trash`, fail at once without a prompt and just get the friendly error.
- **To check in the spike:** whether a first access to an unlisted protected folder fails at once or waits for the prompt. If it waits, the file tools also get a time limit per call, and the list is extended to cover what was found.
- The smoke client tries `read_file ~/Documents/x`, `list_dir ~/Downloads` and `search ~` and expects the protected-folder error and skip note. The checklist confirms that no prompt appeared at the Mac.

### Keep-awake

Two assertions, held for the process lifetime: `IOPMAssertionCreateWithName(kIOPMAssertionTypePreventUserIdleSystemSleep, …)` and `kIOPMAssertionTypePreventUserIdleDisplaySleep` (the display one alone implies the other; both are taken for clarity), both named "Setu keeps this Mac awake for agents". The display therefore never sleeps from idle, so the screen never locks from idle and the screen tools keep working unattended (John, 2026-11-22: "yes, keep the display awake"). This is a deliberate difference from Windows, where only system sleep is prevented. Locking by hand, or closing the lid, still works. On battery this drains it to empty. As on Windows, if an assertion fails it is logged and the server runs on. `pmset -g assertions` shows both.

### Token

The existing `TokenStore` with `UnixTokenFileProtector`. It is created 0600, and tightened to 0600 on every start. Reading it: `ssh jb@mac 'cat ~/Library/Application\ Support/setu/token'`.

### Install scripts (`install/mac/`, zsh, `set -euo pipefail` style)

- **`common.sh`**: the paths, the label, the port read from `config.json`, `wait_listening` (via `/usr/bin/nc -z 127.0.0.1 <port>`), `ensure_signing_identity`, `build_bundle <binary> <dest>` and `sign_bundle`.
- **`install.sh --binary <path>`**:
  1. Checks that it is on macOS 15+ arm64, and that John is logged on (`launchctl print gui/$UID`).
  2. Creates the signing identity if it is missing.
  3. Builds the bundle in a temp folder, signs it, verifies it, and moves it into place.
  4. Writes the plist, bootstraps it, and waits for the token and the port.
  5. Prints the port, how to read the token, and the permission steps. It opens the two System Settings panes on the Mac.
- **`update.sh --binary <path>`**:
  1. Builds and signs the new bundle beside the old one, and verifies that the designated requirement matches.
  2. Stops Setu with `launchctl bootout`, swaps the bundle (`rm -rf` the old one, then `mv` the new one into place; the same path), and bootstraps it again.
  3. Waits until it is listening. It then calls nothing that needs permissions; the smoke client and checklist check that the grants survived.
- **`uninstall.sh [--remove-data]`**: bootout, then removes the plist and the app. `--remove-data` also deletes the data folder and the signing keychain. It also tries to reset both grants with `tccutil reset ScreenCapture com.greatworx.setu` and `tccutil reset Accessibility com.greatworx.setu`. If macOS refuses (those grants live in the system permission database), it prints how to remove Setu from both lists in System Settings.

### Docs

- **`README.md`** covers both platforms and both tool lists.
- **`docs/connecting.md`** gets a macOS section. The build and copy steps are `scp` of the binary plus `install/mac/*.sh`. The tunnel uses a different local port, for example `ssh -N -L 47632:127.0.0.1:47631 jb@<mac>`, so the Windows and Mac tunnels can run side by side, and the `.mcp.json` server is named `mac`. It also covers the two permissions and the monthly re-approval.
- **`docs/smoke-checklist-mac.md`** (below).

### Tests

The Mac has no .NET SDK, and stays that way. Unit tests run on Linux, and Mac behaviour is proven by the smoke client and the checklist on the Mac.

- **Unit tests (Linux, xUnit v3, in `Setu.Core.Tests`, plus a `Setu.Mac.Tests` project for pure Mac logic):**
  - The Mac `KeySpec` vocabulary.
  - The key-code and modifier-flag mapping.
  - The `screencapture` and `sips` argument building, and the scale and origin math.
  - Unicode chunking for `type_text`, including surrogate pairs.
  - The zsh command line and environment.
  - The `start_process` decision between app and executable.
  - `BlockedReason` ordering, through a fake.
  - Tool registration: the Mac list is exactly 14 tools, with no `powershell` or `cmd`, and the Windows list is unchanged.
  - The default line endings and the ISO-8859-1 round trip.
  - The protected-folder check, including symlinks pointing into a protected folder, `..` tricks, case differences (APFS is case-insensitive by default), and the `search` skip note.
  - `~` expansion.
  - Existing tests keep passing, with `FakeDesktop` updated to `BlockedReason`.
- **Smoke client.** It is platform-aware: it sees `shell` in the tool list and runs the Mac set. That set has the same names where they apply:
  - `auth_rejects_bad_token`, `list_tools_complete`, `write_read_edit_roundtrip_lf`, `list_dir_newest_first` and `search_content`.
  - `shell_unicode_output`, `shell_error_plain`, `shell_exit_code`, `shell_prompt_does_not_hang` (`read x`) and `shell_timeout_kills_tree`.
  - `start_process_survives`.
  - `list_windows_shows_textedit`, `screenshot_png_nonempty`, and `click_type_into_textedit`, which reads the text back with `pbpaste` through `shell`.
  - `read_locked_file` is Windows-only.
  - The scratch folder (under `/tmp/setu-smoke`) and TextEdit are cleaned up even on failure.
- **Mac checklist** (`docs/smoke-checklist-mac.md`), with a results table like the Windows one:
  1. **Before John grants anything:**
     - `screenshot` returns the Screen Recording error.
     - `click` returns the Accessibility error.
     - `list_dir` works.
  2. **John grants both permissions at the Mac.** Then the smoke client passes.
  3. **No Dock icon and no menu bar** for Setu.
  4. **A second start exits with code 2.**
  5. **The grants survive an update.** Run `update.sh` twice, from two different builds, over SSH. Then `screenshot` shows other apps' windows and `click_type_into_textedit` passes, with no one at the Mac.
  6. **Restart after a crash:** `kill -9`, then it is back within about 15 s.
  7. **A `start_process` app survives an update.**
  8. **Locked screen:** lock with `pmset displaysleepnow` (with "require password immediately"). `screenshot` returns the locked error and `list_dir` works. After unlocking it works again.
  9. **Secure input:** open a password field, for example in Safari, or Terminal with Secure Keyboard Entry on. `type_text` returns the secure-input error naming the process, and `click` still works. Record whether input would really have been dropped.
  - **Local Network:** `shell` with `nc -z 172.16.1.4 22`, or another LAN host and port, succeeds through Setu after the grant, and still succeeds after an update.
  - **Protected folders through `shell`:** `ls ~/Documents` fails at once with "Operation not permitted", and no prompt appears at the Mac.
  10. **Keep-awake:** both assertions show in `pmset -g assertions`. After idling past the display-sleep and sleep timeouts with the lid open, the display is still on, the screen is not locked, and `screenshot` works.
  11. **Monthly prompt:** cannot be forced without changing the clock, which we will not do on John's Mac. Record it when it first happens, and check that the error text is right.

## Left to the builder

- `ProcessRequest` passes arguments as one string today. The zsh path (`zsh -l -c <command>` must be exactly three arguments) and the `posix_spawn` path need a real argument list. Use `ProcessStartInfo.ArgumentList`. For `start_process`, decide how the `arguments` string is split, and document it in the tool description.

- Exact P/Invoke signatures and CF memory handling.
- How `sips` and the temp files are named and cleaned.
- The `open` and `lsappinfo` details for `start_process`, including how document arguments are passed.
- The `ScreenUse` enum's exact name.
- Where the shared startup class lives and what it is called.
- Script internals.
- The smoke client's TextEdit choreography.
- How to detect the monthly-prompt state if `CGPreflightScreenCaptureAccess()` does not.
- The P/Invoke parts that can't be tested on Linux are built so the smoke client exercises every path.
- **First implementation step:** a throwaway spike on the Mac. Prove that a Linux-built `osx-arm64` single-file binary, signed with the self-signed certificate from a separate keychain over SSH, runs from a LaunchAgent, keeps its grant across a re-sign of a rebuilt binary, and can run `screencapture` under Setu's grant. Every later step rests on this. If it fails, stop and come back to John.

## Decided after the first draft

- **Who works at the Mac.** The builder (an agent) has SSH as `jb` and does everything over SSH. John is needed at the Mac only for things that need a person there: granting the permissions in the spike and after the first real install, unlocking the screen after the lock test, and anything the spike says needs trust at the Mac. The Mac was locked when this was checked, so the spike's first GUI step needs John there. **The grant visit is one visit:**
  - turn on Screen Recording, Accessibility and Local Network;
  - click Don't Allow on each protected-folder prompt;
  - then Setu is restarted (`kickstart`). The plan groups these into as few visits as possible, and says ahead of time exactly what John does at each one.

- Keep the display awake too (John, 2026-11-22). See *Keep-awake*.
- Secure input: the spike decides; refuse only if typing is really dropped, otherwise type and add a note (John, 2026-11-22: "do what you'd do").
- Local Network fallback: if the grant does not stick, use the one-time `sudo defaults write com.apple.network.local-network …` subnet allowance for 172.16.1.0/24; John types his admin password once, and it is never stored (John, 2026-11-22: "y").
- Install the Command Line Tools on the Mac during the grant visit (John, 2026-11-22: "install em"). This is a setup step for the Mac, not part of Setu.
- The spike signs with the final identity (bundle id `com.greatworx.setu`, the real Setu certificate). Grants John gives during the spike then carry over to the real Setu, and the spike's re-sign test is the same check that protects later updates.
- The todo's "sakshi first" was not John's phrase and has no Mac meaning to him, so there is no Sakshi-specific checklist item. The goal stands: agents do Mac work and testing themselves.
