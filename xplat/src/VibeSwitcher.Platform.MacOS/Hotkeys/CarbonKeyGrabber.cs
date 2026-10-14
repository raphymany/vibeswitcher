using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using VibeSwitcher.Models;
using VibeSwitcher.Platform;
using VibeSwitcher.Platform.MacOS.Interop;

namespace VibeSwitcher.Platform.MacOS.Hotkeys;

// Global hotkeys on macOS through Carbon's RegisterEventHotKey — still the standard way for apps to
// own a system-wide shortcut, and unlike keyboard monitoring it needs no Accessibility permission.
// Registered "exclusive", so a shortcut another app already owns is refused (eventHotKeyExistsErr),
// which is reported as InUse — the macOS counterpart of Windows' "hotkey already registered".
//
// Key presses arrive as Carbon events on the application's main event loop (which the UI framework
// runs), through one handler installed on first use.
public sealed class CarbonKeyGrabber : IGlobalKeyGrabber
{
    private const string Lib = "/System/Library/Frameworks/Carbon.framework/Carbon";
    private const int EventHotKeyExistsErr = -9878;
    private const uint ExclusiveOption = 1; // kEventHotKeyExclusive
    private static readonly uint Signature = FourCC.Of("VbSw");
    private static readonly uint EventClassKeyboard = FourCC.Of("keyb");
    private const uint EventHotKeyPressed = 5;
    private static readonly uint ParamDirectObject = FourCC.Of("----");
    private static readonly uint TypeEventHotKeyID = FourCC.Of("hkid");

    // Carbon modifier bits
    internal const uint CmdKey = 0x0100, ShiftKey = 0x0200, OptionKey = 0x0800, ControlKey = 0x1000;

    [StructLayout(LayoutKind.Sequential)]
    private struct EventHotKeyID { public uint Signature; public uint Id; }

    [StructLayout(LayoutKind.Sequential)]
    private struct EventTypeSpec { public uint EventClass; public uint EventKind; }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int EventHandlerProc(IntPtr callRef, IntPtr eventRef, IntPtr userData);

    [DllImport(Lib)] private static extern IntPtr GetApplicationEventTarget();
    [DllImport(Lib)] private static extern int RegisterEventHotKey(uint keyCode, uint modifiers, EventHotKeyID id,
        IntPtr target, uint options, out IntPtr hotKeyRef);
    [DllImport(Lib)] private static extern int UnregisterEventHotKey(IntPtr hotKeyRef);
    [DllImport(Lib)] private static extern int InstallEventHandler(IntPtr target, IntPtr handler, nuint numTypes,
        EventTypeSpec[] list, IntPtr userData, out IntPtr handlerRef);
    [DllImport(Lib)] private static extern int CreateEvent(IntPtr allocator, uint eventClass, uint kind, double when,
        uint attributes, out IntPtr eventRef);
    [DllImport(Lib)] private static extern int SetEventParameter(IntPtr eventRef, uint name, uint type, nuint size, ref EventHotKeyID data);
    [DllImport(Lib)] private static extern int SendEventToEventTarget(IntPtr eventRef, IntPtr target);
    [DllImport(Lib)] private static extern void ReleaseEvent(IntPtr eventRef);
    [DllImport(Lib)] private static extern int GetEventParameter(IntPtr eventRef, uint name, uint desiredType,
        IntPtr actualType, nuint bufferSize, IntPtr actualSize, out EventHotKeyID data);

    // One handler for the process (Carbon calls it for every hotkey); it routes by the id we chose.
    private static readonly EventHandlerProc Handler = OnHotKey;
    private static readonly object InstallGate = new();
    private static bool _installed;
    private static readonly ConcurrentDictionary<uint, CarbonKeyGrabber> Owners = new();
    private static int _nextGrabber;

    private readonly uint _grabberId;
    private readonly Dictionary<ushort, IntPtr> _refs = new();
    private readonly object _gate = new();
    private bool _disposed;

    public event Action<ushort>? Pressed;

    public CarbonKeyGrabber()
    {
        EnsureHandler(); // first: if it throws, nothing is left registered
        _grabberId = (uint)(Interlocked.Increment(ref _nextGrabber) & 0xFFFF);
        Owners[_grabberId] = this;
    }

