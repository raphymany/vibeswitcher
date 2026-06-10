# Handoff: VibeSwitcher Full UI Redesign

## Overview
This is a complete visual and UX redesign of the VibeSwitcher Windows WPF app. The existing codebase (MVVM, WPF/.NET 8) is fully functional — **all services, ViewModels, and business logic stay exactly as-is**. Only the Views (XAML) need to be rebuilt to match this design.

The redesign moves from a vertical scrolling list to a **card grid layout** inspired by Logitech G HUB — dark, clean, card-per-profile, with a custom title bar and top navigation.

## About These Files
The files in this bundle (`VibeSwitcher.html`, `card.jsx`, `settings.jsx`, `app.jsx`) are **high-fidelity HTML prototypes** — they show exact colors, spacing, typography, animations, and interactions. They are **not production code**. The task is to recreate this design in WPF/XAML, wiring it to the existing ViewModels and services already in the codebase.

## Fidelity
**High-fidelity.** Colors, spacing, typography, border radii, hover states, and animations should match the HTML prototype as closely as WPF allows. Where WPF has constraints (e.g. font rendering, blur effects), use the closest available equivalent.

---

## Architecture Mapping

| HTML Component | WPF Equivalent |
|---|---|
| `TitleBar` | Custom `WindowChrome` with `WindowStyle="None"` |
| `TopNav` | Top `DockPanel` / `Grid` row |
| `FilterBar` | Collapsible `WrapPanel` with animated `Height` |
| Profile Grid | `ItemsControl` with `WrapPanel` or `UniformGrid` |
| `ProfileCard` | Custom `UserControl` → `ProfileCardView.xaml` |
| `ProfileDetailModal` | `Window` or `Popup` styled as modal overlay |
| `SettingsPanel` | Existing `SettingsWindow.xaml` — restyle in-place |
| `AboutPanel` | Existing `AboutWindow.xaml` — restyle in-place |
| Splash Screen | New `SplashWindow.xaml` shown before `SettingsWindow` |
| Toggle switch | Custom `ToggleButton` template |
| Filter chips | `ToggleButton` items in an `ItemsControl` |

The existing `SettingsViewModel` and `ProfileCardViewModel` already contain all the data bindings needed — the work is purely visual (new XAML templates, no ViewModel changes required).

---

## Screens & Views

### 1. Splash Screen
**Purpose:** Shown on app launch for ~2.2 seconds, then transitions to the main window.

**Layout:** Full-screen dark window, all content centered vertically and horizontally. No title bar, no chrome.

**Animation sequence:**
1. `0–380ms`: Static — icon + app name + tagline visible, opacity 1
2. `380ms`: VS icon **rises upward** (`TranslateTransform.Y` animates from 0 → -26px over 340ms, easing `EaseOut`)
3. `~720ms`: Icon **slams down** (`TranslateTransform.Y` goes from -23px → +11px over 280ms, `ScaleTransform.ScaleY` compresses to 0.87, easing `EaseIn`)
4. `~1000ms`: **Spring release** — Y bounces to -10px, ScaleY to 1.06, then settles to 0/1.0 over 560ms
5. `1860ms`: Whole window fades out (`Opacity` 1 → 0 over 340ms with scale 1 → 1.04)
6. `2200ms`: Show main window

**Components:**
- Background: `#0D0D12` solid
- VS Icon SVG: 96×96px centered (see Assets section for SVG source)
- App name: "VibeSwitcher", Segoe UI Bold 26px, `#E4E4EF`, `LetterSpacing -0.5px` (use `CharacterSpacing=-10`)
- Tagline: "Manage your device profiles and hotkeys", Segoe UI Regular 12.5px, `#38384E`
- Gaps: 14px between each element

---

### 2. Main Window — Shell
**Window:** `WindowStyle="None"`, `ResizeMode="CanResizeWithGrip"`, `Background="#0D0D12"`. Use `WindowChrome` for drag and snap.

