# VibeSwitcher — UI Redesign Implementation Prompt for Claude Code

> **Copy and paste this entire file as your first message to Claude Code.**
> Claude Code should open your VibeSwitcher solution, read the handoff files,
> and implement the redesign. All files referenced below are in the
> `design_handoff_vibeswitcher/` folder alongside this prompt.

---

## Your Task

Implement a complete visual redesign of the VibeSwitcher WPF app based on the
design prototype and XAML skeletons in `design_handoff_vibeswitcher/`.

**Golden rule: Do not touch any service, ViewModel, Model, or business logic.
Only XAML files and their code-behinds change. All data bindings, commands,
and services stay exactly as they are.**

Open `VibeSwitcher.html` in a browser first to see the full interactive
prototype — that is your visual target.

---

## Files in This Handoff

| File | What it is |
|------|-----------|
| `VibeSwitcher.html` | Full interactive prototype — your visual reference |
| `card.jsx` | Profile card component with all SVG icons |
| `settings.jsx` | Settings, About, FAQ panels |
| `app.jsx` | Splash screen, main shell, profile detail modal |
| `DarkTheme-Redesign.xaml` | Drop-in replacement body for `Themes/DarkTheme.xaml` |
| `SplashWindow.xaml` | New splash screen with key-press animation (ready to compile) |
| `ProfileCardView.xaml` | New profile card UserControl with real VM bindings |
| `SettingsWindow-Redesign.xaml` | Full new main window layout with all real bindings |
| `README.md` | Deep design spec with pixel values, animations, and WPF notes |

---

## Step-by-Step Implementation Plan

Work through these steps in order. Commit after each one so you can roll back.

---

### STEP 1 — Update the colour theme

**File:** `VibeSwitcher/Themes/DarkTheme.xaml`

Replace the body of `DarkTheme.xaml` with the content of
`design_handoff_vibeswitcher/DarkTheme-Redesign.xaml`.

Keep the `<ResourceDictionary>` wrapper. All existing `x:Key` names are
preserved — only the colour values change. The new keys prefixed `VS` are
additive; add matching neutral/light values to `LightTheme.xaml` as well
(use the same key names with lighter colours as appropriate).

**Verify:** Build and run. The existing UI should load with the new colour
palette. No other changes yet.

---

### STEP 2 — Add SplashWindow

**New file:** `VibeSwitcher/Views/SplashWindow.xaml`
Copy `design_handoff_vibeswitcher/SplashWindow.xaml` into the project.

**New file:** `VibeSwitcher/Views/SplashWindow.xaml.cs`
The complete code-behind is written in the large comment at the bottom of `SplashWindow.xaml`.
Copy it out, remove the XML comment wrapper (`<!--` / `-->`), and save as `SplashWindow.xaml.cs`.

**Key animation facts — implement these exactly, do not simplify:**
- The **V chevron Path** (`VChevron`) animates independently via `VTranslate` (TranslateTransform.Y)
- Each **bar Rectangle** (`Bar1`–`Bar5`) animates independently via its own `BarNScale` (ScaleTransform.ScaleY)
- Bars are inside a `Border` with `ClipToBounds="True"` starting at Canvas y=62 — this is a **hard ceiling** so bars can NEVER visually overlap the V chevron (whose tip sits at y=54)
- **Phase 1 (press, 420–2020ms):** V rises up (−16px), slams down (+11px), bounces, settles. Bars compress then wave outward from center to edges. All keyframes are in `PressStoryboard` in the XAML.
- **Phase 2 (loop, 2060ms+):** Code-behind starts a `RepeatBehavior="Forever"` Storyboard on all 5 bars — each bar gets a different period (0.36–0.52s) so they look like a live equalizer.
- **Fade out** starts at 3400ms via `PressStoryboard`. Window closes and fires `AnimationComplete` at 3750ms.
- Total splash: ~3.85s

**Wire in `App.xaml.cs`:** See the "Wire in App.xaml.cs" section at the bottom of `SplashWindow.xaml`.
Make `OnStartup` async: `protected override async void OnStartup(StartupEventArgs e)`.

