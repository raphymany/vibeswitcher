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

    public ProfileMode Mode { get; set; } = ProfileMode.Both;

    public HotkeyDefinition Hotkey { get; set; } = new();

    // Absolute path to .ico file; null = use bundled default
    public string? IconPath { get; set; }

    public int SortOrder { get; set; }
}
