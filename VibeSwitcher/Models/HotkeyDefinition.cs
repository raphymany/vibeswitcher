using System.Windows.Input;
using VibeSwitcher.NativeMethods;

namespace VibeSwitcher.Models;

public class HotkeyDefinition
{
    public int VirtualKeyCode { get; set; }
    public bool UseAlt { get; set; }
    public bool UseCtrl { get; set; }
    public bool UseShift { get; set; }
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
        if (UseAlt)   flags |= WinApi.MOD_ALT;
        if (UseCtrl)  flags |= WinApi.MOD_CTRL;
        if (UseShift) flags |= WinApi.MOD_SHIFT;
        if (UseWin)   flags |= WinApi.MOD_WIN;
        return flags;
    }

    public string ToDisplayString()
    {
        if (IsEmpty) return "Not set";

        var parts = new List<string>();
        if (UseCtrl)  parts.Add("Ctrl");
        if (UseAlt)   parts.Add("Alt");
        if (UseShift) parts.Add("Shift");
        if (UseWin)   parts.Add("Win");

        var key = KeyInterop.KeyFromVirtualKey(VirtualKeyCode);
        parts.Add(key != Key.None ? key.ToString() : $"VK({VirtualKeyCode})");

        return string.Join("+", parts);
    }
}