**Verify:** App shows splash → V slams into bars → bars wave → bars loop as equalizer → fade out → main window.

---

### STEP 3 — Replace DarkTheme.xaml app-level styles

**File:** `VibeSwitcher/App.xaml`

The existing `App.xaml` global styles (buttons, ComboBox, TextBox, ScrollBar,
ToggleSwitch, etc.) are good and mostly stay. Make these targeted updates:

1. **`PrimaryButton`** — change `Foreground` from `White` to `#0D0D12`
   (dark text on orange button, matching new accent colour).

2. **`ToggleSwitchStyle`** — update the Track `Width` to `38`, `Height` to
   `21`, the Thumb `Width`/`Height` to `15`, and the on-margin to `To="20,0,0,0"`.
   Update the `ToggleOffColor` resource to `#252540` (already in DarkTheme).

3. **`ActionButton`** — keep as-is; it will inherit the new `HoverBg` colour.

4. **Global `Window` style** — no change needed; `Segoe UI` default stays.

---

### STEP 4 — Add ProfileCardView UserControl

**New file:** `VibeSwitcher/Views/ProfileCardView.xaml`
Copy `design_handoff_vibeswitcher/ProfileCardView.xaml` into the project.

**New file:** `VibeSwitcher/Views/ProfileCardView.xaml.cs`

```csharp
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace VibeSwitcher.Views;

public partial class ProfileCardView : UserControl
{
    public static readonly RoutedEvent CardExpandedEvent =
        EventManager.RegisterRoutedEvent(
            "CardExpanded", RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(ProfileCardView));

    public event RoutedEventHandler CardExpanded
    {
        add => AddHandler(CardExpandedEvent, value);
        remove => RemoveHandler(CardExpandedEvent, value);
    }

    public ProfileCardView()
    {
        InitializeComponent();
        MouseEnter += (_, _) => AnimateActions(1);
        MouseLeave += (_, _) => AnimateActions(0);
    }

    private void AnimateActions(double to)
    {
        var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(to > 0 ? 150 : 200));
        anim.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
        ActionStrip.BeginAnimation(OpacityProperty, anim);
        ActionStrip.IsHitTestVisible = to > 0;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        // Only expand if the click was on the card body, not an action button
        if (e.Source is Button) return;

        // Press animation
        var kf = new DoubleAnimationUsingKeyFrames();
        kf.KeyFrames.Add(new EasingDoubleKeyFrame(1.0,  KeyTime.FromPercent(0)));
        kf.KeyFrames.Add(new EasingDoubleKeyFrame(0.93, KeyTime.FromPercent(0.4))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        kf.KeyFrames.Add(new EasingDoubleKeyFrame(1.0,  KeyTime.FromPercent(1.0))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        kf.Duration = TimeSpan.FromMilliseconds(220);

        CardBorder.RenderTransformOrigin = new Point(0.5, 0.5);
        CardBorder.RenderTransform = new ScaleTransform(1, 1);
        ((ScaleTransform)CardBorder.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, kf);
        ((ScaleTransform)CardBorder.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, kf);

        // Fire expand event after brief delay (let press animation start first)
        Dispatcher.BeginInvoke(() => RaiseEvent(new RoutedEventArgs(CardExpandedEvent, this)),
            System.Windows.Threading.DispatcherPriority.Background);
    }
}
```

**Add these small properties to `ProfileCardViewModel.cs`** (no logic changes):

```csharp
// Add to the existing public properties section:
public bool IsHotkeySet => !_model.Hotkey.IsEmpty;
public ICommand ToggleSilentCommand           => new RelayCommand(() => Silent = !Silent);
public ICommand TogglePinnedCommand           => new RelayCommand(() => IsPinned = !IsPinned);
public ICommand ToggleTriggerOnConnectCommand => new RelayCommand(() => TriggerOnConnect = !TriggerOnConnect);
```

---

### STEP 5 — Rebuild SettingsWindow layout

**File:** `VibeSwitcher/Views/SettingsWindow.xaml`

This is the largest change. The existing XAML is ~2000 lines. The strategy is:

