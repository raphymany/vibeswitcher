namespace VibeSwitcher.Platform;

public enum GrabResult
{
    Ok,
    InUse,        // another app already has this shortcut (Windows' ERROR_HOTKEY_ALREADY_REGISTERED)
    Unsupported,  // this system can't grab that key (e.g. media keys on macOS)
    Failed,       // anything else; see the error text
}

// The per-system part of global hotkeys — the counterpart of Win32 RegisterHotKey/UnregisterHotKey
// and the WM_HOTKEY message. Keys and modifiers use the app's canonical (Windows) form, so saved
// shortcuts mean the same thing on every system:
//   modifiers: HotkeyDefinition.MOD_ALT | MOD_CTRL | MOD_SHIFT | MOD_WIN
//   key:       a Windows virtual-key code
// Implementations never throw from Register/Unregister (failures are a GrabResult), and Unregister
// after Dispose is a no-op — the same guarantees the Win32 calls give the Windows HotkeyService.
public interface IGlobalKeyGrabber : IDisposable
{
    GrabResult Register(ushort id, int modifiers, int virtualKey, out string? error);
    void Unregister(ushort id);

    // Raised with the registration id when the shortcut is pressed — on whatever thread the system
    // delivers it (the app marshals to its UI thread, as the Windows app does for WM_HOTKEY). Handlers
    // may call back into the grabber; they should post their work rather than block.
    event Action<ushort>? Pressed;
}