**Layout (top to bottom):**
```
┌─ TitleBar ─────────────────────────────────── 30px ─┐
├─ TopNav ───────────────────────────────────── 54px ─┤
├─ FilterBar (collapsible) ────────────── 0–52px ─────┤
└─ MainContent (ScrollViewer) ──────── flex 1 fill ───┘
```

---

### 3. Title Bar
**Height:** 30px  
**Background:** `#09090E`  
**Border-bottom:** 1px `#16162A`

**Left side:** VS icon (14×14px) + "VibeSwitcher" text (Segoe UI 11.5px, `#44445A`, Weight 500)

**Right side (window controls):**
- Minimize: 44×30px button, hover bg `rgba(255,255,255,0.05)`, icon `─`
- Maximize: 44×30px, hover bg same, icon `□`
- Close: 44×30px, hover bg `#C42B1C`, hover text `#FFFFFF`, icon `✕`
- All: transparent background at rest, text `#555570`

---

### 4. Top Navigation Bar
**Height:** 54px  
**Background:** `#10101A`  
**Border-bottom:** 1px `#1C1C2C`  
**Padding:** 0 18px  
**Layout:** Horizontal `StackPanel` / `DockPanel`, items with 9px gap

**Left section:**
- VS Icon: 26×26px
- "Vibe**Switcher**" — "Vibe" in `#E4E4EF`, "Switcher" in `#F5820A`, Segoe UI Bold 15px
- Vertical divider: 1px wide, 22px tall, `#1E1E30`

**Center (profile grid view only):**
- Search box: flex-fill up to 260px wide, 32px tall, bg `#12121C`, border `#1E1E30` → `rgba(245,130,10,.35)` on focus, radius 8px, Segoe UI 13px
- Filters button: "Filters" label + funnel icon, 32px tall, border `#1E1E30`, radius 8px → orange on active/open. Shows orange badge with count when filters active.

**Right section:**
- FAQ button (? icon): 34×34px, radius 8px
- About button (ℹ icon): 34×34px, radius 8px  
- Settings button (⚙ icon): 34×34px, radius 8px
- Active state for all nav buttons: bg `rgba(245,130,10,.07)`, border `rgba(245,130,10,.25)`, icon color `#F5820A`
- Vertical divider
- **"+ New Profile" button:** bg `#F5820A`, text `#0D0D12`, Segoe UI SemiBold 12.5px, padding 8px 15px, radius 8px. Hover: `#E07409`. Triggers `ProfileTypeDialog`.

---

### 5. Filter Bar
**Height (open):** ~52px (wraps to 2 rows if needed)  
**Background:** `#0D0D12`  
**Border-bottom:** 1px `#181826`  
**Padding:** 9px 18px  
**Visibility:** Collapsed by default, animated open/close via `Height` storyboard (0 → auto, duration 300ms, EaseInOut)

**Filter chips:** `ToggleButton` items in a `WrapPanel`, gap 6px  
- Rest: border `#1E1E30`, text `#38384E`, radius 20px, bg transparent, Segoe UI 11.5px, padding 4px 10px
- Hover: border `#303048`, text `#7878A0`, bg `#16162A`
- Active: border `rgba(245,130,10,.25)`, text `#F5820A`, bg `rgba(245,130,10,.07)`

**Chips:** Playback only · Recording only · Both devices · ★ Pinned · ✓ Active · Silent · Has hotkey · Scheduled · Has trigger · Has sound

**"✕ Clear" chip:** shown when any filter is active, color `#E05555`

**Filtering logic:** Bind each chip to a boolean in `SettingsViewModel`. Filter the `ObservableCollection<ProfileCardViewModel>` using a `CollectionViewSource` with a predicate that AND-gates all active filters.

---

### 6. Profile Card Grid
**Container:** `ScrollViewer` (VerticalScrollBarVisibility=Auto) wrapping an `ItemsControl`  
**ItemsPanel:** `WrapPanel` (or custom `UniformGrid` variant) with item min-width 192px, gap 13px  
**Padding:** 22px 18px  

**Grid header:** "PROFILES" label (Segoe UI 11px, Weight 600, uppercase, `#38384E`, letter-spacing 0.1em) + count badge  

