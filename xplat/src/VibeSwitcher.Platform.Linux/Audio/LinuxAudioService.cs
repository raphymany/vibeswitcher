using VibeSwitcher.Audio;
using VibeSwitcher.Helpers;
using VibeSwitcher.Models;
using VibeSwitcher.Platform.Linux.Processes;
using VibeSwitcher.Services;

namespace VibeSwitcher.Platform.Linux.Audio;

// The Linux implementation of the app's audio service (the Windows AudioService's counterpart),
// built on pactl/pacat/parec so it works with both PipeWire and PulseAudio.
//
// Device ids are sink/source names (e.g. "alsa_output.usb-…analog-stereo"), which survive reboots.
// Behaviour mirrors Windows:
//   - lists show connected devices first, then unplugged ones (jack-sensing ports with nothing in);
//     monitor sources ("Monitor of …") are hidden, as Windows has no such inputs;
//   - applying a profile sets each device as the default, reports a device that isn't present as
//     missing, and one that refused as "set failed". Unlike Windows it always sets, even when the
//     device already looks like the default: re-setting is harmless here, and on PipeWire the
//     reported default is the *effective* one, which can differ from the configured one (e.g. after
//     the configured device was unplugged) — skipping would leave the old choice to come back;
//   - DevicesChanged fires (debounced) when devices appear, disappear or change connection state;
//     DevicePropertyChanged(id) fires when something is plugged into one of a still-present device's
//     jacks (a port turns from "not available" to "available" — e.g. headphones into a laptop's
//     jack) — the Linux counterpart of the Windows power-on property signal that
//     DeviceTriggerService uses for auto-switching. Like that Windows signal, unplugging raises
//     nothing (no revert).
public sealed class LinuxAudioService : IPlatformAudioService
{
    private const int MicRate = 16000;
    private const int ToneRate = 48000;
    private const int ToneChannels = 2;

    private readonly IAppLogger _logger;
    private readonly ISessionErrorTracker _errorTracker;
    private readonly PactlClient _pactl;
    private readonly PulseStreams _streams;
    private readonly IUsbSysfs? _sysfs;
    private readonly PulseEventWatcher? _watcher;
    private readonly TimeSpan _debounce;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _diffLock = new(1, 1); // one diff at a time, so none raises from a stale baseline
    private Dictionary<string, DeviceState>? _lastStates;
    private Dictionary<string, string?> _paths = new(StringComparer.Ordinal);
    private DateTime _pathsAt = DateTime.MinValue;
    private CancellationTokenSource? _pending;
    private int _unavailableReported; // 1 while a "sound server unavailable" error is already in the session log
    private bool _snapshotFailing;    // log a failing background refresh once, not on every retry
    private volatile bool _disposed;

    public event Action? DevicesChanged;
    public event Action<string>? DevicePropertyChanged;

    public LinuxAudioService(IAppLogger logger, ISessionErrorTracker errorTracker)
        : this(logger, errorTracker, ProcessRunner.Instance, new UsbSysfs(), listenForChanges: true) { }

    internal LinuxAudioService(IAppLogger logger, ISessionErrorTracker errorTracker, IProcessRunner runner,
        IUsbSysfs? sysfs, bool listenForChanges, TimeSpan? debounce = null, Func<int, TimeSpan>? watcherBackoff = null,
        bool forceTextOutput = false, TimeSpan? unavailableCooldown = null)
    {
        _logger = logger;
        _errorTracker = errorTracker;
        _pactl = new PactlClient(runner, forceTextOutput, unavailableCooldown);
        _streams = new PulseStreams(runner);
        _sysfs = sysfs;
        _debounce = debounce ?? TimeSpan.FromMilliseconds(500);
        if (listenForChanges)
        {
            _watcher = new PulseEventWatcher(runner, logger, watcherBackoff);
            _watcher.Changed += ScheduleRefresh;
            _watcher.ListeningFailed += reason => _errorTracker.Record(ErrorCode.DeviceNotificationFailed,
                "Device Notification Setup Failed",
                $"Could not subscribe to device change events ({reason}). The device list will not refresh automatically.");
            // Baseline first, so the first real change can be diffed against it; start listening regardless.
            var watcher = _watcher;
            Task.Run(() =>
            {
                try { _pactl.ClearCooldown(); TrySnapshot(); }
                catch (Exception ex) { _logger.Warning("LinuxAudioService", ex.Message); }
                finally { watcher.Start(); }
            });
        }
    }

    // ── Listing ─────────────────────────────────────────────────────────────────────────────────

    public IReadOnlyList<AudioDeviceInfo> GetPlaybackDevices() => List(sinks: true);

    public IReadOnlyList<AudioDeviceInfo> GetRecordingDevices() => List(sinks: false);

