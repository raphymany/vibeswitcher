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
| Tray menu | Menu-bar icon opens the **same styled dark menu** | Native desktop menu: can't be styled | Same as X11. **Stock GNOME (e.g. Fedora) has no tray** without the AppIndicator extension; VibeSwitcher detects this, explains it, and won't hide on close. |
| Notification banners | Styled in-app banner (native once the app is signed) | ✅ Native | ✅ Native |
| Mini Mode (always on top, translucent, remembers position) | ✅ | ✅ | ✅ via XWayland |
| Start at login | ✅ (macOS shows a "background item added" notice) | ✅ | ✅ |
| App launch triggers | ✅ (`.app` bundles) | ✅ (+ Flatpak / Snap apps) | ✅ |
| Wireless headset power on/off detection | ✅ | Needs a permission rule: installed automatically by the `.deb`, one click for the AppImage | Same as X11 |
| Gallery emoji icons | Slightly different artwork (a different emoji font) | Same as macOS | Same as macOS |
| First launch | Unsigned for now: needs "Open Anyway" once in System Settings › Privacy & Security | ✅ | ✅ |
| Uninstall from inside the app | Moves the app to the Trash (+ optional data) | AppImage: deletes it; `.deb`: removes the package | Same as X11 |

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

## System integration notes

- **Files** live in `~/Library/Application Support/VibeSwitcher` (macOS) or `~/.config/VibeSwitcher` (Linux, or
  `$XDG_CONFIG_HOME/VibeSwitcher`) with the same layout as `%APPDATA%\VibeSwitcher` on Windows, so a config exported
  on one system imports on another (devices then need re-picking, since device ids differ per system).
- **Launching it again** brings the running VibeSwitcher's Settings forward, as on Windows.
- **Command line (new on macOS/Linux).** `vibeswitcher --switch "<profile>"`, `--mute mic|speakers|both` and
  `--open` drive the running app. Linux needs this so keyboard shortcuts set up in the desktop's own settings can
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
