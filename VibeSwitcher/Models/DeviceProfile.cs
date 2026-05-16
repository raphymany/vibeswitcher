using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace VibeSwitcher.Models;

// Writes strings ("Playback"/"Recording"/"Both") but still accepts legacy integer values
// from configs created before this change was applied.
internal sealed class ProfileModeConverter : StringEnumConverter
{
    public ProfileModeConverter() { AllowIntegerValues = true; }
}

[JsonConverter(typeof(ProfileModeConverter))]
public enum ProfileMode { Playback, Recording, Both }

public class DeviceProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Profile";

    // Persistent IMMDevice endpoint IDs — stable across reboots
    public string? PlaybackDeviceId { get; set; }
    public string? RecordingDeviceId { get; set; }

    public ProfileMode Mode { get; set; } = ProfileMode.Both;

    public HotkeyDefinition Hotkey { get; set; } = new();

    // Absolute path to .ico file; null = use bundled default
    public string? IconPath { get; set; }

    public int SortOrder { get; set; }
}