    private IReadOnlyList<AudioDeviceInfo> List(bool sinks)
    {
        if (_disposed) return [];
        try
        {
            var devices = Visible(_pactl.ListDevices(sinks));
            ServerReachable();
            var infos = new List<AudioDeviceInfo>(devices.Count);
            lock (_gate)
                foreach (var d in devices)
                {
                    var path = LinuxHardwareKey.InstancePath(d, _sysfs);
                    _paths[d.Name] = path;
                    infos.Add(ToInfo(d, path));
                }
            // Active devices first, unplugged at the bottom — both groups sorted by name (as Windows).
            return infos.OrderBy(d => d.IsConnected ? 0 : 1).ThenBy(d => d.FriendlyName).ToList();
        }
        catch (Exception ex) when (IsPactlFailure(ex))
        {
            _logger.Warning("LinuxAudioService.List", ex.Message);
            ReportUnavailable(ex, ErrorCode.AudioEnumerationFailed, "Audio Device List Unavailable");
            return [];
        }
    }

    private static bool IsPactlFailure(Exception ex) =>
        ex is SoundServerUnavailableException or PactlCommandException or FormatException or System.Text.Json.JsonException;

    private static IReadOnlyList<PulseDevice> Visible(IReadOnlyList<PulseDevice> devices) =>
        devices.Where(d => !d.IsMonitor && !d.IsDummy).ToList();

    private static AudioDeviceInfo ToInfo(PulseDevice d, string? instancePath) => new(
        d.Name, d.Description, d.IsSink,
        IsConnected: d.IsAvailable,
        IsDisabled: false, // Linux has no per-device "disabled" state (a card profile set to Off removes the device)
        HardwareKey: AudioDeviceEnumerator.NormalizeHardwareKey(instancePath));

    // ── Switching ───────────────────────────────────────────────────────────────────────────────

    public Task<ProfileSwitchResult> ApplyProfileAsync(DeviceProfile profile) => Task.Run(() => Apply(profile));

    private ProfileSwitchResult Apply(DeviceProfile profile)
    {
        try
        {
            var (pbApplied, pbMissing, pbFailed) = ApplyOne(sink: true, profile.PlaybackDeviceId);
            var (recApplied, recMissing, recFailed) = ApplyOne(sink: false, profile.RecordingDeviceId);
            ServerReachable();
            return new ProfileSwitchResult(pbApplied, recApplied, pbMissing, recMissing, pbFailed, recFailed);
        }
        catch (SoundServerUnavailableException ex)
        {
            _logger.Error("LinuxAudioService.ApplyProfile", ex.Message);
            ReportUnavailable(ex, ErrorCode.AudioServiceUnavailable, "Audio Service Unavailable", force: true);
            throw new InvalidOperationException(ex.Message, ex);
        }
        catch (Exception ex) when (IsPactlFailure(ex))
        {
            // Couldn't read the device list (set-default failures are handled per device).
            var msg = $"Could not read the audio devices to switch profile: {ex.Message}";
            _logger.Error("LinuxAudioService.ApplyProfile", msg);
            _errorTracker.Record(ErrorCode.ProfileSwitchFailed, "Profile Switch Failed", msg);
            throw new InvalidOperationException(msg, ex);
        }
    }

    private (bool Applied, string? Missing, bool Failed) ApplyOne(bool sink, string? id)
    {
        if (string.IsNullOrEmpty(id)) return (false, null, false);

        var device = Visible(_pactl.ListDevices(sink)).FirstOrDefault(d => d.Name == id);
        if (device == null || !device.IsAvailable) return (false, id, false);

        try
        {
            _pactl.SetDefault(sink, id);
            return (true, null, false);
        }
        catch (PactlCommandException ex)
        {
            _logger.Error("LinuxAudioService.ApplyProfile", ex.Message);
            _errorTracker.Record(ErrorCode.PolicySetDefaultFailed, "Set Default Audio Device Failed", ex.Message);
            return (false, null, true);
        }
    }

    // ── Test tone, mic meter, mute ──────────────────────────────────────────────────────────────

    public Task TestSoundAsync(string deviceId) => Task.Run(() =>
    {
        try
        {
            _streams.PlayPcm(deviceId, TestTone.RenderPcm16(ToneRate, ToneChannels), WavSampleFormat.S16,
                ToneRate, ToneChannels, "Test tone");
        }
        catch (Exception ex)
        {
            _logger.Warning("LinuxAudioService.TestSound", ex.Message);
        }
    });

    public void RunMicLevelMonitor(string deviceId, CancellationToken ct, Action<float> onLevel)
    {
        try
        {
            _streams.Capture(deviceId, MicRate, ct, block => onLevel(PulseStreams.Rms(block)));
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.Warning("LinuxAudioService.RunMicLevelMonitor", ex.Message);
        }
    }

    public bool? GetDefaultDeviceMute(bool playback)
    {
        try
        {
            return DefaultDevice(playback)?.Mute;
        }
        catch (Exception ex) when (IsPactlFailure(ex))
        {
            _logger.Warning("LinuxAudioService.GetDefaultDeviceMute", ex.Message);
            return null;
        }
    }

