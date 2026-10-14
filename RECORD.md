# VibeSwitcher — Pre-Release Audit Report

**Date:** 2026-05-16
**Files reviewed:** 22 source files (all .cs and .xaml in the project)

---

## SECTION 1 — CODE REVIEW & SECURITY AUDIT

**~~1.1 — No path traversal validation on icon paths~~** ✅ *Fixed — fix/polish-and-compat*
`Helpers/IconHelper.cs` — Prefix assertion added: after `Path.GetFullPath` canonicalization, the result is asserted to start with `ConfigService.IconsDir`; if it doesn't, a warning is logged, VS-010 is recorded, and the default icon is returned.

**~~1.2 — `StartupService.Enable` silently does nothing if registry key is null~~** ✅ *Fixed — issue #8*
`Services/StartupService.cs` — Now uses `Registry.CurrentUser.CreateSubKey(...)` which creates the key if absent.

**~~1.3 — `AppConfig.Profiles` can be null after JSON deserialization~~** ✅ *Fixed — issue #4*
`Services/ConfigService.cs` — Added `_config.Profiles ??= new();` after deserialization.

**~~1.4 — `AppDomain.UnhandledException` does not prevent crash, comment misleads~~** ✅ *Fixed — issue #8*
`App.xaml.cs` — Comment corrected; `TaskScheduler.UnobservedTaskException` handler added for fire-and-forget Tasks.

**~~1.5 — `SwitchToProfile` balloon shows generic exception message~~** ✅ *Fixed — issue #8*
`App.xaml.cs` — Now uses `ex.InnerException?.Message ?? ex.Message`.

**~~1.6 — `RegisterHotkeys` only catches the first conflict, then loses ALL hotkeys~~** — See ~~H7~~ ✅ Fixed — issue #13

**~~1.7 — `HotkeyService.TestHotkey` multi-step unregister/re-register is not thread-safe~~** ✅ *Fixed — PR #27*
`Services/HotkeyService.cs` — `Debug.Assert(Dispatcher.CheckAccess())` added; catches any future off-UI-thread call in debug builds.

**~~1.8 — Device enumeration blocks the UI thread from the ViewModel constructor~~** ✅ *Fixed*
`ViewModels/SettingsViewModel.cs` — `LoadDevicesAsync` enumerates once on a background thread; all cards share the single result. No UI thread blocking.

**~~1.9 — `BrowseIcon` silently falls back to original path on copy failure~~** ✅ *Fixed — issue #5*
`ViewModels/ProfileCardViewModel.cs` — Now records VS-006 error and shows alert dialog on copy failure.

**~~1.10 — `BitmapSource` from HICON not frozen before icon is disposed~~** ✅ *Fixed*
`Helpers/IconHelper.cs` — `source.Freeze()` now called before returning from `ToImageSource`.

**~~1.11 — `_defaultIcon` static never disposed~~** ✅ *Fixed*
`Helpers/IconHelper.cs` — `AppDomain.CurrentDomain.ProcessExit` registered in static constructor to dispose `_defaultIcon`.

**~~1.12 — `TrayService.RebuildMenu` leaks old `ContextMenu` objects~~** ✅ *Fixed*
`Tray/TrayService.cs` — Now calls `_contextMenu.Items.Clear()` and repopulates the existing `ContextMenu` instance rather than replacing it.

**~~1.13 — `ConfigService.Save` atomic write is not truly atomic~~** ✅ *Fixed — issue #2*
`Services/ConfigService.cs` — Replaced `File.Copy` + `File.Delete` with `File.Move(overwrite: true)`, which is atomic on NTFS.

**~~1.14 — `ConfigService.LogError` duplicates `AppLogger.Error`~~** ✅ *Fixed*
`Services/ConfigService.cs` — Removed `LogError`; all logging routes through `AppLogger`.

**~~1.15 — `PropVariant` struct declared at 16 bytes but x64 PROPVARIANT is 24 bytes~~** ✅ *Fixed — PR #27*
`NativeMethods/AudioInterop.cs` — Struct expanded to 24 bytes with an 8-byte padding field; no more potential COM write-past-boundary.

**~~1.16 — `GlobalAddAtom`/`GlobalDeleteAtom` type mismatch (int vs ushort ATOM)~~** ✅ *Fixed — issue #13*
`NativeMethods/WinApi.cs` — `GlobalAddAtom` and related methods now declared as `ushort`.

**~~1.17 — Modifier flag constants duplicated between `WinApi` and `HotkeyDefinition`~~** ✅ *Fixed — issue #13*
`Models/HotkeyDefinition.cs` — `GetModifierFlags()` now references `WinApi.MOD_ALT`, `WinApi.MOD_CTRL`, `WinApi.MOD_SHIFT`, `WinApi.MOD_WIN`.

