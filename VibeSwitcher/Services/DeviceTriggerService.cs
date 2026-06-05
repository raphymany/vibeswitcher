using VibeSwitcher.Helpers;
using VibeSwitcher.Models;

namespace VibeSwitcher.Services;

public sealed class DeviceTriggerService : IDisposable
{
    private readonly IAudioService _audioService;
    private readonly IConfigService _configService;
    private readonly Action<DeviceProfile> _switchCallback;
    private readonly Func<DateTime> _clock;
    private volatile HashSet<string> _connectedIds;
    private volatile bool _disposed;

    // Minimum gap between forward property-change triggers for the same device.
    // Prevents false positives from rapid property updates (e.g. volume changes).
    private static readonly TimeSpan PropCooldown = TimeSpan.FromSeconds(30);
    private readonly Dictionary<string, DateTime> _propCooldowns =
        new(StringComparer.OrdinalIgnoreCase);

    // Revert state: when we auto-switch to a profile, remember what was active before
    // so we can switch back when the device disconnects (OnDevicesChanged).
    // Property changes are only used for forward triggers — for always-ready dongles
    // (e.g. Logitech wireless) Windows only fires OnPropertyValueChanged on power-ON,
    // not power-OFF, so property changes cannot be used to detect "headset turned off."
    private readonly record struct RevertInfo(Guid TriggeredProfileId, Guid? PreviousProfileId);
    private RevertInfo? _revertInfo;
    private readonly object _stateLock = new();

    public DeviceTriggerService(
        IAudioService audioService,
        IConfigService configService,
        Action<DeviceProfile> switchCallback,
        Func<DateTime>? clock = null)
    {
        _audioService    = audioService;
        _configService   = configService;
        _switchCallback  = switchCallback;
        _clock           = clock ?? (() => DateTime.UtcNow);
        _connectedIds    = BuildConnectedSet();
        _audioService.DevicesChanged        += OnDevicesChanged;
        _audioService.DevicePropertyChanged += OnDevicePropertyChanged;
    }

