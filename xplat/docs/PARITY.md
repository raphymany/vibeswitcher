# Feature parity: Windows vs macOS vs Linux

The goal is the same function, behaviour and design on every OS. This table records **where the operating system
forces a difference**, and what we do about it. It's kept up to date as each phase lands. ✅ = same as Windows.

| Feature | macOS | Linux (X11) | Linux (Wayland session) |
|---|---|---|---|
| Switch default output/input, mute, test tone, mic meter | ✅ | ✅ | ✅ |
| Re-bind a profile when a USB device gets a new ID | ✅ | ✅ | ✅ |
| "Show disabled devices" | Hidden: macOS has no "disabled device" state | Hidden: Linux has no "disabled" state (a sound card set to the "Off" profile simply has no devices) | Same as X11 |
| Unplugged-jack devices listed | No: macOS hides them | ✅ | ✅ |
| Global hotkeys | ✅ (Option-only combos are blocked by macOS 15) | ✅ | Through the system "Global Shortcuts" prompt (KDE, GNOME 48+). The desktop asks the user to approve, the user may pick a different key, and there's no conflict test. Older GNOME: VibeSwitcher adds GNOME keyboard shortcuts with the user's consent. |
| Hotkey labels | ⌃⌥⇧⌘ glyphs (Mac convention) | "Super" instead of "Win" | Same as X11 |
| Window buttons | Native traffic lights; everything inside the window is identical | Windows-style buttons | Same as X11 |
| Tray menu | Menu-bar icon opens the **same styled dark menu** | Native desktop menu: can't be styled — each profile is one line with its icon (pinned ones marked ★, the active one ticked); the shortcuts are listed in the icon's hover text as on Windows. The last item is "Quit" (Windows: "Exit") | Same as X11. **Stock GNOME (e.g. Fedora) has no tray** without the AppIndicator extension; VibeSwitcher detects this, explains it, and won't hide on close. |
| Notification banners | Styled in-app banner (native once the app is signed) | ✅ Native | ✅ Native |
| Mini Mode (always on top, translucent, remembers position) | ✅ | ✅ | ✅ via XWayland |
| Start at login | ✅ (macOS shows a "background item added" notice) | ✅ | ✅ |
| App launch triggers | ✅ (`.app` bundles) | ✅ (+ Flatpak / Snap apps) | ✅ |
| Wireless headset power on/off detection | ✅ | Needs a permission rule: installed automatically by the `.deb`, one click for the AppImage. When auto-switch is turned on and a supported receiver can't be read, VibeSwitcher explains it and offers "Allow Access" (one password prompt); Windows has no such step | Same as X11 |
| App-trigger picker | Installed = the app bundles in the Applications folders; Running = running apps; icons from each app's `.icns` (an app that keeps its icon only in an asset catalog shows none); "Browse for an app..." picks an `.app` | Installed = the app menu's apps (including Flatpak and Snap); Running adds AppImages, programs under `/opt` or the home folder and Windows games under Wine; icons from the icon theme (PNG or SVG); "Browse for a program..." takes a program, AppImage or `.desktop` file. "Fully ended in System Monitor" instead of Task Manager | Same as X11 |
| Start-up splash | ✅ | ✅ (with no compositor — plain X11 — the card fills its window, square and without a shadow, as the system can't show see-through windows) | Same as X11 |
| Gallery emoji icons | The same single-colour outline style as Windows, from the bundled Noto Emoji font (slightly different artwork; no system emoji font needed) | Same as macOS | Same as macOS |
| Supported-headsets badges | ✅ (the ✅ / ⚠️ after "Tested" / "Untested" are drawn as the same single-colour outlines WPF shows, instead of relying on an emoji font) | ✅ (also where Linux has no emoji font) | Same as X11 |
| First launch | Unsigned for now: needs "Open Anyway" once in System Settings › Privacy & Security | ✅ | ✅ |
| "Sound Settings" button | Opens System Settings › Sound | Opens the desktop's own sound settings (GNOME, KDE, Cinnamon, MATE), or the PulseAudio volume control (`pavucontrol`) where there are none; the Windows-only "legacy sound panel" option doesn't apply | Same as X11 |
| Settings, About and FAQ wording | Mac terms where Windows is named: "Open at login", "menu bar", "Show in Finder", the config path under `~/Library/Application Support`; About lists the Mac building blocks | "Start at login", "tray", the config path under `~/.config` (or `$XDG_CONFIG_HOME`); About lists the Linux building blocks | Same as X11 |
| Profile cards' drag-and-drop | Not offered (as on Windows: the Windows app has drop handlers but never starts a drag) | Same | Same |
| Uninstall from inside the app | Moves the app to the Trash (+ optional data) | AppImage: deletes it; `.deb`: removes the package | Same as X11 |

## Install notes

- **macOS:** a `.dmg` per Mac type (Apple Silicon or Intel) with `VibeSwitcher.app`, dragged to Applications. Until the
  app is signed with an Apple developer account it's "ad-hoc" signed: the first launch needs **System Settings ›
  Privacy & Security › Open Anyway** once (the notarisation script is ready for when an account exists). macOS asks for
  microphone permission the first time the mic meter runs, with VibeSwitcher's own explanation. While it's ad-hoc
  signed, macOS treats every update as a new app: the microphone permission is asked again, and the Login Items entry
  may show a generic name. Needs **macOS 14 (Sonoma) or newer** (what .NET 10 supports).
- **Linux:** an AppImage (download, make executable, run — nothing to install) or a `.deb` for Ubuntu, Debian, Mint and
  similar (`sudo apt install ./vibeswitcher_….deb`). The `.deb` adds the app-menu entry and icon, the `vibeswitcher`
  command, and the wireless-headset permission rule, and pulls in what the app needs (the sound tools and a few X11
  libraries every desktop has). 64-bit PCs for now.
- **Version:** `vibeswitcher --version` (or `VibeSwitcher.app/Contents/MacOS/VibeSwitcher --version`).

## Look notes

- **Same colours, icons and controls.** The Mac/Linux app reads the Windows app's own theme colours (light and dark)
  and icon shapes at start-up, so changes to those carry over by themselves. Its buttons, switches, check boxes and
  dialogs are ported one for one, and a change to any of their Windows originals is flagged so the port follows.
- **Light/dark** follows the system on macOS and on Linux desktops that publish their setting (GNOME, KDE), or the
  user's choice, as on Windows.
- **Fonts are bundled** so text looks the same on every system: Open Sans in place of Segoe UI (Windows' own font,
  which can't be shipped), Cascadia Mono in place of Consolas, Cousine in place of Courier New. Open Sans is a touch
  wider than Segoe UI; Selawik (Microsoft's open font with Segoe UI's exact measurements) can replace it in one place.
- **Window frame.** macOS: the native red/yellow/green buttons over the window's own title bar, native rounded corners
  and shadow. Linux: the Windows app's title bar and buttons, drawn by the app (Linux desktops differ too much for one
  native look), with square corners and the orange outline.
- **Screenshots** of every dialog in both themes and both frames are taken on every change and checked against saved
  ones, so the look can't drift unnoticed. CI also builds a side-by-side page (the "ui-parity" download: open
  `parity.html`) with each dialog as the Windows app draws it next to its Linux and macOS versions.