**"+ New Profile" add card:** same size as regular cards, dashed border `#1E1E30` 1.5px, centered `+` icon + "New Profile" text. Hover: border `rgba(245,130,10,.35)`, text `rgba(245,130,10,.6)`. Click → opens `ProfileTypeDialog`.

---

### 7. Profile Card — `ProfileCardView.xaml`
**Size:** Min-width 192px, Height 244px  
**Background:** `#16161E`  
**Border:** 1px `#222234`, CornerRadius 14  
**Hover:** border `#363650`, bg `#1E1E28`, subtle drop shadow  
**Active profile:** border `rgba(245,130,10,.3)`, bg gradient top `#1C1610` → `#16161E`  
**Cursor:** Hand  
**Clip:** `RectangleGeometry` matching CornerRadius to clip children

**Active bar (top accent):** Horizontal line, 2px tall, position absolute at top, left/right offset 12px, visible only on active profile. Fill: linear gradient transparent → `#F5820A` → transparent.

**Layout (top to bottom, centered):**
```
20px top padding
├── Icon area: 76×76px, CornerRadius 17, bg #1C1C2A, border #282838
│   └── Profile icon SVG: 46×46px
8px gap
├── Profile name: Segoe UI SemiBold 13.5px, #E4E4EF, centered, MaxWidth card-width - 28px
8px gap  
├── Mode badge: (see below)
8px gap
├── Device rows: (see below)
auto margin-top (pushes footer to bottom)
├── Status footer: dots + hotkey chip
```

**Active card icon area:** border `rgba(245,130,10,.28)`, drop glow effect (use `Effect` → `DropShadowEffect` Color=`#F5820A` Opacity=0.1 BlurRadius=18 ShadowDepth=0)

**Mode badge:**
- Both Devices: bg `rgba(245,130,10,.12)`, text `#F5820A`, border `rgba(245,130,10,.25)`
- Playback: bg `rgba(74,168,255,.10)`, text `#4AA8FF`, border `rgba(74,168,255,.20)`
- Recording: bg `rgba(48,210,120,.10)`, text `#30D278`, border `rgba(48,210,120,.20)`
- All: Segoe UI 9.5px Weight 600, uppercase, letter-spacing 0.07em, padding 2.5px 8px, CornerRadius 10

**Device rows:**  
- 5px colored dot (`#4AA8FF` for playback, `#30D278` for recording) + device name text  
- Segoe UI 11px `#7878A0`, clipped with ellipsis if overflow  
- 3px gap between rows

**Status footer (bottom of card, above action bar):**
- Active dot: 6px circle `#2DCA72` with glow (`DropShadowEffect` Color=`#2DCA72` Opacity=0.5 BlurRadius=6)
- Pinned dot: 6px `#F5820A`
- Scheduled dot: 6px `#4AA8FF`
- Hotkey chip: `Border` with bg `#131320`, border `#222234`, CornerRadius 4, Segoe UI Mono 10px `#353550`, padding 1px 5px

**Hover action buttons (appear on `MouseEnter`):**  
Position: `Canvas` or `Grid` row pinned to bottom of card  
Background: gradient `rgba(12,12,18,0)` → `rgba(12,12,18,0.98)` over ~40% of card height  
Animation: `Opacity` 0→1 + `TranslateTransform.Y` 3→0 on `MouseEnter` (150ms EaseOut), reverse on `MouseLeave`  

9 buttons in a `WrapPanel`, gap 3px, centered:

| Button | Icon | Active state | Action |
|--------|------|-------------|--------|
| No Notification | Bell-slash SVG | Orange | Toggle `profile.Silent` |
| App Trigger | External-link SVG | Orange | Open `AppTriggerDialog` |
| Auto-Switch | Refresh-arrows SVG | Orange | Toggle `profile.TriggerOnConnect` |
| Favorite | Star SVG | Orange | Toggle `profile.IsPinned` |
| Activate | Checkmark-circle SVG | **Green** `#2DCA72` | Call `ProfileSwitchOrchestrator.SwitchToProfile` |
| Clone | Copy SVG | — | Clone profile |
| Scheduler | Clock SVG | Orange | Open `ScheduleWizardDialog` |
| Sound Switch | Music-note SVG | Orange | Open `SwitchSoundDialog` |
| Delete | Trash SVG | **Red hover** `#E06060` | Open `ConfirmDeleteDialog` |

