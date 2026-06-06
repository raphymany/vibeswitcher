using System.Runtime.InteropServices;
using VibeSwitcher.Helpers;
using VibeSwitcher.Models;
using VibeSwitcher.NativeMethods;

namespace VibeSwitcher.Services;

internal static class AudioDeviceEnumerator
{
    internal static IReadOnlyList<AudioDeviceInfo> GetDevices(
        EDataFlow flow, IAppLogger logger, ISessionErrorTracker errorTracker)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        IMMDeviceCollection? collection = null;
        try
        {
            // Include Disabled and Unplugged so those devices appear with a red dot rather than disappearing.
            enumerator.EnumAudioEndpoints(flow, AudioDeviceState.Active | AudioDeviceState.Disabled | AudioDeviceState.Unplugged, out collection);
            collection.GetCount(out uint count);

            var results = new List<AudioDeviceInfo>((int)count);
            for (uint i = 0; i < count; i++)
            {
                collection.Item(i, out var device);
                var info = GetDeviceInfo(device, flow == EDataFlow.Render, logger, errorTracker);
                if (info != null) results.Add(info);
            }
            // Active devices first, unplugged at the bottom — both groups sorted by name.
            return results.OrderBy(d => d.IsConnected ? 0 : 1).ThenBy(d => d.FriendlyName).ToList();
        }
        finally
        {
            if (collection != null) Marshal.ReleaseComObject(collection);
            Marshal.ReleaseComObject(enumerator);
        }
    }

    private static AudioDeviceInfo? GetDeviceInfo(
        IMMDevice device, bool isPlayback, IAppLogger logger, ISessionErrorTracker errorTracker)
    {
        IPropertyStore? store = null;
        try
        {
            device.GetId(out string id);
            device.GetState(out AudioDeviceState state);
            device.OpenPropertyStore(0 /* STGM_READ */, out store);

            var key = PROPERTYKEY.DeviceFriendlyName;
            store.GetValue(ref key, out var pv);
            string? name = pv.ToStringValue();
            PropVariant.PropVariantClear(ref pv);

            if (name == null) return null;
            return new AudioDeviceInfo(id, name, isPlayback,
                IsConnected: state == AudioDeviceState.Active,
                IsDisabled: state == AudioDeviceState.Disabled);
        }
        catch (Exception ex)
        {
            logger.Warning("AudioDeviceEnumerator.GetDeviceInfo", ex.Message);
            errorTracker.Record(ErrorCode.AudioDeviceInfoFailed, "Audio Device Info Unavailable",
                $"Could not read info for an audio device: {ex.Message}");
            return null;
        }
        finally
        {
            if (store != null) Marshal.ReleaseComObject(store);
            Marshal.ReleaseComObject(device);
        }
    }
}