## Hotkey notes

- **Same shortcuts everywhere.** Shortcuts are saved in the Windows form, so a config moves between systems unchanged.
  On a Mac, Ctrl/Alt/Shift/Win are ⌃ Control / ⌥ Option / ⇧ Shift / ⌘ Command (Win ↔ ⌘, the usual mapping).
- **Taken shortcuts are reported** on macOS and on Linux X11 the same way as on Windows (including the "is it in use by
  another app?" check), and the same shortcut twice inside VibeSwitcher is a conflict, as on Windows. One macOS
  caveat: the system's own shortcuts (⌘Space, ⌘⇧3, Mission Control's ⌃↑…) aren't reported as taken — they register
  fine but macOS keeps the key press, so avoid them.
- **Linux X11:** shortcuts keep working with Caps Lock or Num Lock on, and after a keyboard-layout change.
- **Keys a system can't use:** macOS keeps the media keys (volume, play/pause), has no Print Screen or F21–F24, and
  since macOS 15 refuses shortcuts that use only ⌥ Option (or ⌥⇧); Linux supports the usual keys including F13–F24
  and media keys (GNOME and KDE already use the bare media keys themselves, so on their own those show as "in use";
  with a modifier they're fine). Choosing an unusable one shows a registration error saying why, instead of silently
  doing nothing.
- **macOS needs no special permission** for shortcuts (no Accessibility prompt).
- **Linux on Wayland** doesn't let apps listen for keys, so VibeSwitcher asks the desktop instead:
  - **KDE Plasma, GNOME 48+ and others with the "Global Shortcuts" service:** the desktop shows its own dialog listing
    VibeSwitcher's shortcuts with the suggested keys. The user approves them there and may choose different keys. The
    dialog only comes back when the set of shortcuts changes (a new or renamed profile shortcut, say), not on every
    save. If the user declines, VibeSwitcher doesn't ask again until its shortcuts change or it's restarted.
  - **Older GNOME:** with the user's permission, VibeSwitcher adds its shortcuts to GNOME's own keyboard settings
    (Settings › Keyboard › Custom Shortcuts, named "VibeSwitcher: …"). They run VibeSwitcher's command line, so they
    work the same, and pressing one while VibeSwitcher is closed starts it. Only VibeSwitcher's own entries are ever
    changed. They're removed again when this is turned off, when VibeSwitcher is uninstalled from its own settings,
    and whenever it uses another way (logging into "GNOME on Xorg", or after upgrading to GNOME 48).
  - **Anywhere else:** shortcuts are off, and VibeSwitcher explains how to bind keys to its command line in the
    desktop's own settings.
  - In all three cases there's no "is it used by another app?" check: the desktop decides.

## App launch trigger notes

- **Checked every 2 seconds, like Windows.** An app that's already running when it's linked to a profile doesn't
  switch until it's closed and opened again, as on Windows.
- **Matched more precisely than on Windows.** Windows recognises an app by its program name. macOS and Linux
  recognise it by what it really is: a Mac app (including its helper processes), the program file on Linux, a
  Flatpak or Snap app by its app ID, or the AppImage file it was started from. Program names are still the fallback,
  so apps linked in a config imported from Windows (e.g. `Discord.exe`) are recognised by name.
- **The app picker lists** the Mac apps in the Applications folders (including one folder deep, like
  "Microsoft Office"), or on Linux every app in the app menu (desktop entries, Flatpak and Snap apps included), plus
  the apps running right now. Browsing to a file works too: anything inside a Mac app picks the app; on Linux a
  `.desktop` file picks its app.
- **Games on Linux.** A Steam, Lutris or Heroic game shortcut in the app menu only opens a link in the launcher, so it
  isn't offered; start the game once and pick it from the running apps instead. Windows games running through
  Wine/Proton are recognised by their `.exe` name, as on Windows.
- **Script-based Linux apps** (Python, Java and the like) are recognised by their script, not the interpreter.
- **One launch, one switch.** If two profiles list the same app in different forms, the first profile in the list wins,
  as on Windows.

## Headset notes

- **Same supported headsets** (Logitech, Corsair, SteelSeries, HyperX), detected the same way, on both systems.
- **Linux needs a permission rule** to talk to the headset's USB dongle. It covers exactly the supported headset
  models (never keyboards, mice or other devices, which could otherwise be read by any program). The `.deb` installs
  it; the AppImage offers to install it with one administrator password prompt. It applies straight away; if a headset
  that was already plugged in still isn't detected, unplugging and replugging its dongle once fixes it.
- **USB dongles only on Linux.** A headset connected over Bluetooth instead of its USB dongle can't be detected on
  Linux (the same as the dongle being unplugged).
- **macOS needs no permission** for this.

## System integration notes

- **Files** live in `~/Library/Application Support/VibeSwitcher` (macOS) or `~/.config/VibeSwitcher` (Linux, or
  `$XDG_CONFIG_HOME/VibeSwitcher`) with the same layout as `%APPDATA%\VibeSwitcher` on Windows, so a config exported
  on one system imports on another (devices then need re-picking, since device ids differ per system).
- **Launching it again** brings the running VibeSwitcher's Settings forward, as on Windows.
- **Command line (new on macOS/Linux).** `vibeswitcher --switch "<profile>"`, `--mute mic|speakers|both`,
  `--open` and `--mini` (show or hide Mini Mode) drive the running app. Linux needs this so keyboard shortcuts set up in the desktop's own settings can
  control VibeSwitcher on Wayland, where apps can't listen for global keys themselves. Windows doesn't need it.
- **Waking from sleep** re-checks schedules and re-applies the active profile, as on Windows.
- **Start at login** is a login item on macOS (a LaunchAgent) and an autostart entry on Linux. Like the Windows
  startup entry, it follows the app if it's moved (for an AppImage, the AppImage file itself is what starts).
  On macOS 13+ it appears under System Settings › General › Login Items › "Allow in the Background"; if it's switched
  off *there*, VibeSwitcher's own setting can still read "on" (planned: use Apple's newer login-item API in the app
  wiring step). A Mac app that hasn't been moved out of Downloads runs from a temporary copy; VibeSwitcher then asks to
  be moved to Applications before it can start at login.