**~~1.18 — Single-instance mutex is global — blocks other users on multi-session machines~~** ✅ *Fixed — PR #18*
`Helpers/SingleInstanceHelper.cs` — Mutex now uses a `Local\` prefix, scoping it per user session so Fast User Switching and Remote Desktop each get their own independent instance.

**~~1.19 — `HotkeyService.RegisterProfile` swallows `HotkeyConflictException` silently~~** ✅ *Fixed — issue #13*
`Services/HotkeyService.cs` — Now has three catch blocks: `HotkeyConflictException` → VS-004, `HotkeyAtomException` → VS-018, `Exception` → VS-009.

**~~1.20 — No `VirtualKeyCode` range validation~~** ✅ *Fixed — issue #13*
`Models/HotkeyDefinition.cs` — Added `IsValid => VirtualKeyCode > 0 && VirtualKeyCode <= 254`; checked before `RegisterHotKey`.

**~~1.21 — Log file grows unboundedly~~** — See ~~M3~~ ✅ Fixed

**~~1.22 — `DispatcherUnhandledException` swallows all exceptions unconditionally~~** — See ~~M15~~ ✅ Fixed

**~~1.23 — `AudioService.IsDeviceActive()` uses a bare `catch` that swallows all exceptions~~** ✅ *Fixed — PR #44*
`Services/AudioService.cs` — Narrowed to `COMException` and `InvalidComObjectException` only; also fixed a device COM object leak when `GetState` returned a non-zero HRESULT, and added HRESULT checking so a failed `GetState` call no longer falsely reports a device as active.

**~~1.24 — `ErrorDialog` shown without an `Owner` window in `TrayService`~~** ✅ *Fixed — PR #44*
`ProfileSwitchOrchestrator.cs` — Now finds the first visible window and sets it as `Owner` with `WindowStartupLocation.CenterOwner`; falls back to the XAML `CenterScreen` default when no window is open.

**~~1.25 — `TrayService` accesses `ActiveProfileId` without null guard~~** ✅ *Fixed — PR #44*
`App.xaml.cs` / `Tray/TrayService.cs` — At startup, if `ActiveProfileId` has a value but no matching profile exists, a warning is logged and the ID is reset to null. `IsChecked` comparison in `RebuildMenu` updated to use the explicit `HasValue && .Value` form.

---

## SECTION 2 — PERFORMANCE

**~~2.1 — UI thread blocked N x 2 times during settings open~~** ✅ *Fixed*
Same as ~~1.8~~. `LoadDevicesAsync` in `SettingsViewModel` enumerates once on a background thread; all profile cards call `card.LoadDevices(pb, rec)` with the shared result.

**~~2.2 — Device list enumerated independently per profile card~~** — See ~~2.1~~ ✅ Fixed

**~~2.3 — `RebuildMenu` does disk I/O on every profile switch~~** ✅ *Fixed*
`Tray/TrayService.cs` — `_iconCache` dictionary caches `ImageSource` per profile ID; `RebuildMenu` never reads from disk. Menu repopulates the existing `ContextMenu` instance.

**~~2.4 — `RunOnSta` creates and destroys a new OS thread per audio call~~** ✅ *Fixed — fix/reliability*
`Services/AudioService.cs` — `RunOnSta<T>` removed entirely. `GetPlaybackDevices`/`GetRecordingDevices` are called directly from within `Task.Run` in `SettingsViewModel.LoadDevicesAsync`; `ApplyProfileAsync` uses `Task.Run` at the call site. No redundant second thread created.

**~~2.5 — Double `Task.Run` wrapping in `SwitchToProfile`~~** ✅ *Fixed — issue #8*
`App.xaml.cs` — `SwitchToProfile` is `async void` and directly `await`s `ApplyProfileAsync` with no outer `Task.Run`.

**~~2.6 — `ObservableCollection` fires `CollectionChanged` per item during load~~** ✅ *Fixed*
`ViewModels/SettingsViewModel.cs` — `Profiles` initialized via the `ObservableCollection(IEnumerable<T>)` constructor, so no per-item `CollectionChanged` fires during load.

**~~2.7 — Icon creation allocations on every load~~** *(Won't fix — GC impact is negligible for a tray app that loads icons infrequently)*

**~~2.8 — Linear LINQ scan in `WndProc` hotkey handler~~** ✅ *Fixed — issue #13*
`Services/HotkeyService.cs` — `HandleHotkey` uses `_atomToProfile` and `_profileById` dictionaries for O(1) dispatch on every `WM_HOTKEY`.

---

## SECTION 3 — CODE QUALITY & ARCHITECTURE

**~~3.1 — MVVM violation: ViewModels directly instantiate and open View classes~~** ✅ Done — PR #35
`IDialogService` extracted with `ShowHotkeyCaptureDialog`, `ShowConfirmDeleteDialog`, `ShowAddProfileDialog`, and `ShowOpenFileDialog` methods; `DialogService` concrete implementation added; all ViewModels updated to receive `IDialogService` via constructor injection.

**~~3.2 — `StartWithWindows` initialized from config JSON, not actual registry state~~** ✅ *Fixed — issue #8*
`ViewModels/SettingsViewModel.cs` — Constructor now sets `_startWithWindows = startupService.IsStartupEnabled()`, reading the actual registry state.

**~~3.3 — `App.xaml.cs` is a God Class~~** ✅ Done — PR #37
`ProfileSwitchOrchestrator` and `AppWindowManager` extracted; `App.xaml.cs` reduced from 248 to ~120 lines as a thin bootstrapper.

**~~3.4 — Button/control styles not in `App.xaml`~~** ✅ *Fixed — fix/settings-ux-3*
`App.xaml` — `RoundedButtonTemplate`, `ActionButton`, `DangerButton`, `PrimaryButton`, and new `ToggleSwitchStyle` moved to `Application.Resources`. Global `<Style TargetType="Window">` sets `FontFamily="Segoe UI"` for all windows. Local duplicates removed from `SettingsWindow` and `AboutWindow`.

**~~3.5 — `ProfileCardViewModel` not `IDisposable`~~** ✅ *Fixed — PR #27*
`ViewModels/ProfileCardViewModel.cs` — `IDisposable` implemented; `SettingsViewModel.DeleteProfile` calls `Dispose()` on the removed card.

**~~3.6 — COM object release in `GetDeviceInfo` not in `finally`~~** ✅ *Fixed — PR #19*
`AudioService.cs` — `IPropertyStore` and `IMMDeviceCollection` COM objects now released unconditionally in `finally` blocks.

**~~3.7 — Magic numbers in `HotkeyDefinition.GetModifierFlags()`~~** — See ~~1.17~~ ✅ Fixed

**~~3.8 — No `IConfigService` interface~~** ✅ Done — PR #35
`IConfigService` extracted; all consumers updated to reference the interface.

**~~3.9 — `async void` tray click handler~~** ✅ *Fixed — issue #1*
`Tray/TrayService.cs` — Click handler changed to `_ = SwitchToProfileAsync(profile)` with a proper `async Task` method that catches and shows errors as notifications.

**~~3.10 — `BuildSectionLabel` is dead code~~** ✅ *Fixed*
`Tray/TrayService.cs` — Dead method removed.

**~~3.11 — `HotkeyService.Refresh` is a trivial alias for `RegisterAll`~~** ✅ *Fixed — issue #13*
`Services/HotkeyService.cs` — `Refresh` method removed entirely.

**~~3.12 — Profile switch logic duplicated between `TrayService` and `ProfileSwitchOrchestrator`~~** ✅ Done — PR #40
`TrayService.SwitchToProfileAsync` removed; tray clicks now fire a `SwitchRequested` delegate wired to `ProfileSwitchOrchestrator.SwitchToProfile`. Single switch path for all trigger sources.

**~~3.13 — No concurrent-switch guard in `ProfileSwitchOrchestrator`~~** ✅ Done — PR #40
`SemaphoreSlim(1,1)` guard added; concurrent requests are dropped and logged as warnings. Orchestrator now implements `IDisposable` and is disposed in `App.OnExit`.

**~~3.12 — `WinApi.MOD_*` constants defined but never referenced~~** ✅ *Fixed — issue #13*
`NativeMethods/WinApi.cs` / `Models/HotkeyDefinition.cs` — `GetModifierFlags` now references `WinApi.MOD_*` constants.

**~~3.13 — `ConfigService._config` not `volatile`~~** ✅ *Fixed — issue #8*
`Services/ConfigService.cs` — `_config` field declared `volatile`.

**~~3.14 — `AboutWindow` version fallback is hardcoded~~** ✅ *Fixed*
`Views/AboutWindow.xaml.cs` — Now reads `AssemblyInformationalVersionAttribute`, stripping the git hash suffix appended by MSBuild.

**~~3.15 — Blanket `catch` in `HotkeyDefinition.ToDisplayString`~~** ✅ *Fixed — issue #13*
`Models/HotkeyDefinition.cs` — `KeyInterop.KeyFromVirtualKey` does not throw; blanket catch removed.

---

## SECTION 4 — USER EXPERIENCE

**~~4.1 — Settings window freezes proportional to profile count~~** ✅ *Fixed*
See ~~1.8~~ / ~~2.1~~. Device enumeration is now fully async and shared.

**~~4.2 — No loading state during profile switch~~** ✅ *Fixed — PR #28*
`Tray/TrayService.cs`, `App.xaml.cs` — `SetSwitchingTooltip("Switching to {name}...")` called before the async switch begins; `UpdateIcon` restores the correct tooltip on both success and failure.

**~~4.3 — Profile name only saves on LostFocus~~** ✅ *Fixed — PR #17*
`SettingsWindow.xaml` — TextBox binding switched to `UpdateSourceTrigger=PropertyChanged`; name is saved immediately on every keystroke.

**~~4.4 — No empty state UI when no profiles exist~~** ✅ *Fixed — PR #17*
`SettingsWindow.xaml` — "No profiles yet — click Add New Profile to get started" message shown when list is empty; hides automatically when a profile is added.

**~~4.5 — Hotkey conflict uses system `MessageBox` instead of styled dialog~~** ✅ *Fixed — PR #17*
`ViewModels/ProfileCardViewModel.cs` — New `AlertDialog` (Warning / Info variants) replaces all `MessageBox.Show` calls; matches the app's visual style and closes on Enter or Escape.

**~~4.6 — No keyboard navigation between profile cards~~** ✅ *Fixed — PR #28*
`Views/SettingsWindow.xaml` — `KeyboardNavigation.DirectionalNavigation="Contained"` on the `ItemsControl`; read-only fields set `IsTabStop="False"`; explicit `TabIndex` on interactive controls.

**~~4.9 — No hotkey summary visible without opening each card~~** ✅ *Fixed — fix/settings-ux-3*
`Tray/TrayService.cs` — `BuildProfileHeader` now adds a third line below the mode label showing the hotkey (e.g. "Ctrl+Page Up") in 10 pt gray text when a hotkey is configured. The Settings card already shows the hotkey display field.

**~~4.10 — Hotkey dialog does not show held modifiers before final key press~~** ✅ *Fixed — PR #17*
`Views/HotkeyCaptureDialog.xaml.cs` — Holding any combination of Ctrl/Shift/Alt/Win now shows e.g. "Ctrl+Shift+" in real time before the final key is pressed.

**~~4.12 — Icon info popup not dismissable via Escape~~** ✅ *Fixed — fix/polish-and-compat*
`SettingsWindow.xaml.cs` — `Window_KeyDown` now calls `CloseOpenIconPopups()` first; if any `IconInfoToggle` `ToggleButton` is checked it is unchecked and the Escape is consumed, so the window is only closed on a second Escape press.

**~~4.16 — Delete dialog Cancel button not `IsDefault`~~** ✅ *Fixed — PR #17*
`Views/ConfirmDeleteDialog.xaml` — Cancel button is now `IsDefault`; pressing Enter safely dismisses the dialog instead of triggering the delete.

**~~4.17 — Window position restore may still partially overflow screen~~** ✅ *Fixed — PR #18*
`Views/SettingsWindow.xaml.cs` — `Math.Clamp` now guards the upper bound so a saved window wider than the current screen is clamped on-screen rather than throwing an `ArgumentException`.

**~~4.18 — `SettingsWindow` `ErrorAdded` event handler not cleaned up on hide~~** ✅ Done — PR #42
`IsVisibleChanged` handler added; subscribes when window becomes visible, unsubscribes when hidden or closed. Removes the accumulation on each show-and-hide cycle and refreshes the button state on every re-open.

**~~4.19 — `LoadDevicesAsync` not cancellable — concurrent calls overwrite each other~~** ✅ Done — PR #42
`CancellationTokenSource? _loadCts` added; swapped atomically via `Interlocked.Exchange` on each call. Previous enumeration cancelled and disposed before a new one starts. `_playbackDevices` and `_recordingDevices` marked `volatile`.

*Remaining open items (4.7 → F2, 4.8 → F3, 4.11/4.15 → F16, 4.13 → F1, 4.14 → F21) are tracked in Section 11 — Feature Additions.*

---

## SECTION 5 — WINDOWS INTEGRATION & COMPATIBILITY

**~~5.1 — Explorer crash permanently loses tray icon~~** ✅ *Fixed — issue #3*
`App.xaml.cs` / `Tray/TrayService.cs` — Added `WM_TASKBARCREATED` handler in `WndProc`; calls `RecreateIcon()` when Explorer restarts.

**~~5.2 — Active profile not re-applied after sleep/hibernate~~** ✅ *Fixed*
`App.xaml.cs` — `SystemEvents.PowerModeChanged` handler subscribed; re-applies the active profile on `PowerModes.Resume`.

**~~5.3 — Device list in open Settings not refreshed on hotplug/unplug~~** ✅ *Fixed — PR #30*
`Services/DeviceNotificationClient.cs`, `Services/AudioService.cs`, `ViewModels/SettingsViewModel.cs` — `IMMNotificationClient` debounces device add/remove/state-change events into a single `DevicesChanged` event after 500 ms; `SettingsViewModel` subscribes and refreshes all dropdowns via `LoadDevicesAsync()`.

**~~5.4 — x64-only build excludes 32-bit environments~~** ✅ *Fixed — PR #18*
`.csproj` — Switched to `AnyCPU` with `<Prefer32Bit>false</Prefer32Bit>` for broader compatibility.

**~~5.5 — Tray icon loaded at 32x32 only — blurry at 192 DPI~~** ✅ *Fixed*
`Helpers/IconHelper.cs` — Icons now loaded at 64×64, giving Windows a large source frame to downsample from at high DPI (e.g. 200% scaling).

**~~5.6 — `control.exe /name Microsoft.Sound` deprecated on Windows 11~~** ✅ *Fixed — fix/polish-and-compat + fix/settings-ux-3*
`Tray/TrayService.cs` / `SettingsWindow.xaml.cs` — Now tries `ms-settings:sound` first; falls back to `control.exe /name Microsoft.Sound`. A new "Use classic Sound control panel" toggle in Settings lets users who prefer the legacy panel get it consistently via tray and Settings footer button.

**~~5.7 — No Windows Audio service restart recovery~~** ✅ *Fixed — PR #30*
`Services/AudioService.cs` — `ApplyProfile` catches `COMException` with `HResult == 0x80070424` (Windows Audio service not running) and records VS-027 with a clear error message instead of an unhandled COM exception.

**~~5.8 — RDP hotkey behavior not documented~~** ✅ *Fixed — PR #27*
`README.md` — Known Limitations section added; documents that `RegisterHotKey` in an RDP session acts on the local machine, not the remote.

**~~5.9 — Mixed-DPI multi-monitor window position inaccuracy~~** *(Won't fix — known WPF platform limitation; not something we can solve in app code)*

**~~5.10 — Startup registry entry breaks on exe move or update~~** ✅ *Fixed — fix/reliability*
`Services/StartupService.cs` — `RefreshRegistryPath()` added; called on every launch. Reads the stored registry value, compares it to `Environment.ProcessPath`, and silently calls `Enable()` to update the path if they differ. No user action required after moving the exe.

**~~5.11 — No `app.manifest` with PerMonitorV2 DPI awareness~~** ✅ *Fixed*
`app.manifest` added with `<dpiAwareness>PerMonitorV2</dpiAwareness>`; referenced from `.csproj`.

---

## SECTION 6 — CONFIG & DATA INTEGRITY

**~~6.1 — `ConfigVersion` stored but never read or acted upon~~** ✅ *Fixed*
`Services/ConfigService.cs` — `Migrate()` method added; currently migrates v1 `WindowLeft = -1` sentinel to `null`.

**~~6.2 — `WindowLeft = -1` sentinel conflicts with valid off-screen coordinates~~** ✅ *Fixed*
`Services/ConfigService.cs` — `Migrate()` converts `-1` to `null` on load; `AppConfig` now uses `double?` with `null` as the unset sentinel.

**~~6.3 — `config.json` read without file lock~~** ✅ *Fixed — PR #27*
`Services/ConfigService.cs` — Opened with `FileShare.ReadWrite`; antivirus/backup concurrent reads no longer cause a JSON parse failure or incorrect `IsFirstRun = true` reset.

**~~6.4 — No config backup / last-known-good copy~~** ✅ *Fixed*
`Services/ConfigService.cs` — `Save()` now writes `config.json.bak` before overwriting `config.json`. `Load()` falls back to the backup if the primary is corrupted.

**~~6.5 — Orphaned icon files in `IconsDir` never cleaned up~~** ✅ *Fixed*
`ViewModels/SettingsViewModel.cs` / `ViewModels/ProfileCardViewModel.cs` — `DeleteOrphanedIcon` called on profile delete and when a profile's icon is replaced; only deletes files within `IconsDir`.

**~~6.6 — `ProfileMode` enum serialized as integer~~** ✅ *Fixed*
`Models/DeviceProfile.cs` — `ProfileModeConverter : StringEnumConverter` added; `[JsonConverter(typeof(ProfileModeConverter))]` applied to the `ProfileMode` enum.

**~~6.7 — `HotkeyDefinition` always serialized even when empty~~** ✅ *Fixed — fix/polish-and-compat*
`Models/DeviceProfile.cs` — `ShouldSerializeHotkey()` added; Newtonsoft.Json suppresses the `Hotkey` block in JSON when `VirtualKeyCode == 0`.

**~~6.8 — No maximum profile name length validation~~** ✅ *Fixed — PR #16*
`SettingsWindow.xaml` — `MaxLength="20"` added to the Name TextBox (20 characters fits comfortably in the tray right-click menu).

---

## SECTION 7 — TESTING

**~~7.1 — No test project exists~~** *(High)* ✅ Fixed — PR #33
~~Zero automated coverage.~~

**~~7.2 — Suggested unit tests for `ConfigService`~~** ✅ Done — PR #33
- `Load()` with valid JSON returns correct `AppConfig`
- `Load()` with invalid JSON sets `IsFirstRun = true` and returns defaults
- `Load()` with missing file sets `IsFirstRun = true`
- `Load()` with `"Profiles": null` does not crash
- `Load()` with corrupted `.json` but valid `.bak` → recovers from backup, does not set `IsFirstRun`
- `Save()` writes valid JSON that is re-readable by `Load()`
- `Save()` atomic: simulate kill after `.tmp` write; verify config recoverable
- `Migrate()` converts `WindowLeft == -1` to `null` and leaves other fields unchanged
- `Load()` with a concurrent reader open (FileShare.ReadWrite) → does not throw

**~~7.3 — Suggested unit tests for `HotkeyDefinition`~~** ✅ Done — PR #33
- `GetModifierFlags()` returns correct bitmask for each modifier combination
- `ToDisplayString()` returns "(none)" for empty definition
- `IsEmpty` returns true when `VirtualKeyCode == 0`

**~~7.4 — Suggested unit tests for `StartupService`~~** ✅ Done — PR #35
4 integration tests (Enable/Disable round-trip, idempotency) with safe HKCU registry save/restore in IDisposable.

**~~7.5 — SettingsViewModel and ProfileCardViewModel unit tests~~** ✅ Done — PR #36
18 tests using fake service stubs: `SettingsViewModelTests` (7 tests — AddProfile, DeleteProfile, StartWithWindows toggle, hotkey re-registration, each with `_profilesChangedCount` assertion) and `ProfileCardViewModelTests` (11 tests — CaptureHotkey cancel/clear/conflict/success/replace-existing, BrowseIcon cancel/copy-success/copy-failure/same-path-skip, DeleteProfile confirm/cancel). `SettingsViewModel.LoadDevicesAsync` null-guarded for headless test environments. AudioService integration tests against real hardware remain genuinely deferred — no planned branch.

**~~7.6 — Suggested unit tests for `HotkeyService`~~** ✅ Done (partial) — PR #35
7 tests covering early-return paths (empty/invalid hotkeys, unknown atom lookup, idempotent unregister). Full registration tests require a real Win32 HWND and are deferred.

**~~7.7 — Suggested unit tests for `IconHelper`~~** ✅ Done — PR #33
- `LoadIcon(null)` returns the default icon (non-null)
- `LoadIcon("nonexistent.ico")` returns the default icon without throwing
- `GetDefaultIcon()` is idempotent
- `ToImageSource()` returns a non-null `ImageSource`

**~~7.8 — Suggested UI automation tests~~** *(Won't fix — requires WinAppDriver infrastructure; 98 unit tests already cover all logic; overkill for a personal tray app)*

**7.9 — Manual test checklist**
- Launch with no config file (first run)
- Launch with corrupted `config.json`
- Launch with another instance already running (single-instance guard)
- Switch profile via tray menu with all devices present
- Switch profile via tray menu with playback device disconnected
- Switch profile via hotkey with device disconnected
- Close settings with "Close to tray" enabled — app stays in tray
- Close settings with "Close to tray" disabled — app exits
- Move window to secondary monitor, save, reopen — position restored
- Resize window, save, reopen — size restored
- Toggle all settings checkboxes and verify persistence after relaunch

**~~7.10 — Mock strategy~~** ✅ Done — PR #35
All five interfaces extracted; each ViewModel receives dependencies via constructor injection. `FakeAudioService`, `FakeConfigService`, `FakeDialogService`, `FakeHotkeyService`, `FakeStartupService` defined in the test project. COM and registry layers remain outside the fake boundary — never mocked at the P/Invoke level.

**~~7.11 — CI/CD pipeline~~** ✅ Done — PR #38
`.github/workflows/ci.yml` and `release.yml` added; CI runs `dotnet build -c Release` and `dotnet test` on every push/PR; release workflow publishes a self-contained win-x64 single-file exe on `v*` tag push.

**~~7.12 — Suggested unit tests for `AppLogger`~~** ✅ Done — PR #33
- Rotation: after writing past 1 MB, `.log` is renamed to `.log.1`, previous `.log.1` to `.log.2`, oldest `.log.2` deleted; new `.log` starts fresh
- Non-fatal: if the log file cannot be opened (locked/missing directory), no exception escapes to the caller
- All three levels (`Info`, `Warning`, `Error`) write the correct level prefix in the output line
- `Error(Exception)` overload captures the exception message and type

**~~7.13 — Suggested unit tests for `SessionErrorTracker`~~** ✅ Done — PR #33
- `Record()` from 10 concurrent threads → `Count == 10` with no corruption or lost entries
- `ErrorAdded` fires exactly once per `Record()` call (not zero, not twice)
- `Errors` property returns an immutable snapshot — modifications to the returned list do not affect the tracker
- `HasErrors` returns false on a fresh tracker; true after first `Record()`

**~~7.14 — Suggested unit tests for `ErrorCode`~~** ✅ Done — PR #33
- `ToCode()` returns `"VS-001"` format for every defined enum value (no gaps in format)
- No two `ErrorCode` values share the same underlying integer (uniqueness assertion across all 28 codes)

**~~7.15 — Suggested unit tests for `DeviceNotificationClient`~~** ✅ Done — PR #33
- Five `Schedule()` calls within 100 ms → `DevicesChanged` fires exactly once after ~500 ms
- A second `Schedule()` call before the 500 ms elapses cancels the first pending task (no double-fire)
- `OnDefaultDeviceChanged` and `OnPropertyValueChanged` are no-ops — calling them does not trigger `DevicesChanged`

**~~7.16 — Additional unit tests identified in deep-dive review~~** ✅ *Fixed — PR #48*
Six new tests: `_loadingDevices` guard (two tests), `ConfigService.Migrate()` asymmetric sentinel, `IconHelper.LoadIcon()` with corrupt data, 10 concurrent `LoadDevicesAsync` calls, and `OnDevicesChanged` from a background thread. 104 tests total.

---

## SECTION 8 — DEPLOYMENT & DISTRIBUTION

*Open items 8.1/C2, 8.2/C3, 8.3/F8, 8.7 are deferred — tracked in Section 11 — Feature Additions / Deferred.*

**~~8.4 — .NET runtime bundling not configured~~** ✅ *Fixed — v1.1.0 release*
Release published with `--self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true` — single 163 MB exe, no .NET install required.

**~~8.5 — No `ApplicationIcon` in csproj~~** ✅ *Fixed — fix/polish-and-compat*
`.csproj` / `Resources/Icons/app.ico` — `<ApplicationIcon>Resources\Icons\app.ico</ApplicationIcon>` added; `app.ico` embedded as a Resource. The exe now shows the VibeSwitcher icon in Explorer, the taskbar, and the Alt+Tab switcher.

**~~8.6 — Version format inconsistency in csproj~~** ✅ *Fixed — PR #16*
`.csproj` — Redundant `<AssemblyVersion>` and `<FileVersion>` removed; MSBuild derives both from `<Version>` automatically.

**8.8 — Microsoft Store is not feasible** *(Info)*
Undocumented `IPolicyConfig` COM API is not available to sandboxed Store apps.

**~~8.9 — No SHA256 checksums~~** ✅ *Fixed — PR #46*
`release.yml` now generates `sha256sums.txt` (PowerShell 7, BOM-free UTF-8, two-space format) and attaches it alongside the zip as a GitHub Release asset.

---

## SECTION 9 — LOGGING & DIAGNOSTICS

**~~9.1 — Log file grows without bound~~** ✅ *Fixed — issue #5*
`Helpers/AppLogger.cs` — Rotation implemented: rotates at 1 MB, keeps 2 backups (`.1`, `.2`).

**~~9.2 — Only `Error` level exists~~** ✅ *Fixed — issue #5*
`Helpers/AppLogger.cs` — `Info`, `Warning`, and `Error` overloads all present.

**~~9.3 — Duplicate log implementation~~** ✅ *Fixed*
`Services/ConfigService.cs` — `ConfigService.LogError` removed; all logging routes through `AppLogger`.

**~~9.4 — No structured (machine-parseable) logging~~** *(Won't fix — existing AppLogger with rotation and levels is sufficient; Serilog adds dependency complexity for negligible gain)*

**~~9.5 — No Windows Event Viewer integration~~** *(Won't fix — enterprise-grade tooling; not relevant for a personal tray app)*

**~~9.6 — No crash dump generation~~** *(Won't fix — existing logging captures call stacks; MiniDumpWriteDump is complex P/Invoke for marginal benefit)*

**~~9.7 — No opt-in crash reporting~~** *(Won't fix — requires third-party service and user consent infrastructure; out of scope)*

**~~9.8 — No diagnostic report feature~~** ✅ *Fixed — fix/polish-and-compat*
`Views/AboutWindow.xaml.cs` — "Copy Diagnostic Info" button added to About window footer. Copies OS version, .NET version, profile count, session error count, and log file path to clipboard; shows "Copied!" feedback for 2 seconds.

---

## SECTION 10 — DOCUMENTATION

**~~10.1 — No README.md~~** — ✅ *Done*

**~~10.2 — No CHANGELOG.md~~** ✅ *Done — 2026-05-17*
`CHANGELOG.md` created covering all changes from v1.0.0 through current fixes.

**~~10.4 — No developer docs / CONTRIBUTING.md~~** ✅ *Fixed — fix/polish-and-compat*
`CONTRIBUTING.md` created at repo root: prerequisites (.NET 8 SDK, Visual Studio or Rider), getting started steps, project layout table, coding conventions, PR process, and bug reporting guidance.

**~~10.5 — No LICENSE file~~** — ✅ *Done*

**~~10.6 — GitHub URLs in About window are placeholders~~** — ✅ *Done*

**~~10.7 — No build instructions documented~~** ✅ *Fixed — fix/polish-and-compat*
`README.md` expanded with "Building from source" section: `dotnet restore`, `dotnet run`, and `dotnet publish --self-contained` commands documented; link added to `CONTRIBUTING.md`.

*Open items 10.3 → F19 (Branch 31) and 10.8 (Deferred) are tracked in Section 11 — Feature Additions.*

---

## SECTION 11 — FINAL PRIORITIZED LIST

### CRITICAL — Must fix before any release

| # | Issue | Location |
|---|-------|----------|
| ~~C1~~ | UI thread freeze during Settings open — **fixed** | ✅ Done |
| ~~C2~~ | ~~No installer / distribution mechanism~~ | ✅ Done — PR #110 |
| C3 | No code signing (SmartScreen blocks app on every first run) | Build pipeline |
| ~~C4~~ | ~~No LICENSE file~~ | ✅ Done |
| ~~C5~~ | ~~No README.md~~ | ✅ Done |
| ~~C6~~ | ~~`async void` tray click handler crashes process on unhandled exception~~ | ✅ Done — issue #1 |
| ~~C7~~ | ~~`ConfigService.Save` not atomic — use `File.Move`~~ | ✅ Done — issue #2 |
| ~~C8~~ | ~~GitHub URLs in About window are dead~~ | ✅ Done |
| ~~C9~~ | ~~Explorer crash permanently removes tray icon~~ | ✅ Done — issue #3 |

### HIGH — Important before v1.0.0

| # | Issue | Location |
|---|-------|----------|
| ~~H1~~ | ~~Device lists enumerated N x 2 times per Settings open (no caching)~~ | ✅ Done |
| ~~H2~~ | ~~`StartupService.Enable` silently fails when registry key is null~~ | ✅ Done — issue #8 |
| ~~H3~~ | ~~`AppConfig.Profiles` null-guard missing after deserialization~~ | ✅ Done — issue #4 |
| ~~H4~~ | ~~Tray icon blurry at high DPI (loaded at 32x32 only)~~ | ✅ Done |
| ~~H5~~ | ~~`BitmapSource` from HICON not frozen before icon disposal~~ | ✅ Done |
| ~~H6~~ | ~~No `app.manifest` with PerMonitorV2 DPI awareness~~ | ✅ Done |
| ~~H7~~ | ~~Only first hotkey conflict reported; all hotkeys lost after conflict~~ | ✅ Done — issue #13 |
| ~~H8~~ | ~~Device hotplug/unplug not reflected in open Settings~~ | ✅ Done — PR #30 |
| ~~H9~~ | ~~No `ApplicationIcon` in csproj — exe has no icon in Explorer~~ | ✅ Done — fix/polish-and-compat |
| ~~H10~~ | ~~`RunOnSta` creates/destroys a new OS thread per audio call~~ | ✅ Done — fix/reliability |

### MEDIUM — Should fix for a quality release

| # | Issue |
|---|-------|
| ~~M1~~ | ~~Dark mode not supported~~ | Moved to Feature Additions (F16) |
| ~~M2~~ | ~~`RebuildMenu` does disk I/O (icon file read) on every profile switch~~ | ✅ Done |
| ~~M3~~ | ~~Log file has no rotation — grows indefinitely~~ | ✅ Done — PR #16 |
| ~~M4~~ | ~~Only Error log level — no Info/Warning~~ | ✅ Done — PR #16 |
| ~~M5~~ | ~~`PropVariant` struct size declared at 16 bytes; x64 PROPVARIANT is 24~~ | ✅ Done — PR #27 |
| ~~M6~~ | ~~MVVM violation — ViewModels directly instantiate View dialogs~~ | ✅ Done — PR #35 |
| ~~M7~~ | ~~Button/control styles not in `App.xaml`~~ | ✅ Done — fix/settings-ux-3 |
| ~~M8~~ | ~~Double `Task.Run` wrapping in `SwitchToProfile`~~ | ✅ Done — PR #19 |
| ~~M9~~ | ~~Active profile not re-applied after system sleep/resume~~ | ✅ Done — PR #18 |
| ~~M10~~ | ~~Single-instance mutex is global — blocks other users on multi-session machines~~ | ✅ Done — PR #18 |
| ~~M11~~ | ~~No profile reorder UI (SortOrder never updated via UI)~~ | Feature — see F2 |
| ~~M12~~ | ~~`control.exe` Sound panel deprecated on Windows 11~~ | ✅ Done — fix/polish-and-compat + fix/settings-ux-3 |
| ~~M13~~ | ~~`ProfileMode` enum serialized as integer (fragile to reordering)~~ | ✅ Done — PR #16 |
| ~~M14~~ | ~~`ConfigVersion` stored but no migration code exists~~ | ✅ Done — PR #16 |
| ~~M15~~ | ~~`DispatcherUnhandledException` swallows all exceptions unconditionally~~ | ✅ Done — PR #14 |
| ~~M16~~ | ~~Duplicate profile-switch logic in `TrayService` and `ProfileSwitchOrchestrator`~~ | ✅ Done — PR #40 |
| ~~M17~~ | ~~No concurrent-switch guard — hotkey spam causes overlapping `ApplyProfileAsync` calls~~ | ✅ Done — PR #40 |
| ~~M18~~ | ~~`SettingsWindow` `ErrorAdded` handler survives hide~~ | ✅ Done — PR #42 |
| ~~M19~~ | ~~`LoadDevicesAsync` not cancellable~~ | ✅ Done — PR #42 |

### LOW — Nice to fix before or after release

| # | Issue | Location |
|---|-------|----------|
| ~~L1~~ | ~~`HotkeyDefinition` uses inline modifier literals instead of `WinApi.MOD_*`~~ | ✅ Done — PR #13 |
| ~~L2~~ | ~~`BuildSectionLabel` dead code method~~ | ✅ Done |
| ~~L3~~ | ~~`HotkeyService.Refresh` trivial alias adds no value~~ | ✅ Done — PR #13 |
| ~~L4~~ | ~~`ConfigService.LogError` duplicates `AppLogger`~~ | ✅ Done |
| ~~L5~~ | ~~`WindowLeft = -1` sentinel conflicts with valid off-screen coordinates~~ | ✅ Done — PR #18 |
| ~~L6~~ | ~~Icon files in IconsDir never cleaned up on profile delete~~ | ✅ Done — PR #16 |
| ~~L7~~ | ~~No max profile name length validation~~ | ✅ Done — PR #16 |
| ~~L8~~ | ~~Hotkey capture dialog does not show held modifiers before key press~~ | ✅ Done — PR #17 |
| ~~L9~~ | ~~No empty-state UI when profile list is empty~~ | ✅ Done — PR #17 |
| ~~L10~~ | ~~`MessageBox.Show` for hotkey conflict is visually inconsistent~~ | ✅ Done — PR #17 |
| ~~L11~~ | ~~COM `store` object not released in `finally` in `GetDeviceInfo`~~ | ✅ Done — PR #19 |
| ~~L12~~ | ~~`_defaultIcon` static never disposed~~ | ✅ Done — PR #19 |
| ~~L13~~ | ~~Misleading comment on `UnhandledException` handler~~ | ✅ Done — PR #14 |
| ~~L14~~ | ~~Profile name TextBox uses LostFocus — name lost if window closed immediately~~ | ✅ Done — PR #17 |
| ~~L15~~ | ~~Tab order and keyboard navigation not explicitly set~~ | ✅ Done — PR #28 |
| ~~L16~~ | ~~`WinApi.GlobalAddAtom` type mismatch (int vs ushort ATOM)~~ | ✅ Done — PR #13 |
| ~~L17~~ | ~~Dark mode / high-contrast mode not supported — see F16 (same feature)~~ | Feature — see F16 |
| ~~L18~~ | ~~`AboutWindow` version falls back to hardcoded "1.0.0"~~ | ✅ Done — PR #16 |
| ~~L19~~ | ~~`StartupService.Enable` uses `OpenSubKey` instead of `CreateSubKey`~~ | ✅ Done — PR #14 |
| ~~L20~~ | ~~No SHA256 checksums published with binaries~~ | ✅ Done — PR #46 |
| ~~L21~~ | ~~`AudioService.IsDeviceActive()` bare `catch` swallows all exceptions — see 1.23~~ | ✅ Done — PR #44 |
| ~~L22~~ | ~~`ErrorDialog` shown without `Owner` in `TrayService` — see 1.24~~ | ✅ Done — PR #44 |
| ~~L23~~ | ~~`TrayService` reads `ActiveProfileId` without null guard — see 1.25~~ | ✅ Done — PR #44 |

### TECHNICAL DEBT

| # | Item |
|---|------|
| ~~TD1~~ | ~~No test project — zero automated test coverage~~ | ✅ Done — PR #33 |
| ~~TD2~~ | ~~No `IAudioService` / `IConfigService` interfaces preventing testability~~ | ✅ Done — PR #35 |
| ~~TD3~~ | ~~`App.xaml.cs` has too many responsibilities (God Class)~~ | ✅ Done — PR #37 |
| ~~TD4~~ | ~~No `IDialogService` abstraction — ViewModel/View tightly coupled~~ | ✅ Done — PR #35 |
| ~~TD5~~ | ~~Two separate duplicate log implementations~~ | ✅ Done — Branch 3 (ConfigService.LogError removed; items 1.14 + 9.3) |
| ~~TD6~~ | ~~`RunOnSta` pattern creates/destroys OS threads per operation~~ | ✅ Done — fix/reliability (RunOnSta removed; calls use Task.Run at call sites) |
| ~~TD7~~ | ~~No CI/CD pipeline configured~~ | ✅ Done — PR #38 |

### REFACTORING OPPORTUNITIES

| # | Opportunity |
|---|-------------|
| ~~R1~~ | ~~Move all button/control styles to `App.xaml`~~ | ✅ Done — fix/settings-ux-3 |
| ~~R2~~ | ~~Extract `ProfileSwitchOrchestrator` from `App.xaml.cs`~~ | ✅ Done — PR #37 |
| ~~R3~~ | ~~Replace `RunOnSta` with a persistent STA pump thread in `AudioService`~~ | ✅ Done (partially — RunOnSta removed; using Task.Run at call sites) — fix/reliability |
| ~~R4~~ | ~~Share one device enumeration result across all profile cards~~ | ✅ Done — Branch 5 (LoadDevicesAsync enumerates once, passes result to all cards) |
| ~~R5~~ | ~~Incremental `ContextMenu` update instead of full rebuild on every switch~~ | ✅ Done — PR #27 (SetActiveProfile flips IsChecked only; RebuildMenu called only on config changes) |
| ~~R6~~ | ~~Replace Newtonsoft.Json with `System.Text.Json` (built-in, faster, no NuGet dependency)~~ | ✅ Done — PR #29 |

### FEATURE ADDITIONS (post-v1.0.0)

| # | Feature |
|---|---------|
| ~~F1~~ | ~~Import/export `config.json` via Settings for backup and sharing~~ | ✅ Done — PR #68 |
| ~~F2~~ | ~~Drag-and-drop profile reorder~~ | ✅ Done — PR #59 |
| ~~F3~~ | ~~"Test sound" button to verify active device plays audio~~ | ✅ Done — PR #61 |
| ~~F4~~ | ~~Middle-click tray to toggle between last two profiles~~ | Removed |
| ~~F5~~ | ~~Hotkey cheat sheet in tray tooltip — hover text shows all profiles with their assigned hotkeys (e.g. "Desktop Setup: Ctrl+PgUp / Gaming: Ctrl+PgDn") so you can see the full list without opening any window~~ | ✅ Done — PR #57 |
| ~~F6~~ | ~~Re-apply active profile on system resume from sleep/hibernate~~ | ✅ Done — PR #18 |
| ~~F7~~ | ~~`IMMNotificationClient` for real-time device plug/unplug in Settings~~ | ✅ Done — PR #30 |
| F8 | Auto-updater with GitHub Releases version check |
| ~~F9~~ | ~~Windows 11 Toast notifications — replace the current balloon tip with the modern Windows 10/11 Toast API so notifications persist in Action Center, support richer formatting, and stack/dismiss properly~~ | Dropped — blocked by Windows App SDK tooling |
| ~~F10~~ | ~~Per-profile volume level~~ | Dropped — user prefers Windows tray |
| ~~F11~~ | ~~Profile scheduler (e.g., work headset 9-5, speakers evenings)~~ | ✅ Done — PR #74 |
| ~~F12~~ | ~~Command-line interface: `VibeSwitcher.exe --switch "Profile Name"`~~ | Removed |
| F13 | Portable mode — if a file named `portable.txt` exists next to the exe, config is stored in the same folder instead of `%APPDATA%`; no CLI needed, just drop the file there once and the app auto-detects it on every launch; useful for USB/portable installs |
| ~~F14~~ | ~~System tray scroll wheel for volume control~~ | Removed |
| ~~F15~~ | ~~Diagnostic report copy-to-clipboard in About window~~ | ✅ Done — fix/polish-and-compat |
| ~~F16~~ | ~~Light / dark support — themed resource dictionaries so the app follows the Windows OS theme automatically, with an in-app Auto / Light / Dark override in General Settings; covers all windows, dialogs, profile cards, tray menu, and icon chip backgrounds~~ | ✅ Done — PR #70 |
| ~~F17~~ | ~~Built-in profile icons — emoji gallery picker with Black/White color toggle; renders 64×64 PNG-embedded ICO files; Browse button stays alongside gallery picker~~ | ✅ Done — PR #66 |
| ~~F18~~ | ~~Field feedback — green border flash when a field change is saved; inline validation message for invalid input~~ | ✅ Done — PR #68 |
| ~~F19~~ | ~~In-app help — "?" button in Settings opens a getting-started walkthrough dialog~~ | ✅ Done — PR #68 |
| ~~F20~~ | ~~Pre-made profile names — name suggestion chips appear while the name is still the auto-assigned "Profile N"; picking a chip sets the name and silently applies the matching gallery icon~~ | ✅ Done — PR #66 |
| ~~F21~~ | ~~Left-click tray cycles profiles — left-clicking the tray icon switches to the next profile in sort order, wrapping from last back to first; right-click still opens the context menu as normal~~ | ✅ Done — PR #57 |
| F22 | Expand-to-fit button in Settings — small toggle button (↗↙ diagonal arrows) in the top-right corner of the Settings window; click expands the window height to show all profile cards and the Add New Profile button without a scrollbar, capped at screen height; click again collapses back to the default compact size |
| ~~F23~~ | ~~Profile clone button — a duplicate icon next to each profile card's delete button; clones the name (with " (copy)" suffix) and device selections into a new profile appended to the list; the hotkey and icon path are not copied (avoids conflicts and file-sharing issues)~~ | ✅ Done — PR #59 |
| ~~F24~~ | ~~Global hotkey to open Settings — user-configurable key combo set in Settings (like any other hotkey), with an option to disable it entirely; focuses the Settings window from anywhere~~ | ✅ Done — PR #57 |
| ~~F25~~ | ~~Per-profile silent switch — a checkbox in each profile card ("Silent — no notification"); when enabled, switching to that profile skips the balloon tip entirely~~ | ✅ Done — PR #59 |
| ~~F26~~ | ~~Device connectivity indicator — green/red dot next to each device in Settings dropdowns~~ | ✅ Done — PR #61 |
| ~~F27~~ | ~~Profile color tag~~ | Dropped — profiles already have icons; color adds no value |
| ~~F30~~ | ~~Tray icon switch flash — briefly pulses the tray icon when a profile switch completes; provides visual confirmation of the switch, especially useful when balloon notifications are disabled via F25~~ | ✅ Done — PR #57 |
| ~~F31~~ | ~~Audio endpoint aliases — user-defined friendly name per device shown in Settings dropdowns instead of the raw Windows device name (e.g. "GoXLR", "Desk Speakers"); stored as a `{deviceId → alias}` dictionary in config~~ | ✅ Done — PR #80 |
| ~~F32~~ | ~~Profile notes — optional short description field on each profile card (e.g. "For work meetings — webcam mic disabled"); stored per profile, shown below the profile name~~ | ✅ Done — PR #76 |
| ~~F33~~ | ~~Favorite / pinned profiles — star flag per profile; pinned profiles appear at the top of the tray right-click menu above unpinned ones for quick access when the list grows long~~ | ✅ Done — PR #76 |
| ~~F34~~ | ~~Profile validation warnings — inline warning flag on cards when a hotkey is duplicated across profiles, a selected device is missing or disabled, or an icon path is invalid; surfaces silent failures before they cause confusion~~ | ✅ Done — PR #76 |
| ~~F35~~ | ~~Search / filter in Settings — text box at the top of the profile list; filters cards in real time by profile name, device name, hotkey, mode (Playback/Recording/Both), pinned status, schedule presence, or schedule day-of-week; clears on Escape~~ | ✅ Done — PR #82 |
| ~~F36~~ | ~~Optional switch sound — global default sound that plays on every profile switch, with optional per-profile override; choose from pre-made built-in tones or a custom .wav file; adjustable volume (0–100%) at both the global and per-profile level; per-profile silent toggle to disable entirely; pairs with F25 (silent switch) and F30 (icon flash)~~ | ✅ Done — PR #84 |
| ~~F37~~ | ~~Deafen / panic hotkey — global configurable hotkey that instantly mutes system-wide; configurable scope: recording devices only (mic mute), playback devices only (deafen), or both; distinct activate and deactivate sounds (pre-made tones or custom .wav); tray icon flashes red while active; pressing again unmutes and restores previous levels~~ | ✅ Done — PR #86 |
| ~~F38~~ | ~~Temporary / transient profile switch — optional app-wide feature with a configurable keybind; switches to a profile temporarily and auto-reverts to the previous profile when a timer expires or a linked app closes; useful for quick calls without forgetting to switch back~~ | Dropped — not useful enough to prioritize |
| ~~F39~~ | ~~Auto-switch on device connect — link a specific audio device endpoint to a profile; when that device appears (Bluetooth pair, USB plug-in) VibeSwitcher automatically activates the linked profile; per-device toggle to enable or disable~~ | ✅ Done — PR #90 |
| ~~F40~~ | ~~Monitor / dock awareness — trigger a profile switch when a specific display or dock connects or disconnects (HDMI, USB-C, Thunderbolt); designed for hybrid work setups where undocking a laptop should switch to built-in speakers automatically~~ | Dropped — too broad to scope |
| ~~F41~~ | ~~App-aware auto-switching — link an executable to a profile; VibeSwitcher switches automatically when that process launches or gains focus and reverts to the previous profile when the app closes; per-rule toggle to enable or disable~~ | ✅ Done — PR #96 |
| ~~F42~~ | ~~Settings sub-card layout — each settings group (Startup, Notifications, Shortcuts) gets its own inner card within the General Settings card for clearer visual grouping; part of planned UI/UX redesign~~ | ✅ Done — PR #68 |
| ~~F43~~ | ~~Card-based enable/disable — settings cards that support toggling (e.g. Shortcuts hotkey) use card-level visual state (full-opacity "live" vs. dimmed "off") instead of per-row pill toggles; clicking the card or its toggle fades the whole card; part of planned UI/UX redesign~~ | ✅ Done — PR #68 |
| ~~F44~~ | ~~Compact / mini Settings window — condensed view that shrinks the window to a minimal layout for users with many profiles; full window restores on demand~~ | ✅ Done — PR #108 |
| ~~F45~~ | ~~Light theme support for the redesigned UI — theme-aware resource keys for profile cards, action strips, overlays, and icon frames so the G HUB-inspired redesign adapts to light/dark~~ | ✅ Done — PR #106 |
| F46 | macOS + Linux support — one shared cross-platform app (Avalonia, `xplat/`) with the same function, behaviour and design as Windows; Windows app unchanged. Plan: 11 phases (foundation, Linux audio, macOS audio, OS services, hotkeys, triggers/HID, UI foundation, packaging, Settings UI, dialogs, app wiring) then two friend test sessions | 🚧 In progress — phases 1–8 and 9 parts 1–5 done (PR #116, #118, #120, #122, #124, #126, #128, #130, #132, #134, #136, #138, #140, #142, #144, #146, #148, #150, #152, #154, #156, #158) |

---

## SUMMARY COUNT

| Category | Total | Fixed | Remaining |
|----------|-------|-------|-----------|
| Critical | 9 | 8 | 1 (C3) |
| High | 10 | 10 | 0 |
| Medium | 19 | 17 | 0 |
| Low | 23 | 22 | 0 |
| Technical Debt | 7 | 7 | 0 |
| Refactoring Opportunities | 6 | 6 | 0 |
| Feature Additions | 44 | 32 | 4 |
| **Total** | **118** | **102** | **5** |

*Totals count every tracked item. Remaining open feature additions are F8, F13, F22, and F46 (macOS + Linux, in progress). The 11-item difference between Total and Fixed + Remaining is reclassified work: M1 → F16, M11 → F2, and L17 → F16 (moved to Feature Additions); F4, F12, F14 (removed); and F10, F27, F9, F38, F40 (dropped).*

---

*Remaining before any public release: C3 (code signing — dropped, no certificate) and F8 (auto-updater — next up now that the installer exists).*

---

## SECTION 12 — BRANCH EXECUTION PLAN

This section captures the agreed grouping of remaining work into branches so it is not lost between sessions.

**All remaining work is tracked in BACKLOG.md — Planned Branches (Branches 28–43, in execution order).**

**Deferred (no branch):** F8/8.3 (auto-updater — unblocked by PR #110, planned next), 8.7 (distribution and discovery — post-v1.0), 10.8 (website).
**Dropped:** C3/8.2 (code signing — needs certificate), F9 (WinRT toast — blocked by Windows App SDK tooling), Mica/Acrylic (UI/UX redesign phase).

*(M16, M17 resolved PR #40; M18, M19 resolved PR #42; L21, L22, L23 resolved PR #44; L20/8.9 resolved PR #46; 7.16 resolved PR #48)*

---

### ~~Branch 1: `fix/hotkey-reliability`~~ ✅ Merged — PR #13
**Theme:** Hotkeys silently failing, conflict dropping all registrations, inconsistent constant definitions.

| Item | Description | Status |
|------|-------------|--------|
| H7 | First conflict drops ALL hotkeys including non-conflicting ones | ✅ Done |
| 1.19 | Empty catch in `RegisterProfile` swallows `HotkeyConflictException` silently | ✅ Done |
| 1.20 | No `VirtualKeyCode` range validation before `RegisterHotKey` | ✅ Done |
| L1 / 3.7 | `HotkeyDefinition` hardcodes modifier literals — reference `WinApi.MOD_*` | ✅ Done |
| L3 | `HotkeyService.Refresh` is a trivial alias for `RegisterAll` — remove | ✅ Done |
| L16 | `GlobalAddAtom` declared as `int` instead of correct `ushort` ATOM | ✅ Done |
| 3.15 | Blanket `catch` in `HotkeyDefinition.ToDisplayString` — method never throws, remove catch | ✅ Done |

---

### ~~Branch 2: `fix/startup-and-exceptions`~~ ✅ Merged — PR #14
**Theme:** Silent failures at startup, swallowed exceptions, misleading error messages.

| Item | Description | Status |
|------|-------------|--------|
| H2 / L19 | `StartupService.Enable` uses `OpenSubKey` (null if absent) — "Start with Windows" silently never writes | ✅ Done |
| 3.2 | `StartWithWindows` checkbox reads from JSON not actual registry — can show wrong state | ✅ Done |
| M15 / 1.22 | `DispatcherUnhandledException` swallows everything — only recover what is recoverable | ✅ Done |
| 1.4 | Add `TaskScheduler.UnobservedTaskException` handler; fix misleading comment | ✅ Done |
| 1.5 | Error balloon shows outer exception message — prefer `InnerException?.Message` | ✅ Done |
| L13 | Misleading comment on `UnhandledException` handler | ✅ Done |
| 3.13 | `ConfigService._config` not `volatile` | ✅ Done |

*Also completed in this branch: M9 (sleep/resume re-apply), M8 (double Task.Run), 2.8 (O(1) hotkey dispatch).*

---

### ~~Branch 3: `fix/config-data-integrity`~~ ✅ Merged — PR #16
**Theme:** How config is read, written, stored and validated — serialization, orphaned files, log hygiene.

| Item | Description | Status |
|------|-------------|--------|
| M13 / 6.6 | `ProfileMode` enum serializes as integer — add `StringEnumConverter` | ✅ Done |
| M14 / 6.1 | `ConfigVersion` stored but no migration code — add v1 scaffold | ✅ Done |
| 6.3 | Config read without file lock — antivirus/backup can cause partial read → wrong `IsFirstRun` | Open (deferred) |
| 6.4 | No backup copy before overwrite — write `config.json.bak` | ✅ Done |
| L6 / 6.5 | Orphaned icon files never deleted when profile is deleted or icon changed | ✅ Done |
| L7 / 6.8 | No max profile name length — `MaxLength="20"` in TextBox | ✅ Done |
| 1.9 | `BrowseIcon` silently falls back on `File.Copy` failure — log via `AppLogger` | ✅ Done |
| 1.1 | Icon path not validated against `IconsDir` — canonicalize and assert prefix | Partial (canonicalize done; prefix assertion deferred) |
| M3 / 9.1 | Log file has no rotation — rotate at 1 MB, keep 2 backups | ✅ Done |
| M4 / 9.2 | Only `Error` log level — add `Info` and `Warning` overloads | ✅ Done |
| L18 | `AboutWindow` version fallback hardcoded — use `AssemblyInformationalVersionAttribute` | ✅ Done |
| 8.6 | `Version` and `AssemblyVersion` in csproj differ — derive both from one property | ✅ Done |

---

### ~~Branch 4: `fix/dpi-and-windows`~~ ✅ Merged — PR #18
**Theme:** How the app looks and behaves at high DPI, multi-monitor, multi-session, and after sleep.

| Item | Description | Status |
|------|-------------|--------|
| H6 | No `app.manifest` with PerMonitorV2 — WPF blurry on secondary monitors | ✅ Done |
| H4 | Tray icon loaded at 32×32 only — blurry at 192 DPI | ✅ Done |
| M9 | Active profile not re-applied after sleep/hibernate | ✅ Done |
| M10 | Single-instance mutex is global — blocks other users on multi-session machines | ✅ Done |
| L5 / 6.2 | `WindowLeft = -1` sentinel conflicts with valid off-screen coordinates | ✅ Done |
| 4.17 | Window restore position not clamped to screen boundary | ✅ Done |
| 5.4 | x64-only build — switch to `AnyCPU` with `Prefer32Bit=false` | ✅ Done |

---

### ~~Branch 5: `fix/tray-and-performance`~~ ✅ Merged — PR #19
**Theme:** Performance and memory correctness of the tray menu and Settings window.

| Item | Description | Status |
|------|-------------|--------|
| H1 / 2.1 / 4.1 | Device enumeration blocks UI thread — enumerate once async in `SettingsViewModel`, share across cards | ✅ Done |
| M2 / 2.3 | `RebuildMenu` reads icon file from disk on every profile switch — cache `ImageSource` per profile | ✅ Done |
| 1.12 | Old `ContextMenu` objects abandoned on every rebuild — repopulate existing instead | ✅ Done |
| M8 / 2.5 | Double `Task.Run` wrapping in `SwitchToProfile` | ✅ Done |
| 2.6 | `ObservableCollection` fires `CollectionChanged` per item during load | ✅ Done |
| 2.8 | Linear LINQ scan on every `WM_HOTKEY` — use `Dictionary<Guid, DeviceProfile>` | ✅ Done |
| L11 / 3.6 | COM `store` object not released in `finally` in `GetDeviceInfo` | ✅ Done |
| L12 / 1.11 | `_defaultIcon` static never disposed — register `AppDomain.ProcessExit` cleanup | ✅ Done |
| M5 / 1.15 | `PropVariant` struct declared at 16 bytes; x64 PROPVARIANT is 24 — add padding | Open (deferred) |

---

### ~~Branch 6: `fix/settings-ux`~~ ✅ Merged — PR #17
**Theme:** Small UX improvements to the Settings window — all visible to the user on daily use.

| Item | Description | Status |
|------|-------------|--------|
| L14 / 4.3 | Profile name lost if window closed immediately after typing — use `PropertyChanged` | ✅ Done |
| L9 / 4.4 | No "no profiles yet" message when list is empty | ✅ Done |
| L8 / 4.10 | Hotkey capture dialog doesn't show held modifiers before final key press | ✅ Done |
| L10 / 4.5 | Hotkey conflict uses plain Windows `MessageBox` — visually inconsistent | ✅ Done |
| 4.16 | Delete dialog Cancel button should be `IsDefault` so Enter = cancel, not delete | ✅ Done |

---

### ~~Branch 7: `fix/polish-and-compat`~~ ✅ Merged
**Theme:** Small isolated fixes, diagnostics, and docs — all low-risk and self-contained.

| Item | Description | Status |
|------|-------------|--------|
| 1.1 | Assert icon path starts with `ConfigService.IconsDir` after canonicalization | ✅ Done |
| 4.12 | Escape key closes the icon info popup in Settings | ✅ Done |
| 5.6 | Try `ms-settings:sound` before falling back to `control.exe` | ✅ Done |
| 6.7 | Skip serializing `HotkeyDefinition` when `VirtualKeyCode == 0` | ✅ Done |
| H9 / 8.5 | Add `<ApplicationIcon>` to csproj so exe shows app icon in Explorer | ✅ Done |
| 9.8 | "Copy Diagnostic Info" button in About window (OS, .NET, device count, log path) | ✅ Done |
| 10.4 | Create `CONTRIBUTING.md` with architecture overview and build instructions | ✅ Done |
| 10.7 | Add build prerequisites and `dotnet publish` command to `README.md` | ✅ Done |

---

### ~~Branch 8: `fix/reliability`~~ ✅ Merged
**Theme:** Correctness fixes — addresses real failure paths.

| Item | Description | Status |
|------|-------------|--------|
| 2.4 | Remove `RunOnSta` — `EnumerateDevices` called directly from `Task.Run`; `ApplyProfileAsync` uses `Task.Run` at call site | ✅ Done |
| 5.10 | `RefreshRegistryPath()` in `StartupService` — silently corrects startup registry path on every launch | ✅ Done |

*Deferred from original plan: 6.3 (config file lock), 1.15 (PropVariant 24 bytes), 1.7 (hotkey thread assertion) — all resolved in PR #27.*

---

### ~~Branch 9: `fix/settings-ux-3`~~ ✅ Merged (PR #26)
**Theme:** Settings visual polish and UX improvements.

| Item | Description | Status |
|------|-------------|--------|
| 3.4 | Move `ActionButton`, `DangerButton`, `PrimaryButton`, `RoundedButtonTemplate` to `Application.Resources` | ✅ Done |
| 3.4+ | Add `ToggleSwitchStyle` for `CheckBox`; global `Window FontFamily="Segoe UI"` | ✅ Done |
| 4.9 | Show hotkey in tray profile header (small gray third line below mode label) | ✅ Done |
| 5.6+ | "Use classic Sound control panel" toggle — tray and Settings button both honour it | ✅ Done |
| New | "Open Sound Settings" button in Settings footer | ✅ Done |
| New | "(None)" sentinel as first option in every device dropdown; fallback for disconnected devices | ✅ Done |
| New | Icon filename: `{sanitized-name}-{8-char-guid}.ico` instead of `{full-guid}.ico` | ✅ Done |
| New | `ErrorDialog` title corrected from 14 pt to 15 pt | ✅ Done |

*Deferred from original plan: 4.2 (switch loading state), 4.6 (keyboard navigation between cards).*

---

### ~~Branch 10: `fix/code-quality`~~ ✅ Merged — PR #27
**Theme:** Small correctness fixes — none touch any visible feature.

| Item | Description | Status |
|------|-------------|--------|
| 1.7 | `Debug.Assert(Dispatcher.CheckAccess())` in `HotkeyService.TestHotkey` | ✅ Done |
| 1.15 / M5 | `PropVariant` struct expanded from 16 → 24 bytes (padding field added) | ✅ Done |
| 3.5 | `ProfileCardViewModel` implements `IDisposable`; `DeleteProfile` calls `Dispose` | ✅ Done |
| 6.3 | `config.json` opened with `FileShare.ReadWrite` — antivirus/backup reads no longer corrupt parse | ✅ Done |
| R5 | `SetActiveProfile` flips `IsChecked` only; `RebuildMenu` no longer called on plain switch | ✅ Done |
| 5.8 | README: Known Limitations section documents RDP hotkey behaviour | ✅ Done |

---

### ~~Branch 11: `fix/ux-polish`~~ ✅ Merged — PR #28
**Theme:** Switching feedback and keyboard navigation improvements.

| Item | Description | Status |
|------|-------------|--------|
| 4.2 | `"Switching to {name}..."` tooltip during async switch; restores correctly on failure | ✅ Done |
| L15 / 4.6 | `DirectionalNavigation="Contained"` on profile `ItemsControl`; `IsTabStop="False"` on read-only fields | ✅ Done |
| — | Device selection reverting to (None) after relaunch — `_loadingDevices` guard in `ProfileCardViewModel` | ✅ Done |

---

### ~~Branch 12: `refactor/system-text-json`~~ ✅ Merged — PR #29
**Theme:** Remove the Newtonsoft.Json NuGet dependency; replace with the built-in System.Text.Json.

| Item | Description | Status |
|------|-------------|--------|
| R6 | Replace `JsonConvert.DeserializeObject`/`SerializeObject` with `JsonSerializer`; rewrite `ProfileModeConverter` as `JsonConverter<ProfileMode>`; remove `ShouldSerializeHotkey()` (Newtonsoft-only convention); add `PropertyNameCaseInsensitive = true` to match Newtonsoft defaults; remove package reference | ✅ Done |

---

### ~~Branch 13: `feat/audio-reliability`~~ ✅ Merged — PR #30
**Theme:** Live device refresh on plug/unplug and Windows Audio service failure detection.

| Item | Description | Status |
|------|-------------|--------|
| H8 / 5.3 | `IMMNotificationClient` — debounces device add/remove/state-change events into a single `DevicesChanged` event after 500 ms; `SettingsViewModel` subscribes and refreshes dropdowns without requiring a Settings restart | ✅ Done |
| 5.7 | Detect `HRESULT 0x80070424` (Windows Audio service stopped) and record VS-027 with a clear error message instead of an unhandled COM exception | ✅ Done |

---

### ~~Branch 14: `fix/keyboard-nav-focus`~~ ✅ Merged — PR #31
**Theme:** Keyboard navigation visibility and hotkey dialog Tab capture.

| Item | Description | Status |
|------|-------------|--------|
| — | `Key.Tab` (plus Apps, Pause, PrintScreen, Scroll) excluded from hotkey capture — no longer assignable as a global hotkey; Tab now navigates between dialog buttons | ✅ Done |
| — | `ToggleSwitchStyle`: `FocusRing` wrapper border + `IsKeyboardFocused` trigger shows a blue ring around the pill when focused via Tab | ✅ Done |
| — | `ActionButton`, `DangerButton`, `PrimaryButton`: `IsFocused` trigger highlights the border in the matching accent colour so Tab focus is visible on all buttons | ✅ Done |

---

### ~~Branch 15: `test/unit-tests`~~ ✅ Merged — PR #33
**Theme:** Create the test project and write pure-logic unit tests — zero risk to the running app.

| Item | Tests | Status |
|------|-------|--------|
| TD1 | Create `VibeSwitcher.Tests` xUnit project; add project reference; 69 tests pass | ✅ Done |
| 7.2 | `ConfigService`: load/save round-trip, corrupt+backup recovery, Migrate(), FileShare.ReadWrite concurrent read, atomic `.tmp` write (9 tests) | ✅ Done |
| 7.3 | `HotkeyDefinition`: `GetModifierFlags()` bitmask, `ToDisplayString()` "(none)", `IsEmpty`, `IsValid` range (16 tests) | ✅ Done |
| 7.7 | `IconHelper`: null/empty path returns default, outside-dir rejected, traversal canonicalized (5 tests) | ✅ Done |
| 7.12 | `AppLogger`: rotation trigger at 1 MB, `.1`/`.2` backup chain, non-fatal on locked file, level prefixes (8 tests) | ✅ Done |
| 7.13 | `SessionErrorTracker`: 10-thread concurrent `Record()`, `ErrorAdded` fires once, `Errors` snapshot immutability, `HasErrors` (8 tests) | ✅ Done |
| 7.14 | `ErrorCode`: `ToCode()` format for all 28 codes, integer uniqueness across all values (7 tests) | ✅ Done |
| 7.15 | `DeviceNotificationClient`: debounce coalesces 5 rapid calls into 1 fire, second schedule cancels first, no-op callbacks (7 tests) | ✅ Done |

Production enablers (no behavior change): ConfigService baseDir injection, AppLogger `_logPathOverride`, SessionErrorTracker `Reset()`, DeviceNotificationClient debounce injectable, `InternalsVisibleTo`, IconHelper.LoadIcon takes explicit iconsDir param.

*StartupService (7.4) and HotkeyService (7.6) resolved in PR #35. ViewModel tests (7.5) resolved in PR #36.*

---

### ~~Branch 16: `refactor/interfaces`~~ ✅ Merged — PR #35
**Theme:** Extract interfaces for every service — pure additive, no behavior change. Enables safe mocking in Branches 18–19.

5 interfaces extracted (`IAudioService`, `IConfigService`, `IStartupService`, `IHotkeyService`, `IDialogService`) + `DialogService` concrete class. All ViewModels, TrayService, and App.xaml.cs updated to use interface types. M6, TD2, TD4, 3.8 resolved as part of this branch. 11 new tests (80 total): `StartupServiceTests` (7.4, 4 tests) and `HotkeyServiceTests` (7.6, 7 tests). Fake stubs (`FakeAudioService`, `FakeConfigService`, `FakeDialogService`, `FakeHotkeyService`, `FakeStartupService`) added to unblock Branch 17.

---

### ~~Branch 17: `refactor/viewmodel-dialogs`~~ ✅ Merged — PR #36
**Theme:** Add `SettingsViewModel` and `ProfileCardViewModel` unit tests using fake services — resolves 7.5.

18 new tests (80 → 98 total). `SettingsViewModelTests`: AddProfile confirm/cancel, DeleteProfile confirm/cancel, StartWithWindows enable/disable, profile change triggers hotkey re-registration — each mutation test asserts `_profilesChangedCount` fires exactly once. `ProfileCardViewModelTests`: CaptureHotkey cancel/clear/conflict/success/replace-existing, BrowseIcon cancel/copy-success/copy-failure/same-path-skip, DeleteProfile confirm/cancel. Also null-guards `Application.Current?.Dispatcher` in `LoadDevicesAsync` for headless test environments.

---

### ~~Branch 18: `refactor/god-class`~~ ✅ Done — PR #37
**Theme:** Split `App.xaml.cs` — resolves TD3/R2.

`ProfileSwitchOrchestrator` extracted: owns `SwitchToProfile()`, `OnPowerModeChanged()`, and the full async switch flow. `AppWindowManager` extracted: owns `OpenSettingsWindow()` and `OpenAboutWindow()`. `App.xaml.cs` reduced from 248 to ~120 lines as a thin bootstrapper. Bug fix: `OnExit` null-guards `_orchestrator` for the second-instance early-exit path. `TrayService` and `SettingsWindow` untouched. 98 tests, 0 failures.

---

### ~~Branch 19: `ci/cd-pipeline`~~ ✅ Done — PR #38
**Theme:** GitHub Actions build + test pipeline.

`.github/workflows/ci.yml` added: triggers on every push and pull request to `main`; runs `dotnet build -c Release` then `dotnet test` on `windows-latest`. `.github/workflows/release.yml` added: triggers on `v*` tag push; runs `dotnet publish --self-contained -r win-x64 -p:PublishSingleFile=true`, zips the output, and uploads it as a GitHub Release artifact via `softprops/action-gh-release@v2` (with `permissions: contents: write`). `Microsoft.CodeAnalysis.NetAnalyzers` NuGet package removed — the .NET SDK on the runner already bundles a newer version, and adding it explicitly produced a version-conflict warning. 98 tests, 0 failures, 0 build warnings.

---

**Still deferred (no branch planned):**
C2/C3 (installer, code signing — external tooling/money), L17 (high-contrast — low priority), 2.7 (GC pressure — minor), 5.9 (mixed-DPI — WPF limitation), 7.8 (UI automation — requires WinAppDriver, out of scope), Sections 8–10 remaining deployment/logging/docs items.

---

### ~~Branch 20: `fix/switch-reliability`~~ ✅ Merged — PR #40
**Theme:** Remove duplicate switch logic and add a concurrent-switch guard.

| Item | Description | Status |
|------|-------------|--------|
| M16 / 3.12 | `TrayService.SwitchToProfileAsync` duplicates the switch flow — delegate to `ProfileSwitchOrchestrator` instead | ✅ Done |
| M17 / 3.13 | No in-progress flag in `ProfileSwitchOrchestrator` — hotkey spam triggers overlapping `ApplyProfileAsync` calls; add `SemaphoreSlim(1,1)` guard | ✅ Done |

---

### ~~Branch 21: `fix/settings-async`~~ ✅ Merged — PR #42
**Theme:** Fix the two async/event correctness issues in the Settings window.

| Item | Description | Status |
|------|-------------|--------|
| M18 / 4.18 | `SessionErrorTracker.ErrorAdded` subscription never removed when window is hidden (close-to-tray path) — switched to `IsVisibleChanged` to subscribe/unsubscribe on visibility | ✅ Done |
| M19 / 4.19 | `LoadDevicesAsync` has no cancellation — rapid plug/unplug events cause concurrent enumerations that overwrite each other; added `CancellationTokenSource` with `Interlocked.Exchange` | ✅ Done |

---

### ~~Branch 22: `fix/null-safety`~~ ✅ Merged — PR #44
**Theme:** Three small robustness fixes found in deep-dive review.

| Item | Description | Status |
|------|-------------|--------|
| L21 / 1.23 | `AudioService.IsDeviceActive()` bare `catch` narrowed to COM exceptions; device COM object leak and unchecked `GetState` HRESULT also fixed | ✅ Done |
| L22 / 1.24 | `ErrorDialog` in `ProfileSwitchOrchestrator` now sets `Owner` to the first visible window with `CenterOwner` placement | ✅ Done |
| L23 / 1.25 | Stale `ActiveProfileId` reset at startup with a warning log; `IsChecked` in `RebuildMenu` uses explicit `HasValue && .Value` | ✅ Done |

---

### ~~Branch 23: `ci/sha256-checksums`~~ ✅ Merged — PR #46
**Theme:** Publish SHA256 checksums alongside each GitHub Release zip.

| Item | Description | Status |
|------|-------------|--------|
| L20 / 8.9 | Added "Generate SHA256 checksum" step to `release.yml` — uses `Get-FileHash` (PowerShell 7, BOM-free UTF-8) and writes `sha256sums.txt` in two-space format; both files attached to the release | ✅ Done |

---

### ~~Branch 24: `test/additional-coverage`~~ ✅ Merged — PR #48
**Theme:** Additional unit tests identified in the deep-dive review.

| Item | Description | Status |
|------|-------------|--------|
| 7.16a | `_loadingDevices` guard: two tests confirm flag suppresses `_onChanged` during `LoadDevices()` but allows it on direct setter assignment | ✅ Done |
| 7.16b | `ConfigService.Migrate()` asymmetric sentinel: `WindowLeft = -1` nulled, `WindowTop = 200.0` preserved | ✅ Done |
| 7.16c | `IconHelper.LoadIcon()` with ASCII garbage bytes — default icon returned and `HasErrors` is true | ✅ Done |
| 7.16d | 10 concurrent `RaiseDevicesChanged()` calls from background threads — no exception thrown | ✅ Done |
| 7.16e | `RaiseDevicesChanged()` from a single background thread — no exception thrown | ✅ Done |

---

### ~~Branch 25: `feat/app-icon-refresh`~~ ✅ Merged — PR #50
**Theme:** Replace the single-frame 16px app icon with a multi-frame ICO and use the 256px frame wherever the icon appears in the UI.

| Item | Description | Status |
|------|-------------|--------|
| New | `app.ico` replaced with a 4-frame ICO (16/32/48/256px) generated from the keycap source icon via Pillow LANCZOS resampling | ✅ Done |
| New | `IconHelper.GetAppIconImageSource()` added: loads the 256px frame via `BitmapDecoder` with `BitmapCacheOption.OnLoad`; double-checked lock for thread safety; falls back to the solid-colour icon if the pack URI fails | ✅ Done |
| New | Settings window header updated to a horizontal `StackPanel` with the app icon next to the title | ✅ Done |
| New | About window uses `GetAppIconImageSource()` instead of a blue-square fallback | ✅ Done |
| New | Tray context menu gains a non-interactive header row showing the app icon and "VibeSwitcher" label | ✅ Done |
| New | `GetDefaultIcon()` wrapped in the same double-checked lock; bare `catch {}` blocks now log via `AppLogger.Warning` | ✅ Done |

---

### ~~Branch 26: `feat/toast-notifications`~~ ✅ Merged — PR #53
**Theme:** Show the VibeSwitcher app icon in balloon notification body; clear the error log at session start.

| Item | Description | Status |
|------|-------------|--------|
| F9 (partial) | `IconHelper.GetBalloonIconHandle()` — creates a cached 32×32 HICON from `app.ico` once; `DestroyIcon` called at process exit | ✅ Done |
| F9 (partial) | `TrayService.ShowBalloon` updated to pass `customIconHandle` + `largeIcon: true` so the app icon appears in the balloon body | ✅ Done |
| New | `AppLogger.StartSession()` truncates `error.log` at startup so logs from previous sessions do not accumulate | ✅ Done |

---

### ~~Branch 27: `feat/tray-interactions`~~ ✅ Merged — PR #57
**Theme:** Tray icon and global hotkey UX — left-click cycle, hotkey tooltip, Settings hotkey, icon flash, and follow-up hotkey UX polish.

| Item | Description | Status |
|------|-------------|--------|
| F21 | Left-click tray icon cycles to the next profile in sort order, wrapping around | ✅ Done |
| F5 | Hotkey cheat sheet tooltip — hovering the tray icon shows all profile hotkeys | ✅ Done |
| F24 | Global hotkey to open/close Settings — user-configurable, enable/disable toggle, keyboard badge chip in UI | ✅ Done |
| F30 | Tray icon switch flash — brief icon blink (~300 ms) after each profile switch | ✅ Done |
| New | Hotkey conflict notification names the owner profile instead of a generic rejection | ✅ Done |
| New | Conflict retry dialog (custom styled) offers Try Again / Close instead of dismiss-only | ✅ Done |
| New | All hotkeys unregistered before capture dialog opens so profile hotkeys can't fire during capture | ✅ Done |
| New | `ReregisterHotkeys` re-registers the Settings hotkey after `RegisterAll` wipes it | ✅ Done |
| New | Shortcuts section redesigned to a two-row grid — feature name + toggle on top, description + badge + button below | ✅ Done |

---

### ~~Branch 28: `feat/profile-management`~~ ✅ Merged — PR #59
**Theme:** Per-card controls in the Settings profile list — reorder, clone, and silent switch.

| Item | Description | Status |
|------|-------------|--------|
| F2 | Drag-and-drop profile reorder — ⠿ grip on each card; dragging reorders the list and persists SortOrder | ✅ Done |
| F23 | Profile clone button — duplicates name+" (copy)", devices, mode, silent flag; hotkey and icon path not copied | ✅ Done |
| F25 | Per-profile silent switch — checkbox skips the Windows notification banner on switch; device warnings always show | ✅ Done |
| Fix | SortOrder re-compacted after every delete to prevent future collisions | ✅ Done |

---

### ~~Branch 29: `feat/device-enhancements`~~ ✅ Merged — PR #61
**Theme:** Audio device interaction features — test sound, mic level test, and device connectivity indicator.

| Item | Description | Status |
|------|-------------|--------|
| F3 | Test sound button on each profile — plays a 440 Hz tone directly through the selected playback device via WASAPI; supports float32 and PCM-16 | ✅ Done |
| F3 ext | Mic test button — opens a level meter dialog that captures from the selected recording device for 5 seconds, showing real-time RMS and peak level | ✅ Done |
| F26 | Device connectivity indicator — green dot for active, red dot for disabled or unplugged devices in Settings dropdowns; disabled devices now stay visible in the list instead of disappearing | ✅ Done |
| F10 | Per-profile volume override | Dropped — user prefers Windows tray for volume control |

---

### ~~Branch 30: `feat/settings-ux`~~ ✅ Merged — PR #64
**Theme:** Settings window UX polish — device visibility controls, collapsible settings card, tray menu clarity, and visual consistency.

| Item | Description | Status |
|------|-------------|--------|
| — | Show/hide disabled devices toggle — separate checkbox in Settings to include or exclude software-disabled audio devices from profile card dropdowns; persists to config | ✅ Done |
| — | Show/hide disconnected devices toggle — separate checkbox to include or exclude unplugged devices from dropdowns; filters immediately without re-enumerating | ✅ Done |
| — | Collapsible General Settings card — ToggleButton header with gear icon, title, and subtitle; card body collapses/expands on click; `SettingsCardExpanded` persisted in config | ✅ Done |
| — | Window title simplified to "VibeSwitcher" | ✅ Done |
| — | Tray right-click menu: VibeSwitcher header now clickable (opens app); ambiguous "Settings" item removed | ✅ Done |
| — | Button corner radius unified to 7 across all button styles (`RoundedButtonTemplate`) | ✅ Done |
| — | `SettingsCardExpanded` property guarded against same-value writes to prevent spurious config saves on window open | ✅ Done |

---

### ~~Branch 31: `feat/profile-visual`~~ ✅ Merged — PR #66
**Theme:** Visual identity features for profiles — built-in icon gallery and profile name suggestion chips.

| Item | Description | Status |
|------|-------------|--------|
| F17 | Icon gallery dialog — 12 emoji icons with Black/White color toggle; saves a 64×64 PNG-embedded ICO, bypassing `GetHicon()` to preserve quality | ✅ Done |
| F17 | Color mask rendering — Pbgra32 pixel recolor with pre-multiplied alpha; Black/White paths recolor the emoji; Auto path leaves natural emoji colors | ✅ Done |
| F17 | `IconColor` persisted on `DeviceProfile` — chosen color saved to config; dark background chip in context menu and settings card applied correctly after restarts | ✅ Done |
| F17 | Dark background chip for white icons — `#4A4A4A` rounded border in tray right-click menu and settings card icon preview; tray icon itself stays clean | ✅ Done |
| F20 | Profile name suggestion chips — appear when name is still "Profile N"; picking a chip sets the name and silently auto-applies the matching gallery icon | ✅ Done |
| — | Drag-to-reorder grip — six-dot (⠿) grip to the left of the Recording row; Hand cursor on hover; tooltip explains drag behavior | ✅ Done |
| — | Bug fix: `IconColor.Auto` was persisted to model when name suggestion auto-applied an icon; now stores `Black` (the correct value for a natural-color emoji) | ✅ Done |

