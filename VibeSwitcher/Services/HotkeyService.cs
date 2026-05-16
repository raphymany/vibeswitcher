using System.Runtime.InteropServices;
using VibeSwitcher.Helpers;
using VibeSwitcher.Models;
using VibeSwitcher.NativeMethods;
using static VibeSwitcher.Helpers.ErrorCode;

namespace VibeSwitcher.Services;

public class HotkeyConflictException : Exception
{
    public HotkeyDefinition Hotkey { get; }
    public HotkeyConflictException(HotkeyDefinition hotkey)
        : base($"Hotkey conflict: {hotkey.ToDisplayString()}") => Hotkey = hotkey;
}

internal sealed class HotkeyAtomException : Exception
{
    public HotkeyAtomException(string profileName)
        : base($"GlobalAddAtom returned 0 for '{profileName}' — atom table may be full.") { }
}

public class HotkeyService : IDisposable
{
    // Maps atom ID → profile Guid
    private readonly Dictionary<ushort, Guid> _atomToProfile = new();
    // Maps profile Guid → (atom, hotkey) for re-registration during TestHotkey
    private readonly Dictionary<Guid, (ushort Atom, HotkeyDefinition Hotkey)> _profileToAtom = new();
    // Maps profile Guid → profile — rebuilt on every RegisterAll so WM_HOTKEY dispatch is O(1)
    private Dictionary<Guid, DeviceProfile> _profileById = new();
    private readonly IntPtr _hwnd;

    public HotkeyService(IntPtr messageWindowHandle)
    {
        _hwnd = messageWindowHandle;
    }

    // Returns a list of conflicts found; non-conflicting hotkeys are still registered.
    public List<HotkeyConflictException> RegisterAll(IEnumerable<DeviceProfile> profiles)
    {
        UnregisterAll();
        _profileById = new Dictionary<Guid, DeviceProfile>();
        var conflicts = new List<HotkeyConflictException>();

        foreach (var profile in profiles)
        {
            _profileById[profile.Id] = profile;
            if (profile.Hotkey.IsEmpty || !profile.Hotkey.IsValid) continue;
            try
            {
                RegisterOne(profile);
            }
            catch (HotkeyConflictException ex)
            {
                conflicts.Add(ex);
            }
            catch (HotkeyAtomException ex)
            {
                AppLogger.Error("HotkeyService.RegisterAll", ex);
                SessionErrorTracker.Record(HotkeyAtomCreateFailed, "Hotkey Atom Creation Failed",
                    $"Could not create global atom for '{profile.Name}' — the system atom table may be full.");
            }
            catch (Exception ex)
            {
                AppLogger.Error("HotkeyService.RegisterAll", ex);
                SessionErrorTracker.Record(HotkeyRegistrationFailed, "Hotkey Registration Failed",
                    $"Could not register hotkey for '{profile.Name}': {ex.Message}");
            }
        }

        return conflicts;
    }

    private void RegisterOne(DeviceProfile profile)
    {
        string atomName = $"VibeSwitcher_{profile.Id}";
        ushort atom = WinApi.GlobalAddAtom(atomName);
        if (atom == 0) throw new HotkeyAtomException(profile.Name);

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
        _profileById.Clear();
    }

    /// <summary>
    /// Returns true if the hotkey is in use by another application (not this one).
    /// Temporarily unregisters all own hotkeys to avoid false positives.
    /// </summary>
    public bool TestHotkey(HotkeyDefinition hotkey)
    {
        if (hotkey.IsEmpty || !hotkey.IsValid) return false;

        // Unregister all our hotkeys temporarily so we don't detect our own registrations
        foreach (var (atom, _) in _atomToProfile)
            WinApi.UnregisterHotKey(_hwnd, atom);

        string testAtomName = $"VibeSwitcher_Test_{Guid.NewGuid()}";
        ushort testAtom = WinApi.GlobalAddAtom(testAtomName);
        bool inUseByOther = false;

        if (testAtom == 0)
        {
            AppLogger.Warning("HotkeyService.TestHotkey", "GlobalAddAtom returned 0 — atom table may be full; conflict check skipped.");
            SessionErrorTracker.Record(HotkeyAtomCreateFailed, "Hotkey Atom Creation Failed",
                "Could not probe for hotkey conflicts — the system atom table may be full.");
        }
        else
        {
            bool ok = WinApi.RegisterHotKey(_hwnd, testAtom, hotkey.GetModifierFlags(), hotkey.VirtualKeyCode);
            inUseByOther = !ok;
            if (ok) WinApi.UnregisterHotKey(_hwnd, testAtom);
            WinApi.GlobalDeleteAtom(testAtom);
        }

        // Re-register all our own hotkeys
        var failedAtoms = new List<ushort>();
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
        if (profile.Hotkey.IsEmpty || !profile.Hotkey.IsValid || _profileToAtom.ContainsKey(profile.Id)) return;
        try { RegisterOne(profile); }
        catch (HotkeyConflictException ex)
        {
            AppLogger.Error("HotkeyService.RegisterProfile", ex);
            SessionErrorTracker.Record(HotkeyConflict, "Hotkey Conflict",
                $"Could not register '{ex.Hotkey.ToDisplayString()}' — another app is using it.");
        }
        catch (HotkeyAtomException ex)
        {
            AppLogger.Error("HotkeyService.RegisterProfile", ex);
            SessionErrorTracker.Record(HotkeyAtomCreateFailed, "Hotkey Atom Creation Failed",
                "Could not create atom for hotkey — the system atom table may be full.");
        }
        catch (Exception ex)
        {
            AppLogger.Error("HotkeyService.RegisterProfile", ex);
            SessionErrorTracker.Record(HotkeyRegistrationFailed, "Hotkey Registration Failed",
                $"Could not register hotkey: {ex.Message}");
        }
    }

    public DeviceProfile? HandleHotkey(ushort atomId)
    {
        if (!_atomToProfile.TryGetValue(atomId, out var id)) return null;
        return _profileById.TryGetValue(id, out var profile) ? profile : null;
    }

    public void Dispose() => UnregisterAll();
}
