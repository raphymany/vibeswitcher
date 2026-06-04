using VibeSwitcher.Models;

namespace VibeSwitcher.Services;

public sealed class DeviceTriggerService : IDisposable
{
    private readonly IAudioService _audioService;
    private readonly IConfigService _configService;
    private readonly Action<DeviceProfile> _switchCallback;
    private volatile HashSet<string> _connectedIds;
    private volatile bool _disposed;
    private readonly Dictionary<string, DateTime> _propCooldowns =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _cooldownLock = new();

    // Minimum gap between property-change-triggered switches for the same device.
    // Prevents false positives from rapid property updates (e.g. volume changes).
    private static readonly TimeSpan PropCooldown = TimeSpan.FromSeconds(30);

    public DeviceTriggerService(
        IAudioService audioService,
        IConfigService configService,
        Action<DeviceProfile> switchCallback)
    {
        _audioService    = audioService;
        _configService   = configService;
        _switchCallback  = switchCallback;
        _connectedIds    = BuildConnectedSet();
        _audioService.DevicesChanged       += OnDevicesChanged;
        _audioService.DevicePropertyChanged += OnDevicePropertyChanged;
    }

    private HashSet<string> BuildConnectedSet()
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in _audioService.GetPlaybackDevices())
            if (d.IsConnected) ids.Add(d.Id);
        foreach (var d in _audioService.GetRecordingDevices())
            if (d.IsConnected) ids.Add(d.Id);
        return ids;
    }

    private void OnDevicesChanged()
    {
        if (_disposed) return;

        var current = BuildConnectedSet();
        var newlyConnected = new HashSet<string>(
            current.Except(_connectedIds, StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
        _connectedIds = current;

        if (newlyConnected.Count == 0) return;

        var profile = _configService.Current.Profiles
            .Where(p => p.TriggerOnConnect && p.Id != _configService.Current.ActiveProfileId)
            .OrderByDescending(p => p.IsPinned)
            .ThenBy(p => p.SortOrder)
            .FirstOrDefault(p => IsTriggeredBy(p, newlyConnected));

        if (profile == null) return;
        DispatchSwitch(profile);
    }

    // Fallback path for devices whose Windows state never changes on power-on/off
    // (e.g. wireless headsets whose USB dongle keeps the endpoint "ready" at all times).
    // When a device's properties change, we check if it matches a TriggerOnConnect profile
    // and switch — subject to a per-device cooldown to avoid false positives.
    private void OnDevicePropertyChanged(string deviceId)
    {
        if (_disposed) return;

        var profile = _configService.Current.Profiles
            .Where(p => p.TriggerOnConnect && p.Id != _configService.Current.ActiveProfileId)
            .OrderByDescending(p => p.IsPinned)
            .ThenBy(p => p.SortOrder)
            .FirstOrDefault(p => IsTriggeredByDevice(p, deviceId));

        if (profile == null) return;

        lock (_cooldownLock)
        {
            if (_propCooldowns.TryGetValue(deviceId, out var last) &&
                DateTime.UtcNow - last < PropCooldown)
                return;
            _propCooldowns[deviceId] = DateTime.UtcNow;
        }

        DispatchSwitch(profile);
    }

    private void DispatchSwitch(DeviceProfile profile)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null)
            dispatcher.InvokeAsync(() => _switchCallback(profile));
        else
            _switchCallback(profile);
    }

    private static bool IsTriggeredBy(DeviceProfile profile, HashSet<string> newlyConnected) =>
        profile.Mode switch
        {
            ProfileMode.Playback  => profile.PlaybackDeviceId  != null && newlyConnected.Contains(profile.PlaybackDeviceId),
            ProfileMode.Recording => profile.RecordingDeviceId != null && newlyConnected.Contains(profile.RecordingDeviceId),
            // Either endpoint connecting is enough — USB headsets often register playback
            // before recording, so requiring both would miss the first event.
            ProfileMode.Both      => (profile.PlaybackDeviceId  != null && newlyConnected.Contains(profile.PlaybackDeviceId)) ||
                                     (profile.RecordingDeviceId != null && newlyConnected.Contains(profile.RecordingDeviceId)),
            _ => false
        };

    private static bool IsTriggeredByDevice(DeviceProfile profile, string deviceId) =>
        profile.Mode switch
        {
            ProfileMode.Playback  => StringComparer.OrdinalIgnoreCase.Equals(profile.PlaybackDeviceId,  deviceId),
            ProfileMode.Recording => StringComparer.OrdinalIgnoreCase.Equals(profile.RecordingDeviceId, deviceId),
            ProfileMode.Both      => StringComparer.OrdinalIgnoreCase.Equals(profile.PlaybackDeviceId,  deviceId) ||
                                     StringComparer.OrdinalIgnoreCase.Equals(profile.RecordingDeviceId, deviceId),
            _ => false
        };

    public void Dispose()
    {
        _disposed = true;
        _audioService.DevicesChanged       -= OnDevicesChanged;
        _audioService.DevicePropertyChanged -= OnDevicePropertyChanged;
    }
}
