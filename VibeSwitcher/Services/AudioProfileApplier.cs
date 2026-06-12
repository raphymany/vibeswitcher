using System.Runtime.InteropServices;
using VibeSwitcher.Helpers;
using VibeSwitcher.Models;
using VibeSwitcher.NativeMethods;

namespace VibeSwitcher.Services;

internal static class AudioProfileApplier
{
    internal static ProfileSwitchResult Apply(
        DeviceProfile profile, IAppLogger logger, ISessionErrorTracker errorTracker)
    {
        // Created inside the try so the unsupported-OS throw below cannot leak these COM objects.
        IMMDeviceEnumerator? enumerator = null;
        object? rawPolicy = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            rawPolicy = new PolicyConfigClient();

            // Try modern IID (Win7/8/10/11) first, fall back to Vista IID.
            // Returns true only when all three roles were set successfully.
            Func<EDataFlow, string, bool>? setDefault = rawPolicy switch
            {
                IPolicyConfig modern => (flow, id) => TrySetAllRoles(
                    enumerator, (eid, role) => modern.SetDefaultEndpoint(eid, role), flow, id, logger, errorTracker),
                IPolicyConfigVista vista => (flow, id) => TrySetAllRoles(
                    enumerator, (eid, role) => vista.SetDefaultEndpoint(eid, role), flow, id, logger, errorTracker),
                _ => null
            };

            if (setDefault == null)
            {
                var msg = "PolicyConfig COM interface is not supported on this Windows version. Audio switching is unavailable.";
                logger.Error("AudioProfileApplier.Apply", msg);
                errorTracker.Record(ErrorCode.PolicyConfigUnsupported, "PolicyConfig Not Supported", msg);
                throw new NotSupportedException(msg);
            }

            bool playbackApplied = false;
            bool recordingApplied = false;
            string? missingPlayback = null;
            string? missingRecording = null;
            bool playbackFailed = false;
            bool recordingFailed = false;

            if (!string.IsNullOrEmpty(profile.PlaybackDeviceId))
            {
                if (IsDeviceActive(enumerator, profile.PlaybackDeviceId))
                {
                    playbackApplied = setDefault(EDataFlow.Render, profile.PlaybackDeviceId);
                    playbackFailed = !playbackApplied; // active device, but SetDefaultEndpoint failed
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
                    recordingApplied = setDefault(EDataFlow.Capture, profile.RecordingDeviceId);
                    recordingFailed = !recordingApplied;
                }
                else
                {
                    missingRecording = profile.RecordingDeviceId;
                }
            }

            return new ProfileSwitchResult(
                playbackApplied, recordingApplied, missingPlayback, missingRecording,
                playbackFailed, recordingFailed);
        }
        catch (COMException ex) when (ex.HResult == unchecked((int)0x80070424))
        {
            var msg = "Windows Audio service is not running. Start the Audio service and try again.";
            logger.Error("AudioProfileApplier.Apply", msg);
            errorTracker.Record(ErrorCode.AudioServiceUnavailable, "Audio Service Unavailable", msg);
            throw new InvalidOperationException(msg, ex);
        }
        finally
        {
            if (rawPolicy != null) Marshal.ReleaseComObject(rawPolicy);
            if (enumerator != null) Marshal.ReleaseComObject(enumerator);
        }
    }

    private static bool TrySetAllRoles(
        IMMDeviceEnumerator enumerator, Func<string, ERole, int> set, EDataFlow flow, string id,
        IAppLogger logger, ISessionErrorTracker errorTracker)
    {
        foreach (var role in new[] { ERole.Console, ERole.Multimedia, ERole.Communications })
        {
            // Skip roles where this device is already the default. Re-setting an already-default
            // endpoint makes Windows tear down and re-initialize it, causing a brief audio dropout —
            // e.g. re-selecting the profile you're already on. (It's already correct, so this counts
            // as applied.)
            if (IsDefaultEndpoint(enumerator, flow, role, id)) continue;

            int hr = set(id, role);
            if (hr != 0)
            {
                var msg = $"SetDefaultEndpoint returned HRESULT 0x{hr:X8} for device '{id}'.";
                logger.Error("AudioProfileApplier.Apply", msg);
                errorTracker.Record(ErrorCode.PolicySetDefaultFailed,
                    "Set Default Audio Endpoint Failed", msg);
                return false;
            }
        }
        return true;
    }

    // True if 'deviceId' is already the default endpoint for (flow, role) — used to avoid the audio
    // dropout from redundantly re-applying a default that's already set.
    private static bool IsDefaultEndpoint(IMMDeviceEnumerator enumerator, EDataFlow flow, ERole role, string deviceId)
    {
        IMMDevice? dev = null;
        try
        {
            if (enumerator.GetDefaultAudioEndpoint(flow, role, out dev) != 0 || dev == null) return false;
            return dev.GetId(out var id) == 0 && id != null &&
                   string.Equals(id, deviceId, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is COMException or InvalidComObjectException)
        {
            return false;
        }
        finally
        {
            if (dev != null) Marshal.ReleaseComObject(dev);
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
