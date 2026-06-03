using System.Runtime.InteropServices;
using VibeSwitcher.Helpers;
using VibeSwitcher.Models;
using VibeSwitcher.NativeMethods;

namespace VibeSwitcher.Services;

public class MuteService
{
    // Tracks which scopes are currently muted by us (not pre-existing OS mutes).
    private readonly HashSet<MuteScope> _activeMutes = new();

    public bool IsAnyMuteActive => _activeMutes.Count > 0;

    public event Action? MuteStateChanged;

    public void Toggle(MuteScope scope)
    {
        if (_activeMutes.Contains(scope))
            Unmute(scope);
        else
            Mute(scope);
        MuteStateChanged?.Invoke();
    }

    private void Mute(MuteScope scope)
    {
        if (scope == MuteScope.Mic || scope == MuteScope.Both)
            SetDeviceMute(EDataFlow.Capture, true);
        if (scope == MuteScope.Speakers || scope == MuteScope.Both)
            SetDeviceMute(EDataFlow.Render, true);
        _activeMutes.Add(scope);
    }

    private void Unmute(MuteScope scope)
    {
        if (scope == MuteScope.Mic || scope == MuteScope.Both)
            SetDeviceMute(EDataFlow.Capture, false);
        if (scope == MuteScope.Speakers || scope == MuteScope.Both)
            SetDeviceMute(EDataFlow.Render, false);
        _activeMutes.Remove(scope);
    }

    private static void SetDeviceMute(EDataFlow flow, bool mute)
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            enumerator.GetDefaultAudioEndpoint(flow, ERole.Console, out device);
            if (device == null) return;

            var volumeGuid = typeof(IAudioEndpointVolume).GUID;
            device.Activate(ref volumeGuid, 23, IntPtr.Zero, out var obj);
            if (obj is not IAudioEndpointVolume vol) return;

            var ctx = Guid.Empty;
            vol.SetMute(mute, ref ctx);
            Marshal.ReleaseComObject(vol);
        }
        catch (Exception ex)
        {
            AppLogger.Warning("MuteService.SetDeviceMute", ex.Message);
        }
        finally
        {
            if (device != null) Marshal.ReleaseComObject(device);
            if (enumerator != null) Marshal.ReleaseComObject(enumerator);
        }
    }
}
