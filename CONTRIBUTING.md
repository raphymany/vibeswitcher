# Contributing to VibeSwitcher

Thanks for your interest in contributing! This document covers how to set up the project, the coding conventions used, and the pull request process.

---

## Prerequisites

- **Windows 10 or 11** — the app uses Windows-only APIs (COM audio, WinAPI hotkeys, system tray)
- **.NET 8 SDK** — [download](https://dotnet.microsoft.com/download/dotnet/8.0)
- **IDE**: Visual Studio 2022 (Community or higher) or JetBrains Rider

## Getting started

```bash
git clone https://github.com/raphymany/vibeswitcher.git
cd vibeswitcher
dotnet restore VibeSwitcher/VibeSwitcher.csproj
```

Open `VibeSwitcher.sln` in Visual Studio or Rider and press **F5** to run.

The app starts in the system tray. Right-click the tray icon → **Settings** to configure profiles.

Config and logs are written to `%APPDATA%\VibeSwitcher\`.

## Project layout

| Path | Purpose |
|---|---|
| `VibeSwitcher/Models/` | Plain data classes (`DeviceProfile`, `AppConfig`, `HotkeyDefinition`) |
| `VibeSwitcher/Services/` | Business logic (`AudioService`, `HotkeyService`, `ConfigService`) |
| `VibeSwitcher/ViewModels/` | WPF binding layer (`SettingsViewModel`, `ProfileCardViewModel`) |
| `VibeSwitcher/Views/` | XAML windows and dialogs |
| `VibeSwitcher/Tray/` | System-tray lifecycle (`TrayService`) |
| `VibeSwitcher/Helpers/` | Utilities (`IconHelper`, `AppLogger`, `SessionErrorTracker`) |
| `VibeSwitcher/NativeMethods/` | P/Invoke declarations (`WinApi`) |

## Coding conventions

- **C# 12, .NET 8** — use file-scoped namespaces, primary constructors where they reduce noise, and `nullable enable`.
- **No comments on obvious code** — names should be self-explanatory; add a comment only when the *why* is non-obvious (a Windows quirk, a workaround, a hidden invariant).
- **Error handling at boundaries** — catch at service/UI call sites, log with `AppLogger`, record structured errors via `SessionErrorTracker.Record(ErrorCode.X, ...)`. Don't swallow exceptions silently.
- **Atomic config writes** — always use `ConfigService.SaveImmediate()`, never write `config.json` directly.
- **Icon paths** — icon files must live inside `ConfigService.IconsDir`; `IconHelper.LoadIcon` enforces this.

## Pull request process

1. Fork the repo and create a branch from `main` (e.g. `fix/hotkey-conflict`, `feat/dark-mode`).
2. Keep each PR focused on one concern — separate bug fixes from feature additions.
3. Make sure the project builds cleanly: `dotnet build VibeSwitcher/VibeSwitcher.csproj -c Release`.
4. Update `CHANGELOG.md` under `[Unreleased]` with a one-line entry for your change.
5. Open a PR against `main` with a clear title and description of what changed and why.

## Reporting bugs

Open an issue at [github.com/raphymany/vibeswitcher/issues](https://github.com/raphymany/vibeswitcher/issues).

Include:
- Windows version and audio device names
- Steps to reproduce
- The diagnostic info from **About → Copy Diagnostic Info** (no sensitive data is included)
