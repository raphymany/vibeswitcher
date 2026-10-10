using VibeSwitcher.Keys;

namespace VibeSwitcher.Models;

// How modifier keys are labelled in ToDisplayString. The app picks the style for the running OS;
// the default matches the Windows app exactly.
public enum HotkeyLabelStyle { Windows, MacOS, Linux }

// FORK of VibeSwitcher/Models/HotkeyDefinition.cs — that file uses WPF KeyInterop and Win32 MOD_*
// constants. Same JSON shape (Windows virtual-key codes stay the canonical key in config.json, so
// configs move between OSes), same validity rules, same text on Windows-style labels.
// Tracked in xplat/forks.txt.
public class HotkeyDefinition
{
    // Win32 RegisterHotKey modifier bits (WinApi.MOD_*), kept for GetModifierFlags parity.
    public const int MOD_ALT = 0x0001;
    public const int MOD_CTRL = 0x0002;
    public const int MOD_SHIFT = 0x0004;
    public const int MOD_WIN = 0x0008;

    public static HotkeyLabelStyle LabelStyle { get; set; } = HotkeyLabelStyle.Windows;

    public int VirtualKeyCode { get; set; }
    public bool UseAlt { get; set; }
    public bool UseCtrl { get; set; }
    public bool UseShift { get; set; }
    // The Windows key; Command (⌘) on macOS, Super on Linux.
    public bool UseWin { get; set; }

    public bool IsEmpty => VirtualKeyCode == 0;

    public bool IsValid => VirtualKeyCode > 0 && VirtualKeyCode <= 254;

    public bool Matches(HotkeyDefinition other) =>
        VirtualKeyCode == other.VirtualKeyCode &&
        UseAlt == other.UseAlt && UseCtrl == other.UseCtrl &&
        UseShift == other.UseShift && UseWin == other.UseWin;

    public int GetModifierFlags()
    {
        int flags = 0;
        if (UseAlt)   flags |= MOD_ALT;
        if (UseCtrl)  flags |= MOD_CTRL;
        if (UseShift) flags |= MOD_SHIFT;
        if (UseWin)   flags |= MOD_WIN;
        return flags;
    }

    public string ToDisplayString()
    {
        if (IsEmpty) return "Not set";

        var key = VirtualKeyMap.GetName(VirtualKeyCode) ?? $"VK({VirtualKeyCode})";

        if (LabelStyle == HotkeyLabelStyle.MacOS)
        {
            // macOS convention: glyphs in the fixed order ⌃⌥⇧⌘, no separators.
            var mods = (UseCtrl ? "⌃" : "") + (UseAlt ? "⌥" : "") + (UseShift ? "⇧" : "") + (UseWin ? "⌘" : "");
            return mods + key;
        }

        var parts = new List<string>();
        if (UseCtrl)  parts.Add("Ctrl");
        if (UseAlt)   parts.Add("Alt");
        if (UseShift) parts.Add("Shift");
        if (UseWin)   parts.Add(LabelStyle == HotkeyLabelStyle.Linux ? "Super" : "Win");
        parts.Add(key);

        return string.Join("+", parts);
    }
}
