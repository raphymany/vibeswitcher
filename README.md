# VibeSwitcher

A lightweight Windows tray app for switching audio devices instantly — no digging through Sound Settings required.

![Windows](https://img.shields.io/badge/Windows-10%2F11-0078D4?logo=windows)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)
![License](https://img.shields.io/badge/license-MIT-green)

## Features

- **Profiles** — group a playback device + microphone into a named profile (e.g. "Headset", "Desktop Speakers")
- **Global hotkeys** — switch profiles without leaving your current app
- **System tray** — lives quietly in the tray, right-click to switch instantly
- **Both audio roles** — sets Default Device *and* Default Communications Device on every switch, so apps like Discord don't fall out of sync
- **Custom icons** — assign a `.ico` file to each profile for quick visual identification
- **Persistent config** — profiles and hotkeys survive reboots

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

## License

MIT — see [LICENSE](LICENSE) for details.
