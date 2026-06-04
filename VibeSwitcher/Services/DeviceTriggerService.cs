using VibeSwitcher.Models;

namespace VibeSwitcher.Services;

public sealed class DeviceTriggerService : IDisposable
{
    private readonly IAudioService _audioService;
    private readonly IConfigService _configService;
    private readonly Action<DeviceProfile> _switchCallback;
    private volatile HashSet<string> _connectedIds;
    private volatile bool _disposed;

    public DeviceTriggerService(
        IAudioService audioService,
        IConfigService configService,
        Action<DeviceProfile> switchCallback)
    {
        _audioService    = audioService;
        _configService   = configService;
        _switchCallback  = switchCallback;
        _connectedIds    = BuildConnectedSet();
        _audioService.DevicesChanged += OnDevicesChanged;
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

    public void Dispose()
    {
        _disposed = true;
        _audioService.DevicesChanged -= OnDevicesChanged;
    }
}
