# Setu on macOS: spike results

These are the results of Task 1 of `docs/superpowers/plans/2026-11-22-setu-mac.md`, run on 2026-11-22 and 2026-11-23. The Mac was jb@172.16.1.219, running macOS 15.8 on arm64. The probe was a throwaway, self-contained osx-arm64 console exe, built on Linux. It ran as `~/Applications/Setu.app` under the LaunchAgent `com.greatworx.setu`, and it has been deleted.

## Signing

- **Signing by SHA-1 over SSH works.** The probe was signed from the separate keychain `~/Library/Keychains/setu-signing.keychain-db`, with no GUI prompt and no password typed.
- **`codesign --verify --strict` prints `verify ok`.** The `CSSMERR_TP_NOT_TRUSTED` error we expected did not appear. Task 10 should still accept either result.
- **The designated requirement takes the `leaf` form:** `identifier "com.greatworx.setu" and certificate leaf = H"dda83628de1588f772b458e308012fa4501b728f"`.
- **The certificate SHA-1 is `DDA83628DE1588F772B458E308012FA4501B728F`.** The certificate, the keychain and its password file at `~/Library/Application Support/setu/signing-keychain-password` (mode 0600) stay on the Mac. The real install reuses them.
- **The rcodesign fallback was not needed.**

## Before the grants (screen locked)

- `preflight`: every check came back false.
- `secure=True`. The lock screen holds secure input.
- `names`: 1 foreign window, 0 with names.
- `screencapture`: exit 0, with a full 3024 px image. **screencapture gives back an image even without the grant**, so its exit code can't tell whether Setu has the grant.
- `request`: returned false. It showed no dialog while the screen was locked.

## The questions from Step 5 and Step 6

| Question | Answer | Evidence |
| --- | --- | --- |
| Screen Recording grant | **It works.** Once John switched Setu on, the next start showed `sr=True`, and `names` returned other apps' window titles (`Terminal:jb — -zsh`, `System Settings:Accessibility`). | Probe at 10:03 and 10:25 |
| Does preflight turn true without a restart? | **Not tested.** Every probe run was a fresh process. Task 7 must not rely on a live process seeing a new grant; restarting Setu (`kickstart -k`) is what makes it show. | n/a |
| The macOS 15 "bypass the private window picker" dialog | **It appears on the first `screencapture` after the grant.** It reads *"Setu" is requesting to bypass the system private window picker and directly access your screen and audio*, with Allow and Open System Settings buttons. `screencapture` still returned a correct image while the dialog was waiting. This is the monthly re-approval. It doesn't fail the capture; it shows up in the picture. | Screenshot at 10:03 |
| Accessibility | **It works** once switched on (`post=True ax=True`). The AX prompt from `AXIsProcessTrustedWithOptions` didn't show a dialog of its own while the Screen Recording dialog was up, so John added Setu by hand. | Probe at 10:25 |
| Do other apps' windows show in `names`? | **Yes**, as long as there are windows. With nothing open, `foreign=0 named=0`, which looks the same as having no grant. **The name probe can't tell "no grant" apart from "empty desktop".** Task 7 should use `CGPreflightScreenCaptureAccess()` as the grant test, and treat the name probe as a second check that only counts when at least one foreign window exists. | Probes at 11:36 and 11:37 |
| Protected folders, first access | **It blocks until the prompt is answered**, more than 20 s. Prompts come one at a time, so each later folder waits behind the first. John saw "Setu wants to manage iCloud Drive" and the Desktop prompt. The prompts appear on the Mac's screen with no notice to the agent. | Probes at 10:25 and 11:27 |
| Protected folders after Don't Allow | **Refused at once**, in 3–42 ms, with `UnauthorizedAccessException` (EPERM). | Probe at 11:35 |
| Another app's data (`~/Library/Containers/com.apple.mail`) | **Refused at once with no prompt**, in 8 ms. | Probe at 10:27 |
| Does secure input drop posted keystrokes? | **No.** While a hidden-answer `display dialog` held secure input (`secure=True`), CGEvent unicode typing of `spike123` plus Return reached the box: `osascript` printed `spike123`. **Task 8 types and adds a note; it doesn't refuse.** We tested only the osascript dialog, not a browser or loginwindow password field. | Probe at 11:36:24 |
| Local Network | **It works after Allow.** At 10:03 the UDP send failed with `SocketException: Broken pipe` before the prompt was answered. At 10:07 and later, TCP to 172.16.1.4:22 connected. John didn't say whether he saw a Local Network prompt, so the visit 2 checklist must confirm the first-run behaviour on a clean grant. | Probes at 10:03 and 10:07 |
| Grants after rebuild and re-sign | **All kept** (Screen Recording, Accessibility, Local Network and the folder denials), and no new dialogs. | build-B at 11:36:48 |
| Grants after bootout, rebuild and re-sign | **All kept.** | build-C at 11:37:09 |

## What this means for the later tasks

- **Task 7 (state, permissions, capture):**
  - Keep the plan's rule: Screen Recording counts as granted when `CGPreflightScreenCaptureAccess()` is true, or when a foreign window has a name. With no foreign windows, trust preflight, because the name probe can't decide.
  - A missing grant can't be detected from screencapture's exit code.
  - While the picker dialog was waiting, preflight stayed true and the capture still worked. This answers the spec's open question (§ permissions, "whether preflight returns false while the monthly prompt is waiting"): the prompt doesn't block capture, so `screenshot` needs no extra detection for it. The dialog may show up inside a screenshot, and the docs should say that someone at the Mac must click Allow.
- **Task 8 (input):** with secure input on, type anyway and add a note to the result naming the process that holds it. Don't refuse.
- **Task 9 and Task 4 (protected folders and startup prompts):**
  - A first touch of a protected folder blocks a tool call until someone at the Mac answers.
  - Setu should raise those prompts on purpose at install (MacStartupPrompts), so John can deny them once.
  - It should never let an agent's call be the first touch. Once denied, access fails at once.
- **Task 10 (install scripts):**
  - The `leaf` DR plus the kept keychain identity keep every grant across updates, including a bootout.
  - Tolerate both a `verify ok` and a `NOT_TRUSTED` result.
  - The Local Network subnet fallback **isn't needed**.
- **Tasks 11 and 13 (smoke):**
  - `launchctl asuser 501 …` over SSH fails ("Could not switch to audit session: Operation not permitted") without root.
  - To show a dialog in John's session, bootstrap a one-off LaunchAgent into `gui/501` (`LimitLoadToSessionType Aqua`) instead.
  - `screencapture` run straight from SSH has no grant and returns only the wallpaper.

## Left on the Mac

- **Kept:** the signing keychain and certificate, the keychain password file, the TCC grants, and `~/Applications/Setu.app`. The real install replaces the app.
- **Installed:** the Command Line Tools, at `/Library/Developer/CommandLineTools`.
- **Removed:** the spike LaunchAgent, `~/setu-spike`, and the probe's log folder.
