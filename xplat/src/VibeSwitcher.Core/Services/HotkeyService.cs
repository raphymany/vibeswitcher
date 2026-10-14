using VibeSwitcher.Helpers;
using VibeSwitcher.Models;
using VibeSwitcher.Platform;
using static VibeSwitcher.Helpers.ErrorCode;

namespace VibeSwitcher.Services;

// FORK of VibeSwitcher/Services/HotkeyService.cs (tracked in xplat/forks.txt). The bookkeeping is the
// Windows app's, line for line: one registration per profile, plus the Settings, Mini Mode and mute
// shortcuts; conflicts collected or recorded exactly as on Windows; TestHotkey temporarily releases
// every own shortcut so it only detects other apps. What changed: Win32 RegisterHotKey + global
// atoms are replaced by the platform's IGlobalKeyGrabber with plain numeric ids (the "atom" values
// HandleHotkey and the Is…Hotkey checks receive are these ids). Two deliberate differences:
//   - TestHotkey reports "in use by another app" only for a real conflict (GrabResult.InUse). Windows
//     counts any failure, but here a key can also be one this system can't use at all (a media key
//     on macOS…), which registration then reports as such instead of blaming another app.
//   - Its UI-thread Debug.Assert passes when no UI dispatcher exists (unit tests).
// Added: before each registration it tells the grabber what the shortcut is for (Describe), which
// Wayland needs so the desktop keeps the user's approved shortcuts across saves.
public class HotkeyService : IHotkeyService
{
    // Maps id → profile Guid
    private readonly Dictionary<ushort, Guid> _atomToProfile = new();
    // Maps profile Guid → (id, hotkey) for re-registration during TestHotkey
    private readonly Dictionary<Guid, (ushort Atom, HotkeyDefinition Hotkey)> _profileToAtom = new();
    // Maps profile Guid → profile — rebuilt on every RegisterAll so dispatch is O(1)
    private Dictionary<Guid, DeviceProfile> _profileById = new();
    private readonly IGlobalKeyGrabber _grabber;
    private ushort _settingsAtom;
    private HotkeyDefinition? _settingsHotkeyDef;
    private ushort _compactAtom;
    private HotkeyDefinition? _compactHotkeyDef;
    private readonly Dictionary<MuteScope, (ushort Atom, HotkeyDefinition Hotkey)> _muteAtoms = new();
    private readonly IAppLogger _logger;
    private readonly ISessionErrorTracker _errorTracker;
    private readonly HashSet<ushort> _ids = new();
    private ushort _lastId;

    public HotkeyService(IGlobalKeyGrabber grabber, IAppLogger logger, ISessionErrorTracker errorTracker)
    {
        _grabber = grabber;
        _logger = logger;
        _errorTracker = errorTracker;
    }

    // Stands in for GlobalAddAtom: a fresh non-zero id that isn't in use.
    private ushort NewId()
    {
        if (_ids.Count >= ushort.MaxValue) throw new InvalidOperationException("Too many hotkeys registered.");
        do { _lastId = (ushort)(_lastId == ushort.MaxValue ? 1 : _lastId + 1); }
        while (_ids.Contains(_lastId));
        _ids.Add(_lastId);
        return _lastId;
    }

