using System.Collections.Concurrent;
using VibeSwitcher.Audio;
using VibeSwitcher.Compat;
using VibeSwitcher.Helpers;
using VibeSwitcher.Models;
using VibeSwitcher.Services;

namespace VibeSwitcher.Platform.MacOS.Audio;

// The macOS implementation of the app's audio service (the Windows AudioService's counterpart),
// on Core Audio and Audio Queues.
//
// Device ids are Core Audio device UIDs, which persist across reboots (a USB device without a
// serial number gets a UID that includes the port it's plugged into, so moving it to another port
// gives a new UID — the automatic re-binding handles that, using the USB vendor/product fingerprint).
// Behaviour mirrors Windows:
//   - a device with output streams is listed as an output, with input streams as an input (one
//     device can be both); hidden devices and those that can't be the default aren't offered;
//     macOS doesn't list unplugged devices, so every listed device is connected;
//   - applying a profile sets the default output and, where the device allows it, the sound-effects
//     output (Windows sets every default role), and the default input — skipping any role already
//     set (no audio dropout); a device that isn't there is "missing", one Core Audio refuses is
//     "set failed";
//   - DevicesChanged fires (debounced) when devices appear or disappear, gain or lose a direction
//     (a Bluetooth headset switching to its call profile gains a microphone), or are renamed;
//     DevicePropertyChanged(id) fires when headphones are plugged into a jack that shares the
//     built-in device (Intel Macs) — the macOS counterpart of the Windows power-on signal
//     DeviceTriggerService uses for auto-switching.
public sealed class MacAudioService : IPlatformAudioService
{
    private const int MicRate = 16000;
    private const int ToneRate = 48000;
    private const int ToneChannels = 2;
    private static readonly TimeSpan UsbMissRetry = TimeSpan.FromSeconds(30);

    private readonly IAppLogger _logger;
    private readonly ISessionErrorTracker _errorTracker;
    private readonly ICoreAudioSystem _audio;
    private readonly IMacAudioStreams _streams;
    private readonly TimeSpan _debounce;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _diffLock = new(1, 1);
    private IDisposable? _listener;
    private Dictionary<string, DeviceState>? _lastStates;
    private Dictionary<string, string?> _paths = new(StringComparer.Ordinal);
    private DateTime _pathsAt = DateTime.MinValue;
    // USB ids per device UID (the UID names the device itself, not just the port it's in), and
    // when a lookup last found nothing — the IO registry scan isn't free.
    private readonly ConcurrentDictionary<string, (ushort Vendor, ushort Product)> _usbIds = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTime> _usbMisses = new(StringComparer.Ordinal);
    private CancellationTokenSource? _pending;
    private int _listFailedReported;
    private int _disposed;

    public event Action? DevicesChanged;
    public event Action<string>? DevicePropertyChanged;

    public MacAudioService(IAppLogger logger, ISessionErrorTracker errorTracker)
        : this(logger, errorTracker, new CoreAudioSystem(msg => logger.Warning("CoreAudio", msg)), new AudioQueueStreams(), listenForChanges: true) { }

    internal MacAudioService(IAppLogger logger, ISessionErrorTracker errorTracker, ICoreAudioSystem audio,
        IMacAudioStreams streams, bool listenForChanges, TimeSpan? debounce = null)
    {
        _logger = logger;
        _errorTracker = errorTracker;
        _audio = audio;
        _streams = streams;
        _debounce = debounce ?? TimeSpan.FromMilliseconds(500);
        if (!listenForChanges) return;

        try
        {
            _listener = _audio.Listen(ScheduleRefresh);
        }
        catch (Exception ex)
        {
            _logger.Warning("MacAudioService", $"Could not register for device changes: {ex.Message}");
            _errorTracker.Record(ErrorCode.DeviceNotificationFailed, "Device Notification Setup Failed",
                $"Could not subscribe to device change events: {ex.Message}. The device list will not refresh automatically.");
        }
        // Baseline for diffing, off the caller's (startup) thread: it may scan the IO registry.
        Baseline = Task.Run(DiffAndRaise);
    }

