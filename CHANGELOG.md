# Changelog

All notable changes to VibeSwitcher are documented here. Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

---

## [Unreleased]

### Added
- **Profile notes (F32)** — optional short description field on each profile card, placed below the profile name; `MaxLength` of 61 characters; stored per profile with an ⓘ badge tooltip explaining the limit *(PR #76)*
- **Pinned / favorite profiles (F33)** — star toggle in each profile card footer; pinned profiles sort above unpinned ones in both the Settings list and the tray right-click menu; `SortOrder` is renumbered after each pin change to keep ordering stable *(PR #76)*
- **Profile validation warnings (F34)** — inline warning badge on cards for disconnected or unavailable audio devices and missing icon files; device check is suppressed until the first device enumeration completes to avoid false warnings at startup *(PR #76)*
- **Real-time icon file watcher** — a `FileSystemWatcher` in `ProfileCardViewModel` re-validates the icon path when the file is created or deleted while the Settings window is open *(PR #76)*

### Performance
- **Config saves moved off the UI thread** — all config saves (profile switch and settings edits) now run on a background thread via `Task.Run`; no longer blocks the UI thread during disk writes *(PR #78)*
- **Tray icon bytes cache** — raw icon bytes cached per profile after the first load; a fresh `Icon` object is reconstructed from memory on each switch so no disk I/O is needed on repeat switches *(PR #78)*
- **Scheduler timer reduced** — `SchedulerService` background tick interval changed from 1 second to 10 seconds; minute-level precision is sufficient and eliminates over 3,000 unnecessary UI-thread wakeups per hour *(PR #78)*
- **`AppLogger` write path optimized** — removed `Directory.CreateDirectory` syscall from every log write; the directory is guaranteed to exist after session start *(PR #78)*
- **Validation refresh narrowed** — `OnProfileChanged` now refreshes only the card that changed instead of all cards *(PR #78)*

### Fixed
- **`ObjectDisposedException` on tray icon** — H.NotifyIcon disposes the old `Icon` object on each assignment; the previous icon object cache caused a crash when switching back to a profile whose icon had already been disposed. Fixed by caching raw bytes and always providing a fresh `Icon` instance *(PR #78)*

### Fixed
- **`AccentColor` fallback for toggle animation** — toggle switch `ColorAnimation.To` now references `{StaticResource AccentColor}` instead of a hardcoded hex; a `Color` fallback in `App.xaml` satisfies parse-time resolution and theme files override it at runtime *(PR #72)*
- **ConfirmDialog icon badge updates with theme** — badge background switched from `TryFindResource` to `SetResourceReference` so it responds to theme changes while the dialog is open *(PR #72)*
- **`AboutWindow` removed from taskbar** — `ShowInTaskbar="False"` added; it was the only dialog in the app missing the attribute *(PR #72)*
- **TitleBar `StateChanged` handler no longer leaks** — stored in `_stateChangedHandler` field and unsubscribed in a new `OnUnloaded` handler *(PR #72)*
- **SettingsWindow close-time bounds save** — `_boundsTimer` stopped at the top of `OnClosing` before `SaveWindowBounds()` to prevent a redundant debounced write *(PR #72)*
- **Settings header app icon restored** — icon now appears to the left of the "VibeSwitcher" heading in the Settings window *(PR #72)*
- **Title bar maximize button vertically aligned** — □ now sits level with − and ✕ via a `TextBlock` wrapper with a 5 px bottom margin, correcting the font-metric baseline difference in Segoe UI *(PR #72)*
- **Tray separators equalized** — all three separators render at the same height; changed from a 2.5 px rounded border inside a fixed-height `MenuItem` to a 1 px flat line with 4 px top/bottom margin *(PR #72)*
- **Dead `ToggleInactiveBg` brush removed** — unused resource deleted from both `LightTheme.xaml` and `DarkTheme.xaml` *(PR #72)*

### Added
- **Profile scheduler (F11)** — each profile card has an "Add Schedule" button that opens a four-step wizard (day selector, time picker, reminder, silent toggle); a `SchedulerService` running a 1-second background timer switches the active profile automatically when a schedule matches the current day and time; re-evaluates after sleep/wake *(PR #74)*
- **Pre-switch reminder** — each schedule entry has an optional lead-time notification (5, 10, 15, 30 min or custom); fires a balloon tip N minutes before the switch so the user can finish what they're doing or override *(PR #74)*
- **Per-schedule Silent toggle** — independent of the profile card Silent toggle; profile Silent applies only to manual switches (hotkey, tray, Activate button), schedule Silent applies only to scheduled switches *(PR #74)*
- **Activate button on profile cards** — switches to a profile directly from the Settings window; displays a green "✓ Active" state when the profile is currently active; refreshes automatically when the Settings window is opened *(PR #74)*

### Fixed
- **Profile Silent incorrectly suppressed scheduled switch notifications** — `scheduleSilent` changed from `bool` to `bool?` in `ProfileSwitchOrchestrator`; null means manual (use `profile.Silent`), a value means scheduled (use that value, ignoring `profile.Silent`) *(PR #74)*
- **Scheduler dedup blocked same-minute edits** — replaced the 2-minute elapsed-time guard with slot-based comparison (stored hour:minute:day); editing a schedule time now fires correctly within the same 2-minute window *(PR #74)*
- **Dark-mode tooltip text unreadable** — `Foreground` setter added to the local `ToolTip` style in `SettingsWindow.xaml` and explicit `Foreground` on each tooltip `TextBlock` *(PR #74)*

### Added
- **Light / dark mode theming (F16)** — full resource-dictionary theming system with `LightTheme.xaml` and `DarkTheme.xaml` (each ~70 named brushes); `ThemeService` applies the chosen theme by swapping `MergedDictionaries`; an in-app toggle in General Settings persists the preference across launches; covers all windows, dialogs, profile cards, and tray menu *(PR #70)*
- **Tray separator polish** — separators in the right-click tray menu now use a custom 2.5 px rounded `Border` element spanning the full menu width; replaced `new Separator()` with a tagged `MenuItem` to bypass the WPF ControlTemplate indent *(PR #70)*

### Fixed
- **Tray theme live update** — switching the app theme now immediately updates the tray context menu; `RebuildMenu` creates a fresh `ContextMenu` object each time so the new Popup visual tree reads current theme resources on open *(PR #70)*
- **Tray icon switch flash speed** — blink hold time reduced from 350 ms to 150 ms for a snappier visual confirmation on profile switch *(PR #70)*
- **Settings auto-expand when expander opens** — when the General Settings card opens, the window now measures the footer's position in window coordinates (accounting for the 18 px bottom margin) and grows to ensure the footer buttons are always fully visible *(PR #70)*
- **Window size and position not persisting** — `SizeChanged` and `LocationChanged` events now write bounds to config via a 400 ms `DispatcherTimer` debounce; the previous `OnClosing`-only approach did not fire reliably for hidden windows during app shutdown *(PR #70)*
- **Icon preview gray background** — the coloured `IconPreviewBg` background behind icons on profile cards replaced with a transparent background and a 1 px `InputBorder` outline *(PR #70)*
- **Clone dialog icon** — warning triangle replaced with a WPF-drawn copy-overlap visual (two rounded rectangles on a `Canvas`); `ConfirmDialog` now accepts an optional `UIElement` icon override *(PR #70)*
- **About window label colours in dark mode** — WEBSITE, DEVELOPMENT, and SUPPORT section labels changed from `SubtleText` to `SecondaryText` brush so they are readable in both themes *(PR #70)*

### Added
- **Backup & Restore (F1)** — Export writes the current config to a user-chosen `.json` file; Import reads it back with a confirmation dialog and rebuilds the entire profile list from the imported data; both operations give AlertDialog feedback on success or failure *(PR #68)*
- **Save flash on profile cards (F18)** — each profile card briefly flashes green when a change is saved; each card uses its own `SolidColorBrush` instance so animations are independent, with a 250 ms `CancellationTokenSource` debounce to collapse rapid edits into a single flash *(PR #68)*
- **Getting-started help dialog (F19)** — the `?` footer button opens a scrollable walkthrough dialog covering setup, profile switching, tray tips, what ⓘ icons mean, how Backup & Restore works, and where data is stored; button re-rendered with ClearType to eliminate blurriness *(PR #68)*
- **Inner card layout for General Settings (F42)** — the flat settings card is now split into six labelled inner cards: Startup, Notifications, Tray, Devices, Backup & Restore, and Shortcuts; each has a subtle grey background and border *(PR #68)*
- **Opacity fade for all settings toggles (F43)** — when any toggle is off, its label and ⓘ badge fade to 40% opacity; the Shortcuts hotkey assignment row fades independently so the enable toggle itself stays fully opaque *(PR #68)*
- **ⓘ info badges on every toggle** — every settings toggle now has a small blue ⓘ circle with a plain-English tooltip; badge style unified with profile card info icons *(PR #68)*
- **Left-click tray cycle toggle** — new "Left-click tray icon to cycle profiles" toggle in the Tray inner card; when disabled, left-clicking the tray icon opens VibeSwitcher instead of cycling profiles; `AppConfig.LeftClickCyclesProfiles` persists the setting *(PR #68)*
- **ConfirmDialog** — new reusable modal (title, subtitle, message, configurable action label) used for the import confirmation; matches the existing `AlertDialog` / `ConflictRetryDialog` pattern *(PR #68)*
- **Clone button tooltip** — hovering over the Clone button now shows an explanation of what cloning does *(PR #68)*
- **Built-in icon gallery (F17)** — 12 emoji profile icons (Gaming, Work, Music, Headset, Streaming, Calls, Mic, Home, Speakers, Night, Podcast, Desktop) accessible via "Pick Icon" on each profile card; icons are rendered and saved as proper 64×64 PNG-embedded ICO files, bypassing `GetHicon()` to preserve quality *(PR #66)*
- **Black / White icon color toggle (F17)** — gallery dialog includes a color toggle; Black recolors the emoji pixels to black (best on light taskbars), White recolors to white; chosen color persists on the profile and is applied after restarts *(PR #66)*
- **Dark background chip for white icons (F17)** — white icons display with a `#4A4A4A` rounded chip behind them in the tray right-click context menu and the settings card icon preview so they remain visible; the tray taskbar icon itself stays clean without a background *(PR #66)*
- **Profile name suggestion chips (F20)** — while a profile still has its auto-assigned "Profile N" name, clickable name chips appear below the name field (Gaming, Work, Music, Headset, Streaming, Calls, Mic, Home, Speakers); picking a chip sets the name and silently auto-applies the matching gallery icon *(PR #66)*
- **Drag-to-reorder grip tooltip** — hovering the six-dot grip now shows a tooltip explaining that it can be dragged to reorder profiles *(PR #66)*
- **Device visibility toggles** — two new toggle switches under a "Devices" section in General Settings: one to show/hide software-disabled devices in profile card dropdowns, one for disconnected/unplugged devices; both settings persist across sessions and filter immediately without re-enumerating audio endpoints *(PR #64)*
- **Collapsible General Settings card** — the settings card now has a clickable header with a gear icon, a "VibeSwitcher Settings" title, and a subtitle; clicking collapses or expands the card body; collapsed/expanded state is saved to config and restored on next launch *(PR #64)*
- **Tray menu clarity** — the VibeSwitcher logo at the top of the right-click tray menu is now clickable to open the app; the separate "Settings" item (whose label was ambiguous) has been removed *(PR #64)*
- **Test sound button (F3)** — each profile card now has a speaker button next to the playback dropdown; clicking it plays a 440 Hz tone directly through the selected device via WASAPI, bypassing the Windows default so you can confirm which device is active *(PR #61)*
- **Mic test dialog (F3 extension)** — a mic button next to the recording dropdown opens a live level meter dialog that captures audio from the selected device for 5 seconds, showing real-time RMS level and peak reading *(PR #61)*
- **Device connectivity indicator (F26)** — playback and recording dropdowns now show a green dot for active devices and a red dot for disabled or unplugged devices; devices disabled in Windows Sound settings stay visible in the list instead of disappearing *(PR #61)*
- **Drag-and-drop profile reorder (F2)** — each profile card has a ⠿ grip handle on the left; dragging it over another card reorders the list and saves the new order immediately *(PR #59)*
- **Profile clone (F23)** — Clone button on each card duplicates the profile (name + " (copy)", same devices and mode); hotkey and icon are not copied to avoid conflicts and file-sharing issues *(PR #59)*
- **Per-profile silent switch (F25)** — Silent switch checkbox on each card; when enabled, switching to that profile skips the Windows notification banner while device-unavailable warnings still appear *(PR #59)*
- **Left-click tray cycles profiles (F21)** — a single left-click on the tray icon advances to the next profile in sort order, wrapping around; right-click still opens the menu *(PR #57)*
- **Hotkey cheat sheet tooltip (F5)** — hovering the tray icon shows a multi-line tooltip listing every profile with an assigned hotkey; updates automatically on every switch *(PR #57)*
- **Global Settings hotkey (F24)** — a Shortcuts section in the General Settings card lets the user assign any key combo to toggle the Settings window from anywhere; includes an enable/disable toggle and a keyboard badge chip showing the current binding *(PR #57)*
- **Tray icon flash on switch (F30)** — after each profile switch the tray icon briefly blinks to the default icon for ~300 ms as a subtle visual confirmation *(PR #57)*
- **Hotkey conflict notification names the owner** — when a captured hotkey is already in use, the conflict message identifies which profile or setting owns it *(PR #57)*
- **Conflict retry dialog** — after a conflict, a custom styled dialog offers Try Again or Close so the user can immediately try a different key without re-opening the capture dialog *(PR #57)*
- **App icon in balloon notification body** — switching profiles now shows the VibeSwitcher app icon as the large icon in the notification banner body instead of the generic blue "i"; the 32×32 HICON is cached for the app's lifetime *(PR #53)*
- **Multi-frame app icon and high-quality image source** — `app.ico` replaced with a 4-frame ICO (16/32/48/256px); `IconHelper.GetAppIconImageSource()` loads the 256px frame via `BitmapDecoder` with `BitmapCacheOption.OnLoad`, guarded by a double-checked lock *(PR #50)*
- **App icon in Settings window header, About window, and tray context menu** — the keycap icon now appears consistently across all three surfaces *(PR #50)*
- **Additional unit tests for edge cases** — 6 new tests covering the `_loadingDevices` guard in `ProfileCardViewModel`, `ConfigService.Migrate()` sentinel handling, `IconHelper.LoadIcon()` corrupt-bytes fallback, and `RaiseDevicesChanged()` thread safety *(PR #48)*
- **SHA256 checksums on releases** — the release pipeline generates `sha256sums.txt` (BOM-free UTF-8, `sha256sum`-compatible format) and attaches it to every GitHub Release *(PR #46)*
- **GitHub Actions CI and release pipelines** — every push and PR to `main` builds and runs all tests automatically; pushing a `v*` tag publishes a self-contained Windows x64 single-file exe as a GitHub Release *(PR #38)*
- **SettingsViewModel and ProfileCardViewModel unit tests** — 18 new tests covering AddProfile, DeleteProfile, hotkey re-registration, CaptureHotkey flows, BrowseIcon flows, and DeleteProfile confirm/cancel *(PR #36)*
- **Service interfaces** — `IAudioService`, `IConfigService`, `IStartupService`, `IHotkeyService`, and `IDialogService` extracted; all ViewModels now depend on interfaces rather than concrete types, enabling safe fake substitution in tests *(PR #35)*
- **Unit test project** — `VibeSwitcher.Tests` with 69 tests covering ConfigService, HotkeyDefinition, SessionErrorTracker, ErrorCode, AppLogger, DeviceNotificationClient, and IconHelper; `dotnet test` runs in ~2 seconds *(PR #33)*
- **Live device refresh** — Settings dropdowns update automatically when audio devices are plugged in or removed *(PR #30)*
- **Switching tooltip** — tray tooltip shows "Switching to {profile}..." while a switch is in progress; restores to the correct profile name on both success and failure *(PR #28)*
- **Keyboard navigation in Settings** — arrow keys move focus between profile cards; read-only fields are excluded from the Tab order *(PR #28)*

### Changed
- **Single profile-switch path** — `TrayService.SwitchToProfileAsync` removed; tray-menu clicks now delegate to `ProfileSwitchOrchestrator.SwitchToProfile`, the same path used by hotkeys and sleep/resume *(PR #40)*
- **Startup active-profile restore** now goes through the orchestrator instead of calling `AudioService.ApplyProfileAsync` directly, ensuring consistent error handling and tooltip state on launch *(PR #40)*
- **`App.xaml.cs` split into focused classes** — `ProfileSwitchOrchestrator` owns the full async switch flow; `AppWindowManager` owns window management; `App.xaml.cs` reduced from 248 to ~120 lines *(PR #37)*
- **Newtonsoft.Json replaced with System.Text.Json** — built-in serializer removes the NuGet dependency; `PropertyNameCaseInsensitive = true` preserves compatibility with hand-edited configs *(PR #29)*

### Fixed
- **Settings header hover corner radius** — the blue hover highlight on the collapsible Settings card header now has rounded corners on all four sides when the card is expanded *(PR #68)*
- **TrayLeftMouseUp thread safety** — `OpenSettings` is now marshalled via `Dispatcher.InvokeAsync` since `TrayLeftMouseUp` fires on a thread-pool thread *(PR #68)*
- **IconColor.Auto not persisted** — when a name suggestion chip auto-applied an icon, `IconColor.Auto` was incorrectly stored on the profile; now stores `Black`, which matches the natural-color emoji render path *(PR #66)*
- **Window title** — title bar now shows "VibeSwitcher" instead of "VibeSwitcher - Settings" *(PR #64)*
- **Button corner radius unified** — all action buttons now use `CornerRadius="7"` to match the Settings card expander hover style *(PR #64)*
- **Spurious config save on window open** — `SettingsCardExpanded` now guards against same-value writes, matching the pattern of every other settings property *(PR #64)*
- **SortOrder recompacted after delete** — deleting a profile renumbers all remaining `SortOrder` values so cloning and adding profiles never produce order collisions *(PR #59)*
- **Profile hotkeys no longer fire during capture** — all hotkeys are unregistered before any capture dialog opens and re-registered after *(PR #57)*
- **Settings hotkey survives profile edits** — `ReregisterHotkeys` now re-registers the Settings hotkey after `RegisterAll` wipes it *(PR #57)*
- **`error.log` cleared on each startup** — `AppLogger.StartSession()` truncates the log file at launch so entries from previous sessions do not accumulate *(PR #53)*
- **`GetDefaultIcon()` thread safety** — double-checked lock added; bare `catch {}` now logs via `AppLogger.Warning` *(PR #50)*
- **`IsDeviceActive` bare catch narrowed** — now scoped to `COMException` and `InvalidComObjectException` only; also fixed a COM object leak and an unchecked HRESULT that could falsely report a device as active *(PR #44)*
- **Error dialog appears on the correct monitor** — `ErrorDialog` now sets `Owner` to the first visible window and uses `CenterOwner` placement *(PR #44)*
- **Stale `ActiveProfileId` reset at startup** — if the persisted ID does not match any loaded profile, the app resets it to null to keep the tray menu consistent *(PR #44)*
- **Event handler leak when closing to tray** — `SessionErrorTracker.ErrorAdded` subscription is now managed via `IsVisibleChanged` so exactly one subscription is active while the window is visible *(PR #42)*
- **Stale device list after rapid plug/unplug** — `LoadDevicesAsync` cancels any in-progress enumeration atomically before starting a new one *(PR #42)*
- **Concurrent profile switches no longer corrupt audio state** — rapid hotkey presses or tray-menu clicks while a switch is in progress are dropped and logged *(PR #40)*
- **`ProfileSwitchOrchestrator` now disposes its `SemaphoreSlim`** on app exit *(PR #40)*
- **Second-instance crash on exit** — `OnExit` now null-guards `_orchestrator` before unsubscribing from `SystemEvents.PowerModeChanged` *(PR #37)*
- **Clearing a hotkey now applies correctly** — "Clear" in the capture dialog now removes the hotkey instead of silently restoring the previous one *(PR #35)*
- **Tab key excluded from hotkey capture** — Tab now navigates between buttons in the dialog instead of being assigned as a hotkey; Apps, Pause, PrintScreen, and Scroll also excluded *(PR #31)*
- **Keyboard focus visible on all interactive controls** — pill toggles and buttons show a focus ring when tabbed to *(PR #31)*
- **Windows Audio service down** — profile switches now surface a clear "Windows Audio service is not running" message when HRESULT 0x80070424 is detected *(PR #30)*
- **Device selection persists across relaunches** — a `_loadingDevices` guard prevents the TwoWay ComboBox binding from writing null into the model during async device list refresh *(PR #28)*
- **Switching tooltip correctly restored on failure** — tray tooltip and icon are restored to the previously-active profile if a switch fails *(PR #28)*
- **`PropVariant` struct size corrected** — expanded from 16 to 24 bytes to match the actual x64 `PROPVARIANT` layout *(PR #27)*
- **`ProfileCardViewModel` implements `IDisposable`** — icon preview is released when a profile card is removed *(PR #27)*
- **Config file opened with `FileShare.ReadWrite`** — antivirus and backup tools scanning `config.json` concurrently no longer corrupt the deserialization pass *(PR #27)*
- **Tray menu profile switch no longer triggers a full rebuild** — `SetActiveProfile` flips `IsChecked` only; `RebuildMenu` is called only when profiles actually change *(PR #27)*

---

## [1.1.0] - 2026-05-17

All ten planned fix/polish branches merged since v1.0.0.

### Added
- **Pill-style toggle switches** — the four checkboxes in Settings (Start with Windows, Minimize to Tray, Show Notifications, Use classic Sound control panel) are now animated pill toggles that slide and change colour smoothly *(PR #26)*
- **"Use classic Sound control panel" toggle** — when enabled, both the Settings footer button and the tray "Open Sound Settings" item open the legacy Sound control panel instead of `ms-settings:sound` *(PR #26)*
- **"(None)" device option** — both device dropdowns now list "(None)" as the first entry so users can intentionally leave a slot unset; disconnected devices also fall back to "(None)" instead of leaving the field blank *(PR #26)*
- **Hotkey shown in tray profile header** — when a profile has a hotkey assigned, it appears as a small third line (e.g. Ctrl+Page Up) below the mode label in the right-click menu *(PR #26)*
- **Copy Diagnostic Info button** — About window now has a button that copies version, OS, profile count, session errors, and log path to the clipboard *(PR #24)*
- **Startup path self-repair** — on every launch, if the "Start with Windows" registry entry points to the wrong path (e.g. the exe was moved), it is silently corrected *(PR #25)*
- **Error codes and session log** — every failure now gets a structured error code (VS-001 through VS-026) recorded in a per-session tracker; unexpected switch failures show an `ErrorDialog` with a selectable message and "Open Log File" button instead of a silent balloon *(PR #20)*
- **Session log window** — "Logs" button in Settings footer lists all errors since launch with timestamp, code, and summary *(PR #20)*
- **Styled alert dialog** — new `AlertDialog` (Warning / Info variants) replaces all plain `MessageBox.Show` calls for hotkey conflicts and icon copy failures *(PR #17)*
- **Empty state message** — "No profiles yet — click Add New Profile to get started" shown when no profiles exist *(PR #17)*
- **Real-time modifier preview in hotkey dialog** — holding Ctrl/Shift/Alt/Win shows the partial combo (e.g. "Ctrl+Shift+") before the final key is pressed *(PR #17)*
- **Config backup** — `config.json.bak` is written before every save; if the primary config is corrupted on load, the backup is tried automatically *(PR #16)*
- **Log rotation** — `error.log` rotates at 1 MB and keeps 2 backups; `Info` and `Warning` log levels added alongside `Error` *(PR #16)*
- **`app.manifest`** — PerMonitorV2 DPI awareness declared so the app renders crisp on HiDPI and multi-monitor setups *(PR #18)*
- **Sleep/resume recovery** — active audio profile is automatically re-applied when the PC wakes from sleep or hibernation *(PR #18)*
- **Per-user single-instance mutex** — `Local\` prefix scopes the mutex per user session so Fast User Switching and Remote Desktop each get their own independent instance *(PR #18)*

### Fixed
- **Icon filename now includes profile name** — saved icon files are named `{profile-name}-{8-char-guid}.ico` instead of a raw GUID, making them easier to identify on disk *(PR #26)*
- **Global font consistency** — `Segoe UI` is now set once in `Application.Resources` and cascades to all windows and dialogs; duplicate style definitions removed from `SettingsWindow` and `AboutWindow` *(PR #26)*
- **Redundant STA thread removed** — `RunOnSta` helper eliminated from `AudioService`; device enumeration no longer double-hops threads, reducing overhead on Settings open *(PR #25)*
- **Icon path security** — icon paths are validated against the managed icons directory before loading; a hand-edited config pointing outside that folder falls back to the default icon *(PR #24)*
- **Escape key in Settings** — pressing Escape closes the icon-info popup first if one is open; a second press closes the window *(PR #24)*
- **Open Sound Settings link updated** — links to `ms-settings:sound` on Windows 11 with a fallback to the legacy control panel on Windows 10 *(PR #24)*
- **Profiles without a hotkey no longer write a Hotkey block to config.json** *(PR #24)*
- **App icon wired up** — `VibeSwitcher.exe` now shows the correct blue app icon in File Explorer and the taskbar instead of the Windows placeholder *(PR #24)*
- **Profile name saved immediately** — TextBox binding switched to `PropertyChanged` so closing the Settings window right after typing no longer loses the name change *(PR #17)*
- **Hotkey capture shows partial combo** — modifier keys now show in the preview box while held, before the final key is pressed *(PR #17)*
- **Delete dialog Enter key** — Cancel button is `IsDefault`; pressing Enter dismisses the dialog instead of triggering the delete *(PR #17)*
- **Tray icon sharp at high DPI** — icons loaded at 64×64 instead of 16×16 so they scale cleanly at 125–200% scaling *(PR #18)*
- **Window position clamp** — restored window position is clamped on-screen; no longer throws `ArgumentException` when the saved window is wider than the current screen *(PR #18)*
- **Window left/top sentinel** — `WindowLeft = -1` migrated to `null` on upgrade; no longer conflicts with valid off-screen coordinates *(PR #18)*
- **AnyCPU build** — switched from x64-only to `AnyCPU` with `Prefer32Bit=false` for broader compatibility *(PR #18)*
- **Settings window opens instantly** — device enumeration moved to a single async call shared across all profile cards; previously each card spawned two STA threads in its constructor, freezing the UI *(PR #19)*
- **Tray menu icon no longer reads disk on every switch** — `ImageSource` is cached per profile; `RebuildMenu` reads no files *(PR #19)*
- **COM resource leaks fixed** — `IPropertyStore` and `IMMDeviceCollection` objects now released in `finally` blocks; `Icon` from `UpdateIconPreview` disposed after use *(PR #19)*
- **Static default icon disposed on exit** — `AppDomain.ProcessExit` handler registered to dispose `_defaultIcon` *(PR #19)*
- **O(1) hotkey dispatch** — `WM_HOTKEY` handler uses a `Guid→DeviceProfile` dictionary instead of a linear LINQ scan *(PR #19)*
- **`ContextMenu` no longer leaks on rebuild** — existing `ContextMenu` instance is repopulated in place instead of replaced *(PR #19)*
- **ProfileMode serialized as string** — `ProfileModeConverter` added so the enum writes human-readable names and existing integer values still deserialize correctly *(PR #16)*
- **Config migration scaffold** — `Migrate()` added to `ConfigService` for future schema upgrades without resetting user data *(PR #16)*
- **Orphaned icon files cleaned up** — deleting a profile or replacing its icon removes the old `.ico` from `%APPDATA%\VibeSwitcher\Icons` *(PR #16)*
- **BrowseIcon shows error on failure** — shows an error dialog instead of silently falling back to the source path when `File.Copy` fails *(PR #16)*
- **Icon path traversal** — `Path.GetFullPath` canonicalization applied before any filesystem access on icon paths *(PR #16)*
- **Profile name length limit** — Name TextBox capped at 20 characters, which fits cleanly in the tray right-click menu *(PR #16)*
- **About window version** — reads `AssemblyInformationalVersionAttribute` and strips the git hash suffix; no longer falls back to a hardcoded string *(PR #16)*
- **csproj version unified** — redundant `<AssemblyVersion>` and `<FileVersion>` removed; MSBuild derives both from `<Version>` automatically *(PR #16)*
- **`DispatcherUnhandledException` no longer swallows all exceptions** — handler logs and keeps the app alive for recoverable cases only *(PR #14)*
- **`TaskScheduler.UnobservedTaskException` handler added** — fire-and-forget task failures are now logged instead of silently lost *(PR #14)*
- **`SwitchToProfile` error message improved** — uses `InnerException?.Message` so the real cause is shown, not the wrapper *(PR #14)*
- **"Start with Windows" reads actual registry state** — checkbox now reflects the real registry value instead of what was last saved to JSON *(PR #14)*
- **`StartupService.Enable` no longer silently fails** — uses `CreateSubKey` instead of `OpenSubKey`; the key is created if it doesn't exist *(PR #14)*
- **`ConfigService._config` marked `volatile`** — prevents stale reads across threads *(PR #14)*
- **Hotkey conflict no longer drops all hotkeys** — `RegisterAll` continues registering non-conflicting profiles and returns a list of all conflicts; each is reported individually *(PR #13)*
- **`HotkeyConflictException` no longer swallowed silently** — three distinct catch blocks: conflict → VS-004, atom failure → VS-018, other → VS-009 *(PR #13)*
- **`VirtualKeyCode` range validated** — `IsValid` property checks 1–254 before calling `RegisterHotKey` *(PR #13)*
- **Modifier flag constants unified** — `HotkeyDefinition.GetModifierFlags()` references `WinApi.MOD_*` instead of inline magic numbers *(PR #13)*
- **`GlobalAddAtom` type corrected** — declared as `ushort` to match the Windows ATOM type *(PR #13)*
- **`HotkeyService.Refresh` removed** — was a trivial alias for `RegisterAll` *(PR #13)*
- **Tray icon recreated after Explorer crash** — `WM_TASKBARCREATED` handler calls `RecreateIcon()` when Explorer restarts *(PR #6)*
- **Config save is atomic** — uses `File.Move(overwrite: true)` instead of Copy + Delete; prevents partial-write corruption *(PR #6)*
- **Tray click handler made safe** — `async void` replaced with `async Task`; unhandled exceptions no longer crash the process *(PR #6)*

---

## [1.0.0] - Initial release

- System tray app for switching Windows audio playback and recording devices via profiles
- Per-profile hotkey registration (global, survives focus changes)
- Custom profile icons (.ico files)
- "Start with Windows" option via registry
- Settings window with add/delete/edit profiles
- Persistent config at `%APPDATA%\VibeSwitcher\config.json`
- Persistent error log at `%APPDATA%\VibeSwitcher\error.log`
