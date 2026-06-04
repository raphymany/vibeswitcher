using VibeSwitcher.Models;

namespace VibeSwitcher.Services;

public sealed class DeviceTriggerService : IDisposable
{
    private readonly IAudioService _audioService;
    private readonly IConfigService _configService;
    private readonly Action<DeviceProfile> _switchCallback;
    private volatile HashSet<string> _connectedIds;
    private volatile bool _disposed;

    // Minimum gap between forward property-change triggers for the same device.
    // Prevents false positives from rapid property updates (e.g. volume changes).
    private static readonly TimeSpan PropCooldown = TimeSpan.FromSeconds(30);
    private readonly Dictionary<string, DateTime> _propCooldowns =
        new(StringComparer.OrdinalIgnoreCase);

    // Revert state: when we auto-switch to a profile, remember what was active before
    // so we can switch back when the device disconnects or turns off.
    private readonly record struct RevertInfo(Guid TriggeredProfileId, Guid? PreviousProfileId);
    private RevertInfo? _revertInfo;
    private readonly object _stateLock = new();

    public DeviceTriggerService(
        IAudioService audioService,
        IConfigService configService,
        Action<DeviceProfile> switchCallback)
    {
        _audioService    = audioService;
        _configService   = configService;
        _switchCallback  = switchCallback;
        _connectedIds    = BuildConnectedSet();
        _audioService.DevicesChanged        += OnDevicesChanged;
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
        var newlyDisconnected = new HashSet<string>(
            _connectedIds.Except(current, StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
        _connectedIds = current;

        // Revert if the device that triggered the last auto-switch has now disconnected
        if (newlyDisconnected.Count > 0)
        {
            RevertInfo? ri;
            lock (_stateLock) ri = _revertInfo;

            if (ri.HasValue && _configService.Current.ActiveProfileId == ri.Value.TriggeredProfileId)
            {
                var triggeredProfile = _configService.Current.Profiles
                    .FirstOrDefault(p => p.Id == ri.Value.TriggeredProfileId);
                if (triggeredProfile != null && IsTriggeredBy(triggeredProfile, newlyDisconnected))
                {
                    lock (_stateLock) _revertInfo = null;
                    RevertToPrevious(ri.Value.PreviousProfileId);
                    return;
                }
            }
        }

        if (newlyConnected.Count == 0) return;

        var profile = _configService.Current.Profiles
            .Where(p => p.TriggerOnConnect && p.Id != _configService.Current.ActiveProfileId)
            .OrderByDescending(p => p.IsPinned)
            .ThenBy(p => p.SortOrder)
            .FirstOrDefault(p => IsTriggeredBy(p, newlyConnected));

        if (profile == null) return;
        lock (_stateLock) _revertInfo = new RevertInfo(profile.Id, _configService.Current.ActiveProfileId);
        DispatchSwitch(profile);
    }

    // Fallback path for devices whose Windows state never changes on power-on/off
    // (e.g. wireless headsets whose USB dongle keeps the endpoint "ready" at all times).
    private void OnDevicePropertyChanged(string deviceId)
    {
        if (_disposed) return;

        // Check if this property change means the headset turned OFF — revert to previous profile.
        RevertInfo? ri;
        lock (_stateLock) ri = _revertInfo;

        if (ri.HasValue && _configService.Current.ActiveProfileId == ri.Value.TriggeredProfileId)
        {
            var triggeredProfile = _configService.Current.Profiles
                .FirstOrDefault(p => p.Id == ri.Value.TriggeredProfileId);
            if (triggeredProfile != null && IsTriggeredByDevice(triggeredProfile, deviceId))
            {
                lock (_stateLock)
                {
                    _revertInfo = null;
                    _propCooldowns.Remove(deviceId); // allow re-trigger on next power-on
                }
                RevertToPrevious(ri.Value.PreviousProfileId);
                return;
            }
        }

        // Forward trigger: headset is powering ON — switch to its profile.
        var profile = _configService.Current.Profiles
            .Where(p => p.TriggerOnConnect && p.Id != _configService.Current.ActiveProfileId)
            .OrderByDescending(p => p.IsPinned)
            .ThenBy(p => p.SortOrder)
            .FirstOrDefault(p => IsTriggeredByDevice(p, deviceId));

        if (profile == null) return;

        lock (_stateLock)
        {
            if (_propCooldowns.TryGetValue(deviceId, out var last) &&
                DateTime.UtcNow - last < PropCooldown)
                return;
            _propCooldowns[deviceId] = DateTime.UtcNow;
            _revertInfo = new RevertInfo(profile.Id, _configService.Current.ActiveProfileId);
        }

        DispatchSwitch(profile);
    }

    private void RevertToPrevious(Guid? previousProfileId)
    {
        if (!previousProfileId.HasValue) return;
        var prev = _configService.Current.Profiles.FirstOrDefault(p => p.Id == previousProfileId);
        if (prev != null) DispatchSwitch(prev);
    }

    private void DispatchSwitch(DeviceProfile profile)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null)
            dispatcher.InvokeAsync(() => _switchCallback(profile));
        else
            _switchCallback(profile);
    }

    private static bool IsTriggeredBy(DeviceProfile profile, HashSet<string> deviceIds) =>
        profile.Mode switch
        {
            ProfileMode.Playback  => profile.PlaybackDeviceId  != null && deviceIds.Contains(profile.PlaybackDeviceId),
            ProfileMode.Recording => profile.RecordingDeviceId != null && deviceIds.Contains(profile.RecordingDeviceId),
            // Either endpoint connecting is enough — USB headsets often register playback
            // before recording, so requiring both would miss the first event.
            ProfileMode.Both      => (profile.PlaybackDeviceId  != null && deviceIds.Contains(profile.PlaybackDeviceId)) ||
                                     (profile.RecordingDeviceId != null && deviceIds.Contains(profile.RecordingDeviceId)),
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
        _audioService.DevicesChanged        -= OnDevicesChanged;
        _audioService.DevicePropertyChanged -= OnDevicePropertyChanged;
    }
}
