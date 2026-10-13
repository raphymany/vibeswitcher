# VibeSwitcher for macOS and Linux (`xplat/`)

The cross-platform VibeSwitcher app, built with Avalonia on .NET 10. The Windows app lives in `../VibeSwitcher/` and
is unaffected by anything here. Design: [`docs/ADR-001-xplat.md`](docs/ADR-001-xplat.md). Per-OS differences:
[`docs/PARITY.md`](docs/PARITY.md).

**Status:** in progress. Done so far: the shared core library, and the Linux and macOS audio engines
(`src/VibeSwitcher.Platform.Linux`, `src/VibeSwitcher.Platform.MacOS`). The app itself arrives in later phases.

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
`scripts/wsl-test.sh` copies the repo into the Linux filesystem (so Windows and Linux builds never share `bin/` and
`obj/`) and runs the tests there; add `--audio` to also run the Linux audio tests against a real, throwaway PulseAudio
server (downloaded into your home folder if it isn't installed — no sudo needed).

CI (`.github/workflows/ci-xplat.yml`) runs everything on Ubuntu, macOS (Apple Silicon and Intel) and Windows, plus the
Linux audio tests against PipeWire, PulseAudio, and Ubuntu 22.04 using pactl's text output (`scripts/run-linux-audio-tests.sh`),
and the macOS audio tests against real Core Audio on Apple Silicon and Intel, with the BlackHole virtual audio devices.

## Branches

Until the first Mac/Linux version is finished and tested, `main` stays exactly what's released. Each phase's PR
merges into the holding branch `xplat/dev` (branch new phases from it). One final PR brings `xplat/dev` into `main`
for the release; after that, Mac/Linux work merges straight into `main` like everything else. If `main` changes in
the meantime (e.g. a Windows fix release), merge `main` into `xplat/dev` so the linked files stay current.

## Rules of the road

- **Never edit the Windows app to make `xplat/` compile.** Link the file, add a shim member, or fork it and list the
  fork in `forks.txt`.
- When `check-forks.sh` flags a change, port it into the fork (or confirm it doesn't apply), then run
  `scripts/check-forks.sh --update`.
