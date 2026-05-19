# VibeSwitcher — Open Items (extracted from AUDIT.md)

**Last updated:** 2026-05-19 — reflects PR #53 (feat/toast-notifications).

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

*(All remaining items tracked in Planned Branches / Deferred — 8.1/C2 → Deferred, 8.2/C3 → Deferred, 8.3 → F8 Deferred, 8.7 → Deferred)*

---

## ~~SECTION 9 — LOGGING & DIAGNOSTICS~~

*(All items resolved or dropped — 9.4–9.7 won't fix)*

---

## ~~SECTION 10 — DOCUMENTATION~~

*(All remaining items tracked in Feature Additions / Deferred — 10.3 → F19 Branch 31, 10.8 → Deferred)*

---

## SECTION 11 — OPEN ITEMS BY PRIORITY

### ~~CRITICAL — Must fix before any release~~

*(C2 and C3 deferred — installer and code signing saved for last)*

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
| F1 | Import/export `config.json` via Settings for backup and sharing | 31 |
| F2 | Drag-and-drop profile reorder (drag handles, Spotify-style) | 29 |
| F3 | "Test sound" button to verify active device plays audio | 30 |
| F5 | Hotkey cheat sheet in tray tooltip — hover text shows all profiles with their assigned hotkeys | 28 |
| F8 | Auto-updater with GitHub Releases version check | Deferred |
| F9 | Full WinRT toast notifications — persist in Action Center, richer formatting, stack/dismiss | Deferred |
| F10 | Per-profile volume level — sets device default volume when switching to that profile | 30 |
| F11 | Profile scheduler — per-profile time + day-of-week schedule with automatic switching; optional pre-switch reminder notification fires X minutes before ("Gaming Setup activates in 5 min") so the user can finish what they're doing or override | 34 |
| F13 | Portable mode — auto-detected via `portable.txt` next to the exe; stores config in the same folder instead of `%APPDATA%` | 33 |
| F16 | Dark mode + high-contrast — `SystemColors` brushes so the app follows the OS light/dark/high-contrast setting | 31 |
| F17 | Built-in profile icons gallery picker — browse bundled icons instead of a file path | 32 |
| F18 | Field feedback — brief green border flash on a card when a change is saved | 31 |
| F19 | In-app help — "?" button in Settings opens a getting-started walkthrough dialog | 31 |
| F20 | Pre-made profile name suggestions — chips or dropdown with common names; pairs with F17 for zero-typing onboarding | 32 |
| F21 | Left-click tray cycles profiles — switches to the next profile in sort order, wrapping around; right-click still opens the menu | 28 |
| F22 | Expand-to-fit button in Settings — grows the window to show all cards without scrolling, capped at screen height | 31 |
| F23 | Profile clone button — duplicates name, devices, hotkey, and icon into a new profile | 29 |
| F24 | Global hotkey to open Settings — user-configurable, with option to disable entirely | 28 |
| F25 | Per-profile silent switch — checkbox on each card to skip the balloon notification when switching to that profile | 29 |
| F26 | Device connectivity indicator — green/red dot next to each device in the Settings dropdowns | 30 |
| F27 | Profile color tag — small colored circle on each card and in the tray menu; user picks any color via color picker | 32 |

---

**Release process:**
- SHA256 checksums are now generated automatically by the release pipeline (PR #46) — no manual step needed.

---

## PLANNED BRANCHES

Grouped by shared UI surface or implementation concern. Features within each branch can ship together without stepping on each other.

---

### Branch 28: `feat/tray-interactions`
**Theme:** Tray icon and global hotkey UX — no Settings UI changes required.

| # | Feature |
|---|---------|
| F21 | Left-click tray icon cycles to the next profile in sort order (wraps around) |
| 4.14 / F21 variant | Middle-click toggles between the last two active profiles |
| F5 | Hotkey cheat sheet in tray tooltip — hover shows all profile hotkeys |
| F24 | Global hotkey to open Settings — user-configurable, optional |

---

### Branch 29: `feat/profile-management`
**Theme:** Per-card controls in the Settings profile list.

| # | Feature |
|---|---------|
| F2 / 4.7 | Drag-and-drop profile reorder — Spotify-style 3-line grip on left of each card |
| F23 | Profile clone button — duplicates name, devices, hotkey, and icon into a new profile |
| F25 | Per-profile silent switch — checkbox to skip the balloon notification for that profile |

---

### Branch 30: `feat/device-enhancements`
**Theme:** Audio device interaction features — all touch `AudioService` or the device dropdowns.

| # | Feature |
|---|---------|
| F3 / 4.8 | "Test sound" button on each profile — plays a short tone through the selected playback device |
| F10 | Per-profile volume level — sets the device default volume when switching to that profile |
| F26 | Device connectivity indicator — green/red dot next to each device in the Settings dropdowns |

---

### Branch 31: `feat/settings-polish`
**Theme:** Settings window refinements that don't require new audio logic.

| # | Feature |
|---|---------|
| F18 | Field feedback — brief green border flash on a card when a change is saved |
| F22 | Expand-to-fit button — toggle that grows the window to show all cards without scrolling |
| F16 | Dark mode + high-contrast — `SystemColors` brushes to follow the OS theme |
| F1 / 4.13 | Config import/export — backup and transfer profiles via Settings |
| F19 / 10.3 | In-app help — "?" button in Settings opens a getting-started walkthrough dialog |

---

### Branch 32: `feat/profile-visual`
**Theme:** Visual identity features for profiles — icons, names, and colors.

| # | Feature |
|---|---------|
| F17 | Built-in profile icons gallery picker — browse bundled icons instead of a file path |
| F20 | Pre-made profile name suggestions — chips or dropdown with common names; pairs with F17 |
| F27 | Profile color tag — small colored circle on each card and in the tray menu |

---

### Branch 33: `feat/portable-mode`
**Theme:** Portable install support — config stored next to the exe when `portable.txt` is present.

| # | Feature |
|---|---------|
| F13 | Portable mode — auto-detected via `portable.txt` next to the exe; stores config locally |

---

### Branch 34: `feat/profile-scheduler`
**Theme:** Time-based automatic profile switching.

| # | Feature |
|---|---------|
| F11 | Profile scheduler — per-profile schedule (time + days of week); background timer checks current time and switches automatically; integrates with the existing power-mode wake handler so schedules re-evaluate correctly after sleep/wake |
| F11 (reminder) | Optional pre-switch reminder notification — each schedule entry has a configurable lead time (e.g. 5 min before, or disabled); fires a balloon tip like "Gaming Setup activates in 5 minutes" so the user can finish what they're doing or manually override before the switch happens |

---

### Deferred — No branch planned yet

| # | Feature | Why deferred |
|---|---------|--------------|
| C2 / 8.1 | Installer (Inno Setup or WiX) | Saving for last |
| C3 / 8.2 | Code signing (Authenticode) | No certificate yet |
| F8 / 8.3 | Auto-updater (GitHub Releases check) | Needs installer first |
| 8.7 | winget / Chocolatey package | Post-v1.0 distribution |
| F9 | Full WinRT toast notifications | Blocked by VS tooling requirement for Windows App SDK |
| 10.8 | Website ("coming soon") | External, not in this repo |
