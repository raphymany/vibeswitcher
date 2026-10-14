using VibeSwitcher.Models;

namespace VibeSwitcher.Platform.Linux.Hotkeys;

// A saved shortcut (Windows virtual-key code + modifiers) written the way Wayland desktops take it:
//   - the GlobalShortcuts portal's "preferred trigger" (the XDG shortcuts format): "CTRL+ALT+g"
//   - a GNOME keyboard-shortcut binding (GTK accelerator):                          "<Control><Alt>g"
// Both name the key by its xkb keysym name, so the key comes from the same X11Keysyms table the X11
// grabber uses. Null when the key has no name a desktop would accept.
internal static class ShortcutTriggers
{
    public static string? Portal(int modifiers, int vk)
    {
        if (KeyName(vk) is not { } key) return null;
        var parts = new List<string>(5);
        if ((modifiers & HotkeyDefinition.MOD_CTRL) != 0) parts.Add("CTRL");
        if ((modifiers & HotkeyDefinition.MOD_ALT) != 0) parts.Add("ALT");
        if ((modifiers & HotkeyDefinition.MOD_SHIFT) != 0) parts.Add("SHIFT");
        if ((modifiers & HotkeyDefinition.MOD_WIN) != 0) parts.Add("LOGO");
        parts.Add(key);
        return string.Join('+', parts);
    }

    public static string? Gnome(int modifiers, int vk)
    {
        if (KeyName(vk) is not { } key) return null;
        return ((modifiers & HotkeyDefinition.MOD_CTRL) != 0 ? "<Control>" : "")
            + ((modifiers & HotkeyDefinition.MOD_ALT) != 0 ? "<Alt>" : "")
            + ((modifiers & HotkeyDefinition.MOD_SHIFT) != 0 ? "<Shift>" : "")
            + ((modifiers & HotkeyDefinition.MOD_WIN) != 0 ? "<Super>" : "")
            + key;
    }

    internal static string? KeyName(int vk)
    {
        if (X11Keysyms.FromVirtualKey(vk) is not uint sym) return null;
        if (sym is >= 'a' and <= 'z' or >= '0' and <= '9') return ((char)sym).ToString();
        if (sym is >= 0xFFBE and <= 0xFFD5) return "F" + (sym - 0xFFBE + 1);   // F1–F24
        if (sym is >= 0xFFB0 and <= 0xFFB9) return "KP_" + (sym - 0xFFB0);     // numpad 0–9
        return Names.TryGetValue(sym, out var name) ? name : null;
    }

    // xkb names for every other keysym X11Keysyms produces.
    private static readonly Dictionary<uint, string> Names = new()
    {
        [0xFF69] = "Cancel", [0xFF08] = "BackSpace", [0xFF09] = "Tab", [0xFF0B] = "Clear",
        [0xFF60] = "Select", [0xFF62] = "Execute", [0xFF6A] = "Help", [0xFF67] = "Menu",
        [0xFF0D] = "Return", [0xFF13] = "Pause", [0xFFE5] = "Caps_Lock", [0xFF1B] = "Escape",
        [0x0020] = "space", [0xFF55] = "Page_Up", [0xFF56] = "Page_Down", [0xFF57] = "End",
        [0xFF50] = "Home", [0xFF51] = "Left", [0xFF52] = "Up", [0xFF53] = "Right", [0xFF54] = "Down",
        [0xFF61] = "Print", [0xFF63] = "Insert", [0xFFFF] = "Delete",
        [0xFFAA] = "KP_Multiply", [0xFFAB] = "KP_Add", [0xFFAC] = "KP_Separator", [0xFFAD] = "KP_Subtract",
        [0xFFAE] = "KP_Decimal", [0xFFAF] = "KP_Divide", [0xFF7F] = "Num_Lock", [0xFF14] = "Scroll_Lock",
        [0x003B] = "semicolon", [0x003D] = "equal", [0x002C] = "comma", [0x002D] = "minus",
        [0x002E] = "period", [0x002F] = "slash", [0x0060] = "grave", [0x005B] = "bracketleft",
        [0x005C] = "backslash", [0x005D] = "bracketright", [0x0027] = "apostrophe", [0x003C] = "less",
        [0x1008FF2F] = "XF86Sleep", [0x1008FF26] = "XF86Back", [0x1008FF27] = "XF86Forward",
        [0x1008FF29] = "XF86Refresh", [0x1008FF28] = "XF86Stop", [0x1008FF1B] = "XF86Search",
        [0x1008FF30] = "XF86Favorites", [0x1008FF18] = "XF86HomePage", [0x1008FF19] = "XF86Mail",
        [0x1008FF32] = "XF86AudioMedia", [0x1008FF33] = "XF86MyComputer", [0x1008FF1D] = "XF86Calculator",
        [0x1008FF12] = "XF86AudioMute", [0x1008FF11] = "XF86AudioLowerVolume", [0x1008FF13] = "XF86AudioRaiseVolume",
        [0x1008FF17] = "XF86AudioNext", [0x1008FF16] = "XF86AudioPrev", [0x1008FF15] = "XF86AudioStop",
        [0x1008FF14] = "XF86AudioPlay",
    };
}
