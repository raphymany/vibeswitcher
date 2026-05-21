# VibeSwitcher — Architecture

VibeSwitcher is a Windows system tray application built with WPF and .NET 8. It switches the Windows default audio device (both Multimedia and Communications roles) by activating a saved profile, triggered by a global hotkey, tray menu click, or left-click cycle.

---

## Layer overview

```
┌─────────────────────────────────────────────┐
│                  Views (XAML)               │
│  SettingsWindow · HelpDialog · AlertDialog  │
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
│  AudioService · HotkeyService              │
│  ConfigService · StartupService            │
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
| `ProfileSwitchOrchestrator` | `App.xaml.cs` area | Serialises all profile switch operations |
| `AppWindowManager` | `App.xaml.cs` area | Opens and focuses Settings / About windows |
| `AppLogger` | `Helpers/` | File-based logging with rotation |
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
| `Services/HotkeyService.cs` | Registers global hotkeys via `HwndSource`; maps atom IDs back to profiles |
| `Tray/TrayService.cs` | Owns the `TaskbarIcon`; rebuilds the context menu when profiles change |
| `ViewModels/SettingsViewModel.cs` | All settings and profile list logic; fires `SaveImmediate` on every property change |
| `ViewModels/ProfileCardViewModel.cs` | Per-profile bindings, hotkey capture, icon browse, save flash |

---

## Startup sequence

1. Mutex check — second instance exits immediately.
2. `ConfigService.Load()` — reads `config.json`; falls back to `.bak` on corruption; sets `IsFirstRun` if neither exists.
3. Hidden `HwndSource` created — receives `WM_HOTKEY` and `WM_TASKBARCREATED` messages.
4. `AudioService`, `HotkeyService`, `TrayService` initialised.
5. `HotkeyService.RegisterAll(profiles)` — registers all profile hotkeys and the Settings hotkey.
6. `ProfileSwitchOrchestrator.RestoreActiveProfile()` — re-applies the last active profile so the Windows default matches what the config says.
7. If `IsFirstRun` → `OpenSettingsWindow()`.
8. App runs with `ShutdownMode = OnExplicitShutdown`; only the tray Exit item calls `Application.Shutdown()`.

---

## Profile switch flow

```
Trigger: hotkey press / tray menu click / left-click cycle
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
  ├─ ConfigService: ActiveProfileId = profile.Id → SaveImmediate()
  ├─ TrayService.SetActiveProfile(profile.Id)   ← fast path, no rebuild
  ├─ TrayService.UpdateIcon(profile)
  ├─ TrayService.FlashSwitch(profile)           ← brief icon pulse
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