---

### ~~Branch 32: `feat/settings-polish`~~ ✅ Merged — PR #68
**Theme:** Settings window refinements — visual feedback, inner card layout, info badges, tray behaviour toggle, and backup/restore.

| Item | Description | Status |
|------|-------------|--------|
| F18 | Save flash — profile cards briefly flash green when a change is saved; per-card `SolidColorBrush` with `CancellationTokenSource` debounce (250 ms) | ✅ Done |
| F42 | Inner card layout — General Settings split into six labelled inner cards: Startup, Notifications, Tray, Devices, Backup & Restore, Shortcuts | ✅ Done |
| F43 | Opacity fade on all toggles — label and ⓘ badge fade to 40% when the toggle is off; Shortcuts hotkey row fades independently so the enable toggle stays fully opaque | ✅ Done |
| F1 | Backup & Restore — Export writes config to a user-chosen `.json`; Import reads it back with a confirmation dialog and rebuilds the profile list; `IConfigService` extended with `ExportTo` and `TryImport` | ✅ Done |
| F19 | Help dialog — `?` footer button opens a scrollable getting-started walkthrough covering setup, switching, tray tips, ⓘ icons, backup/restore, and data location | ✅ Done |
| — | ⓘ info badges — every settings toggle has a blue ⓘ badge with a plain-English tooltip; badge style unified across settings and profile cards | ✅ Done |
| — | Left-click tray toggle — new "Left-click tray icon to cycle profiles" toggle in the Tray inner card; when disabled, left-click opens VibeSwitcher instead; `TrayLeftMouseUp` made thread-safe via `Dispatcher.InvokeAsync` | ✅ Done |
| — | Shortcuts section restructured — enable toggle moved to left of label matching all other toggles; "Enabled" text removed; ⓘ badge added | ✅ Done |
| — | ConfirmDialog — new reusable modal used for the import confirmation; matches `AlertDialog` / `ConflictRetryDialog` pattern | ✅ Done |
| — | Settings header hover corner radius — `CornerRadius="7"` added to `IsMouseOver` trigger so blue highlight has rounded corners on all sides when card is expanded | ✅ Done |
| F22 | Expand-to-fit button — toggle that grows the window to show all cards without scrolling | Not delivered — pulled from branch; may be revisited |

