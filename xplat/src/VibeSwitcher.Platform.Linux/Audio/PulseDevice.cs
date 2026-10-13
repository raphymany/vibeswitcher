namespace VibeSwitcher.Platform.Linux.Audio;

internal enum PortAvailability { Unknown, Available, NotAvailable }

internal sealed record PulsePort(string Name, string Description, PortAvailability Availability);

// One sink (output) or source (input) as pactl reports it. Works the same on PulseAudio and on
// PipeWire (whose pipewire-pulse server answers the same protocol).
internal sealed record PulseDevice(
    uint Index,
    string Name,
    string Description,
    bool IsSink,
    bool Mute,
    string? MonitorOfSink,
    IReadOnlyList<PulsePort> Ports,
    string? ActivePort,
    IReadOnlyDictionary<string, string> Properties)
{
    // A source that just echoes a sink's output ("Monitor of …") — never shown as a microphone,
    // just as Windows never lists one.
    public bool IsMonitor => !IsSink &&
        (MonitorOfSink != null || string.Equals(Prop("device.class"), "monitor", StringComparison.OrdinalIgnoreCase));

    // PulseAudio's placeholder when there's no real device.
    public bool IsDummy => Name is "auto_null" or "auto_null.monitor";

    // The Windows "Unplugged" state: a device with jack-sensing ports where none has anything plugged
    // in. Devices without ports (USB, Bluetooth, virtual) are present whenever they're listed.
    public bool IsAvailable => Ports.Count == 0 || Ports.Any(p => p.Availability != PortAvailability.NotAvailable);

    public string? Prop(string key) => Properties.TryGetValue(key, out var v) && v.Length > 0 ? v : null;
}

// What `pactl info` reports that matters here.
internal sealed record PulseServerInfo(string? DefaultSink, string? DefaultSource, string? ServerName);
