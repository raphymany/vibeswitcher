using VibeSwitcher.Audio;
using VibeSwitcher.Helpers;
using VibeSwitcher.Platform.MacOS.Audio;
using VibeSwitcher.Platform.MacOS.Interop;

namespace VibeSwitcher.Platform.MacOS.Tests;

// An in-memory Core Audio.
internal sealed class FakeCoreAudio : ICoreAudioSystem
{
    private readonly object _gate = new();
    private readonly List<Action> _listeners = [];
    public List<MacDevice> Devices { get; } = [];
    public Dictionary<DefaultRole, uint> Defaults { get; } = [];
    public Dictionary<(uint, bool), bool> Mutes { get; } = [];
    public HashSet<uint> RefuseDefault { get; } = [];
    public Dictionary<uint, (ushort, ushort)> UsbByLocation { get; } = [];
    public List<string> Calls { get; } = [];
    public bool Broken { get; set; }
    public bool ListenFails { get; set; }
    public object Gate => _gate;
    public int UsbLookups;

    public static readonly uint Usb = CoreAudio.TransportUSB;
    public static readonly uint BuiltIn = CoreAudio.TransportBuiltIn;
    public static readonly uint Virtual = CoreAudio.TransportVirtual;

    public int GetDevicesCalls;

    public IReadOnlyList<MacDevice> GetDevices()
    {
        Interlocked.Increment(ref GetDevicesCalls);
        lock (_gate)
        {
            if (Broken) throw new InvalidOperationException("Core Audio unavailable");
            return Devices.ToList();
        }
    }

    public uint? GetDefault(DefaultRole role) { lock (_gate) return Defaults.TryGetValue(role, out var v) ? v : null; }

    public int SetDefault(DefaultRole role, uint objectId)
    {
        lock (_gate)
        {
            Calls.Add($"set {role} {objectId}");
            if (RefuseDefault.Contains(objectId)) return unchecked((int)0x21646576); // '!dev'
            Defaults[role] = objectId;
            return 0;
        }
    }

    public bool? GetMute(uint objectId, bool input) { lock (_gate) return Mutes.TryGetValue((objectId, input), out var m) ? m : null; }

    public bool SetMute(uint objectId, bool input, bool mute)
    {
        lock (_gate)
        {
            if (!Mutes.ContainsKey((objectId, input))) return false; // no mute control
            Mutes[(objectId, input)] = mute;
            return true;
        }
    }

    public IDisposable Listen(Action onChange)
    {
        if (ListenFails) throw new InvalidOperationException("listener refused");
        lock (_gate) _listeners.Add(onChange);
        return new Unsubscribe(() => { lock (_gate) _listeners.Remove(onChange); });
    }

    public int ListenerCount { get { lock (_gate) return _listeners.Count; } }

    // Like Core Audio's property listeners: "something changed, look again".
    public void Fire()
    {
        List<Action> ls;
        lock (_gate) ls = _listeners.ToList();
        foreach (var l in ls) l();
    }

    public (ushort Vendor, ushort Product)? UsbIdsForLocation(uint locationId)
    {
        Interlocked.Increment(ref UsbLookups);
        lock (_gate) return UsbByLocation.TryGetValue(locationId, out var v) ? v : null;
    }

    private sealed class Unsubscribe(Action a) : IDisposable { public void Dispose() => a(); }

    public static MacDevice Device(uint id, string uid, string name, bool output = true, bool input = false,
        uint? transport = null, string? model = null, bool? jack = null, bool hidden = false, bool canDefault = true,
        bool canSystem = true, int inputChannels = 1) =>
        new(id, uid, name, output, input, output && canDefault, input && canDefault, output && canDefault && canSystem,
            input ? inputChannels : 0, transport ?? Virtual, model, jack, hidden);
}

internal sealed class FakeStreams : IMacAudioStreams
{
    public List<(string? Device, byte[] Data, WavSampleFormat Format, int Rate, int Channels)> Played { get; } = [];
    public float[] RecordSamples { get; set; } = [];
    public Exception? PlayError { get; set; }
    public string? CapturedFrom;
    public int CapturedChannels;

    public void PlayPcm(string? deviceUid, ArraySegment<byte> pcm, WavSampleFormat format, int rate, int channels)
    {
        if (PlayError != null) throw PlayError;
        Played.Add((deviceUid, pcm.ToArray(), format, rate, channels));
    }

    public void Capture(string deviceUid, int rate, int channels, CancellationToken ct, Action<float[]> onBlock)
    {
        CapturedFrom = deviceUid;
        CapturedChannels = channels;
        int block = rate / 100;
        for (int i = 0; i + block <= RecordSamples.Length && !ct.IsCancellationRequested; i += block)
            onBlock(RecordSamples[i..(i + block)]);
        if (!ct.IsCancellationRequested) throw new InvalidOperationException("input stopped");
    }
}

internal sealed class RecordingLogger : IAppLogger
{
    private readonly List<string> _lines = [];
    public IReadOnlyList<string> Lines { get { lock (_lines) return _lines.ToList(); } }
    public void Info(string context, string message) { }
    public void Warning(string context, string message) { lock (_lines) _lines.Add($"{context}: {message}"); }
    public void Error(string context, string message) { lock (_lines) _lines.Add($"{context}: {message}"); }
    public void Error(string context, Exception ex) { lock (_lines) _lines.Add($"{context}: {ex.Message}"); }
    public void Debug(string context, string message) { }
}
