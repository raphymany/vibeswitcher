# VibeSwitcher — Architecture

VibeSwitcher is a Windows system tray application built with WPF and .NET 8. It switches the Windows default audio device (Console, Multimedia, and Communications roles) by activating a saved profile, triggered by a global hotkey, tray menu click, or left-click cycle.

---

## Layer overview

```
┌─────────────────────────────────────────────┐
│                  Views (XAML)               │
│  SettingsWindow · AboutPanel · AlertDialog  │
│  HotkeyCaptureDialog · ConfirmDialog · ...  │
└────────────────────┬────────────────────────┘
                     │ binds to
┌────────────────────▼────────────────────────┐
│              ViewModels (MVVM)              │
│  SettingsViewModel · ProfileCardViewModel  │
└────────────────────┬────────────────────────┘
                     │ calls
┌────────────────────▼────────────────────────┐
│                 Services                    │
│  AudioService · HotkeyService · MuteService │
│  ConfigService · StartupService · ThemeService│
│  SchedulerService · SwitchSoundService      │
│  DeviceTriggerService · HidHeadsetService   │
│  AppTriggerService · AppWatcherService      │
└────────────────────┬────────────────────────┘
                     │
┌────────────────────▼────────────────────────┐
│                  Models                     │
│  AppConfig · DeviceProfile · HotkeyDefinition│
└─────────────────────────────────────────────┘
```

Supporting subsystems that cut across layers:

| Subsystem | Location | Purpose |
|---|---|---|
| `TrayService` | `Tray/` | Tray icon lifecycle, context menu, balloon notifications |
| `ProfileSwitchOrchestrator` | root | Serialises all profile switch operations |
| `AppWindowManager` | root | Opens and focuses the Settings window (and navigates to its About / FAQ panels) |
| `MuteService` | `Services/` | Global mute/unmute by scope (mic, speakers, both); manages mute state and plays feedback sounds |
| `SchedulerService` | `Services/` | Per-profile time-of-day schedules with optional advance reminder notifications |
| `ThemeService` | `Services/` | Detects Windows light/dark mode and hot-swaps the app's resource dictionary |
| `SwitchSoundService` | `Services/` | Plays built-in or custom audio cues on profile switch; per-profile and global volume control |
| `DeviceTriggerService` | `Services/` | Watches `AudioService.DevicesChanged`; auto-switches to a profile when its linked device connects |
| `HidHeadsetService` | `Services/` | Reads HID packets from USB wireless headset dongles (Logitech, Corsair, SteelSeries, HyperX) to detect headset power-on/off events |
| `AppTriggerService` | `Services/` | Switches profile when a linked executable launches (only if not already on it); no focus tracking or auto-revert |
| `AppWatcherService` | `Services/` | Polls running process names on a background timer and raises `ProcessLaunched` to support `AppTriggerService` |
| `AppLogger` | `Helpers/` | File-based logging with rotation; `Debug` level writes to stderr only (never to disk) |
| `SessionErrorTracker` | `Helpers/` | Per-session structured error accumulation |
| `IconHelper` | `Helpers/` | ICO loading, image source conversion, security validation |
| `PathSafety` | `Helpers/` | Canonicalizes a path and confirms it stays inside an allowed folder (icons, sounds) before a read/write/delete |
| `WinApi` | `NativeMethods/` | P/Invoke: `RegisterHotKey`, `UnregisterHotKey`, `GlobalAddAtom` |

---

## Key files

| File | Role |
|---|---|
| `App.xaml.cs` | Bootstrap — single-instance check, service wiring, `WM_HOTKEY` / `WM_TASKBARCREATED` message loop |
| `Services/ConfigService.cs` | Load / atomic save of `config.json`; backup-and-recover on corruption |
| `Services/AudioService.cs` | COM audio device enumeration; sets the Console, Multimedia, and Communications roles on every switch (skipping any already correct) |
| `Services/HotkeyService.cs` | Registers global hotkeys via `HwndSource`; maps atom IDs back to profiles and feature hotkeys |
| `Services/MuteService.cs` | Tracks per-scope mute state; toggles Windows audio endpoints and plays feedback sounds |
| `Services/SchedulerService.cs` | Fires switch events on time-of-week schedules; emits reminder events N minutes before switch time |
| `Services/SwitchSoundService.cs` | Resolves and plays profile-switch audio cues using `System.Media.SoundPlayer` |
| `Services/ThemeService.cs` | Listens for `SystemEvents.UserPreferenceChanged` and reads `AppsUseLightTheme` on demand; swaps `LightTheme.xaml` / `DarkTheme.xaml` |
| `Services/DeviceTriggerService.cs` | Maps audio device IDs to profiles; triggers switch on `AudioService.DevicesChanged` |
| `Services/HidHeadsetService.cs` | Opens HID streams to wireless dongle devices; parses vendor-specific packets for headset power state |
| `Services/AppTriggerService.cs` | Maps executable names to profiles; responds to `AppWatcherService` events |
| `Services/AppWatcherService.cs` | Polls `Process.GetProcessesByName()` for each watched executable on a 2s timer; fires `ProcessLaunched` when one appears |
| `Tray/TrayService.cs` | Owns the `TaskbarIcon`; rebuilds the context menu when profiles change |
| `ViewModels/SettingsViewModel.cs` | All settings and profile list logic; fires `SaveDeferred` when settings change |
| `ViewModels/ProfileCardViewModel.cs` | Per-profile bindings, hotkey capture, icon browse, save flash |

