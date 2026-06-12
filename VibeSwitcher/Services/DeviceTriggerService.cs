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

    // Revert stack: each auto-switch pushes an entry so chained reverts work correctly.
    // e.g. Speaker → BT → Logitech: turning off Logitech reverts to BT, turning off BT
    // then reverts to Speaker. HID-managed profiles only revert via OnHidWirelessDisconnected.
    private readonly record struct RevertInfo(Guid TriggeredProfileId, Guid? PreviousProfileId, bool IsHidTriggered = false);
    private readonly Stack<RevertInfo> _revertStack = new();
    private readonly object _stateLock = new();
    private readonly List<HidHeadsetDescriptor> _hidDescriptors = [];

    private readonly IAppLogger _logger;

    public DeviceTriggerService(
        IAudioService audioService,
        IConfigService configService,
        Action<DeviceProfile> switchCallback,
        IAppLogger logger,
        Func<DateTime>? clock = null)
    {
        _audioService    = audioService;
        _configService   = configService;
        _switchCallback  = switchCallback;
        _logger          = logger;
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

    // Audio device-change callbacks arrive on a background (COM/debounce) thread. Marshal the whole
    // handler to the UI thread so the profile-list reads below run on the same thread that mutates
    // config during Settings edits — avoiding a "collection was modified" race that silently drops
    // the auto-switch.
    private void OnDevicesChanged()
    {
        var d = System.Windows.Application.Current?.Dispatcher;
        if (d != null && !d.CheckAccess()) { d.InvokeAsync(HandleDevicesChanged); return; }
        HandleDevicesChanged();
    }

    private void HandleDevicesChanged()
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
            lock (_stateLock) ri = _revertStack.Count > 0 ? _revertStack.Peek() : null;

            if (ri.HasValue && _configService.Current.ActiveProfileId == ri.Value.TriggeredProfileId)
            {
                var triggeredProfile = _configService.Current.Profiles
                    .FirstOrDefault(p => p.Id == ri.Value.TriggeredProfileId);
                if (triggeredProfile != null && IsTriggeredBy(triggeredProfile, newlyDisconnected) && !ri.Value.IsHidTriggered)
                {
                    lock (_stateLock) _revertStack.Pop();
                    RevertToPrevious(ri.Value.PreviousProfileId, current);
                    return;
                }
            }
        }

        if (newlyConnected.Count == 0) return;

        var profile = _configService.Current.Profiles
            .Where(p => p.TriggerOnConnect && p.Id != _configService.Current.ActiveProfileId && !IsHidManaged(p))
            .FirstOrDefault(p => IsTriggeredBy(p, newlyConnected));

        if (profile == null) return;
        lock (_stateLock) _revertStack.Push(new RevertInfo(profile.Id, _configService.Current.ActiveProfileId));
        DispatchSwitch(profile);
    }

    // Fallback path for devices whose Windows state never changes on power-on/off
    // (e.g. wireless headsets whose USB dongle keeps the endpoint "ready" at all times).
    // Only used for forward triggers — Windows only fires OnPropertyValueChanged on
    // power-ON for these devices, so there is no property-change signal for power-OFF.
    // Revert (if applicable) is handled by OnDevicesChanged on actual disconnect.
    private void OnDevicePropertyChanged(string deviceId)
    {
        var d = System.Windows.Application.Current?.Dispatcher;
        if (d != null && !d.CheckAccess()) { d.InvokeAsync(() => HandleDevicePropertyChanged(deviceId)); return; }
        HandleDevicePropertyChanged(deviceId);
    }

    private void HandleDevicePropertyChanged(string deviceId)
    {
        if (_disposed) return;

        var profile = _configService.Current.Profiles
            .Where(p => p.TriggerOnConnect && p.Id != _configService.Current.ActiveProfileId && !IsHidManaged(p))
            .FirstOrDefault(p => IsTriggeredByDevice(p, deviceId));

        if (profile == null) return;

        lock (_stateLock)
        {
            var now = _clock();
            if (_propCooldowns.TryGetValue(deviceId, out var last) &&
                now - last < PropCooldown)
                return;
            _propCooldowns[deviceId] = now;
            _revertStack.Push(new RevertInfo(profile.Id, _configService.Current.ActiveProfileId));
        }

        DispatchSwitch(profile);
    }

    // 'connected' is the connected-device snapshot for the event being processed, passed in so
    // the whole revert cascade reasons over one consistent view rather than re-reading the field
    // (which another DevicesChanged event could swap underneath it).
    private void RevertToPrevious(Guid? previousProfileId, HashSet<string> connected)
    {
        if (!previousProfileId.HasValue) return;
        var prev = _configService.Current.Profiles.FirstOrDefault(p => p.Id == previousProfileId);
        if (prev == null) return;

        // If the target profile's device has since disconnected, skip it and cascade
        // to the next revert entry. This handles e.g. BT turning off while on Logitech —
        // when Logitech later reverts "to BT", BT is gone, so we fall through to Speaker.
        if (prev.TriggerOnConnect && !IsHidManaged(prev) &&
            prev.PlaybackDeviceId != null && !connected.Contains(prev.PlaybackDeviceId))
        {
            RevertInfo? next;
            lock (_stateLock) next = _revertStack.Count > 0 ? _revertStack.Pop() : null;
            RevertToPrevious(next?.PreviousProfileId, connected);
            return;
        }

        DispatchSwitch(prev);
    }

    private void DispatchSwitch(DeviceProfile profile)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null)
            dispatcher.InvokeAsync(() => _switchCallback(profile));
        else
            _switchCallback(profile);
    }

    public void RegisterHidDescriptor(HidHeadsetDescriptor descriptor)
    {
        lock (_stateLock) _hidDescriptors.Add(descriptor);
    }

    // Returns true if the profile's device is monitored via HID — those profiles must only
    // be forward-triggered by OnHidWirelessConnected, not by Windows audio API events.
    private bool IsHidManaged(DeviceProfile profile) =>
        _hidDescriptors.Any(d => IsProfileForDescriptor(profile, d));

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
        var d = System.Windows.Application.Current?.Dispatcher;
        if (d != null && !d.CheckAccess()) { d.InvokeAsync(() => HandleHidConnected(descriptor)); return; }
        HandleHidConnected(descriptor);
    }

    private void HandleHidConnected(HidHeadsetDescriptor descriptor)
    {
        if (_disposed) return;

        var profile = _configService.Current.Profiles
            .Where(p => p.TriggerOnConnect && p.Id != _configService.Current.ActiveProfileId)
            .FirstOrDefault(p => IsProfileForDescriptor(p, descriptor));

        if (profile == null) return;

        lock (_stateLock)
            _revertStack.Push(new RevertInfo(profile.Id, _configService.Current.ActiveProfileId, IsHidTriggered: true));

        _logger.Info("DeviceTriggerService.HidConnect",
            $"{descriptor.ModelName}: switching to '{profile.Name}'.");
        DispatchSwitch(profile);
    }

    // Called by HidHeadsetService when a monitored wireless headset powers off.
    // Triggers the same revert logic as a physical device disconnect.
    public void OnHidWirelessDisconnected(HidHeadsetDescriptor descriptor)
    {
        var d = System.Windows.Application.Current?.Dispatcher;
        if (d != null && !d.CheckAccess()) { d.InvokeAsync(() => HandleHidDisconnected(descriptor)); return; }
        HandleHidDisconnected(descriptor);
    }

    private void HandleHidDisconnected(HidHeadsetDescriptor descriptor)
    {
        if (_disposed) return;

        RevertInfo? ri;
        lock (_stateLock) ri = _revertStack.Count > 0 ? _revertStack.Peek() : null;

        if (!ri.HasValue)
        {
            _logger.Info("DeviceTriggerService.HidRevert",
                $"{descriptor.ModelName}: no revert info set — skipping.");
            return;
        }

        if (_configService.Current.ActiveProfileId != ri.Value.TriggeredProfileId)
        {
            _logger.Info("DeviceTriggerService.HidRevert",
                $"{descriptor.ModelName}: active profile changed since trigger — skipping revert.");
            return;
        }

        var triggeredProfile = _configService.Current.Profiles
            .FirstOrDefault(p => p.Id == ri.Value.TriggeredProfileId);

        if (triggeredProfile == null)
        {
            _logger.Info("DeviceTriggerService.HidRevert",
                $"{descriptor.ModelName}: triggered profile not found — skipping.");
            return;
        }

        if (!IsProfileForDescriptor(triggeredProfile, descriptor))
        {
            _logger.Info("DeviceTriggerService.HidRevert",
                $"{descriptor.ModelName}: triggered profile '{triggeredProfile.Name}' does not match descriptor — skipping.");
            return;
        }

        _logger.Info("DeviceTriggerService.HidRevert",
            $"{descriptor.ModelName}: reverting from '{triggeredProfile.Name}'.");
        lock (_stateLock) _revertStack.Pop();
        RevertToPrevious(ri.Value.PreviousProfileId, _connectedIds);
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
        _logger.Info("DeviceTriggerService.HidRevert",
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
        _logger.Info("DeviceTriggerService.HidRevert",
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
