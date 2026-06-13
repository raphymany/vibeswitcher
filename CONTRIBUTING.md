# Contributing to VibeSwitcher

Thanks for your interest in contributing! VibeSwitcher is a personal project maintained by one developer, so PRs are welcome but response time is best-effort and changes must align with the roadmap in `BACKLOG.md`. Opening an issue to discuss your idea before writing code is strongly recommended — it avoids wasted effort if the change doesn't fit the project's direction.

This document covers how to set up the project, the coding conventions used, and the pull request process.

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

- **C# 12 (the .NET 8 SDK default), .NET 8** — use file-scoped namespaces, primary constructors where they reduce noise, and `nullable enable`.
- **No comments on obvious code** — names should be self-explanatory; add a comment only when the *why* is non-obvious (a Windows quirk, a workaround, a hidden invariant).
- **Error handling at boundaries** — catch at service/UI call sites, log via the injected `_logger` (`IAppLogger`) — or `AppLog` in static contexts — and record structured errors via `_errorTracker.Record(ErrorCode.X, ...)` (`ISessionErrorTracker`), or `AppErrors.Record(...)` from static contexts. Don't swallow exceptions silently.
- **Atomic config writes** — go through `ConfigService.SaveDeferred()` (UI-thread snapshot + background write) or `SaveImmediate()` (startup, exit flush, import); never write `config.json` directly.
- **Icon and sound paths** — custom icon and switch-sound files must live inside `ConfigService.IconsDir` / `SoundsDir`; `IconHelper.LoadIcon` and the config-import guard enforce this via `PathSafety`.

## Pull request process

1. Open an issue first to discuss the change — PRs without a corresponding issue may be closed if they don't align with the project's direction.
2. Fork the repo and create a branch from `main` (e.g. `fix/hotkey-conflict`, `feat/dark-mode`).
3. Keep each PR focused on one concern — separate bug fixes from feature additions.
4. Make sure the project builds cleanly: `dotnet build VibeSwitcher/VibeSwitcher.csproj -c Release`.
5. Open a PR against `main` with a clear title and description of what changed and why. Update `CHANGELOG.md` in your branch as part of the PR (before it is merged).

## What we do NOT accept

- **Changes to the atomic config write pattern** — `ConfigService.SaveDeferred()` / `SaveImmediate()` exist to prevent partial-write corruption; workarounds or direct file writes are rejected.
- **New external API or NuGet dependencies** without prior discussion in an issue. The dependency footprint is intentionally small.
- **Hotkey or audio switching behaviour changes** that could silently affect the user's active audio device without an explicit profile switch action.
- **Features that require elevated privileges** (UAC, admin rights). The app runs as a normal user by design.
- **PRs that skip the test suite** or break existing tests. Run `dotnet test` before submitting.

## Reporting bugs

Open an issue at [github.com/raphymany/vibeswitcher/issues](https://github.com/raphymany/vibeswitcher/issues).

Include:
- Windows version and audio device names
- Steps to reproduce
- Relevant entries from `%APPDATA%\VibeSwitcher\error.log`, or open **Settings → Logs → View session log** (no sensitive data is included)
