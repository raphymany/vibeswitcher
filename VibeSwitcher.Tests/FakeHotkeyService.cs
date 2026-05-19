using VibeSwitcher.Models;
using VibeSwitcher.Services;

namespace VibeSwitcher.Tests;

internal sealed class FakeHotkeyService : IHotkeyService
{
    public List<DeviceProfile> RegisteredProfiles { get; } = new();
    public bool TestHotkeyResult { get; set; }

    public List<HotkeyConflictException> RegisterAll(IEnumerable<DeviceProfile> profiles)
    {
        RegisteredProfiles.Clear();
        RegisteredProfiles.AddRange(profiles.Where(p => !p.Hotkey.IsEmpty && p.Hotkey.IsValid));
        return [];
    }

    public void UnregisterAll() => RegisteredProfiles.Clear();

    public void UnregisterProfile(Guid profileId) =>
        RegisteredProfiles.RemoveAll(p => p.Id == profileId);

    public void RegisterProfile(DeviceProfile profile)
    {
        if (!profile.Hotkey.IsEmpty && !RegisteredProfiles.Any(p => p.Id == profile.Id))
            RegisteredProfiles.Add(profile);
    }

    public DeviceProfile? HandleHotkey(ushort atomId) => null;
    public bool TestHotkey(HotkeyDefinition hotkey) => TestHotkeyResult;
    public HotkeyConflictException? RegisterSettingsHotkey(HotkeyDefinition hotkey) => null;
    public void UnregisterSettingsHotkey() { }
    public bool IsSettingsHotkey(ushort atomId) => false;
    public void Dispose() { }
}
