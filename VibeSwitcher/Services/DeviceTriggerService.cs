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

    // Revert chain: each auto-switch appends an entry (end = top/most-recent).
    // When a device at the top disconnects, we pop and revert.
    // When a device NOT at the top disconnects, we remove its entry and patch any
    // pointer that referenced it — so the next revert skips straight past it.
    // e.g. Speaker → BT → Logitech: BT turns off while on Logitech →
    //   chain becomes Speaker → Logitech; turning off Logitech reverts to Speaker.
    private readonly record struct RevertInfo(Guid TriggeredProfileId, Guid? PreviousProfileId, bool IsHidTriggered = false);
    private readonly List<RevertInfo> _revertChain = [];
    private readonly object _stateLock = new();
    private readonly List<HidHeadsetDescriptor> _hidDescriptors = [];

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

        if (newlyDisconnected.Count > 0)
        {
            var chainSnapshot = string.Join(", ", _revertChain.Select(e =>
            {
                var tp = _configService.Current.Profiles.FirstOrDefault(p => p.Id == e.TriggeredProfileId)?.Name ?? e.TriggeredProfileId.ToString()[..8];
                return $"{tp}(hid={e.IsHidTriggered})";
            }));
            var activeProfileName = _configService.Current.Profiles
                .FirstOrDefault(p => p.Id == _configService.Current.ActiveProfileId)?.Name ?? "none";
            AppLogger.Info("DeviceTriggerService.OnDevicesChanged",
                $"Disconnected devices, active='{activeProfileName}', chain=[{chainSnapshot}]");

            // Top of chain: if the currently-active profile's device disconnected, revert.
            RevertInfo? top;
            lock (_stateLock) top = _revertChain.Count > 0 ? _revertChain[^1] : null;

            if (top.HasValue && _configService.Current.ActiveProfileId == top.Value.TriggeredProfileId)
            {
                var triggeredProfile = _configService.Current.Profiles
                    .FirstOrDefault(p => p.Id == top.Value.TriggeredProfileId);
                bool deviceDisconnected = triggeredProfile != null && IsTriggeredBy(triggeredProfile, newlyDisconnected);
                AppLogger.Info("DeviceTriggerService.OnDevicesChanged",
                    $"Top match: profile='{triggeredProfile?.Name}', deviceDisconnected={deviceDisconnected}, isHid={top.Value.IsHidTriggered}");
                if (triggeredProfile != null && deviceDisconnected && !top.Value.IsHidTriggered)
                {
                    AppLogger.Info("DeviceTriggerService.OnDevicesChanged",
                        $"Reverting from '{triggeredProfile.Name}' via device disconnect.");
                    lock (_stateLock) _revertChain.RemoveAt(_revertChain.Count - 1);
                    RevertToPrevious(top.Value.PreviousProfileId);
                    return;
                }
            }

            // Non-top entries: remove any whose device just disconnected and patch
            // any PreviousProfileId pointers that referenced the removed entry.
            PruneDisconnected(newlyDisconnected);
        }

        if (newlyConnected.Count == 0) return;

        var profile = _configService.Current.Profiles
            .Where(p => p.TriggerOnConnect && p.Id != _configService.Current.ActiveProfileId && !IsHidManaged(p))
            .FirstOrDefault(p => IsTriggeredBy(p, newlyConnected));

        if (profile == null) return;
        lock (_stateLock) _revertChain.Add(new RevertInfo(profile.Id, _configService.Current.ActiveProfileId));
        DispatchSwitch(profile);
    }

    // Removes non-top chain entries whose non-HID device disconnected.
    // Patches PreviousProfileId on any entry that pointed to the removed one,
    // so that future reverts skip straight to the correct destination.
    private void PruneDisconnected(HashSet<string> disconnectedIds)
    {
        lock (_stateLock)
        {
            var profiles = _configService.Current.Profiles;
            // Iterate all but the last (top) entry, bottom-to-top.
            for (int i = _revertChain.Count - 2; i >= 0; i--)
            {
                var entry = _revertChain[i];
                if (entry.IsHidTriggered) continue;
                var p = profiles.FirstOrDefault(x => x.Id == entry.TriggeredProfileId);
                if (p == null || !IsTriggeredBy(p, disconnectedIds)) continue;

                // Patch any entry whose PreviousProfileId pointed here to now
                // point to this entry's own Previous (collapsing the chain link).
                for (int j = 0; j < _revertChain.Count; j++)
                {
                    if (j != i && _revertChain[j].PreviousProfileId == entry.TriggeredProfileId)
                        _revertChain[j] = _revertChain[j] with { PreviousProfileId = entry.PreviousProfileId };
                }
                _revertChain.RemoveAt(i);
            }
        }
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
            _revertChain.Add(new RevertInfo(profile.Id, _configService.Current.ActiveProfileId));
        }

        DispatchSwitch(profile);
    }

    private void RevertToPrevious(Guid? previousProfileId)
    {
        if (!previousProfileId.HasValue) return;
        var prev = _configService.Current.Profiles.FirstOrDefault(p => p.Id == previousProfileId);
        if (prev == null) return;

        // Safety fallback: if the target profile's device is already gone, cascade
        // to the next chain entry rather than landing on a profile with no active device.
        if (prev.TriggerOnConnect && !IsHidManaged(prev) &&
            prev.PlaybackDeviceId != null && !_connectedIds.Contains(prev.PlaybackDeviceId))
        {
            RevertInfo? next;
            lock (_stateLock)
            {
                next = _revertChain.Count > 0 ? _revertChain[^1] : null;
                if (next.HasValue) _revertChain.RemoveAt(_revertChain.Count - 1);
            }
            RevertToPrevious(next?.PreviousProfileId);
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
        if (_disposed) return;

        var profile = _configService.Current.Profiles
            .Where(p => p.TriggerOnConnect && p.Id != _configService.Current.ActiveProfileId)
            .FirstOrDefault(p => IsProfileForDescriptor(p, descriptor));

        if (profile == null) return;

        lock (_stateLock)
            _revertChain.Add(new RevertInfo(profile.Id, _configService.Current.ActiveProfileId, IsHidTriggered: true));

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
        lock (_stateLock) ri = _revertChain.Count > 0 ? _revertChain[^1] : null;

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
        lock (_stateLock) _revertChain.RemoveAt(_revertChain.Count - 1);
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
