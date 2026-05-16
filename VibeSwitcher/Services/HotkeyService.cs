using System.Runtime.InteropServices;
using VibeSwitcher.Models;
using VibeSwitcher.NativeMethods;

namespace VibeSwitcher.Services;

public class HotkeyConflictException : Exception
{
    public HotkeyDefinition Hotkey { get; }
    public HotkeyConflictException(HotkeyDefinition hotkey)
        : base($"Hotkey conflict: {hotkey.ToDisplayString()}") => Hotkey = hotkey;
}

public class HotkeyService : IDisposable
{
    // Maps atom ID → profile Guid
    private readonly Dictionary<int, Guid> _atomToProfile = new();
    // Maps profile Guid → (atom, hotkey) for re-registration during TestHotkey
    private readonly Dictionary<Guid, (int Atom, HotkeyDefinition Hotkey)> _profileToAtom = new();
    private readonly IntPtr _hwnd;

    public HotkeyService(IntPtr messageWindowHandle)
    {
        _hwnd = messageWindowHandle;
    }

    public void RegisterAll(IEnumerable<DeviceProfile> profiles)
    {
        UnregisterAll();

        foreach (var profile in profiles)
        {
            if (profile.Hotkey.IsEmpty) continue;
            try
            {
                RegisterOne(profile);
            }
            catch (HotkeyConflictException)
            {
                // Clean up any partial registrations before re-throwing
                UnregisterAll();
                throw;
            }
        }
    }

    private void RegisterOne(DeviceProfile profile)
    {
        string atomName = $"VibeSwitcher_{profile.Id}";
        int atom = WinApi.GlobalAddAtom(atomName);
        if (atom == 0) throw new InvalidOperationException($"Failed to create atom for profile '{profile.Name}'");

        bool ok = WinApi.RegisterHotKey(_hwnd, atom, profile.Hotkey.GetModifierFlags(), profile.Hotkey.VirtualKeyCode);
        if (!ok)
        {
            int err = Marshal.GetLastWin32Error();
            WinApi.GlobalDeleteAtom(atom);
            if (err == WinApi.ERROR_HOTKEY_ALREADY_REGISTERED)
                throw new HotkeyConflictException(profile.Hotkey);
            throw new InvalidOperationException($"RegisterHotKey failed (error {err}) for profile '{profile.Name}'");
        }

        _atomToProfile[atom] = profile.Id;
        _profileToAtom[profile.Id] = (atom, profile.Hotkey);
    }

    public void UnregisterAll()
    {
        foreach (var (atom, _) in _atomToProfile)
        {
            WinApi.UnregisterHotKey(_hwnd, atom);
            WinApi.GlobalDeleteAtom(atom);
        }
        _atomToProfile.Clear();
        _profileToAtom.Clear();
    }

    public void Refresh(IEnumerable<DeviceProfile> profiles)
    {
        RegisterAll(profiles);
    }

    /// <summary>
    /// Returns true if the hotkey is in use by another application (not this one).
    /// Temporarily unregisters all own hotkeys to avoid false positives.
    /// </summary>
    public bool TestHotkey(HotkeyDefinition hotkey)
    {
        if (hotkey.IsEmpty) return false;

        // Unregister all our hotkeys temporarily so we don't detect our own registrations
        foreach (var (atom, _) in _atomToProfile)
            WinApi.UnregisterHotKey(_hwnd, atom);

        string testAtomName = $"VibeSwitcher_Test_{Guid.NewGuid()}";
        int testAtom = WinApi.GlobalAddAtom(testAtomName);
        bool inUseByOther = false;

        if (testAtom != 0)
        {
            bool ok = WinApi.RegisterHotKey(_hwnd, testAtom, hotkey.GetModifierFlags(), hotkey.VirtualKeyCode);
            inUseByOther = !ok;
            if (ok) WinApi.UnregisterHotKey(_hwnd, testAtom);
            WinApi.GlobalDeleteAtom(testAtom);
        }

        // Re-register all our own hotkeys
        var failedAtoms = new List<int>();
        foreach (var (_, (atom, hkDef)) in _profileToAtom)
        {
            bool reOk = WinApi.RegisterHotKey(_hwnd, atom, hkDef.GetModifierFlags(), hkDef.VirtualKeyCode);
            if (!reOk) failedAtoms.Add(atom);
        }

        // Clean up any atoms that failed re-registration (extremely rare race with another process)
        if (failedAtoms.Count > 0)
        {
            foreach (var atom in failedAtoms)
            {
                WinApi.GlobalDeleteAtom(atom);
                _atomToProfile.Remove(atom);
            }
            var staleProfiles = _profileToAtom
                .Where(kv => failedAtoms.Contains(kv.Value.Atom))
                .Select(kv => kv.Key)
                .ToList();
            foreach (var id in staleProfiles)
                _profileToAtom.Remove(id);
        }

        return inUseByOther;
    }

    public void UnregisterProfile(Guid profileId)
    {
        if (!_profileToAtom.TryGetValue(profileId, out var entry)) return;
        WinApi.UnregisterHotKey(_hwnd, entry.Atom);
        WinApi.GlobalDeleteAtom(entry.Atom);
        _atomToProfile.Remove(entry.Atom);
        _profileToAtom.Remove(profileId);
    }

    public void RegisterProfile(DeviceProfile profile)
    {
        if (profile.Hotkey.IsEmpty || _profileToAtom.ContainsKey(profile.Id)) return;
        try { RegisterOne(profile); }
        catch (HotkeyConflictException) { }
    }

    public Guid HandleHotkey(int atomId)
    {
        return _atomToProfile.TryGetValue(atomId, out var id) ? id : Guid.Empty;
    }

    public void Dispose() => UnregisterAll();
}