## macOS audio notes

- **Built on Core Audio**, the system's own audio API, so there's nothing extra to install.
- **Switching a profile sets the default output, the "play sound effects through" output, and the default input.**
  Windows has three default roles per device and VibeSwitcher sets all of them; macOS splits output into "output" and
  "sound effects", and VibeSwitcher sets both (where the device allows it — a few virtual devices can't play sound
  effects, and then that setting stays as it is) so alerts follow the profile the same way.
- **Device ids are Core Audio device UIDs.** A USB device without a serial number gets a UID that includes the port
  it's plugged into, so moving it to another USB port gives it a new UID; the same automatic re-binding as on Windows
  reconnects the profile, because macOS devices report the same USB vendor/product fingerprint
  (`USB\VID_xxxx&PID_xxxx`).
- **One device, two lists.** A USB headset is usually one macOS device with both output and input; it appears in both
  lists under the same name, as Windows shows its two endpoints.
- **No "unplugged" or "disabled" devices.** macOS only lists devices that are present.
- **Mute works on every device that has a volume.** Windows can mute any device in software. On macOS many microphones
  (including most built-in ones) have a volume but no mute switch; VibeSwitcher mutes those by setting the volume to
  zero and restores the previous volume on unmute. On some USB microphones "zero" is very quiet rather than silent
  (the session log notes it when that's the case). A device with neither control can't be muted (logged too).
- **Headphones: Apple Silicon vs Intel.** On Apple Silicon Macs, plugged-in headphones appear as their own device, so
  auto-switch on plug-in *and* switching back on unplug work exactly as on Windows. On Intel Macs the headphones share
  the built-in device; plugging them in is that device's "changed" signal (plug-in only, as on Linux).
- **Microphone permission.** The mic meter needs macOS's microphone permission; macOS asks the first time it's used
  (the app bundle declares why it needs it). Without permission macOS gives silence rather than an error.

## Linux audio notes

- **Works with PipeWire and PulseAudio.** VibeSwitcher talks to the sound server through the standard `pactl`, `pacat`
  and `parec` tools (package `pulseaudio-utils`), which mainstream distros ship by default. If they're missing, the
  session log says which package to install.
- **One default per direction.** Windows has three default "roles" per device (console, multimedia, communications);
  Linux has a single default output and a single default input. Switching a profile sets those, and apps that follow
  the default move with it — the same visible result.
- **Device ids are the sound server's device names** (e.g. `alsa_output.usb-…analog-stereo`), which stay the same
  across reboots. When a USB device comes back under a new name (PipeWire adds `.2` after a re-plug, for example), the
  same automatic re-binding as on Windows kicks in, because Linux devices report the same kind of hardware fingerprint
  (`USB\VID_xxxx&PID_xxxx&MI_xx`).
- **"Unplugged"** means a sound card whose jack-sensing ports all report nothing plugged in (e.g. HDMI with no monitor
  attached), matching Windows. Devices without jack sensing (USB, Bluetooth) are present whenever they're connected.
- **"Monitor of …" inputs are hidden.** They echo an output rather than record anything; Windows has no equivalent.
- **Auto-switch on a headphone jack: plug-in only.** On many laptops the headphone jack and the speakers are one
  device with two "ports", so plugging headphones in doesn't add a device. VibeSwitcher treats "something was plugged
  into one of this device's jacks" as the device's "changed" signal — the same signal the Windows app's auto-switch
  uses for devices that never disconnect — so a profile set to switch on connect for that device switches. Two limits
  follow from it being one device: unplugging doesn't switch back, and two profiles can't tell "speakers" from
  "headphones" on that same device. Picking a port in the desktop's sound menu doesn't trigger anything.
- **Old `pactl` versions.** `pactl` before 15.99 can't print JSON; VibeSwitcher reads its text output instead (CI tests
  both).
