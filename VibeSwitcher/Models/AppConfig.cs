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
    public bool LeftClickCyclesProfiles { get; set; } = true;
    public bool SettingsCardExpanded { get; set; } = true;

    // "Auto" = follow Windows, "Light" = always light, "Dark" = always dark
    public string Theme { get; set; } = "Auto";

    public bool Use12HourClock { get; set; } = true;

    public HotkeyDefinition? SettingsHotkey { get; set; }
    public bool SettingsHotkeyEnabled { get; set; } = true;

    // Persisted window geometry — null means "not yet saved, use defaults"
    public double WindowWidth  { get; set; } = 0;
    public double WindowHeight { get; set; } = 0;
    public double? WindowLeft { get; set; } = null;
    public double? WindowTop  { get; set; } = null;

    // User-defined friendly names keyed by Windows device ID.
    public Dictionary<string, string> DeviceAliases { get; set; } = new();

    // Mute hotkeys
    public HotkeyDefinition? MuteMicHotkey { get; set; }
    public bool MuteMicHotkeyEnabled { get; set; } = false;
    public HotkeyDefinition? MuteSpeakersHotkey { get; set; }
    public bool MuteSpeakersHotkeyEnabled { get; set; } = false;
    public HotkeyDefinition? MuteBothHotkey { get; set; }
    public bool MuteBothHotkeyEnabled { get; set; } = false;

    // Per-shortcut banner suppression for the mute hotkeys (true = no banner when that hotkey fires).
    // Default false = show the brief "muted/unmuted" banner.
    public bool MuteMicSilent { get; set; } = false;
    public bool MuteSpeakersSilent { get; set; } = false;
    public bool MuteBothSilent { get; set; } = false;

    // Mini (compact) mode
    public bool CompactMode { get; set; } = false;
    public HotkeyDefinition? CompactHotkey { get; set; }
    public bool CompactHotkeyEnabled { get; set; } = true;
    public bool CompactAlwaysOnTop { get; set; } = false;
    public bool CompactTranslucent { get; set; } = false;
    public double? CompactWindowLeft { get; set; } = null;
    public double? CompactWindowTop  { get; set; } = null;

    // "Rows" = full-width rows, "Grid" = icon button grid
    public string CompactLayout { get; set; } = "Rows";
    // Profiles shown in mini mode; empty = show all
    public List<Guid> CompactProfileIds { get; set; } = new();
    // First-time intro dialog has been shown
    public bool CompactIntroShown { get; set; } = false;

    // Wall-clock time of the last scheduler evaluation. Persisted so catch-up only fires schedules
    // genuinely missed while the app wasn't running — reopening within the catch-up window won't
    // re-fire a switch that already ran. null = never evaluated (treat everything as catch-up-able).
    public DateTime? LastSchedulerEvaluation { get; set; }

    // Animated logo: "Full" (60fps), "Reduced" (~24fps, lower CPU), "Static" (no animation).
    public string LogoAnimation { get; set; } = "Full";
}