**A. Keep all `<Window.Resources>` from the existing file** — all existing
styles, converters, and templates are valid. Add the new styles from
`SettingsWindow-Redesign.xaml` (`FilterChip`, `NavIconBtn`, `NavIconBtnActive`,
`TitleBtn`, `TitleBtnClose`) to the resources block.

**B. Replace the root content** with the new 4-row Grid from
`SettingsWindow-Redesign.xaml`:
- Row 0: Custom title bar (30px)
- Row 1: Top nav (54px)
- Row 2: Filter bar (animated, MaxHeight 0→52)
- Row 3: Main content area (4 overlapping views)

**C. Move existing settings content** (all the startup/tray/device/hotkey
sections) into the `SettingsPanel` ScrollViewer (view B in Row 3).
Keep all existing bindings verbatim — only their container changes.

**D. Move existing profile editing fields** (name, notes, device combos,
hotkey, icon) from the existing card expander into the `ProfileDetailOverlay`
modal (view D in Row 3). Again, keep all bindings verbatim.

**E. Replace the existing profile list** (`ListBox` or `ItemsControl` with
inline `DataTemplate`) with the new `ItemsControl` + `ProfileCardView`
UserControl approach from the skeleton.

**F. Update `Window` properties:**
- Remove `Width="540" Height="680"` — window is now freely resizable
- Keep `MinWidth` / `MinHeight` (set to `680` / `480`)
- Change `ResizeMode` to `CanResize`
- Change `WindowStyle` to `None` (already set in original)
- Keep the existing `WindowChrome` but set `CaptionHeight="30"`

**G. Add/update code-behind** (`SettingsWindow.xaml.cs`):
Add the panel navigation, filter bar animation, profile detail modal open/close,
and window control handlers exactly as documented in the comments at the bottom
of `SettingsWindow-Redesign.xaml`.

The existing code-behind logic (hotkey capture, device refresh, drag-drop
reorder, etc.) stays exactly as-is.

---

### STEP 6 — Restyle existing dialogs

All existing dialog Windows keep their code-behind and ViewModel bindings.
Only their XAML visual styling updates. Apply these changes to each:

**All dialogs:**
- `Background="{DynamicResource AppBg}"`
- `WindowStyle="None"` with `WindowChrome CaptionHeight="28"`
- Add a minimal custom title bar (28px, dark, with close button)
- `BorderBrush="{DynamicResource VSAccentBorder}"` on the outer Border
- CornerRadius 14 on the outer content border
- All `CheckBox` toggle switches → apply `Style="{StaticResource ToggleSwitchStyle}"`
- All primary action buttons → `Style="{StaticResource PrimaryButton}"`
- All secondary buttons → `Style="{StaticResource ActionButton}"`
- All destructive buttons → `Style="{StaticResource DangerButton}"` or `DeleteButton`

**Priority order** (most visible to users):
1. `HotkeyCaptureDialog` — shown when setting hotkeys
2. `ScheduleWizardDialog` — shown from Scheduler action button
3. `AppTriggerDialog` — shown from App Trigger action button
4. `SwitchSoundDialog` — shown from Sound Switch action button
5. `ConfirmDeleteDialog` — shown on Delete
6. `IconGalleryDialog` — shown from icon picker
7. `ProfileTypeDialog` — shown on Add Profile
8. `AboutWindow` — move content into SettingsWindow AboutPanel instead
9. All remaining dialogs

---

### STEP 7 — AboutWindow and SessionLogWindow

**AboutWindow:** Its content can optionally move inline into the SettingsWindow
`AboutPanel` ScrollViewer (view C in Row 3). This gives the cleaner in-place
feel shown in the prototype. Keep `AboutWindow.xaml` as a fallback for the
existing tray menu "About" shortcut.

**SessionLogWindow:** Restyle with dark theme. Keep all existing functionality.

---

### STEP 8 — Search bar wiring

Add `SearchText` to `SettingsViewModel`:

```csharp
private string _searchText = "";
public string SearchText
{
    get => _searchText;
    set
    {
        if (!SetField(ref _searchText, value)) return;
        ApplyFilter(); // existing method — extend it to check name
    }
}
```

