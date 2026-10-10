# Feature parity: Windows vs macOS vs Linux

The goal is the same function, behaviour and design on every OS. This table records **where the operating system
forces a difference**, and what we do about it. It's kept up to date as each phase lands. ✅ = same as Windows.

| Feature | macOS | Linux (X11) | Linux (Wayland session) |
|---|---|---|---|
| Switch default output/input, mute, test tone, mic meter | ✅ | ✅ | ✅ |
| Re-bind a profile when a USB device gets a new ID | ✅ | ✅ | ✅ |
| "Show disabled devices" | Hidden: macOS has no "disabled device" state | Partly: only devices with their profile switched off | Same as X11 |
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