Button styles: 28×28px, CornerRadius 6, bg `#1A1A28`, border `#282840`, icon color `#484868`  
Hover: bg `#222236`, border `#383858`, icon `#A0A0C8`  
Active (on): bg `rgba(245,130,10,.12)`, border `rgba(245,130,10,.25)`, icon `#F5820A`  
Tooltip: standard WPF `ToolTip` styled dark

**Click behavior:** Clicking the card body (not the action buttons) opens the **Profile Detail Modal** (see below).

---

### 8. Profile Detail Modal
**Trigger:** Clicking a profile card  
**Animation:** Card does a brief press animation (`ScaleTransform` 1.0 → 0.93 → 1.0, 220ms), then modal appears with `ScaleTransform` 0.88 → 1.0 + `Opacity` 0 → 1 + `TranslateTransform.Y` 16 → 0 (320ms, overshoot easing)  
**Backdrop:** Semi-transparent overlay `rgba(5,5,10,.84)` over full window. `BlurEffect` on the content behind if performance allows.  
**Close:** Click backdrop or ✕ button

**Modal window:** `Border` CornerRadius 18, bg `#17171F`, border `rgba(245,130,10,.16)`, max-width 480px, max-height 80% of window height, centered, `DropShadowEffect` BlurRadius=100 Opacity=0.78  

**Header section (padding 22px 22px 16px, border-bottom `#1E1E2C`):**
- Profile icon: 54×54px, CornerRadius 13, bg `#1C1C2A`, border `#282838`
- Name field: editable `TextBox`, Segoe UI Bold 16px `#E4E4EF`, transparent bg, no border at rest → 1px `rgba(245,130,10,.35)` bottom border on focus
- Mode pills row: 3 `ToggleButton` items — "Both Devices" / "Playback" / "Recording", each styled as pill (CornerRadius 20, Segoe UI 11px SemiBold, border `#262640`). Active state colors match mode badge colors above.
- ✕ close button: 30×30px, CornerRadius 7

**Form body (padding 18px 22px, gap 14px between rows):**  
Each row: label (Segoe UI 11px SemiBold uppercase `#38384E`, letter-spacing) above a control.

| Field | Control | Notes |
|-------|---------|-------|
| Notes | `TextBox` AcceptsReturn=True, 2 rows, same dark style | Bind `Profile.Notes` |
| Playback device | `ComboBox` + "Test" button | Visible when mode is Both or Playback. Bind to `AudioService` device list |
| Recording device | `ComboBox` + "Test" button | Visible when mode is Both or Recording |
| Hotkey | Read-only display + "Set Hotkey" button | Button opens `HotkeyCaptureDialog` |
| Icon | Row of 5 preset icon thumbnails (42×42px, CornerRadius 10) + "Pick File" button | Active thumbnail: orange border. Button opens `IconGalleryDialog` |

**Footer (padding 14px 22px 20px, border-top `#1E1E2C`):**
- "Cancel" button: bg `#1E1E2A`, border `#2A2A3A`, text `#7878A0`, Segoe UI 13px  
- "Save Changes" button: bg `#F5820A`, text `#0D0D12`, Segoe UI SemiBold 13px

---

### 9. Settings Panel
**Access:** Settings ⚙ button in TopNav replaces the main content area (no separate Window)  
**Layout:** Back arrow + "Settings" title header, then `ScrollViewer` with sections

**Sections:** Appearance · Startup · Notifications · Schedules · Tray · Devices · Shortcuts  
Each section: uppercase label + `Border` container (bg `#16161E`, border `#222234`, CornerRadius 12, overflow hidden)  

