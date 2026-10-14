namespace VibeSwitcher.Platform.Linux.Hotkeys;

// Windows virtual-key code (the app's saved form) → X11 keysym. The keysym is turned into the
// keyboard's actual keycode at grab time, so the same shortcut works on any keyboard layout's
// position for that symbol.
internal static class X11Keysyms
{
    public static uint? FromVirtualKey(int vk)
    {
        if (vk is >= 0x41 and <= 0x5A) return (uint)(vk + 0x20);          // A–Z → a–z
        if (vk is >= 0x30 and <= 0x39) return (uint)vk;                   // 0–9
        if (vk is >= 0x70 and <= 0x87) return (uint)(0xFFBE + vk - 0x70); // F1–F24
        if (vk is >= 0x60 and <= 0x69) return (uint)(0xFFB0 + vk - 0x60); // numpad 0–9
        return vk switch
        {
            0x03 => 0xFF69, // Cancel
            0x08 => 0xFF08, // Backspace
            0x09 => 0xFF09, // Tab
            0x0C => 0xFF0B, // Clear
            0x29 => 0xFF60, // Select
            0x2B => 0xFF62, // Execute
            0x2F => 0xFF6A, // Help
            0x5D => 0xFF67, // Menu (Apps)
            0x5F => 0x1008FF2F, // Sleep
            0x6C => 0xFFAC, // numpad separator
            0xA6 => 0x1008FF26, // Browser Back
            0xA7 => 0x1008FF27, // Browser Forward
            0xA8 => 0x1008FF29, // Browser Refresh
            0xA9 => 0x1008FF28, // Browser Stop
            0xAA => 0x1008FF1B, // Browser Search
            0xAB => 0x1008FF30, // Browser Favorites
            0xAC => 0x1008FF18, // Browser Home
            0xB4 => 0x1008FF19, // Mail
            0xB5 => 0x1008FF32, // Select Media
            0xB6 => 0x1008FF33, // Launch App 1 (My Computer)
            0xB7 => 0x1008FF1D, // Launch App 2 (Calculator)
            0xE2 => 0x003C, // the extra key left of Z on ISO keyboards (< >)
            0x0D => 0xFF0D, // Enter
            0x13 => 0xFF13, // Pause
            0x14 => 0xFFE5, // Caps Lock
            0x1B => 0xFF1B, // Escape
            0x20 => 0x0020, // Space
            0x21 => 0xFF55, // Page Up
            0x22 => 0xFF56, // Page Down
            0x23 => 0xFF57, // End
            0x24 => 0xFF50, // Home
            0x25 => 0xFF51, // Left
            0x26 => 0xFF52, // Up
            0x27 => 0xFF53, // Right
            0x28 => 0xFF54, // Down
            0x2C => 0xFF61, // Print Screen
            0x2D => 0xFF63, // Insert
            0x2E => 0xFFFF, // Delete
            0x6A => 0xFFAA, // numpad *
            0x6B => 0xFFAB, // numpad +
            0x6D => 0xFFAD, // numpad -
            0x6E => 0xFFAE, // numpad .
            0x6F => 0xFFAF, // numpad /
            0x90 => 0xFF7F, // Num Lock
            0x91 => 0xFF14, // Scroll Lock
            0xAD => 0x1008FF12, // Volume Mute
            0xAE => 0x1008FF11, // Volume Down
            0xAF => 0x1008FF13, // Volume Up
            0xB0 => 0x1008FF17, // Next Track
            0xB1 => 0x1008FF16, // Previous Track
            0xB2 => 0x1008FF15, // Stop
            0xB3 => 0x1008FF14, // Play/Pause
            0xBA => 0x003B, // ;
            0xBB => 0x003D, // =
            0xBC => 0x002C, // ,
            0xBD => 0x002D, // -
            0xBE => 0x002E, // .
            0xBF => 0x002F, // /
            0xC0 => 0x0060, // `
            0xDB => 0x005B, // [
            0xDC => 0x005C, // \
            0xDD => 0x005D, // ]
            0xDE => 0x0027, // '
            _ => null,
        };
    }

    // HotkeyDefinition.MOD_* → X modifier mask.
    public static uint Modifiers(int mods) =>
        ((mods & Models.HotkeyDefinition.MOD_ALT) != 0 ? X11.Mod1Mask : 0) |
        ((mods & Models.HotkeyDefinition.MOD_CTRL) != 0 ? X11.ControlMask : 0) |
        ((mods & Models.HotkeyDefinition.MOD_SHIFT) != 0 ? X11.ShiftMask : 0) |
        ((mods & Models.HotkeyDefinition.MOD_WIN) != 0 ? X11.Mod4Mask : 0);
}
