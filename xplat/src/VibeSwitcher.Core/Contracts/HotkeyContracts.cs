using VibeSwitcher.Models;

namespace VibeSwitcher.Services;

// FORK of HotkeyConflictException from VibeSwitcher/Services/HotkeyService.cs (that file is Win32
// RegisterHotKey code). Same shape; referenced by the linked IHotkeyService. Tracked in xplat/forks.txt.
public class HotkeyConflictException : Exception
{
    public HotkeyDefinition Hotkey { get; }
    public HotkeyConflictException(HotkeyDefinition hotkey)
        : base($"Hotkey conflict: {hotkey.ToDisplayString()}") => Hotkey = hotkey;
}