**Toggle row:** `Grid` with label text (Segoe UI 13px `#7878A0`) + custom toggle switch on right  
Custom toggle: 38×21px pill, bg `#252540` (off) / `#F5820A` (on), thumb white 15×15px circle that slides  

**Hotkey rows:** label + key chip (bg `#101018`, border `#1E1E30`, Segoe UI Mono, CornerRadius 6) + "Set hotkey" button → opens `HotkeyCaptureDialog`  

---

## Interactions & Animations Summary

| Interaction | Animation |
|---|---|
| Splash icon on load | V rises (-26px) → holds → slams down (+11px, scaleY 0.87) → bounces → settles. 1.4s total. |
| Splash → main window | Splash fades out (opacity 1→0, scale 1→1.04) over 340ms |
| Card hover enter | Actions overlay fades in + slides up 3px → 0. Border lightens. Duration 150ms |
| Card hover leave | Reverse, 200ms |
| Card click (press) | ScaleTransform 1.0 → 0.93 → 1.0 over 220ms EaseOut |
| Modal open | Scale 0.88→1 + Opacity 0→1 + TranslateY 16→0, 320ms, cubic overshoot |
| Filter bar open/close | Height 0 → content height, 300ms EaseInOut |
| Screen transition (Settings/About/FAQ) | Fade + slight slide in from right, 200ms |
| Toggle switch | Thumb TranslateX slide, 200ms, EaseInOut |

---

## Design Tokens

### Colors
```
Background:        #0D0D12
Surface (card):    #16161E
Surface hover:     #1E1E28
Surface raised:    #242432
Border:            #222234
Border hover:      #363650
Title bar bg:      #09090E
Nav bg:            #10101A

Orange accent:     #F5820A
Orange hover:      #E07409
Orange dim bg:     rgba(245,130,10, 0.12)
Orange border:     rgba(245,130,10, 0.25)
Orange glow:       rgba(245,130,10, 0.07)

Text primary:      #E4E4EF
Text secondary:    #7878A0
Text muted:        #38384E

Active green:      #2DCA72
Playback blue:     #4AA8FF
Recording mint:    #30D278
Meeting purple:    #9B69FF
Danger red:        #E05555
```

### Typography (WPF — Segoe UI)
```
Splash title:      26px / Bold   / #E4E4EF
Nav logo:          15px / Bold   / #E4E4EF + #F5820A
Card name:         13.5px / SemiBold / #E4E4EF
Mode badge:        9.5px / SemiBold / uppercase
Device name:       11px / Regular / #7878A0
Hotkey chip:       10px / Regular / #353550 (Courier New / Consolas)
Panel title:       18px / Bold   / #E4E4EF
Section title:     10px / SemiBold / uppercase / #38384E
Row label:         13px / Regular / #7878A0
Button primary:    13px / SemiBold / #0D0D12
Button secondary:  13px / Medium  / #7878A0
```

### Spacing
```
Window padding:    18–22px
Grid gap:          13px
Card padding:      20px top, 14px horizontal, 14px bottom
Card gap (items):  7px
Section gap:       22px
Row height:        ~42px (11px top + 11px bottom + content)
```

### Border Radii
```
Card:              14px
Card icon area:    17px
Modal:             18px
Nav buttons:       8px
Filter chips:      20px (full pill)
Toggle:            11px (full pill)
Action buttons:    6px
Hotkey chip:       4px
Mode pills:        20px
```

---

## WPF-Specific Implementation Notes

1. **Custom window chrome:** Use `<WindowChrome CaptionHeight="30" ResizeBorderThickness="4" />` in window resources. Set `WindowStyle="None"` and `AllowsTransparency="False"` (better performance). Handle minimize/maximize/close in code-behind.

2. **Card grid layout:** Use `ItemsControl` with a `WrapPanel` as the `ItemsPanel`. Set `ItemWidth` on the WrapPanel to let cards fill available width. For responsive minimum width (192px), use a converter or code-behind to calculate `ItemWidth` based on actual panel width.

3. **Hover animations:** Use `EventSetter` with `MouseEnter`/`MouseLeave` triggers in the `ItemContainerStyle`, or `Triggers` inside the `ControlTemplate`. `Storyboard` with `DoubleAnimation` for opacity and translate.

