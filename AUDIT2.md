# VibeSwitcher — Open Items (extracted from AUDIT.md)

**Last updated:** 2026-05-18 — reflects PR #46 (ci/sha256-checksums).

Only items **not yet marked ✅ Done** are listed here. Section numbers, letters, and titles match AUDIT.md exactly.

---

## ~~SECTION 1 — CODE REVIEW & SECURITY AUDIT~~

*(All items resolved as of PR #44)*

---

## ~~SECTION 2 — PERFORMANCE~~

*(All items resolved or dropped — 2.7 won't fix)*

---

## ~~SECTION 3 — CODE QUALITY & ARCHITECTURE~~

*(All items resolved as of PR #40)*

---

## SECTION 4 — USER EXPERIENCE

**4.7 — No way to reorder profiles** *(Medium)*
`SortOrder` is set at creation and never updated via UI.
Fix: drag handles (Spotify-style, 3-line grip on left of card).

**4.8 — No "Test Sound" button** *(Low)*
Users cannot verify the correct device is active without switching away.

**4.13 — No config import/export** *(Low)*
Users cannot back up or transfer their profiles.

**4.14 — No middle-click tray handler** *(Low)*
Convention: middle-click toggles between last two profiles.

~~**4.18** — resolved PR #42~~

~~**4.19** — resolved PR #42~~

---

## ~~SECTION 5 — WINDOWS INTEGRATION & COMPATIBILITY~~

*(All items resolved or dropped — 5.9 won't fix)*

---

## ~~SECTION 6 — CONFIG & DATA INTEGRITY~~

*(All items resolved as of PR #27)*

---

## SECTION 7 — TESTING

*(7.1–7.7, 7.10–7.15 resolved. 7.8 dropped. Open items below.)*

**7.9 — Manual regression checklist** *(Ongoing)*
Run before each release: first-run flow, corrupted config recovery, single-instance guard, profile switch with device present/disconnected, hotkey switch, close-to-tray, window position/size persistence, settings toggles.

**7.16 — Additional unit tests identified in deep-dive review** *(Low)*
- `_loadingDevices` guard: set flag to true via reflection, change `SelectedPlaybackDevice` — verify `_onChanged` is NOT fired
- `ConfigService.Migrate()` asymmetric sentinel: `WindowLeft = -1` and `WindowTop = 200.0` — verify only left is nulled
- `IconHelper.LoadIcon()` with a file that exists but contains invalid icon data — verify default icon returned and `HasErrors` is true
- `LoadDevicesAsync` concurrent calls — verify dropdowns reflect the most recent result and no exception is thrown
- `SettingsViewModel.OnDevicesChanged()` invoked from a background thread — verify no unhandled exception

---

## SECTION 8 — DEPLOYMENT & DISTRIBUTION

**8.1 — No installer** *(High / pre-release blocker)*
No installer project exists. Users must run the exe directly from any directory.
Recommendation: Inno Setup (free, simple) or WiX v4.

**8.2 — No code signing** *(High / pre-release blocker)*
Without Authenticode, Windows SmartScreen blocks the app on every first run for every user.

**8.3 — No auto-updater** *(Medium)*
No mechanism to notify users of or deliver new versions.

**8.7 — No winget or Chocolatey package** *(Low / post-release)*

~~**8.9 — No SHA256 checksums**~~ ✅ Done — PR #46

---

## ~~SECTION 9 — LOGGING & DIAGNOSTICS~~

*(All items resolved or dropped — 9.4–9.7 won't fix)*

---

## SECTION 10 — DOCUMENTATION

**10.3 — No in-app help** *(Feature — F19)*

**10.8 — Website labeled "coming soon"** *(Low)*

---

## SECTION 11 — OPEN ITEMS BY PRIORITY

### CRITICAL — Must fix before any release

| # | Issue | Location |
|---|-------|----------|
| C2 | No installer / distribution mechanism | Build pipeline |
| C3 | No code signing (SmartScreen blocks app on every first run) | Build pipeline |

### HIGH — Important before v1.0.0

*(All High items resolved)*

### MEDIUM — Should fix for a quality release

| # | Issue |
|---|-------|
| M11 | No profile reorder UI — drag handles (Spotify-style) planned for future branch |
| ~~M16~~ | ~~Duplicate profile-switch logic~~ | ✅ Done — PR #40 |
| ~~M17~~ | ~~No concurrent-switch guard~~ | ✅ Done — PR #40 |
| ~~M18~~ | ~~`ErrorAdded` handler survives hide~~ | ✅ Done — PR #42 |
| ~~M19~~ | ~~`LoadDevicesAsync` not cancellable~~ | ✅ Done — PR #42 |

### LOW — Nice to fix before or after release

| # | Issue | Location |
|---|-------|----------|
| L17 | Dark mode + high-contrast mode — see F16 (same feature) | All XAML |
| ~~L20~~ | ~~No SHA256 checksums published with binaries~~ | ✅ Done — PR #46 |
| ~~L21~~ | ~~`AudioService.IsDeviceActive()` bare `catch` swallows all exceptions~~ | ✅ Done — PR #44 |
| ~~L22~~ | ~~`ErrorDialog` shown without `Owner` in `TrayService`~~ | ✅ Done — PR #44 |
| ~~L23~~ | ~~`TrayService` reads `ActiveProfileId` without null guard~~ | ✅ Done — PR #44 |

### TECHNICAL DEBT

*(All Technical Debt items resolved as of PR #38)*

### REFACTORING OPPORTUNITIES

*(All Refactoring Opportunities resolved as of PR #37)*

### FEATURE ADDITIONS (post-v1.0.0)

| # | Feature |
|---|---------|
| F1 | Import/export `config.json` via Settings for backup and sharing |
| F2 | Drag-and-drop profile reorder (drag handles, Spotify-style) |
| F3 | "Test sound" button to verify active device plays audio |
| F4 | Middle-click tray to toggle between last two profiles | REMOVE
| F5 | Hotkey cheat sheet in tray tooltip | WHAT EXACTLY IS THIS?
| F8 | Auto-updater with GitHub Releases version check |
| F9 | Windows 11 Action Center rich notifications | WHAT EXACTLY IS THIS?
| F10 | Per-profile volume level (set device default volume when switching) |
| F11 | Profile scheduler (e.g., work headset 9-5, speakers evenings) |
| F12 | Command-line interface: `VibeSwitcher.exe --switch "Profile Name"` | WHAT EXACTLY IS THIS? IS IT REALLY NEEDED?
| F13 | Portable mode (`--portable` flag storing config next to exe) | WHAT EXACTLY IS THIS?
| F14 | System tray scroll wheel for volume control | WHAT EXACTLY IS THIS?
| F16 | Dark mode + high-contrast mode (covers L17) — `SystemColors` brushes to follow OS light/dark/high-contrast setting |
| F17 | Built-in profile icons gallery picker |
| F18 | Field feedback — green border flash when a field change is saved |
| F19 | In-app help — F1 key handler and "?" button with getting-started walkthrough | WHAT IS F1 KEY HANDLER DO?
| F20 | Pre-made profile name suggestions — chips or dropdown with "Gaming Setup", "Home Office", "Music Studio", "Stream Mode", "Headphones", etc.; pairs with F17 for a zero-typing onboarding path |
| F21 | Left-click tray cycles profiles — left-clicking the tray icon switches to the next profile in sort order, wrapping from last back to first; right-click still opens the context menu as normal |
| F22 | Expand-to-fit button in Settings — small toggle button (↗↙ diagonal arrows) in the top-right corner of the Settings window; click expands the window height to show all profile cards and the Add New Profile button without a scrollbar, capped at screen height; click again collapses back to the default compact size |
| F23 | Profile clone button — a duplicate icon next to each profile card's delete button; clones name, device selections, hotkey, and icon path into a new profile |
| F24 | Global hotkey to open Settings — a fixed non-configurable key combo (e.g. Ctrl+Alt+V) that focuses the Settings window from anywhere | MAKE IT SO THE USER CAN SET THIS AND HAVE AN OPTION FOR THE USER TO DISABLE THIS FEATURE
| F25 | Per-profile silent switch — a checkbox in each profile card ("Silent — no notification") to skip the balloon tip when switching to that profile |
| F26 | Device connectivity indicator — a small red dot or strikethrough on disconnected device dropdown items in Settings | WHAT EXACTLY IS THIS?
| F27 | Profile color tag — a small colored circle (6 preset colors) on each profile card and tray menu entry for visual distinction | CAN THEY ONLY HAVE 6 PRESET COLORS OR THEY CAN CHOOSE ANY COLOR? ALSO WHATS "TRAY MENU ENTRY"?

---

**Release process (not a branch — do at next release):**
- L20 / 8.9: Generate and publish SHA256 checksums alongside the zip in the GitHub release

---

## SECTION 12 — BRANCH EXECUTION LOG

| # | Branch | Status |
|---|--------|--------|
| ~~1~~ | ~~`fix/startup-registry`~~ | ✅ Merged |
| ~~2~~ | ~~`fix/config-integrity`~~ | ✅ Merged |
| ~~3~~ | ~~`fix/hotkey-registration`~~ | ✅ Merged |
| ~~4~~ | ~~`fix/ui-polish`~~ | ✅ Merged |
| ~~5~~ | ~~`fix/dpi-threading`~~ | ✅ Merged |
| ~~6~~ | ~~`fix/settings-performance`~~ | ✅ Merged |
| ~~7~~ | ~~`feat/error-codes-and-logs`~~ | ✅ Merged |
| ~~8~~ | ~~`fix/security-hardening`~~ | ✅ Merged |
| ~~9~~ | ~~`fix/about-diagnostics`~~ | ✅ Merged |
| ~~10~~ | ~~`fix/startup-service`~~ | ✅ Merged |
| ~~11~~ | ~~`fix/ux-polish-2`~~ | ✅ Merged |
| ~~12~~ | ~~`fix/ux-polish-3`~~ | ✅ Merged |
| ~~13~~ | ~~`fix/ux-polish-4`~~ | ✅ Merged |
| ~~14~~ | ~~`feat/audio-reliability`~~ | ✅ Merged — PR #30 |
| ~~15~~ | ~~`fix/keyboard-nav-focus`~~ | ✅ Merged — PR #31 |
| ~~16~~ | ~~`test/unit-tests`~~ | ✅ Merged — PR #33 |
| ~~17~~ | ~~`refactor/interfaces`~~ | ✅ Merged — PR #35 |
| ~~18~~ | ~~`refactor/viewmodel-dialogs`~~ | ✅ Merged — PR #36 |
| ~~19~~ | ~~`refactor/god-class`~~ | ✅ Merged — PR #37 |
| ~~20~~ | ~~`ci/cd-pipeline`~~ | ✅ Merged — PR #38 |
| ~~21~~ | ~~`fix/switch-reliability`~~ | ✅ Merged — PR #40 |
| ~~22~~ | ~~`fix/settings-async`~~ | ✅ Merged — PR #42 |
| ~~23~~ | ~~`fix/null-safety`~~ | ✅ Merged — PR #44 |
| ~~24~~ | ~~`ci/sha256-checksums`~~ | ✅ Merged — PR #46 |
| 25 | `test/additional-coverage` | 7.16 — not started |

---

### ~~Branch 21: `fix/switch-reliability`~~ ✅ Merged — PR #40
**Theme:** Remove duplicate switch logic and add a concurrent-switch guard.

| Item | Description | Status |
|------|-------------|--------|
| M16 / 3.12 | `TrayService.SwitchToProfileAsync` duplicates the switch flow — delegate to `ProfileSwitchOrchestrator` instead | ✅ Done |
| M17 / 3.13 | No in-progress flag in `ProfileSwitchOrchestrator` — hotkey spam triggers overlapping `ApplyProfileAsync` calls; add `SemaphoreSlim(1,1)` guard | ✅ Done |

---

### ~~Branch 22: `fix/settings-async`~~ ✅ Merged — PR #42
**Theme:** Fix the two async/event correctness issues in the Settings window.

| Item | Description | Status |
|------|-------------|--------|
| M18 / 4.18 | `SessionErrorTracker.ErrorAdded` subscription never removed when window is hidden (close-to-tray path) — switched to `IsVisibleChanged` to subscribe/unsubscribe on visibility | ✅ Done |
| M19 / 4.19 | `LoadDevicesAsync` has no cancellation — rapid plug/unplug events cause concurrent enumerations that overwrite each other; added `CancellationTokenSource` with `Interlocked.Exchange` | ✅ Done |

---

### ~~Branch 23: `fix/null-safety`~~ ✅ Merged — PR #44
**Theme:** Three small robustness fixes found in deep-dive review.

| Item | Description | Status |
|------|-------------|--------|
| L21 / 1.23 | `AudioService.IsDeviceActive()` bare `catch` narrowed to COM exceptions; device COM object leak and unchecked `GetState` HRESULT also fixed | ✅ Done |
| L22 / 1.24 | `ErrorDialog` in `ProfileSwitchOrchestrator` now sets `Owner` to the first visible window with `CenterOwner` placement | ✅ Done |
| L23 / 1.25 | Stale `ActiveProfileId` reset at startup with a warning log; `IsChecked` in `RebuildMenu` uses explicit `HasValue && .Value` | ✅ Done |

---

### ~~Branch 24: `ci/sha256-checksums`~~ ✅ Merged — PR #46
**Theme:** Publish SHA256 checksums alongside each GitHub Release zip.

| Item | Description | Status |
|------|-------------|--------|
| L20 / 8.9 | Added "Generate SHA256 checksum" step to `release.yml` — uses `Get-FileHash` (PowerShell 7, BOM-free UTF-8) and writes `sha256sums.txt` in two-space format; both files attached to the release | ✅ Done |

---

### Branch 25: `test/additional-coverage`
**Theme:** Additional unit tests identified in the deep-dive review.

| Item | Description | Status |
|------|-------------|--------|
| 7.16a | `_loadingDevices` guard: verify `_onChanged` is not fired when flag is true, is fired when false | Not started |
| 7.16b | `ConfigService.Migrate()` asymmetric sentinel: `WindowLeft = -1` with `WindowTop = 200.0` — verify only left is nulled | Not started |
| 7.16c | `IconHelper.LoadIcon()` with invalid icon data — verify default returned and `HasErrors` is true | Not started |
| 7.16d | `LoadDevicesAsync` concurrent calls — verify dropdowns reflect most recent result, no exception thrown | Not started |
| 7.16e | `SettingsViewModel.OnDevicesChanged()` invoked from background thread — verify no unhandled exception | Not started |

---

**Still pending (no branch planned yet):**
C2 (installer — after new design), C3 (code signing — needs certificate purchase), M11/F2 (profile reorder — future feature), L17/F16 (dark mode — after new design), 8.3/F8 (auto-updater), 8.7 (winget/Chocolatey), 10.8 (website), and remaining feature additions (F1–F5, F8–F14, F16–F27).