    private static void EnsureHandler()
    {
        lock (InstallGate)
        {
            if (_installed) return;
            var spec = new[] { new EventTypeSpec { EventClass = EventClassKeyboard, EventKind = EventHotKeyPressed } };
            int status = InstallEventHandler(GetApplicationEventTarget(), Marshal.GetFunctionPointerForDelegate(Handler),
                1, spec, IntPtr.Zero, out _);
            if (status != 0) throw new InvalidOperationException($"InstallEventHandler failed (OSStatus {status}).");
            _installed = true;
        }
    }

    public GrabResult Register(ushort id, int modifiers, int virtualKey, out string? error)
    {
        try { return RegisterCore(id, modifiers, virtualKey, out error); }
        catch (Exception ex)
        {
            error = ex.Message;
            return GrabResult.Failed;
        }
    }

    private GrabResult RegisterCore(ushort id, int modifiers, int virtualKey, out string? error)
    {
        error = null;
        Unregister(id);
        lock (_gate)
            if (_disposed) { error = "already shut down"; return GrabResult.Failed; }
        if (MacKeyCodes.FromVirtualKey(virtualKey) is not uint keyCode)
        {
            error = $"key code {virtualKey} can't be a macOS shortcut";
            return GrabResult.Unsupported;
        }
        uint mods = Modifiers(modifiers);
        if (IsOptionOnly(mods) && OperatingSystem.IsMacOSVersionAtLeast(15))
        {
            error = "macOS 15 and later don't allow shortcuts that use only ⌥ Option (or ⌥⇧) — add ⌃ or ⌘";
            return GrabResult.Unsupported;
        }
        // The Carbon id carries both this grabber and the registration: (grabber << 16) | id.
        var hotKeyId = new EventHotKeyID { Signature = Signature, Id = (_grabberId << 16) | id };
        int status = RegisterEventHotKey(keyCode, mods, hotKeyId, GetApplicationEventTarget(), ExclusiveOption, out var hotKeyRef);
        if (status == EventHotKeyExistsErr) return GrabResult.InUse;
        if (status != 0)
        {
            error = $"RegisterEventHotKey failed (OSStatus {status})";
            return GrabResult.Failed;
        }
        lock (_gate)
        {
            if (_disposed) { UnregisterEventHotKey(hotKeyRef); error = "already shut down"; return GrabResult.Failed; }
            _refs[id] = hotKeyRef;
        }
        return GrabResult.Ok;
    }

    internal static bool IsOptionOnly(uint mods) => (mods & (ControlKey | CmdKey)) == 0 && (mods & OptionKey) != 0;

    // For tests: deliver a hotkey event for one of this grabber's ids exactly as Carbon does, through
    // the installed handler (no key press, so no Accessibility permission needed). OSStatus result.
    internal int SimulatePress(ushort id)
    {
        int status = CreateEvent(IntPtr.Zero, EventClassKeyboard, EventHotKeyPressed, 0, 0, out var ev);
        if (status != 0) return status;
        try
        {
            var hk = new EventHotKeyID { Signature = Signature, Id = (_grabberId << 16) | id };
            status = SetEventParameter(ev, ParamDirectObject, TypeEventHotKeyID, (nuint)Marshal.SizeOf<EventHotKeyID>(), ref hk);
            return status != 0 ? status : SendEventToEventTarget(ev, GetApplicationEventTarget());
        }
        finally { ReleaseEvent(ev); }
    }

    public void Unregister(ushort id)
    {
        IntPtr hotKeyRef;
        lock (_gate)
            if (!_refs.Remove(id, out hotKeyRef)) return;
        try { UnregisterEventHotKey(hotKeyRef); } catch (Exception) { }
    }

    // Windows key ↔ ⌘ Command, Alt ↔ ⌥ Option (the usual mapping, matching the ⌃⌥⇧⌘ labels).
    internal static uint Modifiers(int mods) =>
        ((mods & HotkeyDefinition.MOD_CTRL) != 0 ? ControlKey : 0) |
        ((mods & HotkeyDefinition.MOD_ALT) != 0 ? OptionKey : 0) |
        ((mods & HotkeyDefinition.MOD_SHIFT) != 0 ? ShiftKey : 0) |
        ((mods & HotkeyDefinition.MOD_WIN) != 0 ? CmdKey : 0);

    private static int OnHotKey(IntPtr callRef, IntPtr eventRef, IntPtr userData)
    {
        try
        {
            if (GetEventParameter(eventRef, ParamDirectObject, TypeEventHotKeyID, IntPtr.Zero,
                    (nuint)Marshal.SizeOf<EventHotKeyID>(), IntPtr.Zero, out var hk) != 0 || hk.Signature != Signature)
                return -9874; // eventNotHandledErr: not ours, let others see it
            if (Owners.TryGetValue(hk.Id >> 16, out var owner))
                owner.Pressed?.Invoke((ushort)(hk.Id & 0xFFFF));
        }
        catch (Exception) { /* never let an exception reach native code */ }
        return 0;
    }