In `ApplyFilter()`, add a name check:
```csharp
card.IsVisible = card.MatchesFilter(filter) &&
    (string.IsNullOrEmpty(_searchText) ||
     card.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase));
```

Bind the search TextBox in SettingsWindow:
```xml
<TextBox Text="{Binding SearchText, UpdateSourceTrigger=PropertyChanged}" .../>
```

---

### STEP 9 — Final polish

1. **Active card background:** When `IsActive=True`, add a subtle gradient to
   the card background. In `ProfileCardView.xaml`, use a `DataTrigger` on
   `IsActive` to swap the `CardBorder.Background` from `{DynamicResource CardBg}`
   to a `LinearGradientBrush` from `#1C1610` (top) to `{DynamicResource CardBg}`.

2. **Filter count badge:** In `SettingsViewModel`, add:
   ```csharp
   public int ActiveFilterCount =>
       (ModePlayback ? 1 : 0) + (ModeRecording ? 1 : 0) + (ModeBoth ? 1 : 0) +
       (PinnedFilter ? 1 : 0) + (ActiveFilter ? 1 : 0) + (SilentFilter ? 1 : 0) +
       (HotkeyFilter ? 1 : 0) + (ScheduledFilter ? 1 : 0) + (SoundFilter ? 1 : 0) +
       (WarningFilter ? 1 : 0);
   ```
   Notify it in `ApplyFilter()`. Bind the badge `TextBlock.Text` to it.

3. **Dot grid background:** In the main ScrollViewer, set:
   ```xml
   <ScrollViewer.Background>
       <DrawingBrush TileMode="Tile" ViewportUnits="Absolute" Viewport="0,0,28,28">
           <DrawingBrush.Drawing>
               <GeometryDrawing Brush="#05FFFFFF">
                   <GeometryDrawing.Geometry>
                       <EllipseGeometry RadiusX="0.75" RadiusY="0.75" Center="1,1"/>
                   </GeometryDrawing.Geometry>
               </GeometryDrawing>
           </DrawingBrush.Drawing>
       </DrawingBrush>
   </ScrollViewer.Background>
   ```

4. **Card width responsiveness:** In `CenteredWrapPanel`, the existing
   `ItemWidth` is fixed. For responsive behaviour, handle `SizeChanged` on the
   `ItemsControl` and set `ItemWidth` = `Max(192, (ActualWidth - 36) / floor((ActualWidth - 36) / 205))`.

5. **Tray menu:** Update tray context menu styles to match new dark palette
   (MenuBg, MenuBorder, MenuItemHoverBg are already in DarkTheme-Redesign.xaml).

---

## What NOT to Change

- `Services/` — all service classes
- `Models/` — all model classes
- `ViewModels/ProfileCardViewModel.cs` — except the 4 small additions in Step 4
- `ViewModels/SettingsViewModel.cs` — except `SearchText` and `ActiveFilterCount` in Steps 8–9
- `ViewModels/ViewModelBase.cs`
- `App.xaml.cs` — except the splash screen addition in Step 2
- `ProfileSwitchOrchestrator.cs`
- `AppWindowManager.cs`
- `NativeMethods/`
- `Helpers/`
- `Tray/`
- `*.Tests/` — all test files

---

## Quick Reference: Real ViewModel Bindings

### ProfileCardViewModel properties used in ProfileCardView.xaml
```
Name                    — string, TwoWay
ModeLabel               — "Both Devices" | "Playback Only" | "Recording Only"
PlaybackVisible         — Visibility (built-in converter not needed)
RecordingVisible        — Visibility
SelectedPlaybackDevice.FriendlyName  — string
SelectedRecordingDevice.FriendlyName — string
IsActive                — bool
IsPinned                — bool
HasSchedules            — bool
HotkeyDisplay           — string
IsHotkeySet             — bool (ADD THIS)
HasValidationWarning    — bool
ValidationWarning       — string (tooltip)
IconPreview             — ImageSource
Silent                  — bool
TriggerOnConnect        — bool
TriggerOnConnectVisible — Visibility
HasAppTriggers          — bool
Notes                   — string, TwoWay
SoundSummary            — string

Commands:
ActivateCommand         — activates profile
CaptureHotkeyCommand    — opens HotkeyCaptureDialog
PickIconCommand         — opens IconGalleryDialog
CloneCommand            — shows confirm, then clones
DeleteCommand           — shows confirm, then deletes
TestSoundCommand        — plays test tone on playback device
TestMicCommand          — opens MicTestDialog
AddScheduleCommand      — opens ScheduleWizardDialog
ConfigureSoundCommand   — opens SwitchSoundDialog
OpenAppTriggersCommand  — opens AppTriggerDialog
ToggleSilentCommand     — ADD THIS
TogglePinnedCommand     — ADD THIS
ToggleTriggerOnConnectCommand — ADD THIS
```