    private void FreeId(ushort id) => _ids.Remove(id);

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
        ushort atom = NewId();
        _grabber.Describe(atom, ShortcutNames.Profile(profile.Id), $"Switch to {profile.Name}");
        var result = _grabber.Register(atom, profile.Hotkey.GetModifierFlags(), profile.Hotkey.VirtualKeyCode, out var error);
        if (result != GrabResult.Ok)
        {
            FreeId(atom);
            if (result == GrabResult.InUse)
                throw new HotkeyConflictException(profile.Hotkey);
            throw new InvalidOperationException($"Registering the hotkey failed ({error ?? result.ToString()}) for profile '{profile.Name}'");
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
            _grabber.Unregister(atom);
            FreeId(atom);
        }
        _atomToProfile.Clear();
        _profileToAtom.Clear();
        _profileById.Clear();
    }

    public HotkeyConflictException? RegisterMuteHotkey(MuteScope scope, HotkeyDefinition hotkey)
    {
        UnregisterMuteHotkey(scope);
        if (hotkey.IsEmpty || !hotkey.IsValid) return null;

        ushort atom = NewId();
        _grabber.Describe(atom, ShortcutNames.Mute(scope), scope switch
        {
            MuteScope.Mic => "Mute or unmute the microphone",
            MuteScope.Speakers => "Mute or unmute the speakers",
            _ => "Mute or unmute the microphone and speakers",
        });
        var result = _grabber.Register(atom, hotkey.GetModifierFlags(), hotkey.VirtualKeyCode, out var error);
        if (result != GrabResult.Ok)
        {
            FreeId(atom);
            if (result == GrabResult.InUse)
                return new HotkeyConflictException(hotkey);
            _logger.Warning("HotkeyService.RegisterMuteHotkey", $"Registering the hotkey failed ({error ?? result.ToString()}) for mute scope {scope}.");
            return null;
        }

        _muteAtoms[scope] = (atom, hotkey);
        return null;
    }

    public void UnregisterMuteHotkey(MuteScope scope)
    {
        if (!_muteAtoms.TryGetValue(scope, out var entry)) return;
        _grabber.Unregister(entry.Atom);
        FreeId(entry.Atom);
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

        ushort atom = NewId();
        _grabber.Describe(atom, ShortcutNames.Settings, "Open VibeSwitcher settings");
        var result = _grabber.Register(atom, hotkey.GetModifierFlags(), hotkey.VirtualKeyCode, out var error);
        if (result != GrabResult.Ok)
        {
            FreeId(atom);
            if (result == GrabResult.InUse)
                return new HotkeyConflictException(hotkey);
            _logger.Warning("HotkeyService.RegisterSettingsHotkey", $"Registering the hotkey failed ({error ?? result.ToString()})");
            return null;
        }

        _settingsAtom = atom;
        _settingsHotkeyDef = hotkey;
        return null;
    }

    public void UnregisterSettingsHotkey()
    {
        if (_settingsAtom == 0) return;
        _grabber.Unregister(_settingsAtom);
        FreeId(_settingsAtom);
        _settingsAtom = 0;
        _settingsHotkeyDef = null;
    }

    public bool IsSettingsHotkey(ushort atomId) => _settingsAtom != 0 && atomId == _settingsAtom;

    public HotkeyConflictException? RegisterCompactHotkey(HotkeyDefinition hotkey)
    {
        UnregisterCompactHotkey();
        if (hotkey.IsEmpty || !hotkey.IsValid) return null;

        ushort atom = NewId();
        _grabber.Describe(atom, ShortcutNames.MiniMode, "Show or hide VibeSwitcher Mini Mode");
        var result = _grabber.Register(atom, hotkey.GetModifierFlags(), hotkey.VirtualKeyCode, out var error);
        if (result != GrabResult.Ok)
        {
            FreeId(atom);
            if (result == GrabResult.InUse)
                return new HotkeyConflictException(hotkey);
            _logger.Warning("HotkeyService.RegisterCompactHotkey", $"Registering the hotkey failed ({error ?? result.ToString()})");
            return null;
        }

        _compactAtom = atom;
        _compactHotkeyDef = hotkey;
        return null;
    }

    public void UnregisterCompactHotkey()
    {
        if (_compactAtom == 0) return;
        _grabber.Unregister(_compactAtom);
        FreeId(_compactAtom);
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
            System.Windows.Application.Current?.Dispatcher?.CheckAccess() ?? true,
            "TestHotkey must run on the UI thread — concurrent calls during unregister/re-register will lose hotkeys.");

        if (hotkey.IsEmpty || !hotkey.IsValid) return false;

        // Unregister all our hotkeys (including settings + compact + mute) temporarily so we don't detect our own registrations
        bool hadSettingsAtom = _settingsAtom != 0;
        if (hadSettingsAtom) _grabber.Unregister(_settingsAtom);
        bool hadCompactAtom = _compactAtom != 0;
        if (hadCompactAtom) _grabber.Unregister(_compactAtom);
        foreach (var (_, (muteAtom, _)) in _muteAtoms)
            _grabber.Unregister(muteAtom);
        foreach (var (atom, _) in _atomToProfile)
            _grabber.Unregister(atom);

        ushort testAtom = NewId();
        _grabber.Describe(testAtom, ShortcutNames.Probe, "Checking whether a shortcut is free");
        var result = _grabber.Register(testAtom, hotkey.GetModifierFlags(), hotkey.VirtualKeyCode, out _);
        bool inUseByOther = result == GrabResult.InUse;
        if (result == GrabResult.Ok) _grabber.Unregister(testAtom);
        FreeId(testAtom);

        // Re-register mute hotkeys
        foreach (var kv in _muteAtoms)
        {
            var (muteAtom, muteHkDef) = kv.Value;
            _grabber.Register(muteAtom, muteHkDef.GetModifierFlags(), muteHkDef.VirtualKeyCode, out _);
        }

        // Re-register the settings hotkey
        if (hadSettingsAtom && _settingsHotkeyDef != null)
        {
            bool reOk = _grabber.Register(_settingsAtom, _settingsHotkeyDef.GetModifierFlags(), _settingsHotkeyDef.VirtualKeyCode, out _) == GrabResult.Ok;
            if (!reOk)
            {
                FreeId(_settingsAtom);
                _settingsAtom = 0;
                _settingsHotkeyDef = null;
            }
        }

        // Re-register the compact (mini mode) hotkey
        if (hadCompactAtom && _compactHotkeyDef != null)
        {
            bool reOk = _grabber.Register(_compactAtom, _compactHotkeyDef.GetModifierFlags(), _compactHotkeyDef.VirtualKeyCode, out _) == GrabResult.Ok;
            if (!reOk)
            {
                FreeId(_compactAtom);
                _compactAtom = 0;
                _compactHotkeyDef = null;
            }
        }

        // Re-register all our own profile hotkeys
        var failedAtoms = new List<ushort>();
        foreach (var (_, (atom, hkDef)) in _profileToAtom)
        {
            bool reOk = _grabber.Register(atom, hkDef.GetModifierFlags(), hkDef.VirtualKeyCode, out _) == GrabResult.Ok;
            if (!reOk) failedAtoms.Add(atom);
        }

        // Clean up any ids that failed re-registration (extremely rare race with another process)
        if (failedAtoms.Count > 0)
        {
            foreach (var atom in failedAtoms)
            {
                FreeId(atom);
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
        _grabber.Unregister(entry.Atom);
        FreeId(entry.Atom);
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
