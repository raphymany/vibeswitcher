# VibeSwitcher — Open Items (extracted from RECORD.md)

**Last updated:** 2026-06-11 — Branch 51 (`feat/compact-mode`) — PR #108 (Mini Mode compact profile switcher: rows/icon-grid layouts, setup wizard, pin, translucency, global hotkey, mute badge, clickable FAQ actions + 4 new FAQ cards).

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

*(All remaining items tracked in Planned Branches / Deferred — 8.1/C2 → Deferred, 8.2/C3 → Dropped, 8.3 → F8 Deferred, 8.7 → Deferred)*

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
| ~~F1~~ | ~~Import/export `config.json` via Settings for backup and sharing~~ | ✅ Done — PR #68 |
| ~~F2~~ | ~~Drag-and-drop profile reorder~~ | ✅ Done — PR #59 |
| ~~F3~~ | ~~"Test sound" button to verify active device plays audio~~ | ✅ Done — PR #61 |
| ~~F5~~ | ~~Hotkey cheat sheet in tray tooltip~~ | ✅ Done — PR #57 |
| F8 | Auto-updater with GitHub Releases version check | Deferred |
| F9 | Full WinRT toast notifications — persist in Action Center, richer formatting, stack/dismiss | Deferred |
| ~~F10~~ | ~~Per-profile volume level~~ | Dropped — user prefers Windows tray |
| ~~F11~~ | ~~Profile scheduler — per-profile time + day-of-week schedule with automatic switching; optional pre-switch reminder notification fires X minutes before ("Gaming Setup activates in 5 min") so the user can finish what they're doing or override~~ | ✅ Done — PR #74 |
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
| F38 | Temporary / transient profile switch — optional app-wide feature with configurable keybind; switches temporarily and auto-reverts when a timer expires or a linked app closes | Deferred |
| ~~F39~~ | ~~Auto-switch on device connect — link a specific audio device endpoint to a profile; automatically activates when that device connects (Bluetooth, USB); per-device toggle~~ | ✅ Done — PR #90 |
| F40 | Monitor / dock awareness — trigger a profile switch when a specific display or dock connects or disconnects (HDMI, USB-C, Thunderbolt); designed for hybrid work setups | Deferred |
| ~~F41~~ | ~~App-aware auto-switching — link an executable to a profile; switches when that process launches or gains focus; reverts to previous profile when the app closes; per-rule toggle~~ | ✅ Done — PR #96 |
| ~~F42~~ | ~~Settings sub-card layout — each settings group (Startup, Notifications, Shortcuts) gets its own inner card within the General Settings card for clearer visual grouping~~ | ✅ Done — PR #68 |
| ~~F43~~ | ~~Card-based enable/disable — settings cards that support toggling use card-level visual state (full-opacity "live" vs. dimmed "off") instead of per-row pill toggles; the whole card fades when the feature is disabled~~ | ✅ Done — PR #68 |
| ~~F44~~ | ~~Compact / mini Settings window — condensed view that shrinks the window to a minimal layout for users with many profiles; full window restores on demand~~ | ✅ Done — PR #108 |
| ~~F45~~ | ~~Light theme support for redesigned UI — profile cards, action strips, overlays, and icon frames use hardcoded dark hex values from the G HUB-inspired redesign; needs theme-aware resource keys for each~~ | ✅ Done — PR #106 |

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

### Deferred — No branch planned yet

| # | Feature | Why deferred |
|---|---------|--------------|
| C2 / 8.1 | Installer (Inno Setup or WiX) | Saving for last |
| F8 / 8.3 | Auto-updater (GitHub Releases check) | Needs installer first |
| 8.7 | Distribution and discovery — package managers (winget, Chocolatey, Scoop); community posts (Reddit r/Windows + r/Windows11 + r/pcmasterrace, Hacker News Show HN, Product Hunt); AlternativeTo listing; GitHub repo topics; short YouTube demo | Post-v1.0 |
| 10.8 | Website ("coming soon") | External, not in this repo |

---

### Dropped — Will not implement

| # | Feature | Why dropped |
|---|---------|-------------|
| C3 / 8.2 | Code signing (Authenticode) | No certificate yet |
| F9 | Full WinRT toast notifications | Blocked by VS tooling requirement for Windows App SDK |
| — | Mica / Acrylic material for Settings window | Part of planned UI/UX redesign phase |
| F38 | Temporary / transient profile switch | Not useful enough to prioritize |
| F40 | Monitor / dock / environment triggers (display, USB device, power source, network location) | Too broad to scope right now |
