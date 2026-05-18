# VibeSwitcher — Open Items (extracted from AUDIT.md)

**Last updated:** 2026-05-19 — reflects PR #50 (feat/app-icon-refresh).

Only items **not yet marked ✅ Done** are listed here. Section numbers, letters, and titles match AUDIT.md exactly.

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

## SECTION 8 — DEPLOYMENT & DISTRIBUTION

**8.1 — No installer** *(High / pre-release blocker)*
No installer project exists. Users must run the exe directly from any directory.
Recommendation: Inno Setup (free, simple) or WiX v4.

**8.2 — No code signing** *(High / pre-release blocker)*
Without Authenticode, Windows SmartScreen blocks the app on every first run for every user.

**8.3 — No auto-updater** *(Medium)*
No mechanism to notify users of or deliver new versions.

**8.7 — No winget or Chocolatey package** *(Low / post-release)*

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

| # | Feature |
|---|---------|
| F1 | Import/export `config.json` via Settings for backup and sharing |
| F2 | Drag-and-drop profile reorder (drag handles, Spotify-style) |
| F3 | "Test sound" button to verify active device plays audio |
| F5 | Hotkey cheat sheet in tray tooltip — hover text shows all profiles with their assigned hotkeys (e.g. "Desktop Setup: Ctrl+PgUp / Gaming: Ctrl+PgDn") so you can see the full list without opening any window |
| F8 | Auto-updater with GitHub Releases version check |
| F9 | Windows 11 Toast notifications — replace the current balloon tip with the modern Windows 10/11 Toast API so notifications persist in Action Center, support richer formatting, and stack/stack-dismiss properly |
| F10 | Per-profile volume level (set device default volume when switching) |
| F11 | Profile scheduler (e.g., work headset 9-5, speakers evenings) |
| F13 | Portable mode — if a file named `portable.txt` exists next to the exe, config is stored in the same folder instead of `%APPDATA%`; no CLI needed, just drop the file there once and the app auto-detects it on every launch; useful for USB/portable installs |
| F16 | Dark mode + high-contrast mode — `SystemColors` brushes so the app follows the OS light/dark/high-contrast setting |
| F17 | Built-in profile icons gallery picker |
| F18 | Field feedback — green border flash when a field change is saved |
| F19 | In-app help — "?" button in Settings opens a getting-started walkthrough dialog |
| F20 | Pre-made profile name suggestions — chips or dropdown with "Gaming Setup", "Home Office", "Music Studio", "Stream Mode", "Headphones", etc.; pairs with F17 for a zero-typing onboarding path |
| F21 | Left-click tray cycles profiles — left-clicking the tray icon switches to the next profile in sort order, wrapping from last back to first; right-click still opens the context menu as normal |
| F22 | Expand-to-fit button in Settings — small toggle button (↗↙ diagonal arrows) in the top-right corner of the Settings window; click expands the window height to show all profile cards and the Add New Profile button without a scrollbar, capped at screen height; click again collapses back to the default compact size |
| F23 | Profile clone button — a duplicate icon next to each profile card's delete button; clones name, device selections, hotkey, and icon path into a new profile |
| F24 | Global hotkey to open Settings — user-configurable key combo set in Settings (like any other hotkey), with an option to disable it entirely; focuses the Settings window from anywhere |
| F25 | Per-profile silent switch — a checkbox in each profile card ("Silent — no notification") to skip the balloon tip when switching to that profile |
| F26 | Device connectivity indicator — green dot next to connected devices and red dot next to disconnected devices in the Settings dropdowns, so you can see at a glance which are available without trying to switch |
| F27 | Profile color tag — a small colored circle on each profile card and in the tray right-click menu next to the profile name; user picks any color via a color picker (not limited to presets) for visual distinction between profiles |

---

**Release process:**
- SHA256 checksums are now generated automatically by the release pipeline (PR #46) — no manual step needed.

---

**Still pending (no branch planned yet):**
C2 (installer — after new design), C3 (code signing — needs certificate purchase), 8.3/F8 (auto-updater), 8.7 (winget/Chocolatey), 10.8 (website), and remaining feature additions (F1–F27).
