namespace VibeSwitcher.Services;

// FORK of the records at the top of VibeSwitcher/Services/AudioService.cs (that file is Windows COM
// code and can't be linked). Keep the shapes identical — linked code (IAudioService,
// DeviceRebindService, DeviceTriggerService) depends on them. Tracked in xplat/forks.txt.

public record AudioDeviceInfo(string Id, string FriendlyName, bool IsPlayback, bool IsConnected = true, bool IsDisabled = false,
    string? HardwareKey = null)
{
    public bool ShowDot => !string.IsNullOrEmpty(Id);
    public override string ToString() => FriendlyName;
}

public record ProfileSwitchResult(
    bool PlaybackApplied,
    bool RecordingApplied,
    string? MissingPlaybackId,
    string? MissingRecordingId,
    // True when the device was present/active but setting it as default failed (distinct from "missing").
    bool PlaybackSetFailed = false,
    bool RecordingSetFailed = false);

internal static class AudioDeviceEnumerator
{
    // FORK of AudioDeviceEnumerator.NormalizeHardwareKey (VibeSwitcher/Services/AudioDeviceEnumerator.cs).
    // The macOS/Linux backends build Windows-shaped instance paths ("USB\VID_xxxx&PID_xxxx&MI_xx\…")
    // so the linked rebind and HID-matching logic works unchanged.
    // Reduces a hardware instance path to the part that identifies the device *model and interface*,
    // dropping the per-connection instance suffix that changes when the device is re-detected:
    //   "{1}.USB\VID_0BDA&PID_4E27&MI_00\8&404667A&0&0000" → "USB\VID_0BDA&PID_4E27&MI_00"
    internal static string? NormalizeHardwareKey(string? instancePath)
    {
        if (string.IsNullOrWhiteSpace(instancePath)) return null;
        var path = instancePath.Trim();

        // Strip the "{n}." enumerator prefix.
        if (path.StartsWith('{'))
        {
            int dot = path.IndexOf("}.", StringComparison.Ordinal);
            if (dot > 0) path = path[(dot + 2)..];
        }

        // "ENUMERATOR\HARDWARE-ID\INSTANCE" — drop the instance segment.
        var parts = path.Split('\\');
        if (parts.Length >= 3) path = string.Join('\\', parts[..^1]);

        return path.Length == 0 ? null : path.ToUpperInvariant();
    }
}