---

## Startup sequence

1. Mutex check — second instance exits immediately.
2. `ConfigService.Load()` — reads `config.json`; falls back to `.bak` on corruption; sets `IsFirstRun` if neither exists.
3. Hidden `HwndSource` created — receives `WM_HOTKEY` and `WM_TASKBARCREATED` messages.
4. `AudioService`, `HotkeyService`, `TrayService`, `MuteService`, `SwitchSoundService`, `ThemeService`, `SchedulerService`, `DeviceTriggerService`, `HidHeadsetService`, `AppWatcherService`, `AppTriggerService` initialised.
5. `HotkeyService.RegisterAll(profiles)` registers the profile hotkeys; the Settings-open, Mini Mode, and mute hotkeys are registered separately.
6. A dangling `ActiveProfileId` (set in config but matching no profile) is cleared, then `SchedulerService.EvaluateNow()` runs first (fires any schedule missed while the app was off); only if none fired is the last active profile re-applied via `ProfileSwitchOrchestrator.SwitchToProfile()`, so the Windows default matches the config. The same evaluate-then-restore order runs on wake from sleep (`OnSystemResume`).
7. After the splash animation completes, the tray icon is registered (`ShowIcon`) and the Settings window opens — always on first run, and on later launches unless `StartMinimized` is set (a 6-second fallback guarantees the icon still appears if the splash is interrupted).
8. App runs with `ShutdownMode = OnExplicitShutdown`; only the tray Exit item calls `Application.Shutdown()`.

---

## Profile switch flow

```
Trigger: hotkey press / tray menu click / left-click cycle / schedule / device connect / app launch
         │
         ▼
ProfileSwitchOrchestrator.SwitchToProfile(profile)
  │  (SemaphoreSlim — drops concurrent calls)
  ├─ TrayService.SetSwitchingTooltip("Switching to …")
  ├─ AudioService.ApplyProfileAsync(profile)
  │    ├─ playbackId  → set as default for Console, Multimedia, Communications
  │    └─ recordingId → set as default for Console, Multimedia, Communications
  │         (each role skipped if the device is already its default)
  ├─ ConfigService: ActiveProfileId = profile.Id
  ├─ ProfileSwitched event fired               ← an open Settings window refreshes its active indicator
  ├─ ConfigService.SaveDeferred()
  ├─ TrayService.UpdateIcon(profile)               ← refreshes the tray icon to the active profile
  ├─ TrayService.SetActiveProfile(profile.Id)       ← fast path, no rebuild
  │  (switch lock released here — all feedback below runs outside it)
  ├─ SwitchSoundService.PlayAsync(profile)         ← optional audio cue
  └─ if ShowNotifications → TrayService.ShowBalloon(…)
```

If `ApplyProfileAsync` throws (e.g. PolicyConfig unsupported, audio service down), an interactive switch shows an `ErrorDialog` while a scheduled/background switch shows a balloon instead. A partial success — a device that is missing or whose role-set failed — always surfaces as a balloon warning, never a modal.

---

## Config file

Stored at `%APPDATA%\VibeSwitcher\config.json`. Written atomically: the previous primary is copied to `.bak`, any pre-existing `.tmp` is deleted first (symlink safety), the new content is serialised to `.tmp`, then moved over the primary file. On load, if the primary is corrupt the backup is tried automatically.

Device identities are stored as Windows endpoint IDs (`{0.0.0.00000000}.{…}`) which are stable across reboots, unlike friendly device names.

---

## Adding a new service

1. Define an interface in `Services/` (e.g. `IFooService`).
2. Implement it in a concrete class in `Services/`.
3. Add a fake stub to `VibeSwitcher.Tests/` so existing tests keep compiling.
4. Wire it up in `App.xaml.cs` alongside the other service initialisations.
5. Inject via constructor — ViewModels receive services through `SettingsViewModel`'s constructor, not via service locator.
