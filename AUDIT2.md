# VibeSwitcher — Open Items (extracted from AUDIT.md)

**Last updated:** 2026-05-17 — reflects all 14 merged branches (PR #30) + v1.1.0 release.

Only items **not yet marked ✅ Done** are listed here. Section numbers, letters, and titles match AUDIT.md exactly.

---

## SECTION 1 — CODE REVIEW & SECURITY AUDIT

*(All Section 1 open items resolved as of PR #27)*

---

## SECTION 2 — PERFORMANCE

**2.7 — Icon creation allocations on every load** *(Low / deferred)*
Minor GC pressure from `MemoryStream` + `Icon` per user-icon load. `_defaultIcon` caching is correct but browsed icons still allocate per-load.

---

## SECTION 3 — CODE QUALITY & ARCHITECTURE

**3.1 — MVVM violation: ViewModels directly instantiate and open View classes** *(Medium)*
`ViewModels/ProfileCardViewModel.cs`, `ViewModels/SettingsViewModel.cs` — ViewModels directly `new` up `HotkeyCaptureDialog`, `ConfirmDeleteDialog`, `ProfileTypeDialog`, `OpenFileDialog`. Untestable and couples ViewModels to Views.
Fix: extract `IDialogService` interface.

**3.3 — `App.xaml.cs` is a God Class** *(Medium)*
Owns services, handles `WndProc`, orchestrates profile switches, manages window lifecycle, drives startup.
Fix: extract `ProfileSwitchOrchestrator` and `WindowManager`.

**3.8 — No `IConfigService` interface** *(Low)*
All consumers hold concrete `ConfigService`. Prevents testability.

---

## SECTION 4 — USER EXPERIENCE

**4.7 — No way to reorder profiles** *(Medium)*
`SortOrder` is set at creation and never updated via UI.
Fix: drag handles (Spotify-style, 3-line grip on left of card).

**4.8 — No "Test Sound" button** *(Low)*
Users cannot verify the correct device is active without switching away.

**4.11 — Dark mode not supported** *(Feature)*
All colors hardcoded as light-mode hex values. On Windows dark mode, windows look out of place.
Fix: use `SystemColors` brushes or a theme resource dictionary.

**4.13 — No config import/export** *(Low)*
Users cannot back up or transfer their profiles.

**4.14 — No middle-click tray handler** *(Low)*
Convention: middle-click toggles between last two profiles.

**4.15 — No high-contrast mode support** *(Low)*
Custom-styled controls do not respond to Windows High Contrast mode.

---

## SECTION 5 — WINDOWS INTEGRATION & COMPATIBILITY

**5.9 — Mixed-DPI multi-monitor window position inaccuracy** *(Low)*
`Views/SettingsWindow.xaml.cs` — Known WPF limitation with mixed-DPI setups.

---

## SECTION 6 — CONFIG & DATA INTEGRITY

---

## SECTION 7 — TESTING

**7.1 — No test project exists** *(High)*
Zero automated coverage.

*(See AUDIT.md sections 7.2–7.11 for the full suggested test suite.)*

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

| # | Issue | Location |
|---|-------|----------|

### MEDIUM — Should fix for a quality release

| # | Issue |
|---|-------|
| M6 | MVVM violation — ViewModels directly instantiate View dialogs |
| M11 | No profile reorder UI — drag handles (Spotify-style) planned for future branch |

### LOW — Nice to fix before or after release

| # | Issue | Location |
|---|-------|----------|
| L17 | No high-contrast mode support | All XAML |
| L20 | No SHA256 checksums published with binaries | Release pipeline |

### TECHNICAL DEBT

| # | Item |
|---|------|
| TD1 | No test project — zero automated test coverage |
| TD2 | No `IAudioService` / `IConfigService` interfaces preventing testability |
| TD3 | `App.xaml.cs` has too many responsibilities (God Class) |
| TD4 | No `IDialogService` abstraction — ViewModel/View tightly coupled |
| TD7 | No CI/CD pipeline configured |

### REFACTORING OPPORTUNITIES

| # | Opportunity |
|---|-------------|
| R2 | Extract `ProfileSwitchOrchestrator` from `App.xaml.cs` |

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
| F16 | Dark mode / Windows theme support |
| F17 | Built-in profile icons gallery picker |
| F18 | Field feedback — green border flash when a field change is saved |
| F19 | In-app help — F1 key handler and "?" button with getting-started walkthrough |
| F20 | Pre-made profile name suggestions — chips or dropdown with "Gaming Setup", "Home Office", "Music Studio", "Stream Mode", "Headphones", etc.; pairs with F17 for a zero-typing onboarding path |
| F21 | Left-click tray cycles profiles — left-clicking the tray icon switches to the next profile in sort order, wrapping from last back to first; right-click still opens the context menu as normal |
| F22 | Expand-to-fit button in Settings — small toggle button (↗↙ diagonal arrows) in the top-right corner of the Settings window; click expands the window height to show all profile cards and the Add New Profile button without a scrollbar, capped at screen height; click again collapses back to the default compact size |

---

**Release process (not a branch — do at next release):**
- L20 / 8.9: Generate and publish SHA256 checksums alongside the zip in the GitHub release

