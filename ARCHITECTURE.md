# VibeSwitcher — Architecture

VibeSwitcher is a Windows system tray application built with WPF and .NET 8. It switches the Windows default audio device (both Multimedia and Communications roles) by activating a saved profile, triggered by a global hotkey, tray menu click, or left-click cycle.

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
| `AppTriggerService` | `Services/` | Switches profile when a linked executable launches or gains focus; reverts when the app exits |
| `AppWatcherService` | `Services/` | Polls running processes and foreground window to support `AppTriggerService` |
| `AppLogger` | `Helpers/` | File-based logging with rotation; `Debug` level writes to stderr only (never to disk) |
| `SessionErrorTracker` | `Helpers/` | Per-session structured error accumulation |
| `IconHelper` | `Helpers/` | ICO loading, image source conversion, security validation |
| `WinApi` | `NativeMethods/` | P/Invoke: `RegisterHotKey`, `UnregisterHotKey`, `GlobalAddAtom` |

---

## Key files

| File | Role |
|---|---|
| `App.xaml.cs` | Bootstrap — single-instance check, service wiring, `WM_HOTKEY` / `WM_TASKBARCREATED` message loop |
| `Services/ConfigService.cs` | Load / atomic save of `config.json`; backup-and-recover on corruption |
| `Services/AudioService.cs` | COM audio device enumeration; sets both Multimedia and Communications roles on every switch |
| `Services/HotkeyService.cs` | Registers global hotkeys via `HwndSource`; maps atom IDs back to profiles and feature hotkeys |
| `Services/MuteService.cs` | Tracks per-scope mute state; toggles Windows audio endpoints and plays feedback sounds |
| `Services/SchedulerService.cs` | Fires switch events on time-of-week schedules; emits reminder events N minutes before switch time |
| `Services/SwitchSoundService.cs` | Resolves and plays profile-switch audio cues using `System.Media.SoundPlayer` |
| `Services/ThemeService.cs` | Polls the Windows registry for `AppsUseLightTheme`; swaps `LightTheme.xaml` / `DarkTheme.xaml` |
| `Services/DeviceTriggerService.cs` | Maps audio device IDs to profiles; triggers switch on `AudioService.DevicesChanged` |
| `Services/HidHeadsetService.cs` | Opens HID streams to wireless dongle devices; parses vendor-specific packets for headset power state |
| `Services/AppTriggerService.cs` | Maps executable names to profiles; responds to `AppWatcherService` events |
| `Services/AppWatcherService.cs` | Polls `Process.GetProcesses()` and `GetForegroundWindow()` on a background timer |
| `Tray/TrayService.cs` | Owns the `TaskbarIcon`; rebuilds the context menu when profiles change |
| `ViewModels/SettingsViewModel.cs` | All settings and profile list logic; fires `SaveImmediate` on every property change |
| `ViewModels/ProfileCardViewModel.cs` | Per-profile bindings, hotkey capture, icon browse, save flash |

---

## Startup sequence

1. Mutex check — second instance exits immediately.
2. `ConfigService.Load()` — reads `config.json`; falls back to `.bak` on corruption; sets `IsFirstRun` if neither exists.
3. Hidden `HwndSource` created — receives `WM_HOTKEY` and `WM_TASKBARCREATED` messages.
4. `AudioService`, `HotkeyService`, `TrayService`, `MuteService`, `SwitchSoundService`, `ThemeService`, `SchedulerService`, `DeviceTriggerService`, `HidHeadsetService`, `AppWatcherService`, `AppTriggerService` initialised.
5. `HotkeyService.RegisterAll(profiles)` — registers all profile hotkeys, the Settings open hotkey, and all mute hotkeys.
6. `ProfileSwitchOrchestrator.RestoreActiveProfile()` — re-applies the last active profile so the Windows default matches what the config says.
7. If `IsFirstRun` → `OpenSettingsWindow()`.
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
  ├─ AudioService.ApplyProfile(profile)
  │    ├─ SetDefaultAudioEndpoint(playbackId, Multimedia)
  │    ├─ SetDefaultAudioEndpoint(playbackId, Communications)
  │    ├─ SetDefaultAudioEndpoint(recordingId, Multimedia)
  │    └─ SetDefaultAudioEndpoint(recordingId, Communications)
  ├─ SwitchSoundService.PlayAsync(profile)         ← optional audio cue
  ├─ ConfigService: ActiveProfileId = profile.Id → SaveImmediate()
  ├─ TrayService.SetActiveProfile(profile.Id)       ← fast path, no rebuild
  ├─ TrayService.UpdateIcon(profile)               ← re-applies the mute badge if muted
  └─ if ShowNotifications → TrayService.ShowBalloon(…)
```

If a device is missing, `ApplyProfile` returns partial-success flags and the orchestrator shows an `ErrorDialog` instead of a balloon.

---

## Config file

Stored at `%APPDATA%\VibeSwitcher\config.json`. Written atomically: serialised to `.tmp`, then moved over the primary file; the previous primary is copied to `.bak` first. On load, if the primary is corrupt the backup is tried automatically.

Device identities are stored as Windows endpoint IDs (`{0.0.0.00000000}.{…}`) which are stable across reboots, unlike friendly device names.

---

## Adding a new service

1. Define an interface in `Services/` (e.g. `IFooService`).
2. Implement it in a concrete class in `Services/`.
3. Add a fake stub to `VibeSwitcher.Tests/` so existing tests keep compiling.
4. Wire it up in `App.xaml.cs` alongside the other service initialisations.
5. Inject via constructor — ViewModels receive services through `SettingsViewModel`'s constructor, not via service locator.
