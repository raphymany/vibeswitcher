# VibeSwitcher for Mac and Linux — test session 1

Thank you for testing! VibeSwitcher switches your sound output and microphone with one click or a keyboard
shortcut, using "profiles" (for example *Speakers* and *Headphones*). This is the first build for Mac and Linux
(version **2.0.1-rc.1**), so some things will be rough. Your job is to try each step and say where what you see
differs from **Expect**.

- **Time:** about 75 minutes per computer. The Mac and Linux parts are separate; do them on different days if you like.
- **Order:** go in order. If a step fails, write down what happened and carry on: later steps rarely depend on it.
- **Notes:** for each problem, write the step number and one or two sentences ("A4.2: the banner showed twice").
  A phone photo of anything odd on screen helps a lot.
- **At the end** of each part you'll make one diagnostics zip. Please send both zips and your notes.
- **Nothing here changes your system for good.** The last step of each part removes VibeSwitcher again if you want.

**What helps:** two sound outputs (built-in speakers plus headphones, a USB headset, AirPods or a Bluetooth
speaker) and a microphone (the built-in one is fine). If you have a wireless gaming headset with a USB dongle
(Logitech, Corsair, SteelSeries or HyperX), bring it too; it's optional.

**How profiles work (you'll need this in both parts):**
- **Create one:** click **+ New Profile** (top right of the window) and choose **Playback Only**, **Recording Only**
  or **Both Devices**. A card called "Profile 1" appears.
- **Set it up:** click the card to open its details. Type a new name over "Profile 1" (or "Profile 2"…) at the top, pick the
  device(s) in the lists, then click **Done**. Changes save by themselves.
- **Switch to it:** use the **power** button (⏻) at the right-hand end of the card's bottom strip (hover it:
  "Activate this profile"), the menu, or its shortcut.
- **The card's small buttons** have names when you hover over them: "Auto-switch on device connect",
  "App launch triggers", "Schedule this profile" and so on.
- **"Settings › Shortcuts" (and so on)** means: click the gear button at the top right of the window (hover:
  "Settings"), then that item in the list on the left. **Back** at the bottom of that list (or the VibeSwitcher logo)
  returns to your profile cards. The menu's **Settings** opens the window too.

---

## Part A — Mac

### A1. Install (5 min)

1. Find your Mac's chip: Apple menu › **About This Mac** › "Chip". "Apple M…" means **Apple Silicon**, "Intel"
   means **Intel**. (VibeSwitcher needs macOS 14 Sonoma or later.)
2. Open the matching `.dmg` (`…-mac-arm64.dmg` for Apple Silicon, `…-mac-x64.dmg` for Intel) and drag
   **VibeSwitcher** onto **Applications**.
3. Open VibeSwitcher from Applications. macOS says it can't check it for malware (the app isn't signed yet).
   Click **Done** (or **OK**), then go to System Settings › **Privacy & Security**, scroll down and click
   **Open Anyway** next to VibeSwitcher, then confirm.

**Expect:**
- A short splash with the animated logo, then the Settings window opens.
- A VibeSwitcher icon appears in the menu bar at the top right.
- There's **no** Dock icon. That's intended: it's a menu-bar app.

### A2. Profiles and switching (10 min)

1. Create a **Playback Only** profile named "Speakers" with your speakers (see "How profiles work" above).
2. Create a second one, "Headphones", with your other output. In its details click **Pick Icon** and choose any icon\n   (pick **White** at the top of that box if your menu bar is dark).
3. In Headphones' details, click **Test** next to the playback device.
4. Play some sound (a YouTube video). Switch between the two profiles with their power (⏻) buttons.

**Expect:**
- 3: you hear a short tone on the headphones.
- 4: each time, a banner appears at the top right of the screen saying which profile it switched to, and the sound
  moves to that device. System Settings › Sound shows that device as the output.
- 4: while Headphones is active, the menu-bar icon shows the icon you picked.

### A3. The menu-bar menu (5 min)

1. Click the VibeSwitcher icon in the menu bar.
2. Click somewhere else on the screen.
3. Open the menu again and press **Escape**.
4. Open the menu again and click the icon a second time.
5. Open the menu and choose a profile. Then open it again and choose **Open Sound Settings**.
6. Close the Settings window (red button), then open the menu and choose **Settings**.

