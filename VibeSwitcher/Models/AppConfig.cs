namespace VibeSwitcher.Models;

public class AppConfig
{
    public int ConfigVersion { get; set; } = 1;
    public List<DeviceProfile> Profiles { get; set; } = new();
    public Guid? ActiveProfileId { get; set; }
    public bool StartMinimized { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public bool ShowNotifications { get; set; } = true;
    public bool UseLegacySoundPanel { get; set; } = false;
    public bool ShowDisabledDevices { get; set; } = true;
    public bool ShowDisconnectedDevices { get; set; } = true;
    public bool SettingsCardExpanded { get; set; } = true;

    public HotkeyDefinition? SettingsHotkey { get; set; }
    public bool SettingsHotkeyEnabled { get; set; } = true;

    // Persisted window geometry — null means "not yet saved, use defaults"
    public double WindowWidth  { get; set; } = 0;
    public double WindowHeight { get; set; } = 0;
    public double? WindowLeft { get; set; } = null;
    public double? WindowTop  { get; set; } = null;
}
