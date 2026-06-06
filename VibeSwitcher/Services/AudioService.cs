using System.Runtime.InteropServices;
using VibeSwitcher.Helpers;
using VibeSwitcher.Models;
using VibeSwitcher.NativeMethods;

namespace VibeSwitcher.Services;

public record AudioDeviceInfo(string Id, string FriendlyName, bool IsPlayback, bool IsConnected = true, bool IsDisabled = false)
{
    public bool ShowDot => !string.IsNullOrEmpty(Id);
    public override string ToString() => FriendlyName;
}

public record ProfileSwitchResult(
    bool PlaybackApplied,
    bool RecordingApplied,
    string? MissingPlaybackId,
    string? MissingRecordingId);

// Implements IAudioService. Heavy logic lives in the focused helpers:
//   AudioDeviceEnumerator  — device listing
//   AudioProfileApplier    — profile switching via PolicyConfig COM
//   AudioTestTonePlayer    — WASAPI sine-wave test tone
//   AudioMicMonitor        — WASAPI capture + RMS level monitoring
public class AudioService : IAudioService
{
    // Persistent enumerator kept alive solely to hold the notification registration.
    // All per-call audio operations create their own enumerator to avoid sharing state.
    private readonly IMMDeviceEnumerator _notifEnumerator;
    private readonly DeviceNotificationClient _notifClient;
    private IntPtr _notifClientPtr;
    private volatile bool _disposed;

    public event Action? DevicesChanged;
    public event Action<string>? DevicePropertyChanged;

    public AudioService()
    {
        _notifEnumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        _notifClient = new DeviceNotificationClient();
        _notifClient.DevicesChanged += () => DevicesChanged?.Invoke();
        _notifClient.DevicePropertyChanged += id => DevicePropertyChanged?.Invoke(id);

        try
        {
            _notifClientPtr = Marshal.GetComInterfaceForObject(_notifClient, typeof(IMMNotificationClient));
            int hr = _notifEnumerator.RegisterEndpointNotificationCallback(_notifClientPtr);
            if (hr != 0)
            {
                AppLogger.Warning("AudioService", $"RegisterEndpointNotificationCallback returned 0x{hr:X8} — device list will not refresh automatically on plug/unplug");
                SessionErrorTracker.Record(ErrorCode.DeviceNotificationFailed, "Device Notification Setup Failed",
                    $"Could not subscribe to device change events (HRESULT 0x{hr:X8}). The device list will not refresh automatically.");
                Marshal.Release(_notifClientPtr);
                _notifClientPtr = IntPtr.Zero;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warning("AudioService", $"Could not register device notification callback: {ex.Message}");
            SessionErrorTracker.Record(ErrorCode.DeviceNotificationFailed, "Device Notification Setup Failed",
                $"Could not subscribe to device change events: {ex.Message}");
            _notifClientPtr = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        if (_notifClientPtr != IntPtr.Zero)
        {
            _notifEnumerator.UnregisterEndpointNotificationCallback(_notifClientPtr);
            Marshal.Release(_notifClientPtr);
            _notifClientPtr = IntPtr.Zero;
        }
        Marshal.ReleaseComObject(_notifEnumerator);
    }

    public IReadOnlyList<AudioDeviceInfo> GetPlaybackDevices() =>
        _disposed ? [] : AudioDeviceEnumerator.GetDevices(EDataFlow.Render);

    public IReadOnlyList<AudioDeviceInfo> GetRecordingDevices() =>
        _disposed ? [] : AudioDeviceEnumerator.GetDevices(EDataFlow.Capture);

    public Task<ProfileSwitchResult> ApplyProfileAsync(DeviceProfile profile) =>
        Task.Run(() => AudioProfileApplier.Apply(profile));

    public Task TestSoundAsync(string deviceId) =>
        Task.Run(() => AudioTestTonePlayer.Play(deviceId));

    // Returns the symbolic device path for the given audio endpoint (e.g.
    // "\\?\USB#VID_046D&PID_0ABA&MI_00#...") which embeds VID/PID for USB devices.
    public string? GetAudioEndpointPath(string audioDeviceId)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        try
        {
            if (enumerator.GetDevice(audioDeviceId, out var device) != 0) return null;
            IPropertyStore? store = null;
            try
            {
                device.OpenPropertyStore(0, out store);
                var key = PROPERTYKEY.AudioEndpointPath;
                store.GetValue(ref key, out var pv);
                var result = pv.ToStringValue();
                PropVariant.PropVariantClear(ref pv);
                return result;
            }
            finally
            {
                if (store != null) Marshal.ReleaseComObject(store);
                Marshal.ReleaseComObject(device);
            }
        }
        catch { return null; }
        finally { Marshal.ReleaseComObject(enumerator); }
    }
}
