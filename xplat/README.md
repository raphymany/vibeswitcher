# VibeSwitcher for macOS and Linux (`xplat/`)

The cross-platform VibeSwitcher app, built with Avalonia on .NET 10. The Windows app lives in `../VibeSwitcher/` and
is unaffected by anything here. Design: [`docs/ADR-001-xplat.md`](docs/ADR-001-xplat.md). Per-OS differences:
[`docs/PARITY.md`](docs/PARITY.md).

**Status:** foundation only. The shared core library and its tests exist; the app itself arrives in later phases.

## Build and test

Requires the .NET 10 SDK (`xplat/global.json`).

```bash
cd xplat
dotnet test VibeSwitcher.XPlat.sln
bash scripts/check-forks.sh        # are the copied files still in sync with the Windows originals?
```

Run `dotnet` from inside `xplat/`: the SDK is picked from the nearest `global.json`, so building
`xplat/VibeSwitcher.XPlat.sln` from the repo root uses the Windows app's SDK 8 and fails (NETSDK1045).

On Windows, run the same commands from Git Bash or PowerShell. To run on Linux from a Windows PC, use WSL (Ubuntu):
copy the repo into the Linux filesystem first (e.g. `rsync -a --exclude bin --exclude obj …`). This keeps the
Windows and Linux builds from sharing `bin/` and `obj/`.

CI (`.github/workflows/ci-xplat.yml`) runs everything on Ubuntu, macOS (Apple Silicon and Intel) and Windows.

## Rules of the road

- **Never edit the Windows app to make `xplat/` compile.** Link the file, add a shim member, or fork it and list the
  fork in `forks.txt`.
- When `check-forks.sh` flags a change, port it into the fork (or confirm it doesn't apply), then run
  `scripts/check-forks.sh --update`.
