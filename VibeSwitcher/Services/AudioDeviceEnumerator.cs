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

    // Reduces a hardware instance path to the part that identifies the device *model and interface*,
    // dropping the per-connection instance suffix that changes when Windows re-detects the device:
    //   "{1}.USB\VID_0BDA&PID_4E27&MI_00\8&404667A&0&0000" → "USB\VID_0BDA&PID_4E27&MI_00"
    internal static string? NormalizeHardwareKey(string? instancePath)
    {
        if (string.IsNullOrWhiteSpace(instancePath)) return null;
        var path = instancePath.Trim();

        // Strip the "{n}." enumerator prefix.
        if (path.StartsWith('{'))
        {
            int dot = path.IndexOf("}.", StringComparison.Ordinal);
            if (dot > 0) path = path[(dot + 2)..];
        }

        // "ENUMERATOR\HARDWARE-ID\INSTANCE" — drop the instance segment.
        var parts = path.Split('\\');
        if (parts.Length >= 3) path = string.Join('\\', parts[..^1]);

        return path.Length == 0 ? null : path.ToUpperInvariant();
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

            var pathKey = PROPERTYKEY.HardwareInstancePath;
            string? hardwareKey = null;
            if (store.GetValue(ref pathKey, out var pathPv) == 0)
            {
                hardwareKey = NormalizeHardwareKey(pathPv.ToStringValue());
                PropVariant.PropVariantClear(ref pathPv);
            }

            return new AudioDeviceInfo(id, name, isPlayback,
                IsConnected: state == AudioDeviceState.Active,
                IsDisabled: state == AudioDeviceState.Disabled,
                HardwareKey: hardwareKey);
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