**Expect:**
- 1: a rounded menu opens right under the icon, in the app's own colours (not a plain grey macOS menu). It lists
  "VibeSwitcher", your two profiles (the active one marked), About, Help & FAQ, Settings, Mini Mode,
  Open Sound Settings, and Quit.
- 2, 3 and 4: the menu closes each time.
- 5: the profile switches, with a banner. Then System Settings › Sound opens.
- 6: Settings comes to the front, above other windows, even though there's no Dock icon.

### A4. Keyboard shortcuts (10 min)

1. Open "Speakers", click **Set Hotkey**, press ⌃ Control + ⌥ Option + 1, then **OK**. Do the same for
   "Headphones" with ⌃ Control + ⌥ Option + 2.
2. Click into another app (Safari, say) and press each shortcut.
3. In Settings › **Shortcuts**, click **Set** on "Mute Microphone", press ⌃ Control + ⌥ Option + M, click **OK**,
   then press it twice from another app.
4. On the "Mute Speakers" row, click **Set** and press ⌘ Command + Space (Spotlight's shortcut), then
   **OK**. Afterwards clear it with **✕**.

**Expect:**
- 1: no permission prompt (VibeSwitcher doesn't need Accessibility access).
- 2: each shortcut switches profiles, with a banner.
- 3: "Microphone muted" then "Microphone unmuted" banners.
- 4: VibeSwitcher says the shortcut is in use, or accepts it but Spotlight still opens, or Spotlight opens and
  the box never sees the keys. All are known macOS behaviour; just note which.

### A5. Mini Mode (10 min)

1. In Settings › **Mini Window**, turn on **Always on top** and **Translucent when inactive**.
2. Open the menu-bar menu and choose **Mini Mode**. The first time, a "Welcome to Mini Mode" box appears: click
   **Cancel** (Mini Mode still opens with the standard layout).
3. Switch profiles from the small window.
4. Click on another app's window so VibeSwitcher is no longer in front.
5. Drag the small window to a corner of the screen.
6. Click the expand button in its title bar (hover: "Back to full window"). Create a third profile, "Test" (**Playback Only**,
   with your speakers). Then choose **Mini Mode** from the menu again.
7. Click the expand button in its title bar again (hover: "Back to full window") to go back to the full window.

**Expect:**
- 2: a small window with your profiles, staying above other windows. The menu's "Mini Mode" entry now reads
  **Exit Mini Mode**.
- 3: switching works, with a banner.
- 4: the small window becomes see-through, showing the window or desktop behind it (not just a solid colour).
- 6: the full window comes back in its own place. When Mini Mode opens again, it's back in the corner from step 5,
  and it has grown to fit the third profile.

### A6. Banners over a full-screen app (3 min)

1. Make a video or Safari full screen (green button), then press one of your profile shortcuts.

**Expect:** the banner appears at the top right, over the full-screen app.

### A7. Microphone (5 min)

1. Create a **Both Devices** profile named "Call" with your headphones and microphone. Open its details and click
   **Test** next to the microphone.
2. Switch to "Call".

**Expect:**
- 1: macOS asks for microphone permission, with VibeSwitcher's own explanation. After you allow it, the level bar
  moves when you speak.
- 2: System Settings › Sound shows both the output and the input changed.

### A8. Automatic switching (10 min)

1. **On connect** (needs headphones, a USB headset or AirPods): switch to "Speakers". On the "Headphones" card,
   click **Auto-switch on device connect**. A box "Enable Auto-Switch" appears: click **Enable Auto-Switch**.
   Disconnect the headphones, then connect them again. Then disconnect them once more.
2. **App launch:** on the "Test" card, click **App launch triggers**, add **TextEdit** (search for it), click
   **Done**. Switch to "Speakers", then open TextEdit.
3. **Schedule:** on the "Headphones" card, click **Schedule this profile**. Set a time three minutes from now and
   click **Next →**. Tick today's day, **Next →**. Leave the reminder at "None", **Next →**. Then click **Save ✓**.
   Switch to "Speakers" and wait.

**Expect:**
- 1: when they connect, VibeSwitcher switches to "Headphones" by itself, with a banner. When they disconnect, it goes
  back to "Speakers".
- 2: opening TextEdit switches to "Test". (Quit TextEdit completely with ⌘Q before trying again.)
- 3: at that time it switches to "Headphones".

### A9. Opening again, quitting and start at login (5 min)

1. Close the Settings window. Then open VibeSwitcher again from Applications.
2. Choose **Quit** from the menu-bar menu, then open VibeSwitcher from Applications.
3. In Settings › **Startup**, turn on **Open at login**. Log out and back in (optional).

**Expect:**
- 1: the Settings window comes back. No second copy starts.
- 2: the splash, then just the menu-bar icon. Settings stays hidden, which is the default ("Start hidden in the
  menu bar").
- 3: macOS may show a "background item added" notice. After logging in again, the menu-bar icon is back by itself.

### A10. Self-test and diagnostics zip (5 min)

1. Open **Terminal** (Applications › Utilities) and paste:

   `/Applications/VibeSwitcher.app/Contents/MacOS/VibeSwitcher --selftest`

2. Then paste:

   `/Applications/VibeSwitcher.app/Contents/MacOS/VibeSwitcher --diagnostics`

**Expect:**
- 1: a list of checks, each marked as passed or with a short explanation. Copy the text into your notes.
- 2: if macOS asks whether Terminal may access your Desktop folder, click **Allow**. (If you don't, the zip goes to
  your home folder instead.) Windows flash briefly while it takes pictures of them. Then it prints
  "Saved …/VibeSwitcher-diagnostics-….zip".
- **Please send this zip.** It contains no passwords or personal files: just the app's settings, its log, the list
  of sound devices and pictures of its windows.

### A11. Uninstall (optional, 2 min)

1. Settings › **Install & Updates** › **Uninstall VibeSwitcher…**. Tick "Also delete my profiles and settings" if you
   want everything gone, then confirm.

**Expect:** the app closes and VibeSwitcher is in the Trash (you can put it back from there).

---

## Part B — Linux

First, note which desktop you're using. Your computer's own Settings app › **About** (or *System › About*) shows the
distribution (e.g. Ubuntu 24.04) and the desktop (GNOME, KDE Plasma, Cinnamon…). Also open a terminal and run
`echo $XDG_SESSION_TYPE` (it prints `x11` or `wayland`). Put both at the top of your Linux notes.

### B1. Install (5 min)

Use **one** of these:
- **Ubuntu, Debian, Mint, Pop!_OS:** open a terminal in the download folder and run
  `sudo apt install ./vibeswitcher_2.0.1~rc.1_amd64.deb`. Then start VibeSwitcher from the app menu.
- **Any other distribution:** in a terminal in the download folder, run
  `chmod +x VibeSwitcher-2.0.1-rc.1-x86_64.AppImage`, then `./VibeSwitcher-2.0.1-rc.1-x86_64.AppImage`. If it
  complains about FUSE, add `--appimage-extract-and-run` to the end of that command.

**Expect:**
- The splash, then the Settings window. A VibeSwitcher icon appears in the system tray or top bar.
- **No tray** (stock GNOME without the AppIndicator extension, e.g. Fedora): VibeSwitcher explains this and how to
  get one. Closing Settings then quits the app instead of hiding it. Note it, and in later steps use the cards' power
  (⏻) buttons instead of the tray menu, and skip B3.

### B2. Profiles and switching (10 min)

Do Part A step A2 (create "Speakers" and "Headphones", pick an icon, Test, switch with the power buttons).

**Expect:** a desktop notification on each switch. The sound moves to the chosen device, and your desktop's sound
settings show it as the default. While Headphones is active, the tray icon shows the icon you picked.

### B3. The tray menu (5 min)

1. Click the tray icon with the **left** mouse button, twice. Then click it with the **right** button.
2. From the menu choose a profile, then **Open Sound Settings**.
3. Open **Mini Mode** from the menu (click **Cancel** in the "Welcome to Mini Mode" box), then open the menu again.
   Leave Mini Mode with **Exit Mini Mode**.

**Expect:**
- 1: **write down exactly what a left click does.** By default it should switch to the next profile, with a
  notification, but on some desktops (GNOME) it opens the menu instead. The right button opens the menu. The menu is
  in your desktop's own style (it can't be styled on Linux) and lists "VibeSwitcher", your profiles (the active one\n  ticked), About,
  Help & FAQ, Settings, Mini Mode, Open Sound Settings, and Quit.
- 2: the profile switches. Then your desktop's sound settings open.
- 3: with Mini Mode open, its menu entry reads **Exit Mini Mode**.

### B4. Keyboard shortcuts (10 min)

1. Set the same shortcuts as in A4 steps 1–3 (Ctrl + Alt + 1, Ctrl + Alt + 2 and Ctrl + Alt + M for Mute
   Microphone). In Settings › **Shortcuts**, read the card at the top, if there is one.
2. Press the shortcuts from another app.
3. In a terminal, run `vibeswitcher --switch "Speakers"` (AppImage: `./VibeSwitcher-…AppImage --switch "Speakers"`).

**Expect, depending on your session:**
- **X11:** no card. The shortcuts just work.
- **Wayland, GNOME before 48** (Ubuntu 22.04/24.04, Debian 12):
  - A card says GNOME needs your OK, with an **Add my shortcuts to GNOME** button. Click it: the shortcuts work
    straight away.
  - They appear in your computer's Settings › Keyboard › View and Customize Shortcuts › Custom Shortcuts as
    "VibeSwitcher: …".
  - Then click **Remove my shortcuts from GNOME**: they disappear from that list and stop working.
- **Wayland, KDE Plasma or GNOME 48+** (Debian 13, Fedora 42+, Ubuntu 25.04+): your desktop shows its own window
  asking you to approve VibeSwitcher's shortcuts (you may pick different keys there). After you approve them, the card
  lists the keys the desktop chose, and they work.
- **Anything else:** the card explains that shortcuts are off and how to set them up in your desktop's settings.
- 3 (on every desktop): it switches to "Speakers", with a notification.

### B5. Mini Mode (10 min)

Do Part A step A5, using the tray menu (or the "Switch to Mini Mode" button at the top of the Settings window).

**Expect:** the same as on the Mac. On a plain X11 desktop without visual effects, the small window may stay solid
instead of see-through; note which you see. Also note any dark or odd-coloured frame around the splash at start-up.

### B6. Microphone and automatic switching (10 min)

Do Part A steps A7 and A8. For the app launch trigger use your text editor (gedit, Text Editor, Kate…) instead of
TextEdit, and close it completely before trying again.

**Expect:** the same as on the Mac. Linux doesn't ask for microphone permission. On many laptops the headphone jack
and the speakers are one device, so plugging headphones into the jack switches, but unplugging them doesn't switch
back. USB and Bluetooth headphones switch back.

### B7. Sleep and wake (5 min)

1. With a profile active, suspend the computer (sleep), wait a minute, and wake it.

**Expect:** about 3 seconds after waking, **one** "Switched to …" notification, and the sound is on the profile's
device. Note if you get two, or none.

### B8. Wireless headset (optional, 5 min)

1. Plug in the headset's dongle and turn on **Auto-switch on device connect** for a profile with that headset.

**Expect:**
- If Linux doesn't let VibeSwitcher read the dongle yet, it explains and offers **Allow Access** (one password
  prompt). It may then say "One more step": unplug the dongle and plug it back in.
- After that, turning the headset on and off switches profiles.
- The `.deb` sets this up when it's installed, so with the `.deb` you shouldn't be asked at all.

### B9. Opening again, quitting and start at login (5 min)

Do Part A step A9, with **Start at login** in Settings › Startup.

**Expect:** the same as on the Mac (the splash, then just the tray icon, on the start after Quit). If Settings is
already in front when you open VibeSwitcher again, it hides instead, as on Windows. After logging in again, the
tray icon is back by itself.

### B10. Self-test (3 min)

In a terminal: `vibeswitcher --selftest` (AppImage: `./VibeSwitcher-2.0.1-rc.1-x86_64.AppImage --selftest`).

**Expect:** a list of checks, and a "VibeSwitcher self-test" notification (note if it doesn't appear). Copy the text
into your notes.

### B11. Diagnostics zip (3 min)

In a terminal: `vibeswitcher --diagnostics` (AppImage: `./VibeSwitcher-2.0.1-rc.1-x86_64.AppImage --diagnostics`).

**Expect:** it prints "Saved …/VibeSwitcher-diagnostics-….zip" (your Desktop, or your home folder).
**Please send this zip.**

### B12. Uninstall (optional, 2 min)

Settings › **Install & Updates** › **Uninstall VibeSwitcher…**, then **Uninstall**.

**Expect:**
- AppImage: the file is deleted. If you allowed headset access in B8, you're asked for your password once.
- `.deb`: you're asked for your password, then the system removes it. If you cancel the password prompt, nothing is
  removed and VibeSwitcher says so.

---

## What to send back

- Your notes, with the step numbers.
- The two diagnostics zips (Mac and Linux).
- The self-test text from A10 and B10 (if it isn't already in your notes).

Thank you!
