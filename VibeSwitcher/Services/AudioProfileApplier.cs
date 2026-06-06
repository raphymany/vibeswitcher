using System.Runtime.InteropServices;
using VibeSwitcher.Helpers;
using VibeSwitcher.Models;
using VibeSwitcher.NativeMethods;

namespace VibeSwitcher.Services;

internal static class AudioProfileApplier
{
    internal static ProfileSwitchResult Apply(DeviceProfile profile)
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
                    AppLogger.Error("AudioProfileApplier.Apply", msg);
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
                    AppLogger.Error("AudioProfileApplier.Apply", msg);
                    SessionErrorTracker.Record(ErrorCode.PolicySetDefaultFailed,
                        "Set Default Audio Endpoint Failed", msg);
                }
            },
            _ => null
        };

        if (setDefault == null)
        {
            var msg = "PolicyConfig COM interface is not supported on this Windows version. Audio switching is unavailable.";
            AppLogger.Error("AudioProfileApplier.Apply", msg);
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
            AppLogger.Error("AudioProfileApplier.Apply", msg);
            SessionErrorTracker.Record(ErrorCode.AudioServiceUnavailable, "Audio Service Unavailable", msg);
            throw new InvalidOperationException(msg, ex);
        }
        finally
        {
            Marshal.ReleaseComObject(rawPolicy);
            Marshal.ReleaseComObject(enumerator);
        }
    }

    internal static bool IsDeviceActive(IMMDeviceEnumerator enumerator, string deviceId)
    {
        try
        {
            int hr = enumerator.GetDevice(deviceId, out var device);
            if (hr != 0) return false;
            try
            {
                int stateHr = device.GetState(out var state);
                return stateHr == 0 && state == AudioDeviceState.Active;
            }
            finally
            {
                Marshal.ReleaseComObject(device);
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidComObjectException)
        {
            return false;
        }
    }
}
