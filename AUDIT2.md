# VibeSwitcher — Open Items (extracted from AUDIT.md)

**Last updated:** 2026-05-20 — Branch 32 merged (PR #68).

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
| ~~F1~~ | ~~Import/export `config.json` via Settings for backup and sharing~~ | ✅ Done — PR #68 |
| ~~F2~~ | ~~Drag-and-drop profile reorder~~ | ✅ Done — PR #59 |
| ~~F3~~ | ~~"Test sound" button to verify active device plays audio~~ | ✅ Done — PR #61 |
| ~~F5~~ | ~~Hotkey cheat sheet in tray tooltip~~ | ✅ Done — PR #57 |
| F8 | Auto-updater with GitHub Releases version check | Deferred |
| F9 | Full WinRT toast notifications — persist in Action Center, richer formatting, stack/dismiss | Deferred |
| ~~F10~~ | ~~Per-profile volume level~~ | Dropped — user prefers Windows tray |
| F11 | Profile scheduler — per-profile time + day-of-week schedule with automatic switching; optional pre-switch reminder notification fires X minutes before ("Gaming Setup activates in 5 min") so the user can finish what they're doing or override | 33 |
| F16 | Dark mode + high-contrast — `SystemColors` brushes so the app follows the OS light/dark/high-contrast setting | 45 |
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
| F31 | Audio endpoint aliases — user-defined friendly name per device shown in Settings dropdowns instead of the raw Windows device name (e.g. "GoXLR", "Desk Speakers") | 36 |
| F32 | Profile notes — optional short description field on each profile card; stored per profile, shown below the profile name | 35 |
| F33 | Favorite / pinned profiles — star flag per profile; pinned profiles appear at the top of the tray menu above unpinned ones | 35 |
| F34 | Profile validation warnings — inline warning on cards for duplicate hotkeys, missing/disabled devices, or invalid icon paths | 35 |
| F35 | Search / filter in Settings — text box at the top of the profile list; filters cards in real time by profile name, device name, or hotkey; clears on Escape | 37 |
| F36 | Optional switch sound — short audio cue on profile switch; built-in tones or custom .wav; per-profile toggle; pairs with F25 and F30 for a fully configurable feedback system | 38 |
| F37 | Deafen / panic hotkey — global configurable hotkey that instantly mutes all recording devices; tray icon flashes red while active; press again to unmute | 39 |
| F38 | Temporary / transient profile switch — optional app-wide feature with configurable keybind; switches temporarily and auto-reverts when a timer expires or a linked app closes | 40 |
| F39 | Auto-switch on device connect — link a specific audio device endpoint to a profile; automatically activates when that device connects (Bluetooth, USB); per-device toggle | 41 |
| F40 | Monitor / dock awareness — trigger a profile switch when a specific display or dock connects or disconnects (HDMI, USB-C, Thunderbolt); designed for hybrid work setups | 42 |
| F41 | App-aware auto-switching — link an executable to a profile; switches when that process launches or gains focus; reverts to previous profile when the app closes; per-rule toggle | 43 |
| ~~F42~~ | ~~Settings sub-card layout — each settings group (Startup, Notifications, Shortcuts) gets its own inner card within the General Settings card for clearer visual grouping~~ | ✅ Done — PR #68 |
| ~~F43~~ | ~~Card-based enable/disable — settings cards that support toggling use card-level visual state (full-opacity "live" vs. dimmed "off") instead of per-row pill toggles; the whole card fades when the feature is disabled~~ | ✅ Done — PR #68 |

---

**Release process:**
- SHA256 checksums are now generated automatically by the release pipeline (PR #46) — no manual step needed.

---

## PLANNED BRANCHES

Grouped by shared UI surface or implementation concern. Features within each branch can ship together without stepping on each other.

---

### ~~Branch 28: `feat/tray-interactions`~~ ✅ Merged — PR #57
**Theme:** Tray icon and global hotkey UX — left-click cycle, hotkey tooltip, Settings hotkey, icon flash, hotkey conflict UX polish.

| # | Feature |
|---|---------|
| ~~F21~~ | ~~Left-click tray icon cycles to the next profile in sort order (wraps around)~~ |
| ~~F5~~ | ~~Hotkey cheat sheet in tray tooltip — hover shows all profile hotkeys~~ |
| ~~F24~~ | ~~Global hotkey to open Settings — user-configurable, optional~~ |
| ~~F30~~ | ~~Tray icon switch flash — brief icon pulse when a profile switch completes~~ |

---

### ~~Branch 29: `feat/profile-management`~~ ✅ Merged — PR #59
**Theme:** Per-card controls in the Settings profile list — reorder, clone, silent switch.

| # | Feature |
|---|---------|
| ~~F2~~ | ~~Drag-and-drop profile reorder — ⠿ grip on each card~~ |
| ~~F23~~ | ~~Profile clone button~~ |
| ~~F25~~ | ~~Per-profile silent switch~~ |

---

### ~~Branch 30: `feat/device-enhancements`~~ ✅ Merged — PR #61
**Theme:** Audio device interaction features — all touch `AudioService` or the device dropdowns.

| # | Feature |
|---|---------|
| ~~F3 / 4.8~~ | ~~"Test sound" button on each profile — plays a short tone through the selected playback device~~ |
| ~~F10~~ | ~~Per-profile volume level~~ — Dropped (user prefers Windows tray) |
| ~~F26~~ | ~~Device connectivity indicator — green/red dot next to each device in the Settings dropdowns~~ |

---

### ~~Branch 31: `feat/profile-visual`~~ ✅ Merged — PR #66
**Theme:** Visual identity features for profiles — icon gallery and name suggestions.

| # | Feature |
|---|---------|
| ~~F17~~ | ~~Built-in profile icons gallery picker — browse bundled icons instead of a file path~~ |
| ~~F20~~ | ~~Pre-made profile name suggestions — chips or dropdown with common names; pairs with F17~~ |

---

### ~~Branch 32: `feat/settings-polish`~~ ✅ Merged — PR #68
**Theme:** Settings window refinements — visual feedback, inner cards, info badges, tray toggle, backup/restore.

| # | Feature |
|---|---------|
| ~~F18~~ | ~~Field feedback — brief green border flash on a card when a change is saved~~ |
| ~~F1 / 4.13~~ | ~~Config import/export — backup and transfer profiles via Settings~~ |
| ~~F19 / 10.3~~ | ~~In-app help — "?" button in Settings opens a getting-started walkthrough dialog~~ |
| ~~F42~~ | ~~Settings sub-card layout — each group (Startup, Notifications, Shortcuts) in its own inner card~~ |
| ~~F43~~ | ~~Card-based enable/disable — toggleable settings cards fade when disabled instead of using pill toggles~~ |
| F22 | Expand-to-fit button — toggle that grows the window to show all cards without scrolling (not delivered — pulled from branch) |

---

### Branch 33: `feat/profile-scheduler`
**Theme:** Time-based automatic profile switching.

| # | Feature |
|---|---------|
| F11 | Profile scheduler — per-profile schedule (time + days of week); background timer checks current time and switches automatically; integrates with the existing power-mode wake handler so schedules re-evaluate correctly after sleep/wake |
| F11 (reminder) | Optional pre-switch reminder notification — each schedule entry has a configurable lead time (e.g. 5 min before, or disabled); fires a balloon tip like "Gaming Setup activates in 5 minutes" so the user can finish what they're doing or manually override before the switch happens |

---

### Branch 35: `feat/profile-card-extras`
**Theme:** Small per-profile additions to the Settings card that don't touch audio logic.

| # | Feature |
|---|---------|
| F32 | Profile notes — optional short description field per card |
| F33 | Favorite / pinned profiles — star flag; pinned profiles appear at the top of the tray menu |
| F34 | Profile validation warnings — inline flags for duplicate hotkeys, missing/disabled devices, or invalid icon paths |

---

### Branch 36: `feat/device-aliases`
**Theme:** Per-device friendly name display throughout the app.

| # | Feature |
|---|---------|
| F31 | Audio endpoint aliases — user-defined friendly names shown in Settings dropdowns and profile cards instead of the raw Windows device name |

---

### Branch 37: `feat/settings-search`
**Theme:** Profile search and filtering in the Settings window.

| # | Feature |
|---|---------|
| F35 | Search / filter — text box at the top of the profile list; filters in real time by profile name, device name, or hotkey; clears on Escape |

---

### Branch 38: `feat/switch-sound`
**Theme:** Audio feedback on profile switch.

| # | Feature |
|---|---------|
| F36 | Optional switch sound — short audio cue on profile switch; built-in tones or custom .wav; per-profile toggle to enable or disable |

---

### Branch 39: `feat/panic-hotkey`
**Theme:** Instant global mute for all recording devices.

| # | Feature |
|---|---------|
| F37 | Deafen / panic hotkey — global configurable hotkey that mutes all recording devices instantly; tray icon flashes red while active; press again to unmute |

---

### Branch 40: `feat/transient-profile`
**Theme:** Temporary profile switching with automatic revert.

| # | Feature |
|---|---------|
| F38 | Temporary / transient profile switch — optional app-wide setting with configurable keybind; switches to a profile temporarily and auto-reverts when a timer expires or a linked app closes |

---

### Branch 41: `feat/device-triggers`
**Theme:** Automatic profile activation when a specific audio device connects.

| # | Feature |
|---|---------|
| F39 | Auto-switch on device connect — link a specific audio device endpoint to a profile; automatically activates when that device connects (Bluetooth, USB); per-device toggle |

---

### Branch 42: `feat/dock-awareness`
**Theme:** Automatic profile switching based on monitor or dock connection.

| # | Feature |
|---|---------|
| F40 | Monitor / dock awareness — trigger a profile switch when a specific display or dock connects or disconnects (HDMI, USB-C, Thunderbolt); useful for hybrid work setups |

---

### Branch 43: `feat/app-switching`
**Theme:** Automatic profile switching based on running application.

| # | Feature |
|---|---------|
| F41 | App-aware auto-switching — link an executable to a profile; switches when that process launches or gains focus; reverts to previous profile when the app closes; per-rule toggle |

---

### ~~Branch 44: `feat/settings-ux`~~ ✅ Merged — PR #64
**Theme:** Settings window UX polish — device visibility controls, collapsible settings card, tray menu clarity, and visual consistency.

| # | Feature |
|---|---------|
| — | Show/hide disabled devices toggle — separate toggle in Settings to include or exclude software-disabled audio devices from profile card dropdowns |
| — | Show/hide disconnected devices toggle — separate toggle to include or exclude unplugged devices from dropdowns |
| — | Collapsible General Settings card — clickable header with gear icon, title, and subtitle collapses/expands the settings body; state persists across sessions |
| — | Window title simplified from "VibeSwitcher - Settings" to "VibeSwitcher" |
| — | Tray right-click menu — VibeSwitcher header is now clickable to open the app; removed the separate ambiguous "Settings" item |
| — | Button corner radius unified to 7 across all button styles to match the Settings card expander |

---

### Branch 45: `feat/dark-mode`
**Theme:** OS theme awareness — dark mode and high-contrast accessibility support throughout the app.

| # | Feature |
|---|---------|
| F16 | Dark mode + high-contrast — detect OS light/dark toggle via registry; swap resource dictionaries; adapt icon chip backgrounds in Settings card and tray menu for both themes |

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
| — | Mica / Acrylic material for Settings window | Part of planned UI/UX redesign phase |
| — | Compact / mini Settings window | After UI/UX redesign |
