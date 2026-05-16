using System.Runtime.InteropServices;
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

public class AudioService
{
    public IReadOnlyList<AudioDeviceInfo> GetPlaybackDevices() =>
        RunOnSta(() => EnumerateDevices(EDataFlow.Render));

    public IReadOnlyList<AudioDeviceInfo> GetRecordingDevices() =>
        RunOnSta(() => EnumerateDevices(EDataFlow.Capture));

    public Task<ProfileSwitchResult> ApplyProfileAsync(DeviceProfile profile) =>
        Task.Run(() => RunOnSta(() => ApplyProfile(profile)));

    // COM audio objects are apartment-neutral but work most reliably on STA threads.
    private static T RunOnSta<T>(Func<T> work)
    {
        T result = default!;
        Exception? error = null;

        var thread = new Thread(() =>
        {
            try { result = work(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (error != null) throw new Exception(error.Message, error);
        return result;
    }

    private static IReadOnlyList<AudioDeviceInfo> EnumerateDevices(EDataFlow flow)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        try
        {
            enumerator.EnumAudioEndpoints(flow, AudioDeviceState.Active, out var collection);
            collection.GetCount(out uint count);

            var results = new List<AudioDeviceInfo>((int)count);
            for (uint i = 0; i < count; i++)
            {
                collection.Item(i, out var device);
                var info = GetDeviceInfo(device, flow == EDataFlow.Render);
                if (info != null) results.Add(info);
            }
            Marshal.ReleaseComObject(collection);
            return results.OrderBy(d => d.FriendlyName).ToList();
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
        }
    }

    private static AudioDeviceInfo? GetDeviceInfo(IMMDevice device, bool isPlayback)
    {
        try
        {
            device.GetId(out string id);
            device.OpenPropertyStore(0 /* STGM_READ */, out var store);

            var key = PROPERTYKEY.DeviceFriendlyName;
            store.GetValue(ref key, out var pv);
            string? name = pv.ToStringValue();
            PropVariant.PropVariantClear(ref pv);

            Marshal.ReleaseComObject(store);

            if (name == null) return null;
            return new AudioDeviceInfo(id, name, isPlayback);
        }
        catch
        {
            return null;
        }
        finally
        {
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
                modern.SetDefaultEndpoint(id, ERole.Console);
                modern.SetDefaultEndpoint(id, ERole.Multimedia);
                modern.SetDefaultEndpoint(id, ERole.Communications);
            },
            IPolicyConfigVista vista => id =>
            {
                vista.SetDefaultEndpoint(id, ERole.Console);
                vista.SetDefaultEndpoint(id, ERole.Multimedia);
                vista.SetDefaultEndpoint(id, ERole.Communications);
            },
            _ => null
        };

        if (setDefault == null)
            throw new NotSupportedException(
                "PolicyConfigClient does not support IPolicyConfig or IPolicyConfigVista on this Windows version.");

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