---

### ~~Branch 33: `feat/appearance-modes`~~ ✅ Done — PR #70
**Theme:** Light / dark mode theming, tray polish, and window quality-of-life fixes.

| Item | Description | Status |
|------|-------------|--------|
| F16 | Light / dark mode — full resource-dictionary theming system; LightTheme.xaml + DarkTheme.xaml with ~70 named brushes each; `ThemeService` with `Apply()` and `ThemeApplied` event; in-app toggle in General Settings | ✅ Done |
| — | Tray separator indent fix — separator items rendered as custom `MenuItem` with `Tag="sep"` instead of `new Separator()`; ControlTemplate trigger zeroes margin/padding for separator items so the line spans the full menu width | ✅ Done |
| — | Tray live theme update — `RebuildMenu()` now creates a fresh `ContextMenu` object each call so the new Popup visual tree reads the current theme resources on open; wired to `ThemeService.ThemeApplied` in `App.xaml.cs` | ✅ Done |
| — | Tray separator styling — custom `Border` inside the separator `MenuItem`; height 2.5 px, rounded ends (CornerRadius 1.25), 8 px side margin; colour from `SeparatorBrush` resource (#C8C8C8 light / #505050 dark) | ✅ Done |
| — | Tray icon switch flash speed — hold time reduced from 350 ms to 150 ms so the blink feels snappier | ✅ Done |
| — | Settings auto-expand on open — when the General Settings expander opens, `EnsureFooterVisible` measures footer position via `TranslatePoint`, accounts for `MainGrid`'s 18 px bottom margin, and grows window height by the overflow amount so footer buttons are never hidden | ✅ Done |
| — | Window size/position persistence — `SizeChanged` and `LocationChanged` events write bounds to config via a 400 ms `DispatcherTimer` debounce; replaces the unreliable `OnClosing`-only approach | ✅ Done |
| — | Icon preview border — gray `IconPreviewBg` background replaced with a transparent background + 1 px `InputBorder` outline so the icon sits cleanly without a coloured chip | ✅ Done |
| — | Clone dialog icon — warning triangle replaced with a WPF-drawn Canvas of two overlapping rounded rectangles (copy/paste visual) using `Accent` stroke and `HoverBg` fill; `ConfirmDialog` extended to accept a `UIElement? iconElement` override | ✅ Done |
| — | About window label colours in dark mode — `SectionLabel` style and app subtitle changed from `SubtleText` to `SecondaryText` so labels (WEBSITE, DEVELOPMENT, SUPPORT) are visible in both themes | ✅ Done |

---

### ~~Branch 34: `fix/appearance-qa`~~ ✅ Done — PR #72
**Theme:** QA follow-ups and UI polish from the appearance-modes branch.

| Item | Description | Status |
|------|-------------|--------|
| — | `AccentColor` fallback in App.xaml — `<Color x:Key="AccentColor">#FF8000</Color>` added before any theme loads so `StaticResource AccentColor` resolves at XAML parse time; toggle `ColorAnimation.To` changed from hardcoded `#FF8000` to `{StaticResource AccentColor}` | ✅ Done |
| — | Dead `ToggleInactiveBg` brush removed — unused `SolidColorBrush x:Key="ToggleInactiveBg"` deleted from both `LightTheme.xaml` and `DarkTheme.xaml`; the toggle animation uses `ToggleOffColor` (a `Color` resource) instead | ✅ Done |
| — | `ConfirmDialog` icon badge live theme binding — badge background changed from `TryFindResource` (one-time static lookup) to `SetResourceReference` so it updates when the theme toggles while the dialog is open | ✅ Done |
| — | `AboutWindow` given `ShowInTaskbar="False"` — it was the only dialog in the app missing the attribute, causing it to show as a separate taskbar button | ✅ Done |
| — | TitleBar `StateChanged` handler stored for proper cleanup — anonymous lambda replaced with `_stateChangedHandler` field; new `OnUnloaded` handler unsubscribes it to prevent stale window references | ✅ Done |
| — | `SettingsWindow` timer stopped before bounds save on close — `_boundsTimer?.Stop()` added at the top of `OnClosing` before `SaveWindowBounds()` to prevent a redundant debounced write | ✅ Done |
| — | Settings header icon restored — `AppHeaderIcon.Source` now set alongside `AppTitleBar.IconSource` in the constructor; the app icon appears to the left of the "VibeSwitcher" heading | ✅ Done |
| — | Title bar maximize button vertically aligned — `MaxBtn` content wrapped in a named `TextBlock` with `Margin="0,0,0,5"`; the 5 px bottom margin shifts the □ glyph up to sit level with − and ✕, which render at different font-metric baselines in Segoe UI | ✅ Done |
| — | Tray separators equalized — changed from a 2.5 px rounded `Border` inside a `Height=13` `MenuItem` to a 1 px flat `Border` with 4 px top/bottom margin and no explicit `MenuItem` height; all three separators are now content-driven at the same 9 px total height | ✅ Done |

---

### ~~Branch 35: `feat/profile-scheduler`~~ ✅ Done — PR #74
**Theme:** Time-based automatic profile switching with optional pre-switch reminder.

| Item | Description | Status |
|------|-------------|--------|
| F11 | `SchedulerService` — 1-second `DispatcherTimer` evaluates every profile's schedule entries on each tick; switches automatically when day + hour + minute match; re-evaluates on power-mode resume after sleep | ✅ Done |
| F11 (wizard) | Four-step schedule wizard (`ScheduleWizardDialog`) — day selector, time picker, reminder picker, silent toggle; replaces the previous inline row editor | ✅ Done |
| F11 (reminder) | Optional pre-switch reminder — configurable lead time per entry (5, 10, 15, 30 min or custom); fires a balloon tip N minutes before the switch | ✅ Done |
| F11 (silent) | Per-schedule Silent toggle — independent of the profile card Silent toggle; profile Silent applies to manual switches only, schedule Silent applies to scheduled switches only | ✅ Done |
| F11 (conflict) | Schedule conflict detection — `ScheduleConflictDialog` prompts when a new schedule time conflicts with an existing entry on the same profile | ✅ Done |
| — | Activate button on profile cards — switches to a profile directly from Settings; shows green "✓ Active" state; refreshes on window show | ✅ Done |
| — | Silent logic fix — `scheduleSilent` changed from `bool` to `bool?` in `ProfileSwitchOrchestrator`; null = manual (use profile.Silent), value = scheduled (use that value) | ✅ Done |
| — | Scheduler dedup fix — slot-based comparison (stored hour:minute:day) replaces the 2-min elapsed-time guard; editing a schedule time now fires correctly the same minute | ✅ Done |
| — | Dark-mode tooltip text — `Foreground` setter added to the local `ToolTip` style in `SettingsWindow` plus explicit `Foreground` on each tooltip `TextBlock` | ✅ Done |

---

### ~~Branch 36: `feat/profile-card-extras`~~ ✅ Done — PR #76
**Theme:** Small per-profile additions to the Settings card that don't touch audio logic.

| Item | Description | Status |
|------|-------------|--------|
| F32 | Profile notes — optional short description field below the profile name on each card; `MaxLength=61`; stored in model; info badge tooltip explains character limit | ✅ Done |
| F33 | Favorite / pinned profiles — star toggle in the card footer; pinned profiles sort above unpinned ones in both Settings and the tray menu; `SortOrder` renumbered after each pin-sort | ✅ Done |
| F34 | Profile validation warnings — inline warning badge on cards for disconnected/unavailable devices or invalid icon paths; device check deferred until first enumeration completes | ✅ Done |
| — | Real-time icon file watcher — `FileSystemWatcher` in `ProfileCardViewModel` re-validates the icon path when the file is created or deleted while Settings is open | ✅ Done |
| — | Hotkey warning removed from card header; error button removed from header | ✅ Done |

---

### ~~Branch 37: `perf/switch-optimizations`~~ ✅ Done — PR #78
**Theme:** Micro-optimizations to reduce UI-thread pressure and disk I/O in the profile switch hot path and idle background.

| Item | Description | Status |
|------|-------------|--------|
| — | Config saves off UI thread — `SaveAsync` helper calls `Task.Run(_configService.SaveImmediate)`; applied in `ProfileSwitchOrchestrator` and all 18 `SettingsViewModel` call-sites; `_saveLock` serializes concurrent saves | ✅ Done |
| — | Tray icon bytes cache — `Dictionary<Guid, byte[]>` replaces disk reads on every switch; fresh `Icon` reconstructed from `MemoryStream` per assignment; fixes `ObjectDisposedException` when H.NotifyIcon disposed a cached `Icon` object | ✅ Done |
| — | `SchedulerService` timer 1 s → 10 s — minute precision is sufficient; eliminates 3,240 unnecessary UI-thread wakeups per hour | ✅ Done |
| — | `AppLogger.Write` — `Directory.CreateDirectory` syscall removed; directory is guaranteed after `StartSession()` | ✅ Done |
| — | `OnProfileChanged` validation scope narrowed — `card.RefreshValidation()` only, not all cards; `ValidationWarning` has no cross-card dependency | ✅ Done |
| — | `SanitizeName` static HashSet — `Path.GetInvalidFileNameChars()` allocated a new `char[]` on every call; replaced with `static readonly HashSet<char>` | ✅ Done |

---

### ~~Branch 38: `feat/device-aliases`~~ ✅ Done — PR #80
**Theme:** Per-device friendly name display throughout the app, plus several UI and quality-of-life improvements.

| Item | Description | Status |
|------|-------------|--------|
| F31 | Audio endpoint aliases — `DeviceAliases` dict in `AppConfig`; `DeviceAliasItem` ViewModel; alias substitution in all device dropdowns via C# `with`-expression so raw names are never shown when an alias is set | ✅ Done |
| — | Device Aliases dialog redesign — Playback / Recording tab buttons; stacked card layout per device (full device name wraps, status line showing which profiles use the device or whether it is disconnected/disabled); alias TextBox with placeholder; devices sorted (used-in-profile first, then A–Z) | ✅ Done |
| — | Placeholder overlap fix — alias TextBox placeholder collapses immediately on focus via a `DataTrigger` on `IsKeyboardFocusWithin` of the parent Grid (fixes `UpdateSourceTrigger=LostFocus` lag) | ✅ Done |
| — | Save flushes focused TextBox — `Save_Click` calls `BindingOperations.GetBindingExpression(tb, TextBox.TextProperty)?.UpdateSource()` before closing so in-progress edits are never lost | ✅ Done |
| — | TextBox implicit style moved to App.xaml — all dialogs (including DeviceAliasesDialog) now inherit the themed rounded TextBox without each window needing a local style | ✅ Done |
| — | Appearance segmented RadioButtons — replaced the Appearance ComboBox in General Settings with three ghost→accent RadioButtons (Follow Windows / Light / Dark) using a shared named style in `Window.Resources` | ✅ Done |
| — | Global themed scrollbar — implicit ScrollBar style in App.xaml: 20 px wide/tall, rounded corners, no arrow buttons, `DynamicResource` colors from `LightTheme.xaml` / `DarkTheme.xaml`; applies to every ScrollViewer in the app | ✅ Done |
| — | Taskbar pin fix — `SingleInstanceHelper` now uses a named `EventWaitHandle` (AutoReset, `Local\` scoped) for cross-process activation signaling; a second instance opens the event and sets it; the background listener in the first instance fires `OpenSettingsWindow` via `Dispatcher.InvokeAsync` | ✅ Done |
| — | `Run.Text` binding `Mode=OneWay` — `Run.Text` defaults to `TwoWay`, which throws `XamlParseException` on a read-only property; fixed with explicit `Mode=OneWay` on `ProfileUsage` binding | ✅ Done |
| — | 8 new unit tests — `DeviceAliasItemTests` (4 tests: property, alias changed event, raises change, clears alias) and `DeviceAliasTests` (4 tests: substitution, no alias, fallback, empty string) | ✅ Done |

---

### ~~Branch 39: `feat/settings-search`~~ ✅ Done — PR #82
**Theme:** Profile search and filtering in the Settings window.

| Item | Description | Status |
|------|-------------|--------|
| F35 | Name search field — live filtering with Esc-to-clear; placeholder explains all filter options | ✅ Done |
| F35 | Filter chips — Mode (Playback / Recording / Both), Pinned, Active, Silent, Has Hotkey, Scheduled, Has Icon, Has Notes, Has Reminder; toggleable and combinable | ✅ Done |
| F35 | Day-of-week chips — appear beneath the Scheduled chip; filter to profiles running on a specific day | ✅ Done |
| F35 | `CenteredWrapPanel` — custom `Panel` subclass that horizontally centers each row of chips regardless of wrap | ✅ Done |
| F35 | "Clear all" button and "No results" empty-state label | ✅ Done |
| F35 | "Remember last search" setting — persists the last name filter in `config.json` across sessions | ✅ Done |
| F35 | Settings card overlay — expanding the settings card collapses the filter bar and profile list so the full panel is visible | ✅ Done |
| — | Window auto-reposition — when expanding the settings card would push the footer off-screen, the window slides upward to stay within the monitor work area | ✅ Done |
| — | 30 new unit tests in `SettingsSearchTests` — name filter, mode chips, pinned, active, silent, hotkey, scheduled, icon, notes, reminder, day chips, combined filters, clear all, no-results state, `IsAnyFilterActive`, remember-search persistence | ✅ Done |

---

### ~~Branch 40: `feat/switch-sound`~~ ✅ Done — PR #84
**Theme:** Audio feedback on profile switch.

| Item | Description | Status |
|------|-------------|--------|
| F36 | SwitchSoundDialog wizard — 7 built-in tone chips (Click, Chime, Blip, Bell, Alert, Soft, Ping) plus a custom WAV file picker, volume slider, Test Sound button, Show notification banner toggle; saving always enables (no redundant enable toggle) | ✅ Done |
| F36 | Add/Edit/Remove Sound pattern on profile cards — summary row shows tone, volume, and "+ Banner" badge; matches the Add/Edit/Remove Schedule pattern | ✅ Done |
| F36 | Bell icon (🔔/🔕) in profile card action row — auto-hides when a switch sound is configured; reappears as "No Notification Banner + Sound" for sound-free profiles | ✅ Done |
| F36 | Notification separation — switch-sound profiles play custom audio only; `SoundShowBanner` drives a separate silent tray banner; non-sound profiles use the bell toggle and Silent flag | ✅ Done |
| — | Section visibility — Schedules section header hidden until a schedule exists; Switch Sound section hidden until a sound is configured | ✅ Done |
| — | Action button label context — "Edit Schedule", "Remove Schedule", "Edit Sound", "Remove Sound" | ✅ Done |
| — | Gray resize bug fix — when settings panel is expanded, Row 2 height is set to 0 and Row 3 to * so the settings card absorbs remaining window height; no gray above or below | ✅ Done |
| — | 118 new unit tests in `SwitchSoundTests` | ✅ Done |

---

### ~~Branch 41: `feat/panic-hotkey`~~ ✅ Done — PR #86
**Theme:** Instant global mute with configurable scope and audio feedback.

| Item | Description | Status |
|------|-------------|--------|
| F37 | Three independent mute hotkeys in the Shortcuts card — Mute Microphone, Mute Speakers, Mute Mic + Speakers; each has its own enable toggle (off by default) and "Set hotkey" button | ✅ Done |
| F37 | `MuteService` — toggles default audio endpoint via `IAudioEndpointVolume` COM interop; independent `_micMuted`/`_speakersMuted` booleans prevent scope-overlap confusion; state only updated after COM call succeeds | ✅ Done |
| F37 | Color-coded tray flash — red (mic), blue (speakers), purple (both); `MakeColorIcon` uses stream-copy + `DestroyIcon` to avoid GDI handle leak | ✅ Done |
| F37 | Distinct built-in sounds — descending blips on mic mute, ascending on unmute, frequency sweep + blips on both-unmute; no sound when muting speakers (inaudible) | ✅ Done |
| F37 | Color-coded "i" badges — placed outside `CheckBox` so tooltip always works; red/blue/purple backgrounds match tray flash | ✅ Done |
| F37 | Conflict detection — `FindHotkeyConflict` checks profiles, Settings hotkey, and other mute hotkeys; profile cancel path restores mute hotkeys; `FindInternalConflictOwner` checks mute hotkeys | ✅ Done |
| F37 | Settings hotkey re-selection false positive fixed — skip settings hotkey check when called from `SettingsHotkeyButton_Click` | ✅ Done |

---

### ~~Branch 42: `fix/codebase-audit-49`~~ ✅ Done — PR #88
**Theme:** Systematic full-codebase audit — dead code, bugs, security, and UI violations.

| Item | Description | Status |
|------|-------------|--------|
| Models | Removed 4 dead properties from `AppConfig` (`SwitchSoundEnabled`, `SwitchSoundTone`, `SwitchSoundCustomPath`, `SwitchSoundVolume`) — defined but never read | ✅ Done |
| Models | Removed `SoundMuted` dead property from `DeviceProfile` — same reason | ✅ Done |
| Services | Removed unused `AppConfig` parameter from `SwitchSoundService.Resolve`/`PlayAsync` and `ISwitchSoundService` — config was never consulted | ✅ Done |
| Services | Removed duplicate `Apply()` overload from `ThemeService` — defined twice with identical bodies | ✅ Done |
| Services | Added `try/catch` + rethrow to `ConfigService.ExportTo` — failures previously swallowed silently | ✅ Done |
| Security | Fixed path traversal in `IconHelper` — `StartsWith(iconsDir)` accepted sibling dirs; now appends `Path.DirectorySeparatorChar` before comparing | ✅ Done |
| ViewModels | Fixed `ProfileFilter.IsActive` missing `ActiveDays.Count > 0` — day-chip filters were silently ignored | ✅ Done |
| ViewModels | Fixed `SettingsViewModel.IsAnyFilterActive` missing `DayChips.Any(d => d.IsSelected)` — same gap | ✅ Done |
| ViewModels | Fixed `ProfileCardViewModel.SetProfileSoundTone` and `ProfileSoundVolume` setter not notifying `SoundSummary` — summary text stayed stale after tone/volume change | ✅ Done |
| Views | Replaced `MessageBox.Show` in `SessionLogWindow` with `AlertDialog` — native OS dialogs prohibited | ✅ Done |
| Comment | Fixed `App.xaml.cs` comment "every 30 seconds" → "every 10 seconds" (scheduler tick) | ✅ Done |
| Dead code | Deleted `ScheduleConflictDialog` (XAML + code-behind) — window was defined but never instantiated; `ConflictRetryDialog` handles schedule conflicts | ✅ Done |
| Dead code | Removed 8 dead tone-picker properties (`ProfileSoundToneClick` through `ProfileSoundToneCustom`), `SetProfileSoundTone`, `ProfileSoundCustomPath`, `ProfileSoundVolume`, and `BrowseProfileSoundCommand` from `ProfileCardViewModel` — leftovers from a planned inline sound editor replaced by the wizard; none bound in any XAML | ✅ Done |
| Filter UI | Removed name search text box and "Remember last search" setting — filtering is now chip-only; removed `NameFilter`, `RememberSearch`, `LastSearch` from `SettingsViewModel` and `AppConfig` | ✅ Done |
| Filter UI | Added "Has sound" filter chip — shows profiles with per-profile sound override enabled; consistent with Has hotkey, Has notes, Has icon, Has reminder | ✅ Done |
| Filter UI | Removed dead `SearchCloseButton` style; updated no-results message to "No profiles match your filters"; updated `HelpDialog` with filter chips section; updated `README.md` feature list | ✅ Done |

---

### ~~Branch 43: `feat/device-triggers`~~ ✅ Done — PR #90
**Theme:** Automatic profile activation when a specific audio device connects or disconnects, with fast HID-based wireless headset detection.

| Item | Description | Status |
|------|-------------|--------|
| F39 | `TriggerOnConnect` toggle — each `DeviceProfile` has a `TriggerOnConnect` bool; when enabled, connecting the profile's assigned device auto-activates it; disconnecting it reverts to the previous profile | ✅ Done |
| F39 | `DeviceTriggerService` — subscribes to `IAudioService.DevicesChanged` and `DevicePropertyChanged`; tracks `_connectedIds` snapshot; fires forward switches on newly-connected IDs and reverts on disconnect; 30-second `PropCooldown` prevents false triggers from rapid Windows property updates | ✅ Done |
| F39 | Revert state machine — `_revertInfo` (`RevertInfo` record with `TriggeredProfileId` + `PreviousProfileId`) persists across device events; revert fires only if the active profile is still the one that was auto-switched to | ✅ Done |
| F39 | Property-change path — `OnDevicePropertyChanged` handles always-ready dongles (LIGHTSPEED) where Windows never fires a state-change on power-on; used as a fallback; revert handled by `OnDevicesChanged` on actual disconnect | ✅ Done |
| F39 | `HidHeadsetService` — monitors Logitech LIGHTSPEED wireless headsets via HID++ vendor interface (usage page `0xFF43`); opens shared non-exclusive `HidStream`; `ReadAsync` with infinite timeout; parses HID++ 1.0 (Sub-ID `0x41`) and HID++ 2.0 (feature `0x06`, broadcast device index `0xFF`) wireless-state reports | ✅ Done |
| F39 | `KnownHidHeadsets.cs` — registry of supported headsets; currently Logitech PRO X Wireless (VID `046D` / PID `0ABA`) | ✅ Done |
| F39 | `OnHidWirelessConnected` / `OnHidWirelessDisconnected` — called by `HidHeadsetService`; forward switch fires instantly (before Windows audio notification); disconnect triggers the same revert logic as physical unplug | ✅ Done |
| F39 | `IsProfileForDescriptor` — matches a profile to a HID descriptor via `PKEY_AudioEndpoint_Path` VID/PID check first; falls back to Windows friendly-name substring match (e.g. "Speakers (Logitech PRO X Wireless Gaming Headset)" contains "Logitech PRO X Wireless") | ✅ Done |
| F39 | `IAudioService.GetAudioEndpointPath` — reads `PKEY_AudioEndpoint_Path` from the Windows property store; returns null when not exposed (typical for audio endpoints) so fallback logic engages | ✅ Done |
| F39 | 6 HID unit tests — path-match revert, no-revert-without-info, descriptor-mismatch skip, user-changed-profile skip, forward-switch on connect, no-switch-when-already-active | ✅ Done |
| Docs | GitHub issue template (`.github/ISSUE_TEMPLATE/add-headset.yml`) — YAML form with VID/PID instructions for requesting new headset support | ✅ Done |
| Docs | README wireless headset section — documents USB/Bluetooth/3.5mm behavior; supported headsets table; link to issue chooser | ✅ Done |

---

### ~~Branch 44: `feat/headset-expansion`~~ ✅ Done — PR #92
**Theme:** Expand wireless headset HID support to Corsair, SteelSeries, and HyperX; add remaining Logitech PIDs.

| Item | Description | Status |
|------|-------------|--------|
| Logitech PIDs | Added 12 additional Logitech PIDs: G633 (0x0A5C), G635 (0x0A89), G933 (0x0A5B), G935 (0x0A87), G733 ×3 (0x0AB5/0x0AFE/0x0B1F), G535 (0x0AC4), G Pro (0x0AA7), G Pro X Wireless (0x0AAA), G Pro X 2 ×2 (0x0AFB/0x0AFC) | ✅ Done |
| HidProtocolType enum | New enum: LogitechHidPP, CorsairVoid, SteelSeriesLegacy, SteelSeriesNova, HyperXAlpha, HyperXCloudII | ✅ Done |
| HidHeadsetDescriptor | Extended record with Protocol, UsagePage, UsageId, PollIntervalMs, ReadTimeoutMs, LegacyQueryPrefix | ✅ Done |
| Corsair VOID/Elite | 12 PIDs; event-driven read loop; usage page 0xFFC5/0x0001; seed query [0xC9,0x64]; data[3]==177 && data[4]!=0 → connected. UNTESTED. | ✅ Done |
| SteelSeries Legacy | 4 PIDs (Arctis 1/7X/7P); poll-based 31-byte query; response[2]==0x01 → offline. UNTESTED. | ✅ Done |
| SteelSeries Nova | 23 PIDs (Nova 7/7X/7P/7+/Nova 5/3P/3X); poll-based 64-byte query; response[3]==0x00 → offline. UNTESTED. | ✅ Done |
| HyperX Cloud Alpha | 1 PID (0x098D); poll-based 3-step 31-byte query; response[3]==0x01 → disconnected. UNTESTED. | ✅ Done |
| HyperX Cloud II | 1 PID (0x0696); poll-based 52-byte wrapped command; valid header → connected. UNTESTED. | ✅ Done |
| SelectInterface | Generalized interface selection: descriptor UsagePage takes priority; falls back to protocol-default heuristics | ✅ Done |
| DeviceReader | Start() routes to ReadLoop (event-driven) or PollLoop (poll-based) based on protocol | ✅ Done |
| README | Updated supported headsets table with brand/model list and tested/untested status | ✅ Done |

---

### ~~Branch 45: `feat/auto-switch-ux`~~ ✅ Done — PR #94
**Theme:** Auto-switch UX improvements — supported headsets dialog, playback-only restriction, and conflict detection.

| Item | Description | Status |
|------|-------------|--------|
| SupportedHeadsetsDialog | New dialog opened when the 🔌 toggle is enabled: shows all brands/models grouped by brand with Tested ✅/Untested ⚠️ badge; "Request support" button links to GitHub template | ✅ Done |
| TriggerOnConnect conflict | Setter in ProfileCardViewModel detects when another profile already claims the same playback device; shows "Auto-Switch Already Enabled" confirm dialog; "Yes, Move It" disables the other profile and refreshes its card ViewModel; "No" reverts the toggle | ✅ Done |
| Playback-only restriction | DeviceTriggerService.BuildConnectedSet now only tracks playback devices; IsTriggeredBy and IsTriggeredByDevice simplified to check PlaybackDeviceId only regardless of profile mode; recording-only profiles hide the toggle | ✅ Done |
| TriggerOnConnectVisible | New Visibility property on ProfileCardViewModel — Collapsed for Recording-only profiles, Visible otherwise; bound in SettingsWindow.xaml | ✅ Done |
| Remove pinned/sort ordering | Removed OrderByDescending(IsPinned).ThenBy(SortOrder) from OnDevicesChanged, OnDevicePropertyChanged, OnHidWirelessConnected | ✅ Done |
| IDialogService | Added ShowConfirm(title, message, actionLabel) and ShowSupportedHeadsets() | ✅ Done |
| Tests | Updated 4 DeviceTriggerServiceTests to reflect playback-only and no-ordering behavior | ✅ Done |
| Chained revert stack | DeviceTriggerService uses a stack so Speaker→BT→Logitech reverts correctly in order; HID-managed profiles only revert via OnHidWirelessDisconnected | ✅ Done |

---

### ~~Branch 46: `feat/app-switching`~~ ✅ Done — PR #96
**Theme:** Automatic profile switching based on running application, plus shortcuts UX redesign.

| Item | Description | Status |
|------|-------------|--------|
| F41 | App-aware auto-switching — `AppWatcherService` polls every 2 seconds; `AppTriggerService` dispatches profile switch when a watched exe goes from not-running to running; skips if already on the target profile | ✅ Done |
| AppTriggerDialog | New dialog with running-process picker, Browse for .exe fallback, inline "Already added" and "Used by [Profile]" badges, search box, and filter chips (All / Running / Installed / In Use); pre-populates from Start Menu shortcuts | ✅ Done |
| Switch-on-Done fix | Deferred auto-switch until the assigned app actually launches (was firing immediately when the dialog closed) | ✅ Done |
| ProfileCardViewModel | `▶` trigger button added to card header; green when triggers are configured, gray otherwise | ✅ Done |
| Shortcuts redesign | Removed all four toggle switches from the Shortcuts section; setting a hotkey now enables it automatically; ✕ clear button (visible only when set) clears back to None and auto-disables | ✅ Done |
| SettingsHotkey auto-enable | `SettingsHotkey` setter auto-enables on assign and auto-disables on clear, mirroring the existing mute hotkey behavior | ✅ Done |

---

### ~~Branch 47: `refactor/pre-release-audit`~~ ✅ Done — PR #98
**Theme:** Full pre-release codebase audit — bugs, dark-mode theming gaps, code quality, dialog polish, and documentation updates.

| Item | Description | Status |
|------|-------------|--------|
| B2 | `HidHeadsetService.ReadLoop` reconnection — outer retry loop with 2 s backoff so a USB hiccup doesn't kill monitoring permanently | ✅ Done |
| B3 | `MicTestDialog` error state — shows error message and cancels auto-close countdown if the mic fails to open | ✅ Done |
| B4 | `DeviceAliasesDialog` Escape key — `Save` had `IsCancel="True"` by mistake; corrected so Escape closes without saving | ✅ Done |
| B5 | Test sound stacking — `SwitchSoundDialog` "Test Sound" button disabled for the duration of playback; `ProfileCardViewModel.TestSound()` guards with `_isTesting` flag | ✅ Done |
| B6 | Remove Schedule badge colour — confirmation dialog used `ErrorBg` (red); changed to `Accent` (orange) to match all other popup dialogs | ✅ Done |
| B7 | Switch sound button always visible — 🎵 icon button was hidden when `SoundOverride=true`; now stays visible and turns green; clicking always opens the sound wizard | ✅ Done |
| B8 | Remove Sound confirmation — clicking "Remove Sound" in the card body now shows a confirmation dialog before removing | ✅ Done |
| B9 | Tray "Settings" item expands settings card — clicking Settings in the tray now calls `OpenSettingsWindowExpanded()`; `ExpandSettings()` on `SettingsWindow` sets `SettingsCardExpanded = true`; all other entry points (hotkey, header click, first-run) are unaffected | ✅ Done |
| P1 | `SaveWindowBounds` off UI thread — disk write dispatched to `Task.Run` background thread | ✅ Done |
| P2 | HID report logging — `LogDebugReport` moved from `AppLogger.Info` (disk) to `AppLogger.Debug` (Console.Error only) | ✅ Done |
| R10 | `DeleteOrphanedIcon` deduplication — `ProfileCardViewModel` delegates to `SettingsViewModel.DeleteOrphanedIcon` | ✅ Done |
| R11 | `MuteService` fire-and-forget — `_ = Task.Run(...)` discard suppresses CS4014 correctly | ✅ Done |
| CQ1 | Duplicate button styles removed from 6 dialogs; all reference global `App.xaml` styles; `DeleteButton` style added | ✅ Done |
| CQ7 | `ComboBox`/`ComboBoxItem` styles promoted to `App.xaml` | ✅ Done |
| CQ8 | `VolumeSlider` style promoted to `App.xaml` | ✅ Done |
| CQ10 | `DeviceAliasesDialog` shared `DeviceAliasRowTemplate` — single template replaces two copy-pasted inline templates | ✅ Done |
| CQ11 | `CustomReminderDialog` error text — hardcoded `#CC3300` replaced with `{DynamicResource ErrorText}` | ✅ Done |
| CQ12 | `AppTriggerDialog` header badge — hardcoded light-green hex replaced with `{DynamicResource Accent}` | ✅ Done |
| T1 | `HelpDialog` — left-click bullet clarified; stale right-click instruction corrected | ✅ Done |
| T4 | `SupportedHeadsetsDialog` — satellite → plug emoji; two-row button layout; Close button centred; brand badges theme-aware | ✅ Done |
| T5 | Settings item restored to tray right-click context menu | ✅ Done |
| T6 | Profile card icon bar — 5 text buttons converted to icon-only; 9-button equal `*` Grid spanning full card width | ✅ Done |
| T7 | Dialog icon headers — orange-circle badge + title added to `ScheduleWizardDialog`, `SwitchSoundDialog`, `DeviceAliasesDialog`, `HotkeyCaptureDialog`, `ProfileTypeDialog` | ✅ Done |
| T8 | Dialog badge standardisation — all popups use `{DynamicResource Accent}` (orange) + black icon; `ConfirmDeleteDialog`, `HelpDialog`, clone dialog updated | ✅ Done |
| T9 | `HotkeyCaptureDialog` — context-aware subtitle per caller; wraps in `Grid` column; app icon sharp via `IconHelper.GetAppIconImageSource()` | ✅ Done |
| T10 | Light mode borders — all border/separator resources darkened for clear visibility | ✅ Done |
| T14 | Playback test tone — frequency lowered from 440 Hz to 261 Hz (C4/middle C) with fade-in/out envelope; less startling | ✅ Done |
| T11 | Profile card icon foregrounds — `DisabledText` → `PrimaryText` (black in light, white in dark) | ✅ Done |
| T12 | Filter chip text — default foreground `SecondaryText` → `PrimaryText` | ✅ Done |
| T13 | `HelpDialog` FAQ cards — each of the 8 sections wrapped in its own card | ✅ Done |
| T15 | Bell button dims when sound configured — 🔔 `IsEnabled="False"` + `Opacity="0.35"` DataTrigger when `SoundOverride=True` (was `Visibility="Collapsed"`) | ✅ Done |
| T16 | "Don't show notification banner" toggle — label inverted; `BannerToggle.IsChecked = !showBanner`; `ConfigureSound` passes `showBanner: true` when no sound is set so toggle starts unchecked (banner on by default) | ✅ Done |
| T17 | Appearance theme picker — `UniformGrid` → `StackPanel`, `Padding="14,0"` per button, 8 px margin between buttons, `Height="30"` to match `ActionButton` | ✅ Done |
| CQ13 | `FakeDialogService` CI fix — `ShowConfirmSoundRemove()` added to test stub after `IDialogService` interface was extended; `ConfirmSoundRemoveResult` property defaults to `true` | ✅ Done |
| Docs | `ARCHITECTURE.md` rewritten with all 12 services; `README.md` adds F41; `CHANGELOG.md` consolidated | ✅ Done |

---

### ~~Branch 48: `refactor/architecture-cleanup`~~ ✅ Done — PR #100
**Theme:** Architecture refactors deferred from Branch 47 — too large to bundle with the audit fixes.

| # | Item | Status |
|---|------|--------|
| R7 | `AudioService` god class (485 lines) — extracted into four internal static helpers: `AudioDeviceEnumerator` (device listing), `AudioProfileApplier` (PolicyConfig switching), `AudioTestTonePlayer` (WASAPI sine-wave), `AudioMicMonitor` (WASAPI capture + RMS); `AudioService` is now a ~110-line coordinator; `IAudioService` and all callers unchanged | ✅ Done |
| R8 | `ProfileCardViewModel` retry loops — three `while(true)` loops replaced with named-flag `while` (CaptureHotkey) and `do-while` (AddSchedule, EditSchedule); all exit paths behaviorally identical | ✅ Done |
| R9 | `AppLogger` injectable interface — implemented as `refactor/injectable-services`; `IAppLogger` and `ISessionErrorTracker` interfaces introduced; all services receive logger/errorTracker via constructor injection; `AppLog`/`AppErrors` static service locators retained for `RelayCommand` and `IconHelper` only | ✅ Done — PR #102 |
| V2 | `AppTriggerDialog` loading indicator — "Loading installed apps…" label visible while background discovery runs; `CancellationTokenSource` wired to `Closed` event so the UI-update callback is skipped if the dialog is dismissed early | ✅ Done |

---

### ~~Branch 49: `refactor/injectable-services`~~ ✅ Done — PR #102
**Theme:** Convert static `AppLogger` and `SessionErrorTracker` to instance classes with constructor injection throughout the codebase, enabling proper test isolation and open-source maintainability.

| # | Item | Status |
|---|------|--------|
| R9 | `IAppLogger` interface introduced; `AppLogger` becomes an instance class with `AppLogger(string? logDir = null)` constructor; `LogPath` remains `static readonly` | ✅ Done |
| R9 | `ISessionErrorTracker` interface introduced; `SessionErrorTracker` becomes a per-instance class; no more static `Reset()` or static `Errors` property | ✅ Done |
| R9 | `AppLog` / `AppErrors` static service locators retained in `AppLog.cs` for `RelayCommand` and `IconHelper` only (the two callers that cannot receive injection); both fields marked `volatile` | ✅ Done |
| R9 | All 14 services (`AudioService`, `ConfigService`, `HotkeyService`, `StartupService`, `MuteService`, `DialogService`, `DeviceTriggerService`, `HidHeadsetService`, `AppWatcherService`, `SwitchSoundService`, `AppTriggerService`, `TrayService`, and the four audio helpers via method parameters) accept logger/errorTracker via constructor or method injection | ✅ Done |
| R9 | All ViewModels (`SettingsViewModel`, `ProfileCardViewModel`) and Views (`ErrorDialog`, `SessionLogWindow`, `AboutWindow`, `SettingsWindow`, `MicTestDialog`, `SwitchSoundDialog`) accept logger/errorTracker via constructor injection | ✅ Done |
| R9 | `App.xaml.cs` creates `new AppLogger()` and `new SessionErrorTracker()` at startup and wires them through every service; `AppLog.Register` / `AppErrors.Register` called immediately after for the two static-locator consumers | ✅ Done |
| R9 | `FakeAppLogger` and `FakeSessionErrorTracker` test doubles added; all 8 affected test files updated to pass fakes; `FakeSessionErrorTracker` uses lock for thread safety matching the production implementation | ✅ Done |
| R9 | `ErrorAdded` event on `SessionErrorTracker` changed from manual add/remove accessors to compiler-generated event for thread-safe subscribe/unsubscribe | ✅ Done |

---

### Branch 50: `feat/ui-redesign` ✅ Done — PR #105, #106
**Theme:** Full visual redesign of the app, delivered in two phases on this branch. Phase 1 rebuilt `SettingsWindow` from a plain vertical list into a G HUB-inspired dark card grid (title bar, tab navigation, compact profile cards with always-visible action strips, animated filter bar, profile detail overlay, and About / FAQ / Settings panels). Phase 2 completed app-wide light/dark theming, introduced a shared geometric icon system, refined the tray menu and dialogs, added keyboard navigation, and removed old-design code. All audio/switching/scheduling/business logic is unchanged — only view-layer code plus a few small view-model/service additions (search & FAQ support, dialog outcomes, the mute badge, icon composition). Verified by four parallel read-only audit agents — clean build, 222 tests pass.

| # | Item | Status |
|---|------|--------|
| UI | `SettingsWindow.xaml` fully rebuilt: 4-row outer Grid — title bar (30px), tab nav (54px), animated filter bar (0↔115px), main panel area (*) | ✅ Done |
| UI | Compact profile card DataTemplate (Width=192) — icon, name, mode badge, device rows, hotkey chip, always-visible 2-row action strip with 9 buttons | ✅ Done |
| UI | Profile detail overlay — `Grid` with `Panel.ZIndex=10`, dismiss on Esc / backdrop click / close button | ✅ Done |
| UI | Tab navigation: Profiles / Settings / About / FAQ — `ShowPanel()` helper collapses all panels, dismisses overlay, closes filter bar | ✅ Done |
| UI | Filter bar: animated `MaxHeight` 0↔115px with `CubicEase`; search box with placeholder TextBlock overlay; category + day-of-week chips | ✅ Done |
| UI | About panel: app icon + version (read from assembly), description, GitHub/Changelog/License/Report a bug links, Built With section, Raphael Mansour credit, copyright | ✅ Done |
| UI | Settings panel: all existing setting sections retained; new Diagnostics section with "View session log" button | ✅ Done |
| UI | FAQ panel: 8 help sections rebuilt as cards; fixed literal `u{XXXX}` escape sequences to proper XML entities | ✅ Done |
| UI | `DarkTheme.xaml`: all new resource keys (VSTitleBarBg, VSNavBg, VSNavBorder, VSModalBg, SaveFlashBg, InnerCardBg, InnerCardBorderBrush, ChipBg/Border/Text/HoverBg, HotkeyBg/Border, SectionLabelText, DisabledText, TertiaryText, SuccessDot, ErrorDot, WarningBg/Border/Text, HoverBg, TooltipBg/Border) | ✅ Done |
| UI | `LightTheme.xaml`: VSTitleBarBg, VSNavBg, VSNavBorder, VSModalBg added for theme parity | ✅ Done |
| UI | `SettingsWindow.xaml.cs`: `ShowPanel()` added; `FiltersBtn_Click` with `DoubleAnimation`; `ProfileCard_Click`, `OverlayClose_Click`, `OverlayBackdrop_MouseDown`; `Window_KeyDown` Esc priority; `AboutPanelIcon` + `AboutVersionText` set on load | ✅ Done |
| Theme | App-wide light/dark: every hardcoded color in `SettingsWindow` + all dialogs mapped to theme tokens; `DarkTheme.xaml`/`LightTheme.xaml` at full key parity (99 keys each) — closes deferred **F45** | ✅ Done |
| Icons | New shared `Controls/Icons.xaml` geometric icon set; all emoji replaced across dialog header badges, the tray menu, and the nav bar (double-note switch sound, alarm-clock schedule, gear settings) | ✅ Done |
| Tray | Menu reordered (About, Help & FAQ, Settings, Open Sound Settings); About/Help & FAQ route to the in-window panels; high-quality icon scaling + spacing | ✅ Done |
| Tray | Static colored mute badge composited onto the active icon (replaces the 500ms blinking color-swap loop); removed the profile-switch icon flash (`FlashSwitch` deleted) | ✅ Done |
| Icon | Regenerated the corrupt multi-resolution `vs-icon.ico` (was 4×1-byte entries) used by the taskbar / Task Manager / notification title | ✅ Done |
| Splash | `ShowActivated="False"` + non-topmost so the startup splash no longer steals focus from a fullscreen app | ✅ Done |
| Dialogs | Unified accent icon-badge headers + 12px content-card radius across all dialogs | ✅ Done |
| A11y | Accent keyboard focus rings (`AccentFocusVisual`); profile cards, card action buttons, and scroll panels focusable; collapsed filter bar leaves the tab order | ✅ Done |
| Cleanup | Removed old-design dead code: `AboutWindow`, `HelpDialog`, `CustomReminderDialog`, legacy `app.ico`/`VibeSwitcherIcon.ico`, and unused commands/styles/theme keys; `ClaudeDesign/` git-ignored | ✅ Done |
| Docs | CHANGELOG ([Unreleased] consolidated), RECORD (this section), BACKLOG (F45 ✅), ARCHITECTURE updated | ✅ Done |

### Branch 51: `feat/compact-mode` ✅ Done — PR #108
**Theme:** Mini Mode (F44) — the Settings window can shrink into a small always-handy profile switcher. Entered via a title-bar shrink button, a tray menu item, a Settings button, or a configurable global hotkey; the full window restores exactly as it was. All switching logic unchanged — mini rows/buttons invoke the existing per-card ActivateCommand. Verified by two QA agent rounds (all High/Medium findings fixed) — clean build, 224 tests pass.

| # | Item | Status |
|---|------|--------|
| Core | EnterCompact/ExitCompact: nav row collapses to 0, panels swap to MiniPanel, window locks to 300px wide with auto height (capped at 85% of work area, then scrolls); full geometry and mini position stored in separate config slots that never cross-contaminate | ✅ Done |
| UI | Rows layout — full-width clickable rows (icon, name, active dot + accent tint, hotkey tooltip); Grid layout — 58px icon buttons in a wrap grid, name on hover; both ordered pinned-first then sort order with live re-sorting | ✅ Done |
| Wizard | `MiniModeSetupDialog` — layout choice (Rows / Icon grid) + profile checklist; "Show all profiles" starts off and auto-enables when every profile is checked; empty selection = show all | ✅ Done |
| Hotkey | New global toggle hotkey (config + registration + WM_HOTKEY dispatch + conflict detection in every capture path, incl. the profile-card path); restored correctly after hotkey-capture cancellation | ✅ Done |
| Tray | "Mini Mode" menu item (after Settings) opens straight into mini; About/FAQ/Settings tray actions exit mini first | ✅ Done |
| Title bar | Mini header: small animated logo, keybind chip, colored mute dot mirroring the tray badge, pin (always-on-top) toggle, expand button; shrink button shown in full mode | ✅ Done |
| Polish | Optional translucency when inactive (180ms eased fade to 65%, hover restores); first-run intro dialog with "Customize First"; no-profiles guard dialog; splash-safe startup restore into mini | ✅ Done |
| Settings | New "Mini Window" category: Customize wizard, toggle shortcut (Set/Clear), Always on top, Translucent when inactive | ✅ Done |
| FAQ | Clickable in-app actions across FAQ answers (new profile, filter bar, settings sections, try mini) + 4 new cards: Mini Mode, panic/mute hotkeys, app launch triggers, switch sounds & silent profiles | ✅ Done |
| Fix | Confirmation-dialog subtitles wrap instead of clipping; re-entrancy guard prevents the mini hotkey from corrupting saved full-window bounds while the intro dialog is open | ✅ Done |
| Tests | 2 new ConfigService tests (mini-mode round-trip + legacy-config defaults); suite at 224 | ✅ Done |

### Branch 52: `feat/installer` ✅ Done — PR #110
**Theme:** Windows installer (C2) — Inno Setup per-user installer published with every release, plus an in-app uninstall flow. Verified end-to-end locally: silent install to `{localappdata}\Programs\VibeSwitcher` with no UAC prompt, Start Menu shortcut, running-instance detection via a new fixed-name mutex, and silent uninstall that removes the app while keeping `%APPDATA%\VibeSwitcher`.

| # | Item | Status |
|---|------|--------|
| Installer | `installer/VibeSwitcher.iss` — per-user (PrivilegesRequired=lowest, no UAC), stable AppId for upgrades, version auto-read from the published exe, optional desktop-shortcut and start-with-Windows tasks (writes the same HKCU Run value StartupService manages), launch-after-install, AppMutex running-app detection | ✅ Done |
| Installer | Uninstall removes app, shortcuts, and the Run value; keeps `%APPDATA%\VibeSwitcher` by default; the `[Code]` section deletes that one constant path only when the explicit `/DELETEDATA=1` parameter is passed (no wildcards anywhere) | ✅ Done |
| CI | `release.yml` — builds the installer with ISCC on tag push; publishes `VibeSwitcher-Setup-vX.Y.Z-win-x64.exe` alongside the portable zip with SHA256 checksums for both | ✅ Done |
| App | Fixed-name mutex `Local\VibeSwitcher_App` in `SingleInstanceHelper` so setup/uninstall can detect a running instance (single-instancing itself unchanged) | ✅ Done |
| UI | "Uninstall" Settings category (hidden for portable copies) with a danger-styled card; new `UninstallDialog` — confirmation plus an "also delete my profiles and settings" toggle that defaults to keep | ✅ Done |
| UI | Uninstall flow: launches `unins000.exe /SILENT` (with `/DELETEDATA=1` only when opted in) via a short delayed start so the app can exit first, then shuts down | ✅ Done |
| UI | Install & Updates section expanded: data folder path + live folder sizes (refreshed on every window show), Explorer shortcuts for both locations; uninstall button disabled with a hint on portable copies | ✅ Done |
| Tray | Icon registers with the shell only after the splash completes; early balloons queued and flushed; hotkeys active from launch; Mini Mode menu item is now a toggle ("Exit Mini Mode" + expand icon while mini is active) | ✅ Done |
| Fix | First-right-click menu flash: menu primed invisibly at dispatcher idle (one-time creation cost absorbed out of sight) + foreground re-asserted on open; startup window-open waits for an open tray menu to close | ✅ Done |
| Fix | Toast attribution icon colors: default tray icon loaded from the embedded `.ico` (no more lossy runtime PNG conversion); 16–32px ico frames regenerated with brand orange restored | ✅ Done |
| Docs | README Installation rewritten (installer primary, portable zip kept); CHANGELOG, RECORD, BACKLOG updated | ✅ Done |

### Branch 53: `fix/pre-v2-audit` ✅ Done — PR #112
**Theme:** Final hardening audit before the v2.0.0 release, plus a run of UI/UX polish that landed on the same branch. Nine parallel review agents swept the entire codebase; this branch fixes everything actionable they found (all High + Medium + cheap Low/Nit), bumps the version, and adds the polish below. Behavior is preserved except where a fix corrects an outright bug and the two user-approved behavior changes (scheduler catch-up, 500ms hotkey debounce). Verified clean by the audit: theme parity, no MessageBox, hotkey atom hygiene, installer safety, SECURITY.md "no network requests".

#### New features
| Item | Status |
|------|--------|
| "Your uploads" library for custom icons (`.ico`) and switch sounds (`.wav`) — kept under `Icons\Library` / `Sounds\Library`, re-pickable + removable in the pickers, browsed files saved there automatically (`UploadLibrary` helper) | ✅ Done |
| Customizable tray menu — show/hide About, Help & FAQ, Mini Mode, Open Sound Settings (Settings + Exit always shown) via Settings → Tray (`AppConfig.TrayShow*`) | ✅ Done |
| Clone Profile wizard — step-by-step copy: name/hotkey, devices/mode, which features carry over, which schedules to bring along | ✅ Done |
| Clear (✕) buttons on the profile card — reset hotkey → "Not set", icon → default | ✅ Done |

#### Changed
| Item | Status |
|------|--------|
| Icon picker previews the chosen Black/White colour live over a neutral checkerboard tile (`GalleryIconHelper.RenderGlyph`) so the preview matches the saved icon | ✅ Done |
| Mini Mode shows each profile's activation hotkey in both layouts (row chip + grid caption); the title bar also shows the Mini Mode toggle shortcut (wordmark dropped so it fits at 300px) | ✅ Done |
| Notifications relabeled "Show switch & warning alerts" with a master-switch description (kept, not removed — it's the only mute for device-unavailable warnings) | ✅ Done |
| Unified "Not set" wording for empty dropdowns / hotkey labels / clone dialog (was "(None)") | ✅ Done |
| Profile card favorite & "no notification banner" buttons swapped; themed accent checkboxes app-wide + unified `DayPillStyle`; About links reordered (Website, GitHub, Report a Bug, ReadMe, Changelog, License) | ✅ Done |
| Scheduler (approved) — range-based catch-up so a switch missed while asleep/off fires on wake/launch (2h window) and survives DST; min-interval clamp; `EvaluateNow` returns whether it fired so startup restore doesn't clobber a due schedule | ✅ Done |
| Mute UX — persistent tray mute badge (and mini-mode mute dot) removed in favor of a brief on-toggle banner with a per-shortcut silence flag (`UpdateMuteFlash`/`UpdateMuteBadge` gone) | ✅ Done |
| Title-bar maximize glyph swaps to a restore glyph when maximized; hotkey anti-repeat shortened to 500ms | ✅ Done |
| Release — version 1.1.0 → 2.0.0; release zip packs `publish\*` (exe at root); About / ARCHITECTURE / CHANGELOG corrected (System.Media + Windows Core Audio + HidSharp, not NAudio/CoreAudio); README notes Win10 1809+ | ✅ Done |

#### Fixed
| Item | Status |
|------|--------|
| Security — `DeleteOrphanedIcon` canonicalizes before `File.Delete` (closes a path-traversal arbitrary-delete via imported config); `IconHelper.LoadIcon` canonicalizes the icons dir too | ✅ Done |
| Data — `TryImport` requires the `ConfigVersion` marker before replacing settings; `Migrate` clamps Hour/Minute/ReminderMinutes, validates `ProfileMode`, nulls a dangling `ActiveProfileId`; custom sounds confined to a managed folder, out-of-folder paths dropped on import | ✅ Done |
| Reliability — tray icon registers via a 6s splash-fallback so an interrupted splash can't leave the app headless; wake/startup evaluates due schedules before restoring the active profile (single `OnSystemResume`) so a due switch can't lose the lock race | ✅ Done |
| Scheduler correctness — fires only the most-recent due switch when several were due at once (marks the rest seen); catch-up no longer re-fires on restart within the window (`AppConfig.LastSchedulerEvaluation` persisted) | ✅ Done |
| Concurrency — config saves snapshot on the caller thread + flush on exit, with a monotonic version guard against out-of-order writes; locked `AppLogger` write; `AppWatcher.Poll` re-entrancy gate; `AppTrigger` and `DeviceTrigger` (device + HID) lookups marshalled to the UI thread | ✅ Done |
| Resource lifetime — COM leak in `AudioProfileApplier` closed; `HidHeadsetService` awaits its loop before disposing the CTS + guards zero-length reports; `DeviceNotificationClient` disposable with per-device debounce + `_disposed` guard; `SettingsViewModel` `IDisposable`; `LogoAnimator` single `WeakReference` subscribe + storyboards stop on Unload; `fmtPtr`/GDI/COM handle leaks on error paths closed; `IconHelper` DCL fields volatile | ✅ Done |
| Profile switching — switch lock released before banners/sound/modal error dialog; applier surfaces present-but-failed applies (partial applies attempt all roles best-effort); scheduled-switch failures show a balloon not a modal; the mute toggle re-reads the device's actual state before flipping; switch feedback fully crash-guarded | ✅ Done |
| Audio — WAV custom-sound parser rewritten as a safe chunk-walker with bounds checks + a 25MB size cap | ✅ Done |
| Mini Mode keybind chip no longer clipped at the 300px width; picking a custom icon right after a built-in one (or clearing it) refreshes the card preview; disabling a profile's assigned device keeps it selected with a red dot instead of resetting to "Not set" | ✅ Done |
| Assorted — dead `ProfileFilter.IsActive` / `Start()` / `HasKnownDevices` removed; stale comments fixed; dialog icon badges + app-trigger chips/badges/Running use theme tokens; title-bar + mic-test controls gained automation names; symlink-safe config temp write; UNC app-trigger targets skipped; `FakeConfigService` reset matches the real one; `WinApi` AUMID P/Invoke checks its HRESULT | ✅ Done |

#### Audit follow-ups (AF) — moved here from BACKLOG
| # | Item | Outcome |
|---|------|---------|
| AF1 | Confine custom switch-sound files to a managed Sounds folder | ✅ Done — picked `.wav` copied into `%APPDATA%\VibeSwitcher\Sounds\`; import drops any path outside it (`PathSafety`); +2 tests |
| AF2 | App-Trigger dialog filter chips keyboard-accessible | ✅ Done — focusable + Enter/Space + automation names (kept as Borders, not restyled to ToggleButtons) |
| AF3 | CI / release hardening — pin `softprops/action-gh-release` to a SHA, pin the choco Inno Setup version, bump xunit / Test.Sdk / coverlet, add a `--logger trx` artifact | ⏸ Deferred — can't validate workflow changes locally; belongs in a CI-only PR |
| AF4 | XAML de-duplication | ✅ Done where clean — accent frame → shared `Views/AccentFrame.xaml` across 19 dialogs; `DayPillStyle` later unified into `App.xaml`. Closed as won't-do: `MuteBannerToggle` bakes its own icon; inline accent trigger setters (magic-number promotion, high churn) |
| AF5 | Doc nits — CONTRIBUTING vs CLAUDE changelog timing + RECORD summary-table recount | ✅ Done — CONTRIBUTING wording aligned with CLAUDE.md; RECORD summary tables re-checked for consistency during the docs accuracy sweep |
| LP1 | Custom switch-sound orphan cleanup on profile rename | ⏸ Deferred (low) — `StoreCustomSound` re-copies to a name-matching file after a rename, leaving the old per-profile copy behind (no `DeleteOrphanedSound`, unlike icons). Harmless stray file in AppData |

#### Deep pre-v2 audit (later session)
| Area | Outcome |
|------|---------|
| App-trigger watcher | ✅ Fixed — was doing N process-table enumerations every 2s (one per watched exe) and never disposing the returned `Process` handles; now one `Process.GetProcesses()` per tick with all handles disposed |
| Imported icon paths | ✅ Fixed — `TryImport` now drops icon paths outside the managed Icons folder (mirrors the existing sound guard); +2 tests |
| Uninstall leftover | ✅ Fixed — installer removes the runtime `AppUserModelId` HKCU key on uninstall |
| Accessibility / keyboard | ✅ Improved — screen-reader names on icon-only controls; volume slider keyboard-adjustable; Clone wizard Enter-to-advance (focus rings only on keyboard nav); the five content scroll panels (Profiles/Settings/About/FAQ/Mini) were focusable Tab stops with no focus visual (an "invisible" Tab stop before the first card) — now `IsTabStop="False"` so Tab skips them, wheel-scroll unaffected |
| DPI / installer / leaks / IO / crash-safety / exception handling | ✅ Verified GOOD, no change — PerMonitorV2 manifest; fixed-AppId upgrade + AppMutex detection + opt-in data deletion; SettingsWindow/ViewModel/LogoAnimator disposal; atomic save + corrupt-fallback; documented crash/shutdown guarantees; global exception handlers + best-effort catches |
| Scheduler / DST | ✅ Verified GOOD, no change — catch-up window covers spring-forward; per-occurrence dedup prevents fall-back double-fire; next-fire always ≤7 days (timer interval in range); multiple-due collapses to most-recent |
| Audio / COM thread-safety | ✅ Verified GOOD, no change — per-call COM enumerators (no cross-apartment sharing) with `Marshal.Release`/`ReleaseComObject` + `PropVariantClear`; `DeviceNotificationClient` lock-guarded debounce with `_disposed` checks; both `DevicesChanged` subscribers marshal UI/config work to the dispatcher; race-safe CTS via `Interlocked.Exchange` |
| Dialog initial focus | ✅ Improved — Clone wizard focuses + selects the name field on open (other dialogs rely on `IsDefault`/`IsCancel` + Tab, which is acceptable) |
| Final report-only audit (8 agents) | ✅ Done — no Critical/High. Fixed the 3 Medium findings: custom switch-sound path confined to `SoundsDir` at playback (`SwitchSoundService`, +4 tests); `WScript.Shell` COM objects released in the app-trigger picker; release-workflow version guard uses an exact normalized comparison instead of a prefix match. Remaining Low/Nit items (incl. M4 upload dedup-by-size) consolidated in BACKLOG "Deferred — pre-v2.0.0 follow-ups". Suite at 255 |

#### Tests
| Item | Status |
|------|--------|
| 8 audit tests + final-pass tests (import-guard reject, reset-to-defaults preservation, log-path) + AF1 sound-confinement ×2 + `UploadLibrary` (6) + library icon pick/save (2) + tray-menu flag round-trip (1) + icon-import path-confinement ×2; suite at **251** | ✅ Done |

---

### Branch 54: `fix/device-id-drift` ✅ Done — PR #114
**Theme:** Post-2.0.0 reliability fixes found in daily use (issue #113). Serial-less USB audio hardware (a USB SPDIF adapter feeding a Sonos, a Logitech BRIO webcam mic) gets a new Windows endpoint ID whenever Windows re-detects it (different port, hub reset, driver update); profiles only stored the ID, so they showed their devices as disconnected until re-picked by hand, and device aliases were stranded on the dead ID. Also: a refused tray tooltip update aborted a whole profile switch, and `error.log` was dominated by harmless noise.

| Item | Status |
|------|--------|
| Device fingerprint per profile slot (`PlaybackDeviceName`/`PlaybackDeviceHardwareKey`, same for recording) — Windows friendly name + normalized hardware key from the endpoint's hardware instance path (`AudioDeviceEnumerator.NormalizeHardwareKey`); backfilled automatically while the device is present | ✅ Done |
| `DeviceRebindService` — rebinds a slot whose ID vanished to the single connected device matching its fingerprint and moves the alias; runs at startup, on every device change (subscribed ahead of `DeviceTriggerService` and Settings), and in `ProfileSwitchOrchestrator` only when a switch reports a missing device (retries the apply once). Conservative: USB keys only, no name-only matches, no ambiguous matches, never onto a device another profile is bound to, never when two different missing devices claim the same new one | ✅ Done |
| Settings window — fingerprints captured on device pick, clone, and import (backfill only; rebinding stays with the app-level service) | ✅ Done |
| Tray — tooltip/icon/notification writes refused by Windows (`Shell_NotifyIcon` FALSE → H.NotifyIcon `InvalidOperationException`) are logged and ignored instead of aborting the switch; bounded (3×, 2 s) retry re-pushes the icon + tooltip | ✅ Done |
| HID headset matching — the "endpoint path" property key was actually `PKEY_AudioEndpoint_ControlPanelPageProvider` (always empty), so matching always fell back to names; now reads the hardware instance path, and the name fallback only runs when the USB path is inconclusive (same vendor or unreadable) | ✅ Done |
| Log noise — per-model "no HID devices found" scan lines and name-match diagnostics moved to debug output (not written to `error.log`) | ✅ Done |
| Tests — `DeviceRebindServiceTests` (32 cases), 3 HID path-matching tests, 2 card fingerprint-clearing tests; suite at **292** | ✅ Done |
| Version bumped to **2.0.1** (`VibeSwitcher.csproj`); CHANGELOG `[2.0.1]` | ✅ Done |

---

### Branch 55: `xplat/core-scaffold` ✅ Done — PR #116
**Theme:** F46 phase 1 of 11 (issue #115). Foundation for the macOS + Linux app. No visible app yet, and no file under `VibeSwitcher/` or `VibeSwitcher.Tests/` changes. Design: `xplat/docs/ADR-001-xplat.md`; per-OS differences: `xplat/docs/PARITY.md`; full plan: phases listed in BACKLOG → Planned Branches. Merged into the F46 holding branch `xplat/dev` (not `main`): every phase lands there, and one final PR brings the first tested Mac/Linux version into `main`.

| Item | Status |
|------|--------|
| `xplat/` tree on .NET 10 with its own solution (`VibeSwitcher.XPlat.sln`), `xplat/global.json` (SDK 10), `Directory.Build.props` (C# 12 to match the Windows app), LF `.gitattributes` for scripts; root `global.json` pins the Windows app to SDK 8.0.4xx so installing SDK 10 changes nothing for it | ✅ Done |
| `VibeSwitcher.Core` — links 31 Windows source files unchanged (models, config, logging, schedules, rebind, HID headsets, device triggers, switch sounds, small view models) | ✅ Done |
| WPF-API shims (`Compat/`) — minimal internal `Application`/`Dispatcher`/`DispatcherTimer`/`CommandManager`/`SoundPlayer` stand-ins forwarding to an injected UI dispatcher and WAV player; inline without a dispatcher (tests); timers started early bind on registration; stale ticks after Stop dropped; batched command refresh | ✅ Done |
| Forks — `AudioDeviceInfo`/`ProfileSwitchResult`/`NormalizeHardwareKey`, `HotkeyConflictException`, `HotkeyDefinition` (per-OS labels: Windows text, macOS ⌃⌥⇧⌘, Linux "Super"; VK→name table generated from WPF, `tools/gen-virtualkeymap.ps1`); tracked in `forks.txt` with upstream git hashes; `scripts/check-forks.sh` drift guard | ✅ Done |
| CI — `.github/workflows/ci-xplat.yml`: build + test on ubuntu-24.04, macos-15 (Apple Silicon), macos-15-intel, windows-latest; fork-drift job; runs on pushes/PRs to `main` and `xplat/dev`. Existing `ci.yml`/`release.yml` untouched | ✅ Done |
| Tests — 175 linked Windows tests + 37 new (hotkey fork, shims, scheduler end-to-end through a UI timer, device-trigger marshalling); **212** passing on Windows and Linux (WSL Ubuntu 24.04) | ✅ Done |

### Branch 56: `xplat/linux-audio` ✅ Done — PR #118
**Theme:** F46 phase 2 of 11 (issue #117). The Linux audio engine (`xplat/src/VibeSwitcher.Platform.Linux`), built on `pactl`/`pacat`/`parec` so it works with PipeWire and PulseAudio. No visible app yet; no file under `VibeSwitcher/` or `VibeSwitcher.Tests/` changes. Merged into `xplat/dev`.

| Item | Status |
|------|--------|
| `LinuxAudioService` (implements the new Core `IPlatformAudioService` = `IAudioService` + mic meter + default-device mute): connected-first lists, unplugged = every jack-sensing port "not available", monitor/dummy sources hidden, apply always sets the default (PipeWire reports the effective default), missing vs set-failed results, Windows-equivalent session error codes | ✅ Done |
| Change events — `pactl subscribe` watcher with restart/backoff (reported only after 3 consecutive failures), 500 ms debounce, snapshot diff: `DevicesChanged` on add/remove/availability, `DevicePropertyChanged` when a jack is plugged in (port not-available → available) | ✅ Done |
| Windows-shaped hardware keys (`USB\VID_xxxx&PID_xxxx&MI_xx\…`, Bluetooth, PCI) from pactl properties or sysfs (relative-link-safe realpath), so the linked rebind and HID-headset logic works unchanged | ✅ Done |
| Parsing — pactl JSON (incl. pactl 16 `monitor_source` quirk, PipeWire 0x ids / `device.bus-path`) and text (older pactl / broken JSON; UCM port names); LC_ALL=C for every tool | ✅ Done |
| Test tone (`Core/Audio/TestTone`, same sound as Windows), mic meter (parec float RMS, same scale), WAV playback for switch/mute sounds (`Core/Audio/WavData` + `PulseWavPlayer`) | ✅ Done |
| Robustness — process timeouts that can't hang on inherited pipes, 3 s fail-fast after "server unavailable" (cleared by background refreshes), pacat write/play deadline with kill, ENOENT-only "not installed" message, leading-`-` device names refused (pactl rejects `--`) | ✅ Done |
| CI `linux-audio` job: real-server integration tests on PipeWire (24.04), PulseAudio (24.04) and Ubuntu 22.04 in forced text mode; `scripts/run-linux-audio-tests.sh` (private throwaway server), `scripts/wsl-test.sh` (WSL, no sudo) | ✅ Done |
| Forks tracked: `TestTone` ← `AudioTestTonePlayer`; `LinuxAudioService` ← `AudioProfileApplier`, `AudioDeviceEnumerator`, `DeviceNotificationClient`, `AudioMicMonitor`, `MuteService` | ✅ Done |
| Tests — 86 Linux-platform unit tests (fake pactl/pacat/parec) + 20 Core audio tests; **318** passing on Windows and Linux (WSL); 8/8 real-server tests on PulseAudio 16.1 in JSON and text mode | ✅ Done |

### Branch 57: `xplat/macos-audio` ✅ Done — PR #120
**Theme:** F46 phase 3 of 11 (issue #119). The macOS audio engine (`xplat/src/VibeSwitcher.Platform.MacOS`), hand-written P/Invoke to Core Audio, Audio Queues, CoreFoundation and IOKit — verified on GitHub's Apple Silicon and Intel Macs with the BlackHole virtual audio driver. No visible app yet; no file under `VibeSwitcher/` or `VibeSwitcher.Tests/` changes. Merged into `xplat/dev`.

| Item | Status |
|------|--------|
| `MacAudioService` (`IPlatformAudioService`): outputs/inputs by streams (one device in both lists), hidden / can't-be-default devices not offered, switching sets output + sound-effects output (where the device allows, `'sflt'`) + input with Windows' already-set skip, missing vs set-failed, Windows-equivalent session error codes | ✅ Done |
| Change detection: HAL notifications moved to the HAL's own thread (`'rnlp'` = NULL — otherwise listeners never fire outside a Cocoa main run loop); crash-safe static listener with an id table; device list, defaults, streams, name and jack listeners; 500 ms debounce + snapshot diff (headphone jack plug-in → `DevicePropertyChanged`) | ✅ Done |
| Mute: settable mute control (main or per channel), else per-channel volume to 0 with saved levels restored on unmute; warns when the device's minimum isn't silent | ✅ Done |
| Audio Queues: test tone / sounds played in full (seen-running then stopped, 150 ms silent tail, deadline), mic meter over every input channel (loudest-channel RMS, shared `Core/Audio/AudioLevel`), Bluetooth-friendly start timeout and unplug watchdog | ✅ Done |
| USB fingerprints for Apple's USB audio driver from the model UID or the IO registry (location from the UID's 4th field incl. unpadded Apple Silicon ids, product-name check, signed `locationID` handled), cached per device UID with a negative cache; other drivers get no guessed key | ✅ Done |
| CI `macos-audio` job (macos-15 arm64, macos-15-intel): BlackHole 2ch/16ch loopback; 10 real-Core-Audio tests incl. switching read-back, the listener firing, mute, tone/WAV/0.1 s blip heard through the loopback, 16-channel meter; microphone-permission diagnostics | ✅ Done |
| Native binding checks (FourCCs, struct layouts/offsets, audio formats); forks tracked (`MacAudioService` ← 5 Windows audio files, `AudioQueueStreams` ← `AudioTestTonePlayer`, both hardware-key files ← `AudioService`); PARITY macOS notes | ✅ Done |
| Tests — 63 macOS unit tests (fake Core Audio, all OSes) + Core audio-level tests; **383** passing on Windows/Linux; 10/10 real-Mac tests on Apple Silicon and Intel | ✅ Done |

### Branch 58: `xplat/os-services` ✅ Done — PR #122
**Theme:** F46 phase 4a of 11 (issue #121). The OS services around the audio engines. No visible app yet; no file under `VibeSwitcher/` or `VibeSwitcher.Tests/` changes. Merged into `xplat/dev`.

| Item | Status |
|------|--------|
| `AppPaths` — `~/Library/Application Support/VibeSwitcher` / `$XDG_CONFIG_HOME/VibeSwitcher` (Windows layout inside); lock + socket kept in that private (0700) folder, never a cleanable temp/runtime folder; socket path checked in UTF-8 bytes | ✅ Done |
| `SingleInstance` — exclusive lock file (only real lock contention counts as "running") + Unix-socket command channel (live socket never taken over, startup race retried, 5 s read / 30 s handler, size limit, back-off); channel failures keep single-instance working | ✅ Done |
| `CommandLine` / `AppCommand` — `--switch`, `--mute`, `--open`, `--help`, `--version` (`=` form, macOS launch arguments ignored), validated on both ends | ✅ Done |
| `ResumeDetector` — wall vs monotonic clock gap (both clocks pause correctly on macOS/Linux sleep); every subscriber isolated | ✅ Done |
| Start at login — `LinuxStartupService` (XDG autostart, Desktop Entry Exec quoting, honours the desktop's own off switch, trusted AppImage only) and `MacStartupService` (LaunchAgent via `open -a`, 0644, AssociatedBundleIdentifiers, refuses App Translocation); both refresh the path and record the Windows error codes | ✅ Done |
| `LinuxNotifier` (Tmds.DBus.Protocol 0.21.3 = Avalonia's version) — replaces the previous banner, 5 s call timeout, markup escaping only when the server renders markup, `$XDG_RUNTIME_DIR/bus` fallback | ✅ Done |
| `MuteService` forked into Core onto `IPlatformAudioService` (same toggle rules and sounds); `InstallInfo` (macOS app incl. translocation, AppImage, .deb, development) | ✅ Done |
| Tests — 454 passing on Windows and Linux (WSL); D-Bus notification test on a private session bus in CI (`VS_DBUS_IT=1`); follow-ups for app wiring logged in BACKLOG (SMAppService, macOS reopen event, dev-build autostart, logind) | ✅ Done |

### Branch 59: `xplat/hotkeys` ✅ Done — PR #124
**Theme:** F46 phase 4b, part 1 (issue #123). Global hotkeys on macOS and Linux X11 (Wayland follows in part 2, `xplat/hotkeys-wayland`). No visible app yet; no file under `VibeSwitcher/` or `VibeSwitcher.Tests/` changes. Merged into `xplat/dev`.

| Item | Status |
|------|--------|
| `IGlobalKeyGrabber` (Core) — Windows-form keys/modifiers, never throws, Pressed off the system thread; `HotkeyService` forked from Windows onto it (atoms → ids; TestHotkey blames another app only for a real conflict — documented) | ✅ Done |
| `CarbonKeyGrabber` (macOS) — exclusive `RegisterEventHotKey` (conflicts reported, no Accessibility prompt), VK → kVK table, Win↔⌘ / Alt↔⌥, macOS 15 Option-only rule reported, disposed guard, `SimulatePress` test hook through the real handler | ✅ Done |
| `X11KeyGrabber` (Linux) — XCB checked grabs (no global error handler), dedicated thread woken by a pipe, Pressed via the thread pool, Caps/Num Lock variants with the real Num Lock modifier, MappingNotify re-grab, F13–F24 evdev fallback, quiet after dispose; Wayland-session detection for the selector | ✅ Done |
| CI — new `linux-desktop` job (Xvfb + xdotool, 12 real-X tests: presses, lock keys, conflicts, re-entrancy, speed, F13/F24, shutdown, end to end); macOS job also runs Carbon tests (registration, conflicts, event delivery) | ✅ Done |
| Tests — 499 passing on Windows and Linux; PARITY hotkey notes | ✅ Done |

### Branch 60: `xplat/hotkeys-wayland` ✅ Done — PR #126
**Theme:** F46 phase 4b, part 2 (issue #125). Global hotkeys on Linux Wayland sessions, where apps can't grab keys: the desktop's Global Shortcuts portal, and a GNOME custom-shortcut fallback for GNOME before 48. No visible app yet; no file under `VibeSwitcher/` or `VibeSwitcher.Tests/` changes. Merged into `xplat/dev`.

| Item | Status |
|------|--------|
| Stable shortcut names (Core) — `IGlobalKeyGrabber.Describe` (default no-op), `ShortcutNames` (`profile-<guid>`, `settings`, `mini-mode`, `mute-<scope>`, `probe`); the `HotkeyService` fork describes each shortcut before registering (documented divergence); new `--mini` command (`AppCommandKind.MiniMode`) | ✅ Done |
| `PortalKeyGrabber` — `org.freedesktop.portal.GlobalShortcuts` over Tmds.DBus.Protocol: host app-id registration, debounced re-bind only when the set changes, one standing Response watch, outdated dialogs withdrawn (Request.Close), unanswered dialog closed, pre-dialog failures retried (5 s / 30 s / 2 min), Activated → Pressed by name, ShortcutsChanged tracked; no conflict test (documented) | ✅ Done |
| `GnomeShortcutBridge` — GNOME custom keybindings via `gsettings` (own `vibeswitcher-*` entries only, user's order kept), commands run the command line (shell-quoted), unusable launch paths refused (AppImage mount, `%`), `RemoveAll`/`RemoveAllEntries` | ✅ Done |
| `LinuxHotkeys` selector — X11 / portal / GNOME (consent) / off with an explanation (`NoKeyGrabber`); leftover GNOME entries removed whenever GNOME uses another way | ✅ Done |
| CI — Desktop-bus step runs the fake-portal tests; `linux-desktop` job also runs the GNOME tests against real gsettings + schemas (keyfile backend) | ✅ Done |
| Tests — 537 passing on Windows + 16 Linux-only (private D-Bus, real gsettings); PARITY Wayland notes. Library quirks found: Tmds 0.21.3 writes the match key `pathNamespace` wrongly; its MessageWriter must be passed by `ref` | ✅ Done |

### Branch 61: `xplat/triggers-hid` ✅ Done — PR #128
**Theme:** F46 phase 4c (issue #127). App launch triggers, the app picker's lists, and wireless-headset access on Linux. No visible app yet; no file under `VibeSwitcher/` or `VibeSwitcher.Tests/` changes. Merged into `xplat/dev`.

| Item | Status |
|------|--------|
| Core — trigger formats + `AppTriggerMatch` (path, Mac bundle incl. translocation/cryptex, `flatpak:`/`snap:`, AppImage, script, Wine `.exe`, desktop-entry id, name fallback with 15-char truncation), `IRunningProcessSource` (null = unreadable), `IAppCatalog`; forks of `AppWatcherService` (per-trigger state, one launch fires once — most specific claims first, unreadable list skipped) and `AppTriggerService` (exact trigger first, then name); `FilePaths.RealPath` moved to Core | ✅ Done |
| Linux — `LinuxProcesses` (/proc, cached by pid+exe+comm, readlink P/Invoke, Flatpak/Snap from cgroup, desktop id from the app scope, AppImage via environ, interpreter scripts by real path), `DesktopEntry` (spec quoting, links/openers/inline code excluded, interpreters, Wine, `Path=`), `LinuxAppCatalog` (XDG + Flatpak/Snap dirs, hide overrides, running apps incl. Wine games, never itself) | ✅ Done |
| macOS — `MacProcesses` (libproc, shared buffers under a lock), `MacAppCatalog` (Applications folders one level deep, real paths, translocated copies not stored) | ✅ Done |
| Linux headsets — `LinuxHidAccess`: udev rule for the exact supported VID:PID dongles (`uaccess`), generated `packaging/linux/70-vibeswitcher-headsets.rules` (LF, drift test), check (USB only, udev file order, user's switch-off respected), pkexec install (argv, temp file + mv, settle) and remove (own file only) | ✅ Done |
| Tests — 547 on Windows, 653 on Linux (real processes, catalogs, symlinks); real Mac processes/bundles/catalog and HidSharp smoke tests in the macOS CI job; PARITY app-trigger and headset notes. Two QA rounds | ✅ Done |

### Branch 62: `xplat/ui-foundation` ✅ Done — PR #130
**Theme:** F46 phase 5, part 1 (issue #129). The first Avalonia project — the app shell and the building blocks of every screen. No file under `VibeSwitcher/` or `VibeSwitcher.Tests/` changes. Merged into `xplat/dev`.

| Item | Status |
|------|--------|
| `VibeSwitcher.App` (Avalonia 11.3.22): theme colours (light/dark, 100 keys) and icon geometries read at start-up from the embedded Windows `Themes/*.xaml` and `Controls/Icons.xaml` (strict loader); Fluent keys aliased to them; `ThemeService` forked onto theme variants (follows the system) | ✅ Done |
| `Styles/Controls.axaml` — the App.xaml keyed styles as control themes with the same keys (ActionButton, PrimaryButton, DangerButton, DeleteButton, ToggleSwitchStyle with transitions, ThemedCheckBox, DayPillStyle, VolumeSlider); TextBox/ComboBox/ToolTip sizing, read-only text boxes, 12 px default text, Windows-like focus rings | ✅ Done |
| Fonts bundled with licences: Open Sans (for Segoe UI; Selawik blocked — see BACKLOG), Cascadia Mono, Cousine | ✅ Done |
| Window frame — `WindowChrome` (Linux: frameless + drawn Windows title bar with drag/maximize; macOS: system buttons, title shifted), `TitleBar`, `AccentFrame`, `VsDialog` base (CenterOwner, no taskbar, app icon) | ✅ Done |
| Dialogs — Alert, Confirm, ConfirmDelete, ConflictRetry, Error (selectable message), Uninstall (per-system wording); async `DialogService` subset (hidden owner → centred on screen); preview window as the temporary main window; ported files in `forks.txt` | ✅ Done |
| Tests — 45 headless (themes, dialogs, focus, frames) incl. 32 screenshots compared with Linux baselines; CI keeps the screenshots of every OS; 688 passing on Windows | ✅ Done |

### Branch 63: `xplat/ui-parity` ✅ Done — PR #132
**Theme:** F46 phase 5, part 2 (issue #131). A side-by-side comparison of the Mac/Linux dialogs with the Windows originals. The Windows app is only read. Merged into `xplat/dev`.

| Item | Status |
|------|--------|
| `xplat/tools/WpfSnapshots` (net8.0-windows) — renders the Windows dialogs (light/dark, 8 px rounded corners) from an already-built app; App.xaml resources parsed as XAML into a plain `Application` (the Windows `App` class is never created, so its start-up never runs); writes `parity.html` (Windows \| Linux \| macOS with sizes and size differences) | ✅ Done |
| CI — "Look vs Windows" job (after build-and-test; Linux and macOS images from those systems' runs; "ui-parity" artifact) | ✅ Done |

### Branch 64: `xplat/packaging` ✅ Done — PR #134
**Theme:** F46 phase 6 (issue #133). The Mac and Linux packages people will download, built and checked by CI on every change. Packages are CI artifacts only (no GitHub releases during the port, so no prerelease workflow). No file under `VibeSwitcher/` or `VibeSwitcher.Tests/` changes. Merged into `xplat/dev`.

| Item | Status |
|------|--------|
| macOS — `packaging/macos/build-macos.sh`: `VibeSwitcher.app` per architecture (Info.plist: `com.raphymany.vibeswitcher` as the login item expects, microphone usage text, macOS 14+, numeric bundle version), `.icns` from the Windows 256 px icon, licences, every file in `Contents/MacOS` ad-hoc signed then the bundle (no `--deep`), `.dmg` with an Applications link (hdiutil retried) | ✅ Done |
| macOS — `notarize.sh` (not run until an Apple developer account exists): per-file Developer ID signing with the hardened runtime, entitlements on the main program only, notarytool via a keychain profile with the verdict checked (log printed on rejection), stapling of the app and the dmg | ✅ Done |
| Linux — `packaging/linux/build-linux.sh`: `.deb` (`/opt/vibeswitcher`, `/usr/bin/vibeswitcher`, `com.raphymany.vibeswitcher.desktop`, hicolor icons, udev headset rule, licences + `copyright`, md5sums, xz, normalised file modes; Depends incl. libice6/libsm6/libxext6/libxfixes3 and ICU 70–80; postinst/postrm reload udev) and AppImage (appimagetool 1.9.0, static runtime) | ✅ Done |
| App — `Cli`: `--version` (`2.0.1-preview`, csproj `VersionPrefix`/`VersionSuffix`) and `--help`, bad option → exit 2; `LinuxAppIdentity` shared by the portal, notifications and the autostart entry's icon | ✅ Done |
| CI — `packages-linux` (build, install, `ldd`, start under Xvfb for 15 s, desktop-file-validate, remove), `packages-linux-distros` (install + start on Debian 12/13, Ubuntu 22.04/26.04), `packages-mac` (Apple Silicon + Intel: open the dmg, start the app, verify the signature and Info.plist) | ✅ Done |
| Tests — 6 command-line tests; 694 passing on Windows. One QA round (ICU names for newer distros, per-file Mac signing, real start-up checks, extra X11 libraries, xz, macOS 14 minimum) | ✅ Done |

### Branch 65: `xplat/settings-logic` ✅ Done — PR #136
**Theme:** F46 phase 7, part 1 (issue #135). The logic behind the Settings window, shared with Windows instead of rewritten. No file under `VibeSwitcher/` or `VibeSwitcher.Tests/` changes. Merged into `xplat/dev`. Phase 7 is now four parts (logic, window + Profiles, Settings sections + About + FAQ, Mini Mode).

| Item | Status |
|------|--------|
| Linked unchanged: `SettingsViewModel`, `ProfileCardViewModel`, `IDialogService`. Public WPF stand-ins `System.Windows.Visibility` and `System.Windows.Media.ImageSource`; forks `Helpers/IconHelper` (same folder check and fallbacks, .ico validation, 16 MB cap, embedded Windows `vs-icon.ico` as default, `ImageFactory` from the app), `Helpers/GalleryIconHelper` (same gallery and .ico writer, `GlyphRenderer` from the app), `Controls/LogoAnimator` (mode only) — all in `forks.txt` | ✅ Done |
| `DialogService` implements `IDialogService`: synchronous calls wait in a nested message loop (`Dispatcher.PushFrame`) like WPF's `ShowDialog`; the app's other windows ignore input meanwhile; dialogs opened from a dialog are owned by it; ownerless dialogs centred and topmost; quitting answers "cancelled" instead of hanging; `.ico` file picker; dialogs still to port log and answer "cancelled". ADR updated (first planned as async forks) | ✅ Done |
| App: `AppImages` (icon bytes → bitmap, gallery emoji drawn and colour-masked as on Windows, `ToBitmap` converter), `Wpf.Visibility` attached property (Hidden keeps space, subtree unfocusable, own values restored) | ✅ Done |
| Tests — 81 Windows tests linked (Settings, profile card, search, device aliases, icons); 23 new (forks, dialog waits, ownership, input blocking, glyph colours, visibility); 798 passing on Windows and Linux. One QA round (quit-while-waiting hang, other windows' input, nested owners, visibility values, default-icon copies) | ✅ Done |

### Branch 66: `xplat/settings-window` ✅ Done — PR #138
**Theme:** F46 phase 7, part 2 (issue #137). The Settings window and its Profiles page, on the shared Windows view models. No file under `VibeSwitcher/` or `VibeSwitcher.Tests/` changes. Merged into `xplat/dev`.

| Item | Status |
|------|--------|
| `Settings/SettingsWindow.axaml(.cs)` (port of `Views/SettingsWindow.xaml(.cs)`): title bar (Linux draws the buttons, macOS keeps its own, title moved right), top bar, sliding filter bar (13 chips, Clear, day chips), Profiles page (cards with chips, 8 card buttons, active highlight, save flash, empty / no-match), profile popup (name chips, notes, devices + Test, hotkey, icon, warning, Clone / Delete / Done; switching cards never mixes devices), Escape, close to tray / quit (never blocks app or OS shutdown), size and place in the Windows app's units (one scale factor both ways). Settings / About / FAQ panels and Mini Mode left empty for parts 3–4 | ✅ Done |
| `Settings/SettingsStyles.axaml` — the window's keyed styles as control themes (WPF trigger precedence kept: "on" colours beat hover) | ✅ Done |
| Controls: `LogoIcon` (Nav / About; Windows key frames incl. the negative-BeginTime repeat quirk; one shared clock; only in showing windows; Static = rest pose), `CenteredWrapPanel`, `ResizeGrip` + `WindowChrome.EnableEdgeResize` (Linux; buttons at the edge keep their clicks), `SaveFlash`, `Converters`, `Wpf.RereadIsChecked` (re-reads after Click so a view model can refuse a toggle) | ✅ Done |
| `Threading/AvaloniaUiThread` registered at start-up (the shared code's Dispatcher / timers) + command re-query on input; `SystemSoundSettings` (macOS System Settings, Linux desktop settings / pavucontrol) | ✅ Done |
| Tests — 39 new (window behaviour, popup, chips, refusal, navigation, tray, logo, resize edges) and 16 screenshots compared with Linux baselines (stable: devices awaited, transitions settled); 837 passing on Windows and Linux. One QA round (chip click opening the popup, quit path, toggle re-read timing, edge resize over buttons, bounds scaling, hidden-window animation, rest pose, slide cancel, save-flash timing) | ✅ Done |

### Branch 67: `xplat/settings-sections` ✅ Done — PR #140
**Theme:** F46 phase 7, part 3 (issue #139). The Settings page, About and the FAQ, on the shared Windows view models. No file under `VibeSwitcher/` or `VibeSwitcher.Tests/` changes. Merged into `xplat/dev`.

| Item | Status |
|------|--------|
| `SettingsWindow.axaml(.cs)`: the Settings sidebar (selected page marked, Back) and its nine sections (Startup, Appearance, Notifications, Devices, Tray, Shortcuts, Mini Window, Logs, Install & Updates), About and the FAQ, ported from `Views/SettingsWindow.xaml(.cs)`. Shortcuts: the Windows conflict check and try-again loop (`FindHotkeyConflict`), hotkeys off while asking and re-registered in a `finally`. Export / Import through the system pickers with the Windows messages. Install page: full version, locations and sizes (links not followed), uninstall offered only for installed copies (off for development builds and translocated Mac apps, with the reason) and handed to an `Uninstall` callback the app wires later. FAQ links as inline buttons on the text baseline (`TextBlock.BaselineOffset` from the font's ascent) | ✅ Done |
| `Settings/Wording.cs` — every Windows-specific text in each system's words (macOS: menu bar, "Open at login", "Show in Finder"; Linux: tray, "Start at login"; no "Windows"/"PC"; the FAQ's config path from `AppPaths`, so `$XDG_CONFIG_HOME` shows); the Windows-only "legacy sound panel" and "show disabled devices" rows left out (PARITY.md) | ✅ Done |
| Styles: `SettingsStyles.axaml` gains the sidebar, segment radio, link chip, card, panel title, mute-banner toggle and FAQ link themes (WPF trigger precedence kept); `Styles/Controls.axaml` gains the Windows ScrollBar (always shown, 20 px, `ScrollViewer.AllowAutoHide=False` so content stops beside it) and the ContextMenu / MenuItem / Separator styles; `Converters.IsValue` for the radio pickers | ✅ Done |
| `SystemOpener` (open / xdg-open; web links only; Finder reveal for the `.app`), `DialogService` JSON save/open pickers (".json" added when a Linux picker omits it; non-local picks logged) and the `ShowHotkeyCapture(seed, subtitle)` / `ShowDeviceAliases` hooks for phase 8; `InstallInfo.AppPath` (a build run via `dotnet` reports its own folder); PATH lookups skip relative folders and non-executable files | ✅ Done |
| Tests — 19 new (sections, wording, switches, pickers, shortcuts and conflicts, export / import, install page, uninstall hand-over, FAQ links and baseline, scroll bar, menu item, config path, dotnet host) and 44 screenshots (9 Settings pages, About, FAQ × light/dark × Linux/macOS); Profiles screenshots updated for the scroll bar; 900 passing on Windows and Linux. One QA round (dotnet-host install path, double size count, save-picker extension and non-local picks, `*.JSON`, PATH lookup safety, `$XDG_CONFIG_HOME`, shared-static test collection, missing tests) | ✅ Done |

### Branch 68: `xplat/mini-mode` ✅ Done — PR #142
**Theme:** F46 phase 7, part 4 (issue #141). Mini Mode, completing the Settings window. No file under `VibeSwitcher/` or `VibeSwitcher.Tests/` changes. Merged into `xplat/dev`.

| Item | Status |
|------|--------|
| `SettingsWindow.axaml(.cs)`: `EnterCompact` / `ExitCompact` / `ToggleCompact` ported (first-time welcome with "Customize First", no-profile guard, re-entry check after the dialogs, 300 px wide, sized to its list up to 85% of the work area, mini title bar, no top bar or resizing, its own place in the config's units, Topmost, restored at start-up, Settings / About / FAQ leave it). Avalonia differences handled: `Height = NaN` (a set Height beats SizeToContent), maximized / full-screen entry takes the last normal size from the config, the fade is a `DoubleTransition` over a see-through window (`TransparencyLevelHint`; stays solid where the system can't), the live-sorted view replaced by `RefreshMiniList` (tray order, Customize selection, follows cards being added / removed / moved / pinned, only while in Mini Mode) | ✅ Done |
| `MiniPanel` (rows and icon grid) and the `MiniRowBtn` / `MiniGridBtn` themes; `Dialogs/MiniModeSetupDialog` (layout, "Show all profiles", checklist); ScrollViewer padding moved onto the content (Avalonia leaves it out of the measured height) | ✅ Done |
| Tests — 18 new (`MiniModeTests`) and 12 screenshots (Mini Mode rows / grid, the setup dialog); the setup dialog added to the WPF side-by-side tool; shared UI test helpers in `Ui.cs`; 930 passing on Windows and Linux. One QA round (maximized / full-screen entry, see-through fade, re-fit, handler leaks, list rebuild cost, transition left attached, save timer after exit, missing tests) | ✅ Done |

### Branch 69: `xplat/dialogs-small` ✅ Done — PR #144
**Theme:** F46 phase 8, part 1 (issue #143). Seven of the remaining dialogs; phase 8 is split into three parts (small dialogs, wizards, app triggers + splash). No file under `VibeSwitcher/` or `VibeSwitcher.Tests/` changes. Merged into `xplat/dev`.

| Item | Status |
|------|--------|
| `Dialogs/HotkeyCaptureDialog` (keys taken in a tunnel handler before a focused button; modifiers tracked from their own presses — X11 reports a modifier's press with the earlier state; `HotkeyDefinition.ModifierPreview` in each system's labels; IME / dead keys ignored) and `Input/VirtualKeys` (Avalonia key → Windows code from `VirtualKeyMap`'s names, falling back to the key's US-keyboard position for non-Latin layouts); profile hotkeys and Settings' shortcuts now use it | ✅ Done |
| `ProfileTypeDialog`, `DeviceAliasesDialog` (Save commits the focused field), `SessionLogWindow` (resizable VsDialog; header + rows for WPF's GridView; invariant time format; log path from the data folder + AppLogger's file name; `SystemOpener.OpenFile`), `ManageSchedulesDialog`, `MicTestDialog` (`IPlatformAudioService.RunMicLevelMonitor`; an early return = couldn't open; WPF's standard green bar), `SupportedHeadsetsDialog` (Windows → macOS / Linux in its text via `Wording`) | ✅ Done |
| `DialogService`: seven placeholders replaced; `Audio` / `Logger` init properties (set by Settings) | ✅ Done |
| Tests — 23 new (`SmallDialogsTests`) and 24 screenshots; five more dialogs in the WPF side-by-side tool; tests that relied on the placeholders now answer the real dialogs; 971 passing on Windows and Linux. One QA round (non-Latin / AltGr keys, IME keys, stuck modifier preview, locale time separator, mic screenshot timing, meter overdraw, unlogged failures, binding coverage of Manage Schedules' buttons, Escape / Enter tests) | ✅ Done |

### Branch 70: `xplat/dialogs-wizards` ✅ Done — PR #146
**Theme:** F46 phase 8, part 2 (issue #145). The four wizards the profile cards open. No file under `VibeSwitcher/` or `VibeSwitcher.Tests/` changes. Merged into `xplat/dev`.

| Item | Status |
|------|--------|
| `Dialogs/CloneProfileDialog` (four steps: name + hotkey, devices + mode, what to copy — only what the profile has; copying the sound turns Silent off — and schedules; `ClonedProfile`), `ScheduleWizardDialog` (24 / 12-hour time, days, preset or custom reminder, silent; Save early and Remove when editing; `ScheduleResult`), `SwitchSoundDialog` (seven tones or a custom `.wav` through the system picker, kept in the uploads; volume, banner, Test Sound; `SoundResult`; `PickWav` / `TestPlay` seams) | ✅ Done |
| `Dialogs/IconGalleryDialog`: the 12 gallery emoji drawn in black or white with a bundled font (`Assets/Fonts/NotoEmoji-Gallery.ttf`: Noto Emoji subset to the 12 symbols at bold weight, 7 KB, OFL licence alongside; `AppFonts.Emoji` / `AppImages.EmojiFont` put it first), so they look the same on every system including Linux installs with no emoji font; earlier uploads with remove, Browse from disk | ✅ Done |
| `DialogService`: four placeholders replaced (only the app-trigger wizard left); `Config` init property set by Settings; Manage Schedules' Edit now opens the schedule wizard | ✅ Done |
| Styles: Windows selection highlight (`#660078D7`, text keeps its colour) on TextBox and SelectableTextBlock; the 7×4 Windows dropdown arrow; `Wording.IconColorHint` (menu bars / panels); PARITY rows for the gallery emoji and the headset badges | ✅ Done |
| Tests — 17 new (`WizardsTests`) and 16 screenshots; the four wizards added to the WPF side-by-side tool; Profiles popup and controls screenshots updated for the arrow; 1004 passing on Windows and Linux. One QA round (gallery redraw on White → Black, second picker while one is open, non-local picks logged, previews disposed, SelectableTextBlock selection, missing tests) | ✅ Done |

### Branch 71: `xplat/dialogs-apps` ✅ Done — PR #148
**Theme:** F46 phase 8, part 3 (issue #147). The last dialogs: every Windows dialog now has a Mac/Linux version. No file under `VibeSwitcher/` or `VibeSwitcher.Tests/` changes. Merged into `xplat/dev`.

| Item | Status |
|------|--------|
| `Dialogs/AppTriggerDialog` (port of `Views/AppTriggerDialog.xaml(.cs)`): linked apps, filter chips, search, add / remove, badges, Browse via the system picker + `IAppCatalog.TriggerForFile` with the Windows alerts; lists from `IAppCatalog` loaded off the UI thread once the dialog is up (installed first: the Linux catalog derives the running list from it), icons in a second pass; apps compared with `SameApp` (equal, or the same program name) for "Added", de-duplication and "Used by"; Mac / Linux wording (`Wording.AppTriggerBackgroundNote`, `BrowseApp`) | ✅ Done |
| App icons: `Imaging/AppIcons` (cached, size-capped, time-limited reads; SVG via Svg.Skia 3.0.6 only when self-contained — the library would fetch external references), `Platform.MacOS/Apps/MacAppIcon` (`.icns` from Info.plist / usual names; streamed, overflow-safe), `Platform.Linux/Apps/LinuxIconLookup` (user theme from kdeglobals / GTK settings, hicolor, pixmaps; PNG ≥ size, then scalable SVG). `LinuxAppCatalog.TriggerForFile` now accepts only runnable files (or `.AppImage` / `.exe`) | ✅ Done |
| `Dialogs/SplashWindow` + `SplashTimeline` (the WPF storyboards as one function of time; WPF repeat / hold semantics kept; square card where windows can't be see-through) — shown at start-up in phase 9 | ✅ Done |
| `Dialogs/HeadsetAccessDialog` (Linux only, no Windows original): offered from `ShowSupportedHeadsets` when a supported receiver can't be read; installs the udev rule through `LinuxHidAccess.Install` off the UI thread and re-checks; replug, turned-off, no-pkexec and Bluetooth cases. `DialogService`: `Apps` / `AppIcon` / `Headsets` (`HeadsetAccessHooks`), set per system by Settings (`PlatformApps`); the last `NotYet` removed | ✅ Done |
| Tests — 29 new (`AppsDialogsTests`), 5 + 4 platform tests (`LinuxIconLookupTests`, `MacAppIconTests`, Linux `TriggerForFile`), 12 screenshots; app-trigger and splash added to the WPF side-by-side tool; 1076 passing on Windows and Linux. One QA round (double Linux scan, icon caching, any-file browse, untrusted icon files and SVG external references, `.icns` overflow, SVG size choice, conflict matching, loads before the dialog shows, headset dialog wording) | ✅ Done |

### Branch 72: `xplat/app-startup` ✅ Done — PR #150
**Theme:** F46 phase 9, part 1 (issue #149). The Mac/Linux app starts and runs for real; phase 9 is split into three parts (start-up and tray; the macOS menu bar; self-test, diagnostics and uninstall). No file under `VibeSwitcher/` or `VibeSwitcher.Tests/` changes. Merged into `xplat/dev`.

| Item | Status |
|------|--------|
| `Program` / `Cli.AcquireOrForward`: the single-instance lock before anything else (a second copy never empties the log); later launches hand `--open` / `--switch` / `--mute` / `--mini` to the running copy (reply printed, errors → exit 1; a too-long settings path explained); no display → plain message | ✅ Done |
| `Hosting/AppHost` (fork of `App.xaml.cs`): the Windows start-up order on `IHostPlatform` (Linux: pactl, `LinuxHotkeys` with GNOME consent off for now, D-Bus notifications, `PulseWavPlayer`; macOS: Core Audio, Carbon, banners logged until part 2); schedules before the restore; a launch command replaces the restore and runs without waiting for the splash; other launches' commands wait until after the splash (so one isn't undone); splash closed early / not shown / 6 s fallback all lead on; the Linux headset-access offer once (marker file beside the config); crash logging (Avalonia's missing global-menu service logged quietly); SIGTERM and Quit save settings and let go of everything | ✅ Done |
| Forks: `Hosting/TrayService` (Avalonia TrayIcon + native menu; profile icons, ★ / tick, hover-text shortcut list, left-click cycle, banners held until the icon shows, "Quit"), `Hosting/ProfileSwitchOrchestrator` (command-line switches wait up to 10 s for their turn; the lock outlives Dispose), `Hosting/AppWindowManager` (one Settings window; the menu's Settings always shows it; maximized stays maximized). `xplat/forks.txt` gains these and the 14 dialogs ported in phases 7–8 | ✅ Done |
| Tests — 17 new (`AppHostTests`); 1093 passing on Windows, 1103 on Linux; the Linux build started for real in WSL and answered second launches. One QA round (commands during the splash undone, splash closed early losing the launch command, lowercase mute scope, headset offer repeating, start-up failure visibility, display errors, SIGTERM, quitting message, disposed lock, device change after quit, double tray dispose, TestResults ignored) | ✅ Done |

### Branch 73: `xplat/menubar-mac` ✅ Done — PR #152
**Theme:** F46 phase 9, part 2 (issue #151). The macOS menu bar. No file under `VibeSwitcher/` or `VibeSwitcher.Tests/` changes. Merged into `xplat/dev`.

| Item | Status |
|------|--------|
| `Platform.MacOS/OS/MacStatusItem` — the app's own NSStatusItem through the Objective-C runtime (a registered target class with an `[UnmanagedCallersOnly]` action; rect returns via `objc_msgSend_stret` on Intel): icon, hover text, press on left / right mouse-down, its place under the item, `PerformClick`; Avalonia's tray icon never reports clicks on macOS. Falls back to Avalonia's icon + native menu if it can't be made. `MacPointer` (CGEvent) as the placement fallback; `MacWindows.ShowOnEverySpace` | ✅ Done |
| `Hosting/StyledTrayMenu` (the Windows tray menu's headers on the app's ContextMenu / MenuItem styles, from a 1×1 anchor window; toggles on a second press; a choice runs before the menu closes; placed in window units — points on macOS) and `TrayService.BuildEntries` (one list of lines for the Linux native menu and the macOS styled menu); `IMenuBarItem` / `MacMenuBarItem` | ✅ Done |
| `Hosting/BannerWindow` + `BannerNotifier` — the app's own banner on macOS (top right, placed and see-through before it appears, over full-screen apps, one at a time, 5 s or a click, never focused); no Dock icon (`LSUIElement`, `ShowInDock = false`); reopen (`IActivatableLifetime`) shows Settings; Mac wording (no left-click option, FAQ "Click the icon…") | ✅ Done |
| CI: the macOS package jobs start the real `.app` with `VS_MENUBAR_CHECK` — the app presses its own item and the job fails unless the styled menu opened (both Apple Silicon and Intel pass: "menu opened at (770, 24)" / "(1666, 24)") | ✅ Done |
| Tests — 13 new (`MenuBarTests`, reopen) and 12 screenshots (styled menu, banner); Mac Settings › Menu Bar and FAQ updated; 1106 passing on Windows, 1116 on Linux. QA rounds (one lost to a PC crash and rerun): Avalonia's Clicked never raised on macOS, Retina placement units, anchor focus closing the menu, action after close, banners over full screen, banner flash, second press | ✅ Done |

### Branch 74: `xplat/diagnostics` ✅ Done — PR #154
**Theme:** F46 phase 9, part 3 (issue #153). The friend-test tools and desktops without a tray; phase 9 gains a fourth part (uninstall, GNOME consent, Linux extras). No file under `VibeSwitcher/` or `VibeSwitcher.Tests/` changes. Merged into `xplat/dev`.

| Item | Status |
|------|--------|
| `Diagnostics/SelfTest` (`--selftest`, run before Avalonia; look-only: `IHostPlatform.CreateKeyGrabberAsync(lookOnly)` / `LinuxHotkeys.CreateAsync(cleanUp: false)`, no portal probe), `Diagnostics/DiagnosticsBundle` + `DiagnosticsRun` + `ScreenshotTour` (`--diagnostics [file]` beside a running app: report, devices, system / screens, config + logs read shared, window pictures from a copy of the settings in the user's theme — `SettingsWindow.ClosesForGood`, a no-op key grabber, menu lines without a tray icon; Bluetooth addresses masked; Desktop → home fallback; no-screen fallback only before the run started; failed until saved); `CommandLine` `LocalTool` | ✅ Done |
| `Platform.Linux/OS/LinuxTrayCheck` (StatusNotifierWatcher owner + `IsStatusNotifierHostRegistered`), `IHostPlatform.IsTrayAvailableAsync`, `AppHost` (`TrayKnown`: re-checked every 2 s × 10; then Settings shown for any launch + explained once via a marker file), `SettingsWindow.TrayAvailable` (closing quits), `AppWindowManager.CanHide` (minimize instead of hide) | ✅ Done |
| CI: package jobs run `--selftest` (must finish) and `--diagnostics` (zip must hold the report and pictures) on Ubuntu 24.04, Debian 12/13, Ubuntu 22.04/26.04 and both Macs | ✅ Done |
| Fixed (found by the new CI self-test output): with `DBUS_SESSION_BUS_ADDRESS` unset, Tmds.DBus's X11 fallback (`Address.Session` → `XOpenDisplay` from "libX11", a development-package name) threw and stopped the whole start-up. `LinuxNotifier.SessionBusAddress` now reads the variable and `$XDG_RUNTIME_DIR/bus` itself and guards the library lookup; the notifier's creation can't stop start-up. CI's "app starts" checks now also fail on the app's "couldn't start" message (it kept running on its dialog, so the 15 s check had passed) | ✅ Done |
| Tests — 25 App + 1 Core + 3 Linux (2 on a real private D-Bus); 1124 passing on Windows, 1134 on Linux; real self-test and diagnostics run in WSL. One QA round (quitting mid-run with "Close to tray" off, missing `unzip` in CI images, tray race at login and KDE's watcher in GNOME, unreachable app on shortcut launches, GNOME shortcut clean-up from the self-test, misleading fallback, theme / shortcuts page in pictures, extra tray icon, CI checks, macOS Desktop permission, address privacy) | ✅ Done |

### Branch 75: `xplat/uninstall` ✅ Done — PR #156
**Theme:** F46 phase 9, part 4 (issue #155). Uninstalling from Settings › Install & Updates on macOS and Linux; the rest of the old part 4 (GNOME shortcuts consent, logind wake signal) becomes part 5. No file under `VibeSwitcher/` or `VibeSwitcher.Tests/` changes. Merged into `xplat/dev`.

| Item | Status |
|------|--------|
| `Hosting/Uninstaller` (`Run`: the app first — the only step that can be refused, then nothing else is touched — then start at login and GNOME shortcuts, each later step only logged when it fails; `DeleteData`: only the app's own entries, then the folder if empty, never through a link) with `IUninstallSystem` / `SystemUninstall`: macOS → Trash (`Platform.MacOS/OS/MacTrash`, NSFileManager with an autorelease pool and the NSError's text), AppImage → checked (ELF + `AI\x02`), deleted, app-menu entries whose `Exec=`/`TryExec=` program is exactly this file, the headset rule; `.deb` → `dpkg-query -S` must name `vibeswitcher`, then `/usr/bin/pkexec /usr/bin/dpkg --remove` waited for without a time limit (126/127/no authentication agent each worded) | ✅ Done |
| `AppHost.UninstallAsync` (one at a time, Quit waits for it, a refusal after Quit becomes a notification, unexpected failures shown and retryable), `SettingsWindow.Uninstall` (button off with "Uninstalling…" while it runs, also after reopening), `Program` deletes the data after Avalonia returns while still holding the instance lock; assemblies preloaded before the app's files go (not a single-file publish); `UninstallText.WithDataFolder`; `LinuxHidAccess.Remove` reloads and re-triggers udev; `GnomeShortcutBridge.RemoveAll` also drops its registrations and pending update | ✅ Done |
| CI: `VS_UNINSTALL_CHECK` (only with `GITHUB_ACTIONS=true`) makes a copy of the packaged app uninstall itself — Linux deletes the copied folder, both Macs move the copy to the Trash — and it must quit with exit 0 and write "uninstalled" after deleting its data | ✅ Done |
| Tests — 23 App (uninstall) + 1 Settings + 2 Linux; 1146 passing on Windows, 1154 on Linux; a Release copy uninstalled itself in WSL. Two QA rounds (dpkg not owning the copy, loose menu-entry matching, steps after removal throwing, double start / Quit mid-prompt, lazy assembly loads after removal, GNOME bridge race, data folder of the user's own, udev not re-triggered, prompt timeout, CI hook in release builds, wording) | ✅ Done |

### Branch 76: `xplat/gnome-consent` ✅ Done — PR #158
**Theme:** F46 phase 9, part 5 (issue #157). The GNOME-shortcuts choice and how shortcuts work, in Settings › Shortcuts; Linux sleep / wake and dev-build start at login move to part 6. No file under `VibeSwitcher/` or `VibeSwitcher.Tests/` changes. Merged into `xplat/dev`.

| Item | Status |
|------|--------|
| `Hosting/XPlatSettings` (`xplat.json` beside the shared `config.json`: `gnomeShortcutsAllowed`; unreadable → not allowed; saved through a new file); `IHostPlatform.CreateKeyGrabberAsync(gnomeShortcutsAllowed)` → `KeyGrabberSetup` (grabber, `ShortcutsWay`, explanation); `Core/Platform/SwitchableKeyGrabber` (the grabber `HotkeyService` keeps; `Swap` puts a new way in place) | ✅ Done |
| `AppHost.SetGnomeShortcutsAsync` (new way set up first — a failure changes nothing — then unregister, swap, register again in one UI-thread step; the old way's GNOME entries removed and it's closed off-thread; the choice only kept where it took effect, a save failure said in a dialog and a removal deleting the file; one change at a time, not during an uninstall); no per-shortcut errors where shortcuts can't work; `ShortcutsInfo` / `WatchShortcutService` (the portal's state changes refresh Settings) | ✅ Done |
| Settings › Shortcuts "How shortcuts work here" card (`Wording.Shortcuts`: the desktop's service with its state and assigned keys — `PortalKeyGrabber.AssignedKeys`, from current registrations — GNOME with "Add my shortcuts to GNOME" / "Remove my shortcuts from GNOME", or why they're off; hidden where the system's own key grabs work); `GnomeShortcutBridge.Dispose` waits for an update in progress; self-test reports the choice without a test shortcut | ✅ Done |
| Tests — 8 App host / Settings + 5 wording + 1 self-test + 2 Core + Linux portal; 1160 passing on Windows, 1170 on Linux. One QA round (choice not saved, hotkeys lost when the new way fails, an OK GNOME couldn't use, UI-thread window during the swap, uninstall overlap, stale profile names in the key list, error per shortcut, old service handler, wording) | ✅ Done |
