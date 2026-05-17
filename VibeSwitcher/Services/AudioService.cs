using System.Runtime.InteropServices;
using VibeSwitcher.Helpers;
using VibeSwitcher.Models;
using VibeSwitcher.NativeMethods;

namespace VibeSwitcher.Services;

public record AudioDeviceInfo(string Id, string FriendlyName, bool IsPlayback)
{
    public override string ToString() => FriendlyName;
}

public record ProfileSwitchResult(
    bool PlaybackApplied,
    bool RecordingApplied,
    string? MissingPlaybackId,
    string? MissingRecordingId);

public class AudioService : IAudioService
{
    // Persistent enumerator kept alive solely to hold the notification registration.
    // All per-call audio operations create their own enumerator to avoid sharing state.
    private readonly IMMDeviceEnumerator _notifEnumerator;
    private readonly DeviceNotificationClient _notifClient;
    private IntPtr _notifClientPtr;
    private volatile bool _disposed;

    public event Action? DevicesChanged;

    public AudioService()
    {
        _notifEnumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        _notifClient = new DeviceNotificationClient();
        _notifClient.DevicesChanged += () => DevicesChanged?.Invoke();

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
        _disposed ? [] : EnumerateDevices(EDataFlow.Render);

    public IReadOnlyList<AudioDeviceInfo> GetRecordingDevices() =>
        _disposed ? [] : EnumerateDevices(EDataFlow.Capture);

    public Task<ProfileSwitchResult> ApplyProfileAsync(DeviceProfile profile) =>
        Task.Run(() => ApplyProfile(profile));

    private static IReadOnlyList<AudioDeviceInfo> EnumerateDevices(EDataFlow flow)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        IMMDeviceCollection? collection = null;
        try
        {
            enumerator.EnumAudioEndpoints(flow, AudioDeviceState.Active, out collection);
            collection.GetCount(out uint count);

            var results = new List<AudioDeviceInfo>((int)count);
            for (uint i = 0; i < count; i++)
            {
                collection.Item(i, out var device);
                var info = GetDeviceInfo(device, flow == EDataFlow.Render);
                if (info != null) results.Add(info);
            }
            return results.OrderBy(d => d.FriendlyName).ToList();
        }
        finally
        {
            if (collection != null) Marshal.ReleaseComObject(collection);
            Marshal.ReleaseComObject(enumerator);
        }
    }

    private static AudioDeviceInfo? GetDeviceInfo(IMMDevice device, bool isPlayback)
    {
        IPropertyStore? store = null;
        try
        {
            device.GetId(out string id);
            device.OpenPropertyStore(0 /* STGM_READ */, out store);

            var key = PROPERTYKEY.DeviceFriendlyName;
            store.GetValue(ref key, out var pv);
            string? name = pv.ToStringValue();
            PropVariant.PropVariantClear(ref pv);

            if (name == null) return null;
            return new AudioDeviceInfo(id, name, isPlayback);
        }
        catch (Exception ex)
        {
            AppLogger.Warning("AudioService.GetDeviceInfo", ex.Message);
            SessionErrorTracker.Record(ErrorCode.AudioDeviceInfoFailed, "Audio Device Info Unavailable",
                $"Could not read info for an audio device: {ex.Message}");
            return null;
        }
        finally
        {
            if (store != null) Marshal.ReleaseComObject(store);
            Marshal.ReleaseComObject(device);
        }
    }

    private static ProfileSwitchResult ApplyProfile(DeviceProfile profile)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        var rawPolicy = new PolicyConfigClient();

        // Try modern IID (Win7/8/10/11) first, fall back to Vista IID
        Action<string>? setDefault = rawPolicy switch
        {
            IPolicyConfig modern => id =>
            {
                int hr;
                if ((hr = modern.SetDefaultEndpoint(id, ERole.Console)) != 0 ||
                    (hr = modern.SetDefaultEndpoint(id, ERole.Multimedia)) != 0 ||
                    (hr = modern.SetDefaultEndpoint(id, ERole.Communications)) != 0)
                {
                    var msg = $"SetDefaultEndpoint returned HRESULT 0x{hr:X8} for device '{id}'.";
                    AppLogger.Error("AudioService.ApplyProfile", msg);
                    SessionErrorTracker.Record(ErrorCode.PolicySetDefaultFailed,
                        "Set Default Audio Endpoint Failed", msg);
                }
            },
            IPolicyConfigVista vista => id =>
            {
                int hr;
                if ((hr = vista.SetDefaultEndpoint(id, ERole.Console)) != 0 ||
                    (hr = vista.SetDefaultEndpoint(id, ERole.Multimedia)) != 0 ||
                    (hr = vista.SetDefaultEndpoint(id, ERole.Communications)) != 0)
                {
                    var msg = $"SetDefaultEndpoint returned HRESULT 0x{hr:X8} for device '{id}'.";
                    AppLogger.Error("AudioService.ApplyProfile", msg);
                    SessionErrorTracker.Record(ErrorCode.PolicySetDefaultFailed,
                        "Set Default Audio Endpoint Failed", msg);
                }
            },
            _ => null
        };

        if (setDefault == null)
        {
            var msg = "PolicyConfig COM interface is not supported on this Windows version. Audio switching is unavailable.";
            AppLogger.Error("AudioService.ApplyProfile", msg);
            SessionErrorTracker.Record(ErrorCode.PolicyConfigUnsupported, "PolicyConfig Not Supported", msg);
            throw new NotSupportedException(msg);
        }

        try
        {
            bool playbackApplied = false;
            bool recordingApplied = false;
            string? missingPlayback = null;
            string? missingRecording = null;

            if (!string.IsNullOrEmpty(profile.PlaybackDeviceId))
            {
                if (IsDeviceActive(enumerator, profile.PlaybackDeviceId))
                {
                    setDefault(profile.PlaybackDeviceId);
                    playbackApplied = true;
                }
                else
                {
                    missingPlayback = profile.PlaybackDeviceId;
                }
            }

            if (!string.IsNullOrEmpty(profile.RecordingDeviceId))
            {
                if (IsDeviceActive(enumerator, profile.RecordingDeviceId))
                {
                    setDefault(profile.RecordingDeviceId);
                    recordingApplied = true;
                }
                else
                {
                    missingRecording = profile.RecordingDeviceId;
                }
            }

            return new ProfileSwitchResult(playbackApplied, recordingApplied, missingPlayback, missingRecording);
        }
        catch (COMException ex) when (ex.HResult == unchecked((int)0x80070424))
        {
            var msg = "Windows Audio service is not running. Start the Audio service and try again.";
            AppLogger.Error("AudioService.ApplyProfile", msg);
            SessionErrorTracker.Record(ErrorCode.AudioServiceUnavailable, "Audio Service Unavailable", msg);
            throw new InvalidOperationException(msg, ex);
        }
        finally
        {
            Marshal.ReleaseComObject(rawPolicy);
            Marshal.ReleaseComObject(enumerator);
        }
    }

    private static bool IsDeviceActive(IMMDeviceEnumerator enumerator, string deviceId)
    {
        try
        {
            int hr = enumerator.GetDevice(deviceId, out var device);
            if (hr != 0) return false;

            device.GetState(out var state);
            Marshal.ReleaseComObject(device);
            return state == AudioDeviceState.Active;
        }
        catch
        {
            return false;
        }
    }
}
