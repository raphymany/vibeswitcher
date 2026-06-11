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

public class HotkeyService : IHotkeyService
{
    // Maps atom ID → profile Guid
    private readonly Dictionary<ushort, Guid> _atomToProfile = new();
    // Maps profile Guid → (atom, hotkey) for re-registration during TestHotkey
    private readonly Dictionary<Guid, (ushort Atom, HotkeyDefinition Hotkey)> _profileToAtom = new();
    // Maps profile Guid → profile — rebuilt on every RegisterAll so WM_HOTKEY dispatch is O(1)
    private Dictionary<Guid, DeviceProfile> _profileById = new();
    private readonly IntPtr _hwnd;
    private ushort _settingsAtom;
    private HotkeyDefinition? _settingsHotkeyDef;
    private ushort _compactAtom;
    private HotkeyDefinition? _compactHotkeyDef;
    private readonly Dictionary<MuteScope, (ushort Atom, HotkeyDefinition Hotkey)> _muteAtoms = new();
    private readonly IAppLogger _logger;
    private readonly ISessionErrorTracker _errorTracker;

    public HotkeyService(IntPtr messageWindowHandle, IAppLogger logger, ISessionErrorTracker errorTracker)
    {
        _hwnd = messageWindowHandle;
        _logger = logger;
        _errorTracker = errorTracker;
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
                _logger.Error("HotkeyService.RegisterAll", ex);
                _errorTracker.Record(HotkeyAtomCreateFailed, "Hotkey Atom Creation Failed",
                    $"Could not create global atom for '{profile.Name}' — the system atom table may be full.");
            }
            catch (Exception ex)
            {
                _logger.Error("HotkeyService.RegisterAll", ex);
                _errorTracker.Record(HotkeyRegistrationFailed, "Hotkey Registration Failed",
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
        UnregisterSettingsHotkey();
        UnregisterCompactHotkey();
        foreach (var scope in _muteAtoms.Keys.ToList())
            UnregisterMuteHotkey(scope);
        foreach (var (atom, _) in _atomToProfile)
        {
            WinApi.UnregisterHotKey(_hwnd, atom);
            WinApi.GlobalDeleteAtom(atom);
        }
        _atomToProfile.Clear();
        _profileToAtom.Clear();
        _profileById.Clear();
    }

    public HotkeyConflictException? RegisterMuteHotkey(MuteScope scope, HotkeyDefinition hotkey)
    {
        UnregisterMuteHotkey(scope);
        if (hotkey.IsEmpty || !hotkey.IsValid) return null;

        string atomName = $"VibeSwitcher_Mute_{scope}";
        ushort atom = WinApi.GlobalAddAtom(atomName);
        if (atom == 0)
        {
            _logger.Warning("HotkeyService.RegisterMuteHotkey", $"GlobalAddAtom returned 0 for mute scope {scope}.");
            return null;
        }

        bool ok = WinApi.RegisterHotKey(_hwnd, atom, hotkey.GetModifierFlags(), hotkey.VirtualKeyCode);
        if (!ok)
        {
            int err = Marshal.GetLastWin32Error();
            WinApi.GlobalDeleteAtom(atom);
            if (err == WinApi.ERROR_HOTKEY_ALREADY_REGISTERED)
                return new HotkeyConflictException(hotkey);
            _logger.Warning("HotkeyService.RegisterMuteHotkey", $"RegisterHotKey failed (error {err}) for mute scope {scope}.");
            return null;
        }

        _muteAtoms[scope] = (atom, hotkey);
        return null;
    }

    public void UnregisterMuteHotkey(MuteScope scope)
    {
        if (!_muteAtoms.TryGetValue(scope, out var entry)) return;
        WinApi.UnregisterHotKey(_hwnd, entry.Atom);
        WinApi.GlobalDeleteAtom(entry.Atom);
        _muteAtoms.Remove(scope);
    }

    public bool IsMuteHotkey(ushort atomId, out MuteScope scope)
    {
        foreach (var kv in _muteAtoms)
        {
            if (kv.Value.Atom == atomId)
            {
                scope = kv.Key;
                return true;
            }
        }
        scope = default;
        return false;
    }

    public HotkeyConflictException? RegisterSettingsHotkey(HotkeyDefinition hotkey)
    {
        UnregisterSettingsHotkey();
        if (hotkey.IsEmpty || !hotkey.IsValid) return null;

        ushort atom = WinApi.GlobalAddAtom("VibeSwitcher_Settings");
        if (atom == 0)
        {
            _logger.Warning("HotkeyService.RegisterSettingsHotkey", "GlobalAddAtom returned 0 — atom table may be full.");
            return null;
        }

        bool ok = WinApi.RegisterHotKey(_hwnd, atom, hotkey.GetModifierFlags(), hotkey.VirtualKeyCode);
        if (!ok)
        {
            int err = Marshal.GetLastWin32Error();
            WinApi.GlobalDeleteAtom(atom);
            if (err == WinApi.ERROR_HOTKEY_ALREADY_REGISTERED)
                return new HotkeyConflictException(hotkey);
            _logger.Warning("HotkeyService.RegisterSettingsHotkey", $"RegisterHotKey failed (error {err})");
            return null;
        }

        _settingsAtom = atom;
        _settingsHotkeyDef = hotkey;
        return null;
    }

    public void UnregisterSettingsHotkey()
    {
        if (_settingsAtom == 0) return;
        WinApi.UnregisterHotKey(_hwnd, _settingsAtom);
        WinApi.GlobalDeleteAtom(_settingsAtom);
        _settingsAtom = 0;
        _settingsHotkeyDef = null;
    }

    public bool IsSettingsHotkey(ushort atomId) => _settingsAtom != 0 && atomId == _settingsAtom;

    public HotkeyConflictException? RegisterCompactHotkey(HotkeyDefinition hotkey)
    {
        UnregisterCompactHotkey();
        if (hotkey.IsEmpty || !hotkey.IsValid) return null;

        ushort atom = WinApi.GlobalAddAtom("VibeSwitcher_Compact");
        if (atom == 0)
        {
            _logger.Warning("HotkeyService.RegisterCompactHotkey", "GlobalAddAtom returned 0 — atom table may be full.");
            return null;
        }

        bool ok = WinApi.RegisterHotKey(_hwnd, atom, hotkey.GetModifierFlags(), hotkey.VirtualKeyCode);
        if (!ok)
        {
            int err = Marshal.GetLastWin32Error();
            WinApi.GlobalDeleteAtom(atom);
            if (err == WinApi.ERROR_HOTKEY_ALREADY_REGISTERED)
                return new HotkeyConflictException(hotkey);
            _logger.Warning("HotkeyService.RegisterCompactHotkey", $"RegisterHotKey failed (error {err})");
            return null;
        }

        _compactAtom = atom;
        _compactHotkeyDef = hotkey;
        return null;
    }

    public void UnregisterCompactHotkey()
    {
        if (_compactAtom == 0) return;
        WinApi.UnregisterHotKey(_hwnd, _compactAtom);
        WinApi.GlobalDeleteAtom(_compactAtom);
        _compactAtom = 0;
        _compactHotkeyDef = null;
    }

    public bool IsCompactHotkey(ushort atomId) => _compactAtom != 0 && atomId == _compactAtom;

    /// <summary>
    /// Returns true if the hotkey is in use by another application (not this one).
    /// Temporarily unregisters all own hotkeys to avoid false positives.
    /// </summary>
    public bool TestHotkey(HotkeyDefinition hotkey)
    {
        System.Diagnostics.Debug.Assert(
            System.Windows.Application.Current?.Dispatcher?.CheckAccess() ?? false,
            "TestHotkey must run on the UI thread — concurrent calls during unregister/re-register will lose hotkeys.");

        if (hotkey.IsEmpty || !hotkey.IsValid) return false;

        // Unregister all our hotkeys (including settings + compact + mute) temporarily so we don't detect our own registrations
        bool hadSettingsAtom = _settingsAtom != 0;
        if (hadSettingsAtom) WinApi.UnregisterHotKey(_hwnd, _settingsAtom);
        bool hadCompactAtom = _compactAtom != 0;
        if (hadCompactAtom) WinApi.UnregisterHotKey(_hwnd, _compactAtom);
        foreach (var (_, (muteAtom, _)) in _muteAtoms)
            WinApi.UnregisterHotKey(_hwnd, muteAtom);
        foreach (var (atom, _) in _atomToProfile)
            WinApi.UnregisterHotKey(_hwnd, atom);

        string testAtomName = $"VibeSwitcher_Test_{Guid.NewGuid()}";
        ushort testAtom = WinApi.GlobalAddAtom(testAtomName);
        bool inUseByOther = false;

        if (testAtom == 0)
        {
            _logger.Warning("HotkeyService.TestHotkey", "GlobalAddAtom returned 0 — atom table may be full; conflict check skipped.");
            _errorTracker.Record(HotkeyAtomCreateFailed, "Hotkey Atom Creation Failed",
                "Could not probe for hotkey conflicts — the system atom table may be full.");
        }
        else
        {
            bool ok = WinApi.RegisterHotKey(_hwnd, testAtom, hotkey.GetModifierFlags(), hotkey.VirtualKeyCode);
            inUseByOther = !ok;
            if (ok) WinApi.UnregisterHotKey(_hwnd, testAtom);
            WinApi.GlobalDeleteAtom(testAtom);
        }

        // Re-register mute hotkeys
        foreach (var kv in _muteAtoms)
        {
            var (muteAtom, muteHkDef) = kv.Value;
            WinApi.RegisterHotKey(_hwnd, muteAtom, muteHkDef.GetModifierFlags(), muteHkDef.VirtualKeyCode);
        }

        // Re-register the settings hotkey
        if (hadSettingsAtom && _settingsHotkeyDef != null)
        {
            bool reOk = WinApi.RegisterHotKey(_hwnd, _settingsAtom, _settingsHotkeyDef.GetModifierFlags(), _settingsHotkeyDef.VirtualKeyCode);
            if (!reOk)
            {
                WinApi.GlobalDeleteAtom(_settingsAtom);
                _settingsAtom = 0;
                _settingsHotkeyDef = null;
            }
        }

        // Re-register the compact (mini mode) hotkey
        if (hadCompactAtom && _compactHotkeyDef != null)
        {
            bool reOk = WinApi.RegisterHotKey(_hwnd, _compactAtom, _compactHotkeyDef.GetModifierFlags(), _compactHotkeyDef.VirtualKeyCode);
            if (!reOk)
            {
                WinApi.GlobalDeleteAtom(_compactAtom);
                _compactAtom = 0;
                _compactHotkeyDef = null;
            }
        }

        // Re-register all our own profile hotkeys
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
            _logger.Error("HotkeyService.RegisterProfile", ex);
            _errorTracker.Record(HotkeyConflict, "Hotkey Conflict",
                $"Could not register '{ex.Hotkey.ToDisplayString()}' — another app is using it.");
        }
        catch (HotkeyAtomException ex)
        {
            _logger.Error("HotkeyService.RegisterProfile", ex);
            _errorTracker.Record(HotkeyAtomCreateFailed, "Hotkey Atom Creation Failed",
                "Could not create atom for hotkey — the system atom table may be full.");
        }
        catch (Exception ex)
        {
            _logger.Error("HotkeyService.RegisterProfile", ex);
            _errorTracker.Record(HotkeyRegistrationFailed, "Hotkey Registration Failed",
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
