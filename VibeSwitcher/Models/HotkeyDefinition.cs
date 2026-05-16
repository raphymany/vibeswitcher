using System.Windows.Input;

namespace VibeSwitcher.Models;

public class HotkeyDefinition
{
    public int VirtualKeyCode { get; set; }
    public bool UseAlt { get; set; }
    public bool UseCtrl { get; set; }
    public bool UseShift { get; set; }
    public bool UseWin { get; set; }

    public bool IsEmpty => VirtualKeyCode == 0;

    public int GetModifierFlags()
    {
        int flags = 0;
        if (UseAlt) flags |= 0x0001;
        if (UseCtrl) flags |= 0x0002;
        if (UseShift) flags |= 0x0004;
        if (UseWin) flags |= 0x0008;
        return flags;
    }

    public string ToDisplayString()
    {
        if (IsEmpty) return "(none)";

        var parts = new List<string>();
        if (UseCtrl) parts.Add("Ctrl");
        if (UseAlt) parts.Add("Alt");
        if (UseShift) parts.Add("Shift");
        if (UseWin) parts.Add("Win");

        try
        {
            var key = KeyInterop.KeyFromVirtualKey(VirtualKeyCode);
            parts.Add(key.ToString());
        }
        catch
        {
            parts.Add($"VK({VirtualKeyCode})");
        }

        return string.Join("+", parts);
    }
}
