# VibeSwitcher — Open Items (extracted from AUDIT.md)

**Last updated:** 2026-05-18 — reflects all 20 merged branches (PR #38) + v1.1.0 release.

Only items **not yet marked ✅ Done** are listed here. Section numbers, letters, and titles match AUDIT.md exactly.

---

## ~~SECTION 1 — CODE REVIEW & SECURITY AUDIT~~

*(All items resolved as of PR #27)*

---

## SECTION 2 — PERFORMANCE

**2.7 — Icon creation allocations on every load** *(Low / deferred)*
Minor GC pressure from `MemoryStream` + `Icon` per user-icon load. `_defaultIcon` caching is correct but browsed icons still allocate per-load.

---

## ~~SECTION 3 — CODE QUALITY & ARCHITECTURE~~

*(All items resolved as of PR #37)*

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

---

## SECTION 5 — WINDOWS INTEGRATION & COMPATIBILITY

**5.9 — Mixed-DPI multi-monitor window position inaccuracy** *(Low)*
`Views/SettingsWindow.xaml.cs` — Known WPF limitation with mixed-DPI setups.

---

## ~~SECTION 6 — CONFIG & DATA INTEGRITY~~

*(All items resolved as of PR #27)*

---

## ~~SECTION 7 — TESTING~~

*(All items resolved as of PR #38)*

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

## SECTION 9 — LOGGING & DIAGNOSTICS

**9.4 — No structured (machine-parseable) logging** *(Low)*
Consider Serilog with a rolling file sink which handles rotation automatically.

**9.5 — No Windows Event Viewer integration** *(Low)*

**9.6 — No crash dump generation** *(Low)*

**9.7 — No opt-in crash reporting** *(Low / business decision)*

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

### LOW — Nice to fix before or after release

| # | Issue | Location |
|---|-------|----------|
| L17 | Dark mode + high-contrast mode — see F16 (same feature) | All XAML |
| L20 | No SHA256 checksums published with binaries | Release pipeline |

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

---

**Still deferred (no branch planned):**
C2/C3 (installer, code signing), L17/L20 (high-contrast/SHA256), M11 (profile reorder), 2.7 (GC pressure), 5.9 (mixed-DPI), 7.8 (UI automation), Sections 8–10 remaining deployment/logging/docs items, and all feature additions (F1–F22 minus F6, F7, F15).
