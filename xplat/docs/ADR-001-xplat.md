# ADR-001 — How VibeSwitcher comes to macOS and Linux

**Status:** accepted (2026-10-10) · **Issue:** #115

## Decision

1. **The Windows app is not touched.** It stays WPF on .NET 8 in `VibeSwitcher/`. macOS and Linux get **one shared
   app** built with [Avalonia UI](https://avaloniaui.net/) in `xplat/`: one codebase, separate downloads per OS
   (`.dmg`, AppImage, `.deb`). Moving Windows onto the shared app later is possible, but optional.
2. **Same repository.** One issue tracker and one Releases page. It also lets the two apps share source files.
3. **Logic is shared by linking, not copying.** `xplat/` compiles the Windows app's own logic files straight from
   `VibeSwitcher/` (`<Compile Include="$(WinSrc)…" Link="…"/>`), so a fix lands in both apps. That covers models,
   config, schedules, device rebinding, headset detection and switch-sound synthesis. Nothing under `VibeSwitcher/`
   or `VibeSwitcher.Tests/` is edited for the port.
4. **WPF shims (`src/VibeSwitcher.Core/Compat/`).** A few linked files call WPF by name: the Dispatcher,
   `DispatcherTimer`, `CommandManager` and `System.Media.SoundPlayer`. Tiny internal stand-ins forward these to
   the Avalonia UI thread and a platform WAV player. Only the members the linked code uses exist. If a Windows
   file starts using another WPF API, the xplat build fails, so drift shows up as a compile error rather than as
   silently different behaviour. With nothing registered (unit tests), linked code runs inline, as it does in the
   Windows tests.
5. **Forks are tracked.** Files that can't be linked because they are Win32/WPF at heart are copied, adapted and
   listed in `forks.txt` together with the git hash of the original. `scripts/check-forks.sh` runs in CI and fails
   when an original changes. The change then gets ported, or confirmed not to apply, before running `--update`.
6. **.NET 10 (LTS)** for `xplat/`, pinned by `xplat/global.json`. A root `global.json` keeps the Windows app on
   SDK 8.0.4xx, so installing SDK 10 changes nothing for it.
7. **Windows-shaped device keys.** The macOS and Linux audio backends report hardware keys in the Windows form
   (`USB\VID_xxxx&PID_xxxx&MI_xx`). The linked rebind and HID-headset matching logic then works unchanged.
8. **Avalonia on Linux runs through X11 (XWayland on Wayland sessions).** This keeps window positioning,
   always-on-top and Mini Mode position restore, which native Wayland forbids. Global hotkeys on Wayland use a
   separate path; see `PARITY.md`.
9. **Config files are portable.** The `config.json` schema is identical, and `HotkeyDefinition` keeps Windows
   virtual-key codes as the canonical key. Device IDs are OS-specific, so profiles imported from another OS show
   their devices as missing until they are re-picked.

## Known trade-offs

- **Fork tracking is per whole file.** `forks.txt` records the hash of the entire original, so an unrelated edit
  (e.g. to COM code in `AudioService.cs`) also flags the fork. Review it, confirm, and run `--update`. That
  costs a minute, and in exchange no real change gets missed.
- **`PathSafety` (linked) compares paths case-insensitively.** That's correct on Windows and macOS (APFS is
  case-insensitive by default). On case-sensitive Linux filesystems, a sibling folder that differs only in letter
  case (`sounds` vs `Sounds`) would count as "inside" the managed folder. Exploiting this needs write access to
  the user's config folder; it's tracked for the Linux phase.
- **`CommandManager` shim.** WPF re-checks every command's CanExecute on any input; Avalonia doesn't. The Windows
  app has one command with a can-execute rule, and it raises the change itself. The app layer re-invalidates
  where needed.

## Layout

```
xplat/
  src/VibeSwitcher.Core/          UI-free: linked Windows files + Compat shims + forks (Contracts/, Models/) + Keys/
  tests/VibeSwitcher.Core.Tests/  linked Windows test files + tests for forks and shims
  (later) src/VibeSwitcher.Platform.Linux, src/VibeSwitcher.Platform.MacOS, src/VibeSwitcher.App,
          tests/*Integration*, tools/, packaging/
  docs/                           this ADR, PARITY.md, (later) friend test script
  forks.txt, scripts/check-forks.sh
```

## UI port mapping rules (for the Avalonia phases)

| WPF | Avalonia |
|---|---|
| `Trigger IsMouseOver/IsPressed/IsChecked/IsEnabled` | `:pointerover` / `:pressed` / `:checked` / `:disabled` selectors |
| `DataTrigger Binding=X Value=True` | `Classes.x="{Binding X}"` + a `.x` selector |
| `MultiTrigger` | combined selector, or a bool view-model property |
| `Visibility` (`Hidden` keeps space) | `IsVisible` (`Hidden` → `Opacity=0` + `IsHitTestVisible=False`) |
| Hover fades (Storyboards) | `Transitions` |
| `DropShadowEffect` | `BoxShadow` |
| `WindowChrome` + `TitleBar` | `VsWindow` (custom chrome; macOS keeps native traffic lights, Linux uses the Windows-style buttons) |
| Synchronous `ShowDialog` | Avalonia dialogs (centred on the owner, no taskbar entry, global button styles — CLAUDE.md §6), awaited by the app's own code. The shared view models keep calling `IDialogService` synchronously: `DialogService` waits in a nested message loop (`Dispatcher.PushFrame`), exactly what WPF's `ShowDialog` does, so `SettingsViewModel` / `ProfileCardViewModel` are linked unchanged instead of rewritten as async (decided in phase 7; first planned as async forks) |
| Negative `BeginTime` logo phases | a single-clock `LogoAnimator` |
| Segoe UI / Consolas / Courier New | bundled Selawik / Cascadia Mono / Cousine (metric-compatible, OFL) |
