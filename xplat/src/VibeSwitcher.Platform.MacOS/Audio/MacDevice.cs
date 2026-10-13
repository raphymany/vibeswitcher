using VibeSwitcher.Audio;

namespace VibeSwitcher.Platform.MacOS.Audio;

// One Core Audio device as the backend sees it. A single macOS device can be both an output and an
// input (e.g. a USB headset is often one device with output and input streams); it's then listed
// in both lists under the same id, as Windows lists its two endpoints.
internal sealed record MacDevice(
    uint ObjectId,
    string Uid,                 // persistent id, used as the app's device id
    string Name,
    bool HasOutput,
    bool HasInput,
    bool CanBeDefaultOutput,
    bool CanBeDefaultInput,
    bool CanBeSystemOutput,     // can play alerts/sound effects (some virtual devices can't)
    int InputChannels,
    uint TransportType,
    string? ModelUid,
    bool? JackConnected,        // null when the device has no jack sensing
    bool Hidden);

internal enum DefaultRole { Output, Input, SystemOutput }

// Core Audio, as far as the service needs it. The real one is CoreAudioSystem; tests use a fake.
internal interface ICoreAudioSystem
{
    IReadOnlyList<MacDevice> GetDevices();
    uint? GetDefault(DefaultRole role);
    // OSStatus (0 = success).
    int SetDefault(DefaultRole role, uint objectId);
    bool? GetMute(uint objectId, bool input);
    bool SetMute(uint objectId, bool input, bool mute);
    // Calls onChange (on a Core Audio thread) whenever the device list, a default, or a device's
    // streams, name or jack state may have changed. Dispose to stop.
    IDisposable Listen(Action onChange);
    // USB vendor/product for the device at a USB location id, from the IO registry. When productName
    // is given, a registry entry whose product name differs doesn't count (guards against a numeric
    // serial number in a device UID being mistaken for a location).
    (ushort Vendor, ushort Product)? UsbIdsForLocation(uint locationId, string? productName);
}

// Playback/capture through Audio Queues. The real one is AudioQueueStreams; tests use a fake.
internal interface IMacAudioStreams
{
    // Plays raw interleaved PCM and returns when it has finished. deviceUid null = default output.
    void PlayPcm(string? deviceUid, ArraySegment<byte> pcm, WavSampleFormat format, int rate, int channels);

    // Calls onBlock with ~10 ms of interleaved float samples (all `channels` channels) from the device
    // until ct is cancelled. Throws if the input stops delivering audio (e.g. the device was unplugged).
    void Capture(string deviceUid, int rate, int channels, CancellationToken ct, Action<float[]> onBlock);
}