    public bool SetDefaultDeviceMute(bool playback, bool mute)
    {
        try
        {
            // No real default device (only a "Monitor of …" or the dummy) → nothing to mute, as on
            // Windows with no capture device. Muting a monitor would silence desktop recording instead.
            var device = DefaultDevice(playback);
            if (device == null) return false;
            _pactl.SetMute(playback, device.Name, mute);
            return true;
        }
        catch (Exception ex) when (IsPactlFailure(ex))
        {
            _logger.Warning("LinuxAudioService.SetDefaultDeviceMute", ex.Message);
            return false;
        }
    }

    // The current default output/input, or null when it's missing, a monitor, or the dummy.
    private PulseDevice? DefaultDevice(bool playback)
    {
        var info = _pactl.GetServerInfo();
        var name = playback ? info.DefaultSink : info.DefaultSource;
        if (name == null) return null;
        var device = _pactl.ListDevices(playback).FirstOrDefault(d => d.Name == name);
        return device == null || device.IsMonitor || device.IsDummy ? null : device;
    }

    // ── Hardware path (headset detection) ───────────────────────────────────────────────────────

    // The Windows-shaped instance path (USB\VID_xxxx&PID_xxxx&MI_xx\…) of a device, or null.
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
        if (refresh) { List(sinks: true); List(sinks: false); }
        lock (_gate) return _paths.TryGetValue(audioDeviceId, out var path) ? path : null;
    }

    // ── Change detection ────────────────────────────────────────────────────────────────────────

    // Per device: connected or not, and which of its ports have something plugged in.
    private sealed record DeviceState(bool Available, IReadOnlySet<string> PluggedPorts);

    private void ScheduleRefresh()
    {
        CancellationTokenSource cts;
        lock (_gate)
        {
            if (_disposed) return;
            _pending?.Cancel();
            _pending?.Dispose();
            _pending = cts = new CancellationTokenSource();
        }
        _ = Task.Delay(_debounce, cts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled || _disposed) return;
            try { DiffAndRaise(); }
            catch (Exception ex) { _logger.Warning("LinuxAudioService", ex.Message); }
        }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    private void DiffAndRaise()
    {
        if (_disposed) return;
        _diffLock.Wait();
        try
        {
            var previous = _lastStates;
            _pactl.ClearCooldown();
            var current = TrySnapshot();
            if (current == null || _disposed) return;
            if (previous == null) { DevicesChanged?.Invoke(); return; }

            bool setChanged = previous.Count != current.Count ||
                current.Any(kv => !previous.TryGetValue(kv.Key, out var old) || old.Available != kv.Value.Available);
            if (setChanged) DevicesChanged?.Invoke();
            // Also on a set change: a jack plugged in during the same moment must not be lost.
            foreach (var (key, now) in current)
                if (previous.TryGetValue(key, out var old) && now.PluggedPorts.Except(old.PluggedPorts).Any())
                    DevicePropertyChanged?.Invoke(key[2..]); // strip the "o:"/"i:" prefix
        }
        finally { _diffLock.Release(); }
    }

    // Keyed "o:<sink>" / "i:<source>" because a sink and a source may share a name in theory.
    private Dictionary<string, DeviceState>? TrySnapshot()
    {
        try
        {
            var snap = new Dictionary<string, DeviceState>(StringComparer.Ordinal);
            var paths = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var sinks in new[] { true, false })
                foreach (var d in Visible(_pactl.ListDevices(sinks)))
                {
                    var plugged = d.Ports.Where(p => p.Availability == PortAvailability.Available).Select(p => p.Name).ToHashSet();
                    snap[(sinks ? "o:" : "i:") + d.Name] = new DeviceState(d.IsAvailable, plugged);
                    paths[d.Name] = LinuxHardwareKey.InstancePath(d, _sysfs);
                }
            lock (_gate)
            {
                _lastStates = snap;
                _paths = paths;
                _pathsAt = DateTime.UtcNow;
                _snapshotFailing = false;
            }
            ServerReachable();
            return snap;
        }
        catch (Exception ex) when (IsPactlFailure(ex))
        {
            bool first;
            lock (_gate) { first = !_snapshotFailing; _snapshotFailing = true; }
            if (first) _logger.Warning("LinuxAudioService.Snapshot", ex.Message);
            return null;
        }
    }

    // ── Error reporting ─────────────────────────────────────────────────────────────────────────

    // One session-log entry per outage (the list is re-read often); force = always (a failed switch
    // is something the user just did and should see).
    private void ReportUnavailable(Exception ex, ErrorCode code, string title, bool force = false)
    {
        if (!force && Interlocked.Exchange(ref _unavailableReported, 1) == 1) return;
        _errorTracker.Record(code, title, ex.Message);
    }

    private void ServerReachable() => Volatile.Write(ref _unavailableReported, 0);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _watcher?.Dispose();
        lock (_gate)
        {
            _pending?.Cancel();
            _pending?.Dispose();
            _pending = null;
        }
    }
}
