# VibeSwitcher

A lightweight Windows tray app for switching audio devices instantly — no digging through Sound Settings required.

![Windows](https://img.shields.io/badge/Windows-10%2F11-0078D4?logo=windows)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)
![License](https://img.shields.io/badge/license-MIT-green)

## Features

- **Profiles** — group a playback device + microphone into a named profile (e.g. "Headset", "Desktop Speakers")
- **Global hotkeys** — switch profiles without leaving your current app
- **System tray** — lives quietly in the tray; left-click cycles profiles, right-click picks one directly
- **Both audio roles** — sets Default Device *and* Default Communications Device on every switch, so apps like Discord don't fall out of sync
- **Custom icons** — browse a built-in gallery or point to your own `.ico` file per profile
- **Friendly device names** — give each audio device a short alias (e.g. "GoXLR") shown throughout the app instead of the raw Windows device name
- **Per-profile schedule** — automatically activate a profile at a set time and day of week, with an optional reminder notification before it fires
- **Switch sounds** — play a configurable tone on every profile switch; set a global default and override it per profile
- **Panic / deafen hotkey** — instantly mute your mic, speakers, or both from anywhere; press again to unmute; tray flashes to show mute state
- **Pinned profiles** — star a profile to keep it at the top of the tray menu
- **Profile notes** — attach a short description to any profile, visible on its card
- **Filter chips** — quickly narrow the profile list by mode, pinned, active, hotkey, sound, schedule, and more
- **Auto-switch on connect** — switch to a profile the moment a USB or 3.5mm audio device is plugged in, and revert when it is removed; supported wireless headsets also detect power-on/off through the USB dongle without needing to unplug anything
- **Drag-and-drop reorder** — rearrange profile cards by dragging them
- **Import / export** — back up your full configuration to a `.json` file and restore it on any machine
- **Light / dark / system theme** — follows the Windows OS theme automatically, or lock it to light or dark
- **Persistent config** — profiles and settings survive reboots; atomic writes with automatic backup

## Requirements

- Windows 10 or 11 (x64)
- No additional runtime required — the release build is self-contained

## Installation

No installer yet — download `VibeSwitcher-vX.X.X-win-x64.zip` from the [latest release](https://github.com/raphymany/vibeswitcher/releases/latest), extract, and run `VibeSwitcher.exe`.

> **Note:** Windows SmartScreen may warn on first run since the app is not yet code-signed. Click "More info" → "Run anyway" to proceed.

## Building from source

**Requirements:** Windows 10/11, [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

```bash
git clone https://github.com/raphymany/vibeswitcher.git
cd vibeswitcher
dotnet restore VibeSwitcher/VibeSwitcher.csproj
dotnet build VibeSwitcher/VibeSwitcher.csproj -c Release
```

The compiled output lands in `VibeSwitcher/bin/Release/net8.0-windows/`.

**Run directly (framework-dependent):**
```bash
dotnet run --project VibeSwitcher/VibeSwitcher.csproj
```

**Publish a self-contained single-file build:**
```bash
dotnet publish VibeSwitcher/VibeSwitcher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/
```

The `publish/` folder contains a single `VibeSwitcher.exe` with no external dependencies.

See [CONTRIBUTING.md](CONTRIBUTING.md) for IDE setup and project structure.

## Usage

1. Run `VibeSwitcher.exe` — it appears in your system tray
2. Right-click the tray icon → **Settings** to create profiles
3. For each profile, choose a playback device and/or microphone
4. Optionally assign a global hotkey and a custom icon
5. Switch profiles via the tray menu or your hotkey

## Known Limitations

- **Remote Desktop (RDP):** Global hotkeys registered with `RegisterHotKey` act on the *local* machine, not the remote session. Hotkeys will not switch audio devices on the remote end while connected via RDP.

## Wireless headset detection

Supported wireless headsets auto-switch the moment you power them on or off —
no unplugging needed. This works even for headsets with a USB dongle that
stays plugged in permanently (like Logitech LIGHTSPEED), where Windows
can't detect the power state on its own.

**Supported headsets**

| Model                   | VID  | PID  |
|-------------------------|------|------|
| Logitech PRO X Wireless | 046D | 0ABA |

**Don't see your headset?** Open a GitHub issue using the
[Add wireless headset](https://github.com/raphymany/vibeswitcher/issues/new?template=add-headset.yml)
template — it walks you through finding the two IDs we need from Device Manager.

> Wired headsets and 3.5mm jacks are already handled automatically.

## License

MIT — see [LICENSE](LICENSE) for details.
