# Changelog

All notable changes to VibeSwitcher are documented here. Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

---

## [Unreleased]

### Added
- **Switching tooltip** — tray tooltip shows "Switching to {profile}..." while an async profile switch is in progress; restores to the correct profile name on both success and failure *(PR #28)*
- **Keyboard navigation in Settings** — arrow keys move focus between profile cards; read-only fields (hotkey display, icon path) are excluded from the Tab order *(PR #28)*

### Changed
- **Newtonsoft.Json replaced with System.Text.Json** — built-in serializer removes the NuGet dependency; `ProfileModeConverter` rewritten to handle both string names and legacy integer values; `PropertyNameCaseInsensitive = true` preserves compatibility with hand-edited configs *(PR #29)*

### Fixed
- **Device selection persists across relaunches** — selected playback and recording devices no longer revert to "(None)" on the next app launch; a `_loadingDevices` guard prevents the TwoWay ComboBox binding from writing null into the model during the async device list refresh *(PR #28)*
- **Switching tooltip correctly restored on failure** — if a profile switch fails, both the tray tooltip and icon are restored to the previously-active profile instead of staying on "Switching..." *(PR #28)*
- **`PropVariant` struct size corrected** — expanded from 16 to 24 bytes to match the actual x64 `PROPVARIANT` layout; prevents incorrect device name reads on some hardware configurations *(PR #27)*
- **`HotkeyService.TestHotkey` thread safety** — `Debug.Assert(Dispatcher.CheckAccess())` added to catch any future off-UI-thread calls *(PR #27)*
- **`ProfileCardViewModel` implements `IDisposable`** — icon preview is released when a profile card is removed *(PR #27)*
- **Config file opened with `FileShare.ReadWrite`** — antivirus and backup tools scanning `config.json` concurrently no longer corrupt the deserialization pass *(PR #27)*
- **Tray menu profile switch no longer triggers a full rebuild** — `SetActiveProfile` flips `IsChecked` only; `RebuildMenu` is only called when profiles actually change *(PR #27)*

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
