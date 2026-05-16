namespace VibeSwitcher.Models;

public class AppConfig
{
    public int ConfigVersion { get; set; } = 1;
    public List<DeviceProfile> Profiles { get; set; } = new();
    public Guid? ActiveProfileId { get; set; }
    public bool StartWithWindows { get; set; } = false;
    public bool StartMinimized { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public bool ShowNotifications { get; set; } = true;

    // Persisted window geometry — 0/negative means "not yet saved, use defaults"
    public double WindowWidth  { get; set; } = 0;
    public double WindowHeight { get; set; } = 0;
    public double WindowLeft   { get; set; } = -1;
    public double WindowTop    { get; set; } = -1;
}