### SettingsViewModel properties used in SettingsWindow.xaml
```
Profiles                — ObservableCollection<ProfileCardViewModel>
HasNoProfiles           — bool
HasNoFilterResults      — bool
IsAnyFilterActive       — bool
ActiveFilterCount       — int (ADD THIS)
SearchText              — string, TwoWay (ADD THIS)

ModePlayback, ModeRecording, ModeBoth  — bool, TwoWay (filter chips)
PinnedFilter, ActiveFilter, SilentFilter, HotkeyFilter,
ScheduledFilter, SoundFilter, WarningFilter — bool, TwoWay
ClearFiltersCommand

StartWithWindows, StartMinimized, CloseToTray,
ShowNotifications, UseLegacySoundPanel,
ShowDisabledDevices, ShowDisconnectedDevices,
LeftClickCyclesProfiles, Use12HourClock, Use24HourClock  — bool, TwoWay
Theme                   — string, TwoWay ("Follow Windows" | "Light" | "Dark")

SettingsHotkeyDisplay, SettingsHotkeyIsSet, SettingsHotkeyEnabled
MuteMicHotkeyDisplay, MuteMicHotkeyIsSet, MuteMicHotkeyEnabled
MuteSpeakersHotkeyDisplay, MuteSpeakersHotkeyIsSet, MuteSpeakersHotkeyEnabled
MuteBothHotkeyDisplay, MuteBothHotkeyIsSet, MuteBothHotkeyEnabled

AddProfileCommand
DeviceAliases           — ObservableCollection<DeviceAliasItem>
```

---

## Design Token Quick Reference (for any missing styles)

```
Background:         #0D0D12      (AppBg)
Card:               #16161E      (CardBg)
Card hover:         #1E1E28      (InnerCardBg)
Nav bar:            #10101A      (VSNavBg)
Title bar:          #09090E      (VSTitleBarBg)
Border:             #222234      (CardBorderBrush)
Border hover:       #363650      (InnerCardBorderBrush)
Input bg:           #101018      (InputBg)
Input border:       #1E1E30      (InputBorder)

Orange accent:      #F5820A      (Accent)
Orange hover:       #E07409      (AccentHover)
Orange dim bg:      #1F1308      (VSAccentDim)
Orange border:      #3D2908      (VSAccentBorder)

Text primary:       #E4E4EF      (PrimaryText)
Text secondary:     #7878A0      (SecondaryText)
Text muted:         #38384E      (MutedText)

Active green:       #2DCA72      (VSGreen / SuccessDot)
Playback blue:      #4AA8FF      (VSBlue / PrimaryBadgeText)
Recording mint:     #30D278      (VSMint / SuccessBadgeText)
Danger red:         #E05555      (ErrorText)

CornerRadius card:  14
CornerRadius modal: 18
CornerRadius btn:   7–8
CornerRadius chip:  13 (pill)
CornerRadius input: 6–8
```

---

## Icon Path Data (for XAML Path elements in action buttons)

These are the 9 action button icon paths at 16×16 viewbox:

```xml
<!-- Bell-slash (No Notification) -->
<Path Data="M8,2 A5,5 0 0 0 3,7 L3,10 L2,12 L14,12 L13,10 L13,7 A5,5 0 0 0 8,2 Z
            M6.5,13.5 A1.5,1.5 0 0 0 9.5,13.5 M1,1 L15,15"
      Stroke="CurrentColor" StrokeThickness="1.5" Fill="Transparent"
      StrokeStartLineCap="Round" StrokeEndLineCap="Round"/>

<!-- External link (App Trigger) -->
<Path Data="M7,3 L4,3 A1,1 0 0 0 3,4 L3,12 A1,1 0 0 0 4,13 L12,13 A1,1 0 0 0 13,12 L13,9
            M10,2 L14,2 L14,6 M9.5,6.5 L14,2"
      Stroke="CurrentColor" StrokeThickness="1.5" Fill="Transparent"
      StrokeStartLineCap="Round" StrokeEndLineCap="Round"/>

<!-- Refresh arrows (Auto-Switch) -->
<Path Data="M14,8 A6,6 0 0 1 3,11.2 M2,8 A6,6 0 0 1 13,4.8
            M2,11.5 L2,8 L5,8 M14,4.5 L14,8 L11,8"
      Stroke="CurrentColor" StrokeThickness="1.5" Fill="Transparent"
      StrokeStartLineCap="Round" StrokeEndLineCap="Round"/>

<!-- Star (Favorite) -->
<Path Data="M8,1.5 L10.2,5.8 L15,6.4 L11.5,9.8 L12.4,14.5 L8,12.1 L3.6,14.5 L4.5,9.8 L1,6.4 L5.8,5.8 Z"
      Fill="CurrentColor"/>

<!-- Check circle (Activate) -->
<Path Data="M8,2.5 A5.5,5.5 0 1 1 8,13.5 A5.5,5.5 0 0 1 8,2.5 M5.5,8 L7.5,10.2 L11,6"
      Stroke="CurrentColor" StrokeThickness="1.5" Fill="Transparent"
      StrokeStartLineCap="Round" StrokeEndLineCap="Round"/>

<!-- Copy (Clone) -->
<Path Data="M5.5,5.5 L13.5,5.5 L13.5,13.5 L5.5,13.5 Z
            M3.5,10.5 L3.5,3.5 A1,1 0 0 1 4.5,2.5 L11.5,2.5"
      Stroke="CurrentColor" StrokeThickness="1.5" Fill="Transparent"
      StrokeStartLineCap="Round" StrokeEndLineCap="Round"/>

<!-- Clock (Scheduler) -->
<Path Data="M8,2.5 A5.5,5.5 0 1 1 8,13.5 A5.5,5.5 0 0 1 8,2.5 M8,5 L8,8.5 L10.5,10.2"
      Stroke="CurrentColor" StrokeThickness="1.5" Fill="Transparent"
      StrokeStartLineCap="Round" StrokeEndLineCap="Round"/>

<!-- Speaker (Sound Switch) -->
<Path Data="M5,5.5 L7,5.5 L11,2 L11,14 L7,10.5 L5,10.5 Z
            M11,6.8 A2,2 0 0 1 11,9.2"
      Stroke="CurrentColor" StrokeThickness="1.5" Fill="Transparent"
      StrokeStartLineCap="Round" StrokeEndLineCap="Round"/>

<!-- Trash (Delete — always red) -->
<Path Data="M2,4 L14,4 M5.5,4 L5.5,2.5 L10.5,2.5 L10.5,4 M3.5,4 L4.3,13.5 L11.7,13.5 L12.5,4"
      Stroke="#E05555" StrokeThickness="1.5" Fill="Transparent"
      StrokeStartLineCap="Round" StrokeEndLineCap="Round"/>
```

---

## VS App Icon (for splash + title bar)

The VS icon is a rounded dark square with:
- V chevron: `M 19,22 L 40,54 L 61,22` (80×80 canvas, stroke `#F5820A`, width 4)
- 5 equalizer bars at x=22/29.5/37.75/46/53.5, varying heights, `#F5820A`

See `card.jsx` → `VSSplashIcon` component for the exact SVG.
See `SplashWindow.xaml` for the XAML Canvas equivalent.

To use as a window icon (.ico), convert this SVG to a multi-size .ico file
at 16/32/48/256px and set `Window.Icon` in all windows.

---

Good luck! Open `VibeSwitcher.html` in Chrome for the visual reference,
start with Step 1, and commit after each step. The ViewModel layer is solid —
focus all effort on the XAML.