    // Completes once the change-detection baseline has been recorded (tests wait on it).
    internal Task Baseline { get; } = Task.CompletedTask;

    private bool Disposed => Volatile.Read(ref _disposed) != 0;

    // ── Listing ─────────────────────────────────────────────────────────────────────────────────

    public IReadOnlyList<AudioDeviceInfo> GetPlaybackDevices() => List(output: true);

    public IReadOnlyList<AudioDeviceInfo> GetRecordingDevices() => List(output: false);

    private IReadOnlyList<AudioDeviceInfo> List(bool output)
    {
        if (Disposed) return [];
        try
        {
            var infos = new List<AudioDeviceInfo>();
            var paths = new List<(string Uid, string? Path)>();
            foreach (var d in Offered(_audio.GetDevices(), output))
            {
                var path = PathOf(d);
                paths.Add((d.Uid, path));
                infos.Add(new AudioDeviceInfo(d.Uid, d.Name, output,
                    IsConnected: true, // macOS only lists devices that are present
                    IsDisabled: false, // and has no "disabled" state
                    HardwareKey: AudioDeviceEnumerator.NormalizeHardwareKey(path)));
            }
            lock (_gate) foreach (var (uid, path) in paths) _paths[uid] = path;
            Volatile.Write(ref _listFailedReported, 0);
            return infos.OrderBy(d => d.FriendlyName).ToList();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.Warning("MacAudioService.List", ex.Message);
            if (Interlocked.Exchange(ref _listFailedReported, 1) == 0)
                _errorTracker.Record(ErrorCode.AudioEnumerationFailed, "Audio Device List Unavailable",
                    $"Could not read the audio devices: {ex.Message}");
            return [];
        }
    }

    private static IEnumerable<MacDevice> Offered(IReadOnlyList<MacDevice> devices, bool output) =>
        devices.Where(d => !d.Hidden && (output ? d.CanBeDefaultOutput : d.CanBeDefaultInput));

    private string? PathOf(MacDevice d) => MacHardwareKey.InstancePath(d, (location, product) => UsbIds(d.Uid, location, product));

