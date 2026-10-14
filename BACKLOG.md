# VibeSwitcher — Open Items (extracted from RECORD.md)

**Last updated:** 2026-10-14 — Branch 67 (`xplat/settings-sections`) — PR #140 (F46 phase 7 part 3: the Settings page, About and FAQ). Previous: Branch 66 (`xplat/settings-window`) — PR #138.

Only items **not yet marked ✅ Done** are listed here. Section numbers, letters, and titles match RECORD.md exactly.

---

## ~~SECTION 1 — CODE REVIEW & SECURITY AUDIT~~

*(All items resolved — PR #44)*

---

## ~~SECTION 2 — PERFORMANCE~~

*(All items resolved or dropped — 2.7 won't fix)*

---

## ~~SECTION 3 — CODE QUALITY & ARCHITECTURE~~

*(All items resolved — PR #40)*

---

## ~~SECTION 4 — USER EXPERIENCE~~

*(All remaining items tracked in Feature Additions table — 4.7 → F2, 4.8 → F3, 4.13 → F1, 4.14 → F21)*

---

## ~~SECTION 5 — WINDOWS INTEGRATION & COMPATIBILITY~~

*(All items resolved or dropped — 5.9 won't fix)*

---

## ~~SECTION 6 — CONFIG & DATA INTEGRITY~~

*(All items resolved — PR #27)*

---

## SECTION 7 — TESTING

**7.9 — Manual regression checklist** *(Ongoing)*
Run before each release: first-run flow, corrupted config recovery, single-instance guard, profile switch with device present/disconnected, hotkey switch, close-to-tray, window position/size persistence, settings toggles.

---

## ~~SECTION 8 — DEPLOYMENT & DISTRIBUTION~~

*(All remaining items tracked in Planned Branches / Deferred — 8.1/C2 → ✅ Done (PR #110), 8.2/C3 → Dropped, 8.3 → F8 Deferred, 8.7 → Deferred)*

---

## ~~SECTION 9 — LOGGING & DIAGNOSTICS~~

*(All items resolved or dropped — 9.4–9.7 won't fix)*

---

## ~~SECTION 10 — DOCUMENTATION~~

*(All remaining items tracked in Feature Additions / Deferred — 10.3 → F19 Branch 31, 10.8 → Deferred)*

---

## SECTION 11 — OPEN ITEMS BY PRIORITY

### ~~CRITICAL — Must fix before any release~~

*(C2 installer ✅ Done — PR #110; C3 code signing dropped — no certificate)*

### ~~HIGH — Important before v1.0.0~~

*(All High items resolved)*

### ~~MEDIUM — Should fix for a quality release~~

*(All Medium items resolved)*

### ~~LOW — Nice to fix before or after release~~

*(All Low items resolved)*

### ~~TECHNICAL DEBT~~

*(All Technical Debt items resolved — PR #38)*

### ~~REFACTORING OPPORTUNITIES~~

*(All Refactoring Opportunities resolved — PR #37)*

### FEATURE ADDITIONS (post-v1.0.0)

| # | Feature | Branch |
|---|---------|--------|
| ~~F1~~ | ~~Import/export `config.json` via Settings for backup and sharing~~ | ✅ Done — PR #68 |
| ~~F2~~ | ~~Drag-and-drop profile reorder~~ | ✅ Done — PR #59 |
| ~~F3~~ | ~~"Test sound" button to verify active device plays audio~~ | ✅ Done — PR #61 |
| ~~F5~~ | ~~Hotkey cheat sheet in tray tooltip~~ | ✅ Done — PR #57 |
| F8 | Auto-updater with GitHub Releases version check | Deferred |
| ~~F9~~ | ~~Full WinRT toast notifications — persist in Action Center, richer formatting, stack/dismiss~~ | Dropped — blocked by Windows App SDK tooling |
| ~~F10~~ | ~~Per-profile volume level~~ | Dropped — user prefers Windows tray |
| ~~F11~~ | ~~Profile scheduler — per-profile time + day-of-week schedule with automatic switching; optional pre-switch reminder notification fires X minutes before ("Gaming Setup activates in 5 min") so the user can finish what they're doing or override~~ | ✅ Done — PR #74 |
| F13 | Portable mode — store config next to the exe when a `portable.txt` marker file is present, instead of `%APPDATA%` (USB/portable installs) | Deferred |
| ~~F16~~ | ~~Light / dark / high-contrast support — replace hardcoded colours with `SystemColors` brushes so the app follows the Windows OS theme automatically~~ | ✅ Done — PR #70 |
| ~~F17~~ | ~~Built-in profile icons gallery picker — browse bundled icons instead of a file path~~ | ✅ Done — PR #66 |
| ~~F18~~ | ~~Field feedback — brief green border flash on a card when a change is saved~~ | ✅ Done — PR #68 |
| ~~F19~~ | ~~In-app help — "?" button in Settings opens a getting-started walkthrough dialog~~ | ✅ Done — PR #68 |
| ~~F20~~ | ~~Pre-made profile name suggestions — chips or dropdown with common names; pairs with F17 for zero-typing onboarding~~ | ✅ Done — PR #66 |
| ~~F21~~ | ~~Left-click tray cycles profiles~~ | ✅ Done — PR #57 |
| F22 | Expand-to-fit button in Settings — grows the window to show all cards without scrolling, capped at screen height | TBD |
| ~~F23~~ | ~~Profile clone button~~ | ✅ Done — PR #59 |
| ~~F24~~ | ~~Global hotkey to open Settings~~ | ✅ Done — PR #57 |
| ~~F25~~ | ~~Per-profile silent switch — checkbox to skip the Windows notification banner~~ | ✅ Done — PR #59 |
| ~~F26~~ | ~~Device connectivity indicator — green/red dot next to each device in the Settings dropdowns~~ | ✅ Done — PR #61 |
| ~~F27~~ | ~~Profile color tag~~ | Dropped — profiles already have icons; color adds no value |
| ~~F30~~ | ~~Tray icon switch flash — brief icon pulse when a profile switch completes~~ | ✅ Done — PR #57 |
| ~~F31~~ | ~~Audio endpoint aliases — user-defined friendly name per device shown in Settings dropdowns instead of the raw Windows device name (e.g. "GoXLR", "Desk Speakers")~~ | ✅ Done — PR #80 |
| ~~F32~~ | ~~Profile notes — optional short description field on each profile card; stored per profile, shown below the profile name~~ | ✅ Done — PR #76 |
| ~~F33~~ | ~~Favorite / pinned profiles — star flag per profile; pinned profiles appear at the top of the tray menu above unpinned ones~~ | ✅ Done — PR #76 |
| ~~F34~~ | ~~Profile validation warnings — inline warning on cards for duplicate hotkeys, missing/disabled devices, or invalid icon paths~~ | ✅ Done — PR #76 |
| ~~F35~~ | ~~Search / filter in Settings — text box at the top of the profile list; filters cards in real time by profile name, device name, hotkey, mode (Playback/Recording/Both), pinned status, schedule presence, or schedule day-of-week; clears on Escape~~ | ✅ Done — PR #82 |
| ~~F36~~ | ~~Optional switch sound — global default sound on every profile switch with optional per-profile override; pre-made built-in tones or custom .wav; adjustable volume (0–100%) at global and per-profile level; per-profile silent toggle; pairs with F25 and F30~~ | ✅ Done — PR #84 |
| ~~F37~~ | ~~Deafen / panic hotkey — global configurable hotkeys (one per scope) that instantly mute system-wide; configurable scope: mic only (tray flashes red), speakers only (tray flashes blue), or both (tray flashes purple); distinct built-in activate/deactivate sounds; press again to unmute~~ | ✅ Done — PR #86 |
| ~~F38~~ | ~~Temporary / transient profile switch — optional app-wide feature with configurable keybind; switches temporarily and auto-reverts when a timer expires or a linked app closes~~ | Dropped — not useful enough to prioritize |
| ~~F39~~ | ~~Auto-switch on device connect — link a specific audio device endpoint to a profile; automatically activates when that device connects (Bluetooth, USB); per-device toggle~~ | ✅ Done — PR #90 |
| ~~F40~~ | ~~Monitor / dock awareness — trigger a profile switch when a specific display or dock connects or disconnects (HDMI, USB-C, Thunderbolt); designed for hybrid work setups~~ | Dropped — too broad to scope |
| ~~F41~~ | ~~App-aware auto-switching — link an executable to a profile; switches when that process launches or gains focus; reverts to previous profile when the app closes; per-rule toggle~~ | ✅ Done — PR #96 |
| ~~F42~~ | ~~Settings sub-card layout — each settings group (Startup, Notifications, Shortcuts) gets its own inner card within the General Settings card for clearer visual grouping~~ | ✅ Done — PR #68 |
| ~~F43~~ | ~~Card-based enable/disable — settings cards that support toggling use card-level visual state (full-opacity "live" vs. dimmed "off") instead of per-row pill toggles; the whole card fades when the feature is disabled~~ | ✅ Done — PR #68 |
| ~~F44~~ | ~~Compact / mini Settings window — condensed view that shrinks the window to a minimal layout for users with many profiles; full window restores on demand~~ | ✅ Done — PR #108 |
| ~~F45~~ | ~~Light theme support for redesigned UI — profile cards, action strips, overlays, and icon frames use hardcoded dark hex values from the G HUB-inspired redesign; needs theme-aware resource keys for each~~ | ✅ Done — PR #106 |
| F46 | macOS + Linux support — one shared cross-platform app (Avalonia, `xplat/`) with the same function, behaviour and design as Windows; Windows app unchanged. Plan: 11 phases (foundation, Linux audio, macOS audio, OS services, hotkeys, triggers/HID, UI foundation, packaging, Settings UI, dialogs, app wiring) then two friend test sessions | 🚧 In progress — phases 1–6 and 7 parts 1–3 done (PR #116, #118, #120, #122, #124, #126, #128, #130, #132, #134, #136, #138, #140) |

---

**Release process:**
- SHA256 checksums are now generated automatically by the release pipeline (PR #46) — no manual step needed.

---

## PLANNED BRANCHES

Grouped by shared UI surface or implementation concern. Features within each branch can ship together without stepping on each other.

*(Completed branches and their full item lists are documented in RECORD.md — Section 12.)*

---

*(Full item lists for all completed branches are in RECORD.md — Section 12.)*

---

### F46 — macOS + Linux app (`xplat/`, Avalonia) — in progress

Plan approved 2026-10-10. One phase per branch/PR; Windows app untouched throughout. Design: `xplat/docs/ADR-001-xplat.md`.
Phase PRs merge into the holding branch `xplat/dev`, not `main`; one final PR brings it into `main` once the first Mac/Linux version is complete and friend-tested.

| Phase | Branch | Contents | Status |
|---|---|---|---|
| 1 | `xplat/core-scaffold` | Foundation: solution, shared core (linked files + shims + forks), tests on every OS, drift guard | ✅ PR #116 |
| 2 | `xplat/linux-audio` | Linux audio (pactl): list/default/mute/events, tone, mic meter, sounds; PulseAudio + PipeWire CI | ✅ PR #118 |
| 3 | `xplat/macos-audio` | macOS audio (CoreAudio/AudioToolbox); virtual-device CI on Apple Silicon + Intel | ✅ PR #120 |
| 4a | `xplat/os-services` | Paths, single instance + CLI, resume detection, start at login, notifications, mute, install info | ✅ PR #122 |
| 4b | `xplat/hotkeys` | Part 1: hotkey logic on a per-system grabber, macOS Carbon, Linux X11 | ✅ PR #124 |
| 4b-2 | `xplat/hotkeys-wayland` | Part 2: Wayland GlobalShortcuts portal + GNOME custom-shortcut bridge (via the command line), grabber selection | ✅ PR #126 |
| 4c | `xplat/triggers-hid` | App-launch triggers + app pickers, HID headsets, Linux udev rule | ✅ PR #128 |
| 5 | `xplat/ui-foundation` | Part 1: Avalonia app, themes (read from the Windows files), styles, fonts, window frame, async dialogs + first 6 dialogs, screenshot tests | ✅ PR #130 |
| 5-2 | `xplat/ui-parity` | Part 2: a Windows tool that renders the same WPF dialogs, and a side-by-side parity report (WPF vs Mac/Linux screenshots) | ✅ PR #132 |
| 6 | `xplat/packaging` | `.dmg` (Apple Silicon + Intel, ad-hoc signed; notarisation script ready), `.deb`, AppImage, `--version`/`--help`; CI installs and starts each package on Ubuntu 22.04/24.04/26.04, Debian 12/13 and both Macs. Packages are CI downloads (no prerelease workflow: no GitHub releases during the port) | ✅ PR #134 |
| 7 | `xplat/settings-logic` | Part 1: the Windows `SettingsViewModel` / `ProfileCardViewModel` / `IDialogService` linked unchanged (WPF stand-ins, IconHelper/GalleryIconHelper/LogoAnimator forks), `DialogService` waiting in a nested message loop like WPF, Windows view-model tests running on Mac/Linux | ✅ PR #136 |
| 7-2 | `xplat/settings-window` | Part 2: the Settings window frame, top bar, sliding filter bar, the Profiles page (cards, chips, card buttons, save flash, empty states), the profile popup, the animated logo (shared clock, follows the setting), closing to the tray, size/place in Windows units, Sound Settings per system, the Avalonia UI thread for the shared code, `Wpf.RereadIsChecked` (refused toggles flip back) | ✅ PR #138 |
| 7-3 | `xplat/settings-sections` | Part 3: the Settings page (sidebar + Startup, Appearance, Notifications, Devices, Tray, Shortcuts, Mini Window, Logs, Install & Updates), About and the FAQ (links inside the sentences), wording per system (`Settings/Wording.cs`: menu bar / tray, "Open at login" / "Start at login", no "Windows" or "PC"), shortcut set / clear with the Windows conflict checks, export / import through the system pickers, the Install page (full version, sizes, uninstall only where possible), the Windows scroll bar and the context-menu / menu-item / separator styles | ✅ PR #140 |
| 7-4 | `xplat/mini-mode` | Part 4: Mini Mode (compact profile list, always on top, translucent when inactive, position restore). Carried over: the title bar's shrink / expand / pin buttons and `MiniPanel` are in place but do nothing yet; so are Settings › Mini Window's "Customize…" (`CompactCustomize_Click`) and "Switch to Mini Mode now", and the FAQ's "try Mini Mode now" (all call `TitleShrink_Click`) | Planned |
| 8 | `xplat/ui-dialogs` | Remaining dialogs and the splash. Carried over: replace every `NotYet` in `DialogService` (hotkey capture — also `ShowHotkeyCapture(seed, subtitle)`, which Settings › Shortcuts / Mini Window call with the row's own subtitle — device aliases (`ShowDeviceAliases`), clone wizard, icon gallery, profile type, mic test, schedule wizard, sound wizard, supported headsets, app triggers, manage schedules) and the session log window (Settings' errors button only logs for now); gallery emoji as bundled pictures so they look the same everywhere (now drawn with the system's emoji font — none on some Linux installs); the app picker on `IAppCatalog` (Installed off the UI thread, Running, Browse via `TriggerForFile`; show stored triggers with `AppTriggerMatch.DisplayName`), app icons (Linux icon-theme names, Mac `.icns`), the Linux headset-permission prompt on `LinuxHidAccess` (Check → Install off the UI thread; explain TurnedOff / Bluetooth) | Planned |
| 9 | `xplat/app-wiring` | Startup orchestration, tray/menu bar, `--selftest`, diagnostics, per-OS install/uninstall. Carried over: macOS login item via SMAppService (reads the System Settings toggle), handle the macOS "reopen" event (relaunch from Finder/Dock → show Settings), start-at-login for dev builds run via the `dotnet` host, Linux logind `PrepareForSleep` as the primary wake signal, hotkeys via `LinuxHotkeys.CreateAsync` (store the GNOME-shortcuts consent in an xplat-only setting, show the explanation / portal state and desktop-assigned keys in Settings, call `GnomeShortcutBridge.RemoveAll` off the UI thread when consent is withdrawn or on uninstall), and `--switch`/`--mute`/`--mini` when the app isn't running start it and then apply the command, app triggers wired as on Windows (`AppWatcherService` on `LinuxProcesses` / `MacProcesses` + `AppTriggerService`; `HidHeadsetService` → `DeviceTriggerService`), no Dock icon on macOS (menu-bar app: `LSUIElement` / `ShowInDock=false`), a `Dispatcher.UIThread.UnhandledException` handler that logs (an error in background work while a dialog waits would otherwise surface inside the view model); set `SettingsWindow.Uninstall` (Settings › Install & Updates hands over the install and the "delete my data" choice: Mac → Trash + login item, AppImage → delete + desktop entries, `.deb` → package removal, then quit; remove the GNOME shortcuts too); the tray menu's last item is "Quit" (Settings › Tray already says "Settings and Quit are always shown"); check the click behaviour per system — macOS menu-bar icons and GNOME's AppIndicator open the menu on a left click, so "Left-click … cycles profiles" (`Wording.LeftClickCycles`), the FAQ's "right-click for the full menu / hover for the cheat sheet" (`Wording.LivesIn`) and the Tray switch may need other words or hiding there; the ContextMenu style's 10 px transparent shadow margin needs a transparent popup (plain X11 without a compositor draws it black) | Planned |
| 10 | `xplat/rc1` | Parity review vs Windows → friend test session 1 | Planned |
| 11 | `xplat/rc1-fixes` | Fixes from session 1 → friend session 2 → release | Planned |

**For the user to decide or check (raised during the port):**

| Item | Detail |
|---|---|
| UI font | The Mac/Linux app uses Open Sans in place of Segoe UI. Selawik (Microsoft's open font with Segoe UI's exact measurements) would match more closely, but its files are only published as GitHub release downloads, which the repo's safety hook blocks. To switch: add Selawik's `.ttf` files to `xplat/src/VibeSwitcher.App/Assets/Fonts/` and change `AppFonts.Ui` |
| .NET 8 end of support | .NET 8 (the Windows app) reaches end of support on 10 Nov 2026; moving the Windows app to .NET 10 is a separate small change |
| Windows bug: app picker | `AppTriggerDialog.xaml.cs` reads the App Paths registry key from a misspelt path (`SOFTWARE\Microsoft\Windows\AppPaths`), so apps listed only there are missing from the picker |
| Windows bug: tests touch the real registry | `StartupServiceTests` writes to the real HKCU Run key while testing |
| Windows bug: error dialog | `ErrorDialog`'s message box inherits the global text-box height (30 px), so long error messages are cut to about one line (the Mac/Linux version shows them in full) |
| Version number for Mac/Linux | The Mac/Linux app is `2.0.1-preview` for now (set in `xplat/src/VibeSwitcher.App/VibeSwitcher.App.csproj`, `VersionPrefix` + `VersionSuffix`). Pick the number for its first public release (e.g. share the Windows numbering, or start at 2.1.0) |
| Apple developer account | Without one ($99/year), the Mac app is "ad-hoc" signed: the first launch needs "Open Anyway" in System Settings, and macOS asks for microphone permission again after each update. With one, `xplat/packaging/macos/notarize.sh` signs and notarises it (needs `SIGN_IDENTITY` and a `NOTARY_PROFILE`) |
| Shared code: file names compared ignoring case | The Windows app's own code (now shared with Mac/Linux) treats `Icons\Gaming.ico` and `icons\gaming.ico` as the same file, which is right on Windows and macOS. On Linux, where they're different files, the only effects are small: renaming a profile with only a case change can leave its old icon file behind. Fixing it means a small change in the Windows code (compare exactly on Linux) |
| Windows quirk: logo bars jump | In the Windows logo animation, four of the five bars restart part-way through their movement each cycle (their storyboards repeat every "duration minus delay"), so they jump slightly instead of looping smoothly. The Mac/Linux copy does the same, to match. A smooth loop would be a small change in both |
| Windows: profile card drag-and-drop | The Windows profile cards have drop handlers for reordering by drag, but no drag is ever started, so it can't be used. The Mac/Linux app leaves it out too. Either finish it on both or remove the leftover code |
| Mac/Linux wording | Where the Windows Settings, About and FAQ texts name Windows things, the Mac/Linux app uses its own words, all in one file (`xplat/src/VibeSwitcher.App/Settings/Wording.cs`): "Follow system" for the theme, "Open at login" (Mac) / "Start at login" (Linux), "menu bar" (Mac) / "tray" (Linux), "Show in Finder", "Quit" as the tray menu's last item, and About's "Built with" list for each system. Worth a read to change any wording you'd prefer |
| Linux on ARM | Packages are built for 64-bit PCs (x86-64) only; ARM Linux (e.g. Raspberry Pi, ARM laptops) could be added later |

---

### Deferred — No branch planned yet

| # | Feature | Why deferred |
|---|---------|--------------|
| F8 / 8.3 | Auto-updater (GitHub Releases check) | Next up — unblocked by the installer (PR #110) |
| 8.7 | Distribution and discovery — package managers (winget, Chocolatey, Scoop); community posts (Reddit r/Windows + r/Windows11 + r/pcmasterrace, Hacker News Show HN, Product Hunt); AlternativeTo listing; GitHub repo topics; short YouTube demo | Post-v1.0 |
| 10.8 | Website ("coming soon") | External, not in this repo |

---

### Deferred — pre-v2.0.0 follow-ups

Open items from the PR #112 review and the final consolidated 8-agent report-only audit. M1–M3 were fixed on this branch; everything below is deferred (none are Critical/High — none crash, lose data, or block core function). The full AF1–AF5 outcomes live in **RECORD.md → Branch 53 → "Audit follow-ups (AF)"**. Severity in parentheses.

| # | Item | Notes |
|---|------|-------|
| M4 | (Medium) `UploadLibrary` de-dups uploads by **name + byte-length only** (`Helpers/UploadLibrary.cs:30-31`) | Two different files with the same name and identical length silently reuse the first; the test enshrines it. Fix: compare a content hash, not just length, then update `UploadLibraryTests`. Low probability. |
| AF3 | (Low) CI / release hardening — pin `softprops/action-gh-release` to a SHA, pin the choco Inno Setup version, bump xunit / Test.Sdk / coverlet, add a `--logger trx` test-results artifact | Can't validate workflow changes locally; a wrong pin would break the release pipeline. Belongs in a CI-only PR. (Overlaps A8.) |
| LP1 | (Low) Custom switch-sound orphan cleanup on profile rename | `StoreCustomSound` re-copies to a name-matching `Sounds\{name}-{guid}.wav` after a rename, leaving the old per-profile copy behind (no `DeleteOrphanedSound` exists, unlike icons). Harmless stray file in AppData. |
| A1 | (Low) `MuteService` "Both" partial-failure banner (`Services/MuteService.cs:46-52`) | If one endpoint's SetMute fails, the banner still announces both changed. Derive the banner state from each call's result. |
| A2 | (Low) `AppWatcherService` two-field swap not atomic (`Services/AppWatcherService.cs:45-46,66-90`) | `_watchedPaths` + `_skipOnNextPoll` swapped separately; a narrow interleave right after editing triggers could fire one spurious auto-switch. Pack into one immutable record swapped atomically. |
| A3 | (Low) Audio enumeration has no outer catch (`Services/AudioDeviceEnumerator.cs`) | If `EnumAudioEndpoints` itself throws, `DeviceTriggerService.BuildConnectedSet`/ctor let it escape to the global handler. Wrap to log `AudioEnumerationFailed` and return empty. |
| A4 | (Low) Scheduler/DeviceTrigger dictionaries + revert stack not pruned on delete/import (`SchedulerService.cs:15-16`, `DeviceTriggerService.cs:25`) | Tiny unbounded growth; a stale revert-stack top can block a legit revert after delete/import. Prune keys/stack against the current config on Reschedule / config replace. |
| A5 | (Low) `PathSafety` doesn't resolve symlinks/junctions (`Helpers/PathSafety.cs:18-22`) | Only `..` traversal is blocked. Requires an attacker who can already write into `%APPDATA%\VibeSwitcher\`. Reject reparse-point components for defense-in-depth; SECURITY.md slightly overstates the guarantee. |
| A6 | (Low) Config import surfaces errors twice and reads the file twice (`Services/ConfigService.cs:240,269-288`) | A bad import logs a session error *and* shows the dialog; minor TOCTOU/double-parse. Read once; suppress the session-error record on the import path. |
| A7 | (Low) `sha256sums.txt` written with a UTF-8 BOM (`release.yml:66`) | Breaks `sha256sum -c` on Linux. Use `[IO.File]::WriteAllLines` / `-Encoding ascii`. |
| A8 | (Low) Pin the Chocolatey Inno Setup version (`release.yml:51`, `installer/VibeSwitcher.iss:46`) | `x64compatible` needs Inno 6.3+; an older runner-resolved version could break the installer build. (Overlaps AF3.) |
| A9 | (Low) 10 dead theme tokens + accent-chip literals | `VSBlue`, `VSMint`, `VSSchedule*`, `VSSound*` in both theme files are unreferenced; `#1FF5820A`/`#40F5820A`/`#66F5820A` chip tints bake the dark accent. Cosmetic (the prior AF4 items). |
| A10 | (Nit) `TrayService.CycleNextProfile` misleading log (`Tray/TrayService.cs:202-213`) | When the active profile isn't found it still lands on profile 0 correctly; only the warning text reads oddly. Make the fallback explicit. |
| A11 | (Nit) `CHANGELOG.md:279` scheduler-timer entry stale | Says "reduced to 10 seconds" (PR #78); the scheduler now uses a dynamic next-event timer. Historical entry — leave as history or annotate. |
| A12 | (Nit) "Install & Updates" label implies an updater | No update-check code exists (F8 deferred). Consider renaming until F8 ships. |

---

### Dropped — Will not implement

| # | Feature | Why dropped |
|---|---------|-------------|
| C3 / 8.2 | Code signing (Authenticode) | No certificate yet |
| — | Mica / Acrylic material for Settings window | Part of planned UI/UX redesign phase |

*(Dropped feature-additions F9, F10, F27, F38, F40 are struck through in the Feature Additions table above.)*