4. **Action buttons z-index:** Use a `Grid` inside the card template. The action button strip sits in the same grid row as the main content but with `VerticalAlignment="Bottom"` and higher `Panel.ZIndex`. Animate its `Opacity` and `RenderTransform`.

5. **Modal overlay:** Create a `Grid` that covers the entire `MainWindow` content area (not a separate `Window`). Toggle its `Visibility` and animate. This avoids separate window positioning issues.

6. **Filter CollectionView:**
   ```csharp
   _view = CollectionViewSource.GetDefaultView(Profiles);
   _view.Filter = obj => {
       var p = (ProfileCardViewModel)obj;
       if (FilterPlayback && p.Mode != ProfileMode.Playback) return false;
       if (FilterPinned && !p.IsPinned) return false;
       // etc.
       return true;
   };
   ```
   Call `_view.Refresh()` whenever a filter toggle changes.

7. **Splash window:** Show it as the startup window (`App.xaml StartupUri="Views/SplashWindow.xaml"`). After the animation completes, show `SettingsWindow` and close `SplashWindow`.

8. **Existing dialogs** (`HotkeyCaptureDialog`, `ScheduleWizardDialog`, `SwitchSoundDialog`, `AppTriggerDialog`, etc.) just need **restyling** — same code-behind and ViewModels, new XAML with the dark theme. Use a shared `ResourceDictionary` (`DarkTheme.xaml`) for common styles (buttons, inputs, toggles, section containers).

9. **Icon SVGs:** WPF cannot render SVG natively. Options:
   - Convert SVGs to `Path` data using a tool like Inkscape or svg2xaml
   - Use the `Wpf.Ui` library which supports SVG via `BitmapImage` conversion  
   - Export as PNG at 2x resolution and use as `Image` sources
   The profile icon SVGs in this design are simple enough to convert to XAML `Path` data.

10. **Font:** Segoe UI is the WPF system default and closely matches Inter used in the HTML prototype. No additional font installation required.

---

## Assets

### VS App Icon (for splash + title bar + tray)
The icon is a custom design: a dark rounded square with a V-chevron (stroke `#F5820A`, strokeWidth 4) and 5 equalizer bars below it (heights: 7, 12, 9.5, 12, 7 px) centered horizontally. The SVG source is in `card.jsx` (function `VSSplashIcon`). Convert to multi-size `.ico` (16, 32, 48, 64, 256px) using `VSSplashIcon` SVG as source.

### Profile Icon SVGs
5 built-in profile icon types defined in `card.jsx`:
- `speaker` — orange speaker cone with sound waves
- `headset` — orange headphone arc with ear cups
- `stream` / `mic` — green microphone with stand
- `gaming` — blue game controller outline
- `meeting` — purple video camera

Each is a 46×46 SVG. Convert to XAML `Path` elements or PNG resources.

---

## Files in This Bundle
| File | Purpose |
|------|---------|
| `VibeSwitcher.html` | Main entry point — loads all CSS + component scripts |
| `card.jsx` | `ProfileCard` component, all SVG icons, `VSSplashIcon` |
| `settings.jsx` | `SettingsPanel`, `AboutPanel`, `FAQPanel` components |
| `app.jsx` | `SplashScreen`, `MainApp`, `ProfileDetailModal`, mock data, root render |

Open `VibeSwitcher.html` in a Chromium-based browser to see the full interactive prototype.

---

## What's Already Built (Don't Rebuild)
The following are **complete and working** in the existing codebase — just re-skin:
- All audio switching logic (`AudioService`, `ProfileSwitchOrchestrator`)
- All dialog code-behind (`HotkeyCaptureDialog`, `ScheduleWizardDialog`, etc.)
- All ViewModels (`SettingsViewModel`, `ProfileCardViewModel`)
- All services (`HotkeyService`, `SchedulerService`, `TrayService`, etc.)
- Config load/save (`ConfigService`)
- Startup/tray behavior
