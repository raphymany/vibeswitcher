# VibeSwitcher — Open Items (extracted from AUDIT.md)

**Last updated:** 2026-05-18 — reflects all 20 merged branches (PR #38) + v1.1.0 release + deep-dive review.

Only items **not yet marked ✅ Done** are listed here. Section numbers, letters, and titles match AUDIT.md exactly.

---

## SECTION 1 — CODE REVIEW & SECURITY AUDIT

*(1.1–1.22 resolved. Open items below from deep-dive review.)*

**1.23 — `AudioService.IsDeviceActive()` uses a bare `catch` that swallows all exceptions** *(Low)*
`Services/AudioService.cs` — Catches all exceptions including `OutOfMemoryException`; a device that threw a real error is indistinguishable from a genuinely inactive device. Should catch only COM-related exceptions.

**1.24 — `ErrorDialog` shown without an `Owner` window in `TrayService`** *(Low)*
`Tray/TrayService.cs` — `new ErrorDialog(...).ShowDialog()` called with no `Owner` set. On multi-monitor setups the dialog can appear on the wrong screen or behind other windows.

**1.25 — `TrayService` accesses `ActiveProfileId` without null guard** *(Low)*
`Tray/TrayService.cs` — On fresh install before any profile exists, `_config.ActiveProfileId` is null. Lookups against the profiles list silently return null rather than failing visibly, leaving the tray icon and menu in an inconsistent state.

---

## ~~SECTION 2 — PERFORMANCE~~

*(All items resolved or dropped — 2.7 won't fix)*

---

## SECTION 3 — CODE QUALITY & ARCHITECTURE

*(3.1–3.11 resolved as of PR #37. Open items below from deep-dive review.)*

**3.12 — Profile switch logic duplicated between `TrayService` and `ProfileSwitchOrchestrator`** *(Medium)*
`Tray/TrayService.cs` / `ProfileSwitchOrchestrator.cs` — Near-identical switch flows with slightly different error handling. Bug fixes applied to one path silently miss the other.
Fix: `TrayService` should delegate to `ProfileSwitchOrchestrator` rather than owning its own switch logic.

**3.13 — No concurrent-switch guard in `ProfileSwitchOrchestrator`** *(Medium)*
`ProfileSwitchOrchestrator.cs` — `SwitchToProfile` is `async void` with no in-progress flag. Hotkey spam or rapid tray-menu clicks can trigger multiple overlapping `ApplyProfileAsync` calls, leaving audio devices in an undefined state.
Fix: add a `_switching` flag (or `SemaphoreSlim(1,1)`) and early-return if a switch is already in progress.

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

**4.18 — `SettingsWindow` `ErrorAdded` event handler not cleaned up on hide** *(Medium)*
`Views/SettingsWindow.xaml.cs` — `SessionErrorTracker.ErrorAdded` is unsubscribed in `OnClosed`, but when "Close to Tray" is enabled the window is hidden (`e.Cancel = true; Hide()`) instead of closed — `OnClosed` never fires. Each open-and-hide cycle accumulates another subscription.
Fix: unsubscribe in both `OnClosed` and the `OnClosing` hide path.

**4.19 — `LoadDevicesAsync` not cancellable — concurrent calls overwrite each other** *(Medium)*
`ViewModels/SettingsViewModel.cs` — Fire-and-forget with no cancellation token. Rapid device plug/unplug events trigger concurrent enumerations; whichever finishes last wins, potentially overwriting fresher results with stale device lists.
Fix: cancel the previous call with a `CancellationTokenSource` before starting a new one.

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

**8.9 — No SHA256 checksums** *(Low)*

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
| M16 | Duplicate profile-switch logic in `TrayService` and `ProfileSwitchOrchestrator` — inconsistent bug fix surface |
| M17 | No concurrent-switch guard — hotkey spam causes overlapping `ApplyProfileAsync` calls |
| M18 | `SettingsWindow` `ErrorAdded` handler survives hide (close-to-tray) — accumulates across opens |
| M19 | `LoadDevicesAsync` not cancellable — concurrent calls can overwrite fresh results with stale data |

### LOW — Nice to fix before or after release

| # | Issue | Location |
|---|-------|----------|
| L17 | Dark mode + high-contrast mode — see F16 (same feature) | All XAML |
| L20 | No SHA256 checksums published with binaries | Release pipeline |
| L21 | `AudioService.IsDeviceActive()` bare `catch` swallows all exceptions | `Services/AudioService.cs` |
| L22 | `ErrorDialog` shown without `Owner` in `TrayService` | `Tray/TrayService.cs` |
| L23 | `TrayService` reads `ActiveProfileId` without null guard | `Tray/TrayService.cs` |

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
| F4 | Middle-click tray to toggle between last two profiles |
| F5 | Hotkey cheat sheet in tray tooltip |
| F8 | Auto-updater with GitHub Releases version check |
| F9 | Windows 11 Action Center rich notifications |
| F10 | Per-profile volume level (set device default volume when switching) |
| F11 | Profile scheduler (e.g., work headset 9-5, speakers evenings) |
| F12 | Command-line interface: `VibeSwitcher.exe --switch "Profile Name"` |
| F13 | Portable mode (`--portable` flag storing config next to exe) |
| F14 | System tray scroll wheel for volume control |
| F16 | Dark mode + high-contrast mode (covers L17) — `SystemColors` brushes to follow OS light/dark/high-contrast setting |
| F17 | Built-in profile icons gallery picker |
| F18 | Field feedback — green border flash when a field change is saved |
| F19 | In-app help — F1 key handler and "?" button with getting-started walkthrough |
| F20 | Pre-made profile name suggestions — chips or dropdown with "Gaming Setup", "Home Office", "Music Studio", "Stream Mode", "Headphones", etc.; pairs with F17 for a zero-typing onboarding path |
| F21 | Left-click tray cycles profiles — left-clicking the tray icon switches to the next profile in sort order, wrapping from last back to first; right-click still opens the context menu as normal |
| F22 | Expand-to-fit button in Settings — small toggle button (↗↙ diagonal arrows) in the top-right corner of the Settings window; click expands the window height to show all profile cards and the Add New Profile button without a scrollbar, capped at screen height; click again collapses back to the default compact size |
| F23 | Profile clone button — a duplicate icon next to each profile card's delete button; clones name, device selections, hotkey, and icon path into a new profile |
| F24 | Global hotkey to open Settings — a fixed non-configurable key combo (e.g. Ctrl+Alt+V) that focuses the Settings window from anywhere |
| F25 | Per-profile silent switch — a checkbox in each profile card ("Silent — no notification") to skip the balloon tip when switching to that profile |
| F26 | Device connectivity indicator — a small red dot or strikethrough on disconnected device dropdown items in Settings |
| F27 | Profile color tag — a small colored circle (6 preset colors) on each profile card and tray menu entry for visual distinction |

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
| 21 | `fix/switch-reliability` | M16 + M17 — not started |
| 22 | `fix/settings-async` | M18 + M19 — not started |
| 23 | `fix/null-safety` | L21 + L22 + L23 — not started |
| 24 | `ci/sha256-checksums` | L20/8.9 — not started |
| 25 | `test/additional-coverage` | 7.16 — not started |

---

**Still pending (no branch planned yet):**
C2 (installer — after new design), C3 (code signing — needs certificate purchase), M11/F2 (profile reorder — future feature), L17/F16 (dark mode — after new design), 8.3/F8 (auto-updater), 8.7 (winget/Chocolatey), 10.8 (website), and remaining feature additions (F1–F5, F8–F14, F16–F27).
