using System.Text.Json;
using System.Text.Json.Serialization;

namespace VibeSwitcher.Models;

// Writes strings ("Playback"/"Recording"/"Both") but still accepts legacy integer values
// from configs created before this converter was introduced.
internal sealed class ProfileModeConverter : JsonConverter<ProfileMode>
{
    public override ProfileMode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
            return (ProfileMode)reader.GetInt32();
        var str = reader.GetString();
        return Enum.TryParse<ProfileMode>(str, ignoreCase: true, out var result) ? result : ProfileMode.Both;
    }

    public override void Write(Utf8JsonWriter writer, ProfileMode value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
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

    // Fingerprint of each assigned device (Windows friendly name + hardware key, see
    // AudioDeviceEnumerator.NormalizeHardwareKey). Windows assigns a brand-new endpoint ID when
    // serial-less USB hardware is re-detected (different port, hub reset, driver update); the
    // fingerprint lets DeviceRebindService find the device again under its new ID.
    // Backfilled automatically while the device is present; null = not yet known.
    public string? PlaybackDeviceName { get; set; }
    public string? PlaybackDeviceHardwareKey { get; set; }
    public string? RecordingDeviceName { get; set; }
    public string? RecordingDeviceHardwareKey { get; set; }

    public ProfileMode Mode { get; set; } = ProfileMode.Both;

    public HotkeyDefinition Hotkey { get; set; } = new();

    // Absolute path to .ico file; null = use bundled default
    public string? IconPath { get; set; }

    public int SortOrder { get; set; }

    public bool Silent { get; set; } = false;

    public bool IsPinned { get; set; } = false;

    public string? Notes { get; set; }

    public List<ScheduleEntry> Schedules { get; set; } = new();

    // Automatically switch to this profile when its device(s) connect
    public bool TriggerOnConnect { get; set; } = false;

    // Full paths to executables that trigger a switch to this profile when launched
    public List<string> AppTriggers { get; set; } = new();

    // Per-profile switch sound override
    public bool SoundOverride { get; set; } = false;
    public bool SoundShowBanner { get; set; } = false;
    public string? SoundTone { get; set; }        // null = inherit global
    public string? SoundCustomPath { get; set; }  // null = inherit global
    public int? SoundVolume { get; set; }          // null = inherit global
}
