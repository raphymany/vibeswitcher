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