    private HashSet<string> BuildConnectedSet()
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in _audioService.GetPlaybackDevices())
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
            .FirstOrDefault(p => IsTriggeredBy(p, newlyConnected));

        if (profile == null) return;
        lock (_stateLock) _revertInfo = new RevertInfo(profile.Id, _configService.Current.ActiveProfileId);
        DispatchSwitch(profile);
    }

    // Fallback path for devices whose Windows state never changes on power-on/off
    // (e.g. wireless headsets whose USB dongle keeps the endpoint "ready" at all times).
    // Only used for forward triggers — Windows only fires OnPropertyValueChanged on
    // power-ON for these devices, so there is no property-change signal for power-OFF.
    // Revert (if applicable) is handled by OnDevicesChanged on actual disconnect.
    private void OnDevicePropertyChanged(string deviceId)
    {
        if (_disposed) return;

        var profile = _configService.Current.Profiles
            .Where(p => p.TriggerOnConnect && p.Id != _configService.Current.ActiveProfileId)
            .FirstOrDefault(p => IsTriggeredByDevice(p, deviceId));

        if (profile == null) return;

        lock (_stateLock)
        {
            var now = _clock();
            if (_propCooldowns.TryGetValue(deviceId, out var last) &&
                now - last < PropCooldown)
                return;
            _propCooldowns[deviceId] = now;
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

    // Auto-switch is playback-only — only the playback device triggers a profile switch.
    private static bool IsTriggeredBy(DeviceProfile profile, HashSet<string> deviceIds) =>
        profile.PlaybackDeviceId != null && deviceIds.Contains(profile.PlaybackDeviceId);

    private static bool IsTriggeredByDevice(DeviceProfile profile, string deviceId) =>
        StringComparer.OrdinalIgnoreCase.Equals(profile.PlaybackDeviceId, deviceId);

    // Called by HidHeadsetService when a monitored wireless headset powers on.
    // Uses the HID signal instead of waiting for the Windows audio property change,
    // which arrives 3-5 seconds later for LIGHTSPEED dongles.
    public void OnHidWirelessConnected(HidHeadsetDescriptor descriptor)
    {
        if (_disposed) return;

        var profile = _configService.Current.Profiles
            .Where(p => p.TriggerOnConnect && p.Id != _configService.Current.ActiveProfileId)
            .FirstOrDefault(p => IsProfileForDescriptor(p, descriptor));

        if (profile == null) return;

        lock (_stateLock)
            _revertInfo = new RevertInfo(profile.Id, _configService.Current.ActiveProfileId);

        AppLogger.Info("DeviceTriggerService.HidConnect",
            $"{descriptor.ModelName}: switching to '{profile.Name}'.");
        DispatchSwitch(profile);
    }

    // Called by HidHeadsetService when a monitored wireless headset powers off.
    // Triggers the same revert logic as a physical device disconnect.
    public void OnHidWirelessDisconnected(HidHeadsetDescriptor descriptor)
    {
        if (_disposed) return;

        RevertInfo? ri;
        lock (_stateLock) ri = _revertInfo;

        if (!ri.HasValue)
        {
            AppLogger.Info("DeviceTriggerService.HidRevert",
                $"{descriptor.ModelName}: no revert info set — skipping.");
            return;
        }

        if (_configService.Current.ActiveProfileId != ri.Value.TriggeredProfileId)
        {
            AppLogger.Info("DeviceTriggerService.HidRevert",
                $"{descriptor.ModelName}: active profile changed since trigger — skipping revert.");
            return;
        }

        var triggeredProfile = _configService.Current.Profiles
            .FirstOrDefault(p => p.Id == ri.Value.TriggeredProfileId);

        if (triggeredProfile == null)
        {
            AppLogger.Info("DeviceTriggerService.HidRevert",
                $"{descriptor.ModelName}: triggered profile not found — skipping.");
            return;
        }

        if (!IsProfileForDescriptor(triggeredProfile, descriptor))
        {
            AppLogger.Info("DeviceTriggerService.HidRevert",
                $"{descriptor.ModelName}: triggered profile '{triggeredProfile.Name}' does not match descriptor — skipping.");
            return;
        }

        AppLogger.Info("DeviceTriggerService.HidRevert",
            $"{descriptor.ModelName}: reverting from '{triggeredProfile.Name}'.");
        lock (_stateLock) _revertInfo = null;
        RevertToPrevious(ri.Value.PreviousProfileId);
    }

    private bool IsProfileForDescriptor(DeviceProfile profile, HidHeadsetDescriptor descriptor)
    {
        var vidPid = $"VID_{descriptor.VendorId:X4}&PID_{descriptor.ProductId:X4}";

        // Try path-based matching first (PKEY_AudioEndpoint_Path contains VID/PID for USB devices).
        var pbPath = profile.PlaybackDeviceId != null
            ? _audioService.GetAudioEndpointPath(profile.PlaybackDeviceId) : null;
        var recPath = profile.RecordingDeviceId != null
            ? _audioService.GetAudioEndpointPath(profile.RecordingDeviceId) : null;

        if ((pbPath != null && pbPath.Contains(vidPid, StringComparison.OrdinalIgnoreCase)) ||
            (recPath != null && recPath.Contains(vidPid, StringComparison.OrdinalIgnoreCase)))
            return true;

        // Fallback: Windows names USB audio devices using the product string, which typically
        // matches or contains the HID model name (e.g. "Headphones (Logitech PRO X Wireless)").
        AppLogger.Info("DeviceTriggerService.HidRevert",
            $"Path not available for '{profile.Name}' — falling back to friendly name match for '{descriptor.ModelName}'.");
        return FriendlyNameMatchesModel(profile.PlaybackDeviceId, descriptor.ModelName)
            || FriendlyNameMatchesModel(profile.RecordingDeviceId, descriptor.ModelName);
    }

    private bool FriendlyNameMatchesModel(string? audioDeviceId, string modelName)
    {
        if (audioDeviceId == null) return false;
        var device = _audioService.GetPlaybackDevices()
            .Concat(_audioService.GetRecordingDevices())
            .FirstOrDefault(d => string.Equals(d.Id, audioDeviceId, StringComparison.OrdinalIgnoreCase));
        if (device == null) return false;
        AppLogger.Info("DeviceTriggerService.HidRevert",
            $"  Audio device name=[{device.FriendlyName}] vs model=[{modelName}]");
        // Match if the Windows name contains the model name or vice versa.
        return device.FriendlyName.Contains(modelName, StringComparison.OrdinalIgnoreCase)
            || modelName.Contains(device.FriendlyName, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        _disposed = true;
        _audioService.DevicesChanged        -= OnDevicesChanged;
        _audioService.DevicePropertyChanged -= OnDevicePropertyChanged;
    }
}
