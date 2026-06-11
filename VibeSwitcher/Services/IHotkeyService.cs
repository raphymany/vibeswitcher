using VibeSwitcher.Models;

namespace VibeSwitcher.Services;

public interface IHotkeyService : IDisposable
{
    List<HotkeyConflictException> RegisterAll(IEnumerable<DeviceProfile> profiles);
    void UnregisterAll();
    void UnregisterProfile(Guid profileId);
    void RegisterProfile(DeviceProfile profile);
    DeviceProfile? HandleHotkey(ushort atomId);
    bool TestHotkey(HotkeyDefinition hotkey);
    HotkeyConflictException? RegisterSettingsHotkey(HotkeyDefinition hotkey);
    void UnregisterSettingsHotkey();
    bool IsSettingsHotkey(ushort atomId);
    HotkeyConflictException? RegisterCompactHotkey(HotkeyDefinition hotkey);
    void UnregisterCompactHotkey();
    bool IsCompactHotkey(ushort atomId);
    HotkeyConflictException? RegisterMuteHotkey(MuteScope scope, HotkeyDefinition hotkey);
    void UnregisterMuteHotkey(MuteScope scope);
    bool IsMuteHotkey(ushort atomId, out MuteScope scope);
}