    private (ushort Vendor, ushort Product)? UsbIds(string uid, uint location, string? product)
    {
        if (_usbIds.TryGetValue(uid, out var ids)) return ids;
        if (_usbMisses.TryGetValue(uid, out var missedAt) && DateTime.UtcNow - missedAt < UsbMissRetry) return null;
        try
        {
            if (_audio.UsbIdsForLocation(location, product) is { } found)
            {
                _usbMisses.TryRemove(uid, out _);
                return _usbIds[uid] = found;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.Debug("MacAudioService.UsbIds", ex.Message);
        }
        _usbMisses[uid] = DateTime.UtcNow;
        return null;
    }

    // ── Switching ───────────────────────────────────────────────────────────────────────────────

    public Task<ProfileSwitchResult> ApplyProfileAsync(DeviceProfile profile) => Task.Run(() => Apply(profile));

    private ProfileSwitchResult Apply(DeviceProfile profile)
    {
        IReadOnlyList<MacDevice> devices;
        try
        {
            devices = _audio.GetDevices();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            var msg = $"Core Audio is not responding: {ex.Message}";
            _logger.Error("MacAudioService.ApplyProfile", msg);
            _errorTracker.Record(ErrorCode.AudioServiceUnavailable, "Audio Service Unavailable", msg);
            throw new InvalidOperationException(msg, ex);
        }

        var (pbApplied, pbMissing, pbFailed) = ApplyOne(devices, output: true, profile.PlaybackDeviceId);
        var (recApplied, recMissing, recFailed) = ApplyOne(devices, output: false, profile.RecordingDeviceId);
        return new ProfileSwitchResult(pbApplied, recApplied, pbMissing, recMissing, pbFailed, recFailed);
    }

    private (bool Applied, string? Missing, bool Failed) ApplyOne(IReadOnlyList<MacDevice> devices, bool output, string? uid)
    {
        if (string.IsNullOrEmpty(uid)) return (false, null, false);
        var device = Offered(devices, output).FirstOrDefault(d => d.Uid == uid);
        if (device == null) return (false, uid, false);

        var roles = !output ? [DefaultRole.Input]
            // Some devices (e.g. some virtual or conferencing drivers) can't play sound effects;
            // that role just stays where it is, which isn't a failure.
            : device.CanBeSystemOutput ? new[] { DefaultRole.Output, DefaultRole.SystemOutput } : [DefaultRole.Output];

        bool ok = true;
        foreach (var role in roles)
        {
            // Re-setting the current default can make apps re-open the device (a brief dropout);
            // it's already correct, so it counts as applied — the same rule as Windows.
            if (_audio.GetDefault(role) == device.ObjectId) continue;
            int status = _audio.SetDefault(role, device.ObjectId);
            if (status != 0)
            {
                var msg = $"Setting '{device.Name}' as the {RoleName(role)} failed (OSStatus {status}).";
                _logger.Error("MacAudioService.ApplyProfile", msg);
                _errorTracker.Record(ErrorCode.PolicySetDefaultFailed, "Set Default Audio Device Failed", msg);
                ok = false; // keep going with the other role, as Windows does
            }
        }
        return ok ? (true, null, false) : (false, null, true);
    }

    private static string RoleName(DefaultRole role) => role switch
    {
        DefaultRole.Output => "output device",
        DefaultRole.Input => "input device",
        _ => "sound effects device",
    };

    // ── Test tone, mic meter, mute ──────────────────────────────────────────────────────────────

    public Task TestSoundAsync(string deviceId) => Task.Run(() =>
    {
        try
        {
            _streams.PlayPcm(deviceId, TestTone.RenderPcm16(ToneRate, ToneChannels), WavSampleFormat.S16, ToneRate, ToneChannels);
        }
        catch (Exception ex)
        {
            _logger.Warning("MacAudioService.TestSound", ex.Message);
        }
    });

    // Meters every input channel and shows the loudest, so an interface whose microphone is on input 2
    // (of many) shows its level at full strength.
    public void RunMicLevelMonitor(string deviceId, CancellationToken ct, Action<float> onLevel)
    {
        try
        {
            int channels = Math.Max(_audio.GetDevices().FirstOrDefault(d => d.Uid == deviceId)?.InputChannels ?? 1, 1);
            _streams.Capture(deviceId, MicRate, channels, ct, block => onLevel(AudioLevel.LoudestChannelRms(block, channels)));
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.Warning("MacAudioService.RunMicLevelMonitor", ex.Message);
        }
        catch (Exception) { /* cancelled: the dialog closed */ }
    }

    public bool? GetDefaultDeviceMute(bool playback)
    {
        try
        {
            return _audio.GetDefault(playback ? DefaultRole.Output : DefaultRole.Input) is uint id
                ? _audio.GetMute(id, input: !playback)
                : null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.Warning("MacAudioService.GetDefaultDeviceMute", ex.Message);
            return null;
        }
    }

    // False when there's no default device, or it has neither a mute nor a volume control.
    public bool SetDefaultDeviceMute(bool playback, bool mute)
    {
        try
        {
            if (_audio.GetDefault(playback ? DefaultRole.Output : DefaultRole.Input) is not uint id)
            {
                _logger.Warning("MacAudioService.SetDefaultDeviceMute", $"No default {(playback ? "output" : "input")} device to {(mute ? "mute" : "unmute")}.");
                return false;
            }
            if (_audio.SetMute(id, input: !playback, mute)) return true;
            _logger.Warning("MacAudioService.SetDefaultDeviceMute", $"The default {(playback ? "output" : "input")} device has no mute or volume control.");
            return false;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.Warning("MacAudioService.SetDefaultDeviceMute", ex.Message);
            return false;
        }
    }

    // ── Hardware path (headset detection) ───────────────────────────────────────────────────────

    // DeviceTriggerService asks for every profile on every headset event, so this answers from the
    // last listing and only re-lists (at most once a second) for an id it hasn't seen.
    public string? GetAudioEndpointPath(string audioDeviceId)
    {
        bool refresh;
        lock (_gate)
        {
            if (_paths.TryGetValue(audioDeviceId, out var known)) return known;
            refresh = DateTime.UtcNow - _pathsAt > TimeSpan.FromSeconds(1);
            if (refresh) _pathsAt = DateTime.UtcNow;
        }
        if (refresh) { List(output: true); List(output: false); }
        lock (_gate) return _paths.TryGetValue(audioDeviceId, out var path) ? path : null;
    }

    // ── Change detection ────────────────────────────────────────────────────────────────────────

    private readonly record struct DeviceState(string Name, bool? Jack);

    private void ScheduleRefresh()
    {
        CancellationTokenSource cts;
        lock (_gate)
        {
            if (Disposed) return;
            _pending?.Cancel();
            _pending?.Dispose();
            _pending = cts = new CancellationTokenSource();
        }
        _ = Task.Delay(_debounce, cts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled || Disposed) return;
            try { DiffAndRaise(); }
            catch (Exception ex) { _logger.Warning("MacAudioService", ex.Message); }
        }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    // The first call only records a baseline.
    private void DiffAndRaise()
    {
        if (Disposed) return;
        _diffLock.Wait();
        try
        {
            bool first;
            Dictionary<string, DeviceState>? previous;
            lock (_gate) { previous = _lastStates; first = previous == null; }
            var current = Snapshot();
            if (current == null || Disposed || first) return;

            bool listChanged = previous!.Count != current.Count ||
                current.Any(kv => !previous.TryGetValue(kv.Key, out var old) || old.Name != kv.Value.Name);
            if (listChanged) DevicesChanged?.Invoke();
            // Headphones plugged in: a jack that was empty now has something in it.
            foreach (var (key, now) in current)
                if (now.Jack == true && previous.TryGetValue(key, out var old) && old.Jack == false)
                    DevicePropertyChanged?.Invoke(key[2..]);
        }
        finally { _diffLock.Release(); }
    }

    // Keyed "o:<uid>" / "i:<uid>": a device that gains a direction is a change.
    private Dictionary<string, DeviceState>? Snapshot()
    {
        try
        {
            var devices = _audio.GetDevices();
            var snap = new Dictionary<string, DeviceState>(StringComparer.Ordinal);
            var paths = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var output in new[] { true, false })
                foreach (var d in Offered(devices, output))
                {
                    snap[(output ? "o:" : "i:") + d.Uid] = new DeviceState(d.Name, d.JackConnected);
                    paths[d.Uid] = PathOf(d);
                }
            lock (_gate)
            {
                _lastStates = snap;
                _paths = paths;
                _pathsAt = DateTime.UtcNow;
            }
            return snap;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.Warning("MacAudioService.Snapshot", ex.Message);
            return null;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        _listener?.Dispose();
        lock (_gate)
        {
            _pending?.Cancel();
            _pending?.Dispose();
            _pending = null;
        }
    }
}

// Plays the app's in-memory sounds (switch sounds, mute blips) on the default output — what
// System.Media.SoundPlayer does on Windows. Registered as WavPlayback.Player at startup.
public sealed class MacWavPlayer : IWavPlayer
{
    private readonly IMacAudioStreams _streams;

    public MacWavPlayer() : this(new AudioQueueStreams()) { }

    internal MacWavPlayer(IMacAudioStreams streams) => _streams = streams;

    public void PlayWav(byte[] wav)
    {
        var data = WavData.Parse(wav);
        _streams.PlayPcm(null, data.Samples, data.Format, data.SampleRate, data.Channels);
    }
}