    public void Dispose()
    {
        List<IntPtr> refs;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            refs = _refs.Values.ToList();
            _refs.Clear();
        }
        foreach (var r in refs) try { UnregisterEventHotKey(r); } catch (Exception) { }
        Owners.TryRemove(_grabberId, out _);
    }
}

// Windows virtual-key code (the app's saved form) → macOS virtual key code (kVK_*, the physical key
// on an ANSI keyboard — as macOS identifies keys for shortcuts).
internal static class MacKeyCodes
{
    private static readonly Dictionary<int, uint> Map = new()
    {
        // Letters
        [0x41] = 0x00, [0x53] = 0x01, [0x44] = 0x02, [0x46] = 0x03, [0x48] = 0x04, [0x47] = 0x05, [0x5A] = 0x06,
        [0x58] = 0x07, [0x43] = 0x08, [0x56] = 0x09, [0x42] = 0x0B, [0x51] = 0x0C, [0x57] = 0x0D, [0x45] = 0x0E,
        [0x52] = 0x0F, [0x59] = 0x10, [0x54] = 0x11, [0x4F] = 0x1F, [0x55] = 0x20, [0x49] = 0x22, [0x50] = 0x23,
        [0x4C] = 0x25, [0x4A] = 0x26, [0x4B] = 0x28, [0x4E] = 0x2D, [0x4D] = 0x2E,
        // Digits
        [0x31] = 0x12, [0x32] = 0x13, [0x33] = 0x14, [0x34] = 0x15, [0x36] = 0x16, [0x35] = 0x17,
        [0x39] = 0x19, [0x37] = 0x1A, [0x38] = 0x1C, [0x30] = 0x1D,
        // Punctuation (VK_OEM_*)
        [0xBB] = 0x18, [0xBD] = 0x1B, [0xDD] = 0x1E, [0xDB] = 0x21, [0xDE] = 0x27, [0xBA] = 0x29,
        [0xDC] = 0x2A, [0xBC] = 0x2B, [0xBF] = 0x2C, [0xBE] = 0x2F, [0xC0] = 0x32,
        [0xE2] = 0x0A, // the extra ISO key next to the left Shift (section sign on Apple ISO keyboards)
        [0x0C] = 0x47, // Clear (the keypad Clear key on Apple keyboards)
        // Editing / navigation
        [0x0D] = 0x24, [0x09] = 0x30, [0x20] = 0x31, [0x08] = 0x33, [0x1B] = 0x35, [0x2D] = 0x72, [0x24] = 0x73,
        [0x21] = 0x74, [0x2E] = 0x75, [0x23] = 0x77, [0x22] = 0x79, [0x25] = 0x7B, [0x27] = 0x7C, [0x28] = 0x7D, [0x26] = 0x7E,
        // Function keys
        [0x70] = 0x7A, [0x71] = 0x78, [0x72] = 0x63, [0x73] = 0x76, [0x74] = 0x60, [0x75] = 0x61, [0x76] = 0x62,
        [0x77] = 0x64, [0x78] = 0x65, [0x79] = 0x6D, [0x7A] = 0x67, [0x7B] = 0x6F, [0x7C] = 0x69, [0x7D] = 0x6B,
        [0x7E] = 0x71, [0x7F] = 0x6A, [0x80] = 0x40, [0x81] = 0x4F, [0x82] = 0x50, [0x83] = 0x5A,
        // Numpad
        [0x60] = 0x52, [0x61] = 0x53, [0x62] = 0x54, [0x63] = 0x55, [0x64] = 0x56, [0x65] = 0x57, [0x66] = 0x58,
        [0x67] = 0x59, [0x68] = 0x5B, [0x69] = 0x5C, [0x6A] = 0x43, [0x6B] = 0x45, [0x6D] = 0x4E, [0x6E] = 0x41, [0x6F] = 0x4B,
    };

    // Null for keys macOS can't use in a shortcut this way (e.g. media keys, F21–F24, Print Screen).
    public static uint? FromVirtualKey(int vk) => Map.TryGetValue(vk, out var code) ? code : null;

    internal static IReadOnlyDictionary<int, uint> All => Map;
}
