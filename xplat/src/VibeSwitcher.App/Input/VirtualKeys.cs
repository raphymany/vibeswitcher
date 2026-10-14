using System.Collections.Frozen;
using Avalonia.Input;
using VibeSwitcher.Keys;

namespace VibeSwitcher.App.Input;

// Avalonia key → Windows virtual-key code: what WPF's KeyInterop.VirtualKeyFromKey gives the Windows
// capture dialog. Hotkeys are stored as Windows codes on every system (configs move between them),
// and the system hotkey grabbers translate them back. Avalonia's Key enum is WPF's (same names), so
// the table is VirtualKeyMap's names read back as Avalonia keys.
public static class VirtualKeys
{
    private static readonly FrozenDictionary<Key, int> Codes = Build();

    private static FrozenDictionary<Key, int> Build()
    {
        var codes = new Dictionary<Key, int>();
        foreach (var (vk, name) in VirtualKeyMap.All)
        {
            // (The first code for a key wins: the generic Shift/Ctrl/Alt codes come before the
            // left/right ones, as WPF names them alike.)
            if (Enum.TryParse<Key>(name, out var key) && key != Key.None) codes.TryAdd(key, vk);
        }
        return codes.ToFrozenDictionary();
    }

    // 0 when the key has no Windows code (as KeyInterop).
    public static int For(Key key) => Codes.TryGetValue(key, out var vk) ? vk : 0;
}
