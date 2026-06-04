using System.Runtime.InteropServices;

namespace VibeSwitcher.NativeMethods;

public enum EDataFlow { Render = 0, Capture = 1, All = 2 }

public enum ERole { Console = 0, Multimedia = 1, Communications = 2 }

[Flags]
public enum AudioDeviceState : uint
{
    Active = 0x1, Disabled = 0x2, NotPresent = 0x4, Unplugged = 0x8, All = 0xF
}

[StructLayout(LayoutKind.Sequential)]
public struct PROPERTYKEY
{
    public Guid fmtid;
    public uint pid;

    // PKEY_Device_FriendlyName
    public static readonly PROPERTYKEY DeviceFriendlyName = new()
    {
        fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),
        pid = 14
    };

    // PKEY_AudioEndpoint_Path — symbolic link of the audio device interface.
    // For USB/HID hardware, includes the device path with VID/PID embedded.
    public static readonly PROPERTYKEY AudioEndpointPath = new()
    {
        fmtid = new Guid("1DA5D803-D492-4EDD-8C23-E0C0FFEE7F0E"),
        pid = 1
    };
}

// Minimal PROPVARIANT — only handles the VT_LPWSTR case we need for device names.
// x64 PROPVARIANT is 24 bytes; Size=16 would let COM write past the struct boundary in PropVariantClear.
[StructLayout(LayoutKind.Explicit, Size = 24)]
public struct PropVariant
{
    [FieldOffset(0)]  public short vt;
    [FieldOffset(8)]  public IntPtr ptr;
    [FieldOffset(16)] private long _padding;

    private const short VT_LPWSTR = 31;

    public string? ToStringValue() =>
        vt == VT_LPWSTR ? Marshal.PtrToStringUni(ptr) : null;

    [DllImport("ole32.dll")]
    public static extern int PropVariantClear(ref PropVariant pvar);
}

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IMMDeviceEnumerator
{
    [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, AudioDeviceState stateMask, out IMMDeviceCollection ppDevices);
    [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);
    [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);
    [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr pClient);
    [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr pClient);
}

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
public class MMDeviceEnumerator { }

[ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IMMDeviceCollection
{
    [PreserveSig] int GetCount(out uint pcDevices);
    [PreserveSig] int Item(uint nDevice, out IMMDevice ppDevice);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IMMDevice
{
    [PreserveSig] int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams,
        [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
    [PreserveSig] int OpenPropertyStore(int stgmAccess, out IPropertyStore ppProperties);
    [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
    [PreserveSig] int GetState(out AudioDeviceState pdwState);
}

[ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IPropertyStore
{
    [PreserveSig] int GetCount(out uint cProps);
    [PreserveSig] int GetAt(uint iProp, out PROPERTYKEY pkey);
    [PreserveSig] int GetValue(ref PROPERTYKEY key, out PropVariant pv);
    [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PropVariant propvar);
    [PreserveSig] int Commit();
}

// COM callback interface — implemented in managed code; Windows Audio calls these methods on an MTA thread.
// No [ComImport] here — this is a CCW (COM Callable Wrapper) target, not an import.
[Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IMMNotificationClient
{
    void OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, AudioDeviceState newState);
    void OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
    void OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
    void OnDefaultDeviceChanged(EDataFlow flow, ERole role, [MarshalAs(UnmanagedType.LPWStr)] string? defaultDeviceId);
    void OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, PROPERTYKEY key);
}

// CPolicyConfigVistaClient — undocumented COM class in AudioSes.dll for setting default endpoints.
// CLSID is stable across Vista through Windows 11.
[ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
public class PolicyConfigClient { }

// Windows 7 / 8 / 10 / 11 version of IPolicyConfig.
// Try this IID first; fall back to IPolicyConfigVista if QueryInterface fails.
[ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IPolicyConfig
{
    [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string dev, IntPtr ppFormat);
    [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string dev, bool bDefault, IntPtr ppFormat);
    [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string dev);
    [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string dev, IntPtr pFmt, IntPtr pMix);
    [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string dev, bool bDefault, IntPtr pDefault, IntPtr pMin);
    [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string dev, IntPtr pPeriod);
    [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string dev, IntPtr pMode);
    [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string dev, IntPtr mode);
    [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string dev, bool bFx, ref PROPERTYKEY key, IntPtr pv);
    [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string dev, bool bFx, ref PROPERTYKEY key, IntPtr pv);
    [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string dev, ERole role);
    [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string dev, bool bVisible);
}

// Vista / Windows 7 legacy version — kept as fallback.
[ComImport, Guid("568B9108-44BF-405D-8EDD-AA3D1A9E5241"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IPolicyConfigVista
{
    [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string dev, IntPtr ppFormat);
    [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string dev, bool bDefault, IntPtr ppFormat);
    [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string dev);
    [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string dev, IntPtr pFmt, IntPtr pMix);
    [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string dev, bool bDefault, IntPtr pDefault, IntPtr pMin);
    [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string dev, IntPtr pPeriod);
    [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string dev, IntPtr pMode);
    [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string dev, IntPtr mode);
    [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string dev, bool bFx, ref PROPERTYKEY key, IntPtr pv);
    [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string dev, bool bFx, ref PROPERTYKEY key, IntPtr pv);
    [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string dev, ERole role);
    [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string dev, bool bVisible);
}

// Minimal WAVEFORMATEX — only the fields needed to determine sample format and rate.
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct WAVEFORMATEX
{
    public ushort wFormatTag;
    public ushort nChannels;
    public uint   nSamplesPerSec;
    public uint   nAvgBytesPerSec;
    public ushort nBlockAlign;
    public ushort wBitsPerSample;
    public ushort cbSize;
}

// IAudioEndpointVolume — used to set per-device master volume level.
[ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAudioEndpointVolume
{
    [PreserveSig] int RegisterControlChangeNotify(IntPtr pNotify);
    [PreserveSig] int UnregisterControlChangeNotify(IntPtr pNotify);
    [PreserveSig] int GetChannelCount(out uint pnChannelCount);
    [PreserveSig] int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
    [PreserveSig] int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
    [PreserveSig] int GetMasterVolumeLevel(out float pfLevelDB);
    [PreserveSig] int GetMasterVolumeLevelScalar(out float pfLevel);
    [PreserveSig] int SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);
    [PreserveSig] int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, ref Guid pguidEventContext);
    [PreserveSig] int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
    [PreserveSig] int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
    [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid pguidEventContext);
    [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
    [PreserveSig] int GetVolumeStepInfo(out uint pnStep, out uint pnStepCount);
    [PreserveSig] int VolumeStepUp(ref Guid pguidEventContext);
    [PreserveSig] int VolumeStepDown(ref Guid pguidEventContext);
    [PreserveSig] int QueryHardwareSupport(out uint pdwHardwareSupportMask);
    [PreserveSig] int GetVolumeRange(out float pflVolumeMindB, out float pflVolumeMaxdB, out float pflVolumeIncrementdB);
}

// IAudioClient — entry point for WASAPI render and capture streams (shared mode).
[ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAudioClient
{
    [PreserveSig] int Initialize(int ShareMode, uint StreamFlags, long hnsBufferDuration,
                                  long hnsPeriodicity, IntPtr pFormat, ref Guid AudioSessionGuid);
    [PreserveSig] int GetBufferSize(out uint pNumBufferFrames);
    [PreserveSig] int GetStreamLatency(out long phnsLatency);
    [PreserveSig] int GetCurrentPadding(out uint pNumPaddingFrames);
    [PreserveSig] int IsFormatSupported(int ShareMode, IntPtr pFormat, out IntPtr ppClosestMatch);
    [PreserveSig] int GetMixFormat(out IntPtr ppDeviceFormat);
    [PreserveSig] int GetDevicePeriod(out long phnsDefaultDevicePeriod, out long phnsMinimumDevicePeriod);
    [PreserveSig] int Start();
    [PreserveSig] int Stop();
    [PreserveSig] int Reset();
    [PreserveSig] int SetEventHandle(IntPtr eventHandle);
    [PreserveSig] int GetService(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
}

// IAudioRenderClient — fills the render buffer with PCM or float audio frames.
[ComImport, Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAudioRenderClient
{
    [PreserveSig] int GetBuffer(uint NumFramesRequested, out IntPtr ppData);
    [PreserveSig] int ReleaseBuffer(uint NumFramesWritten, uint dwFlags);
}

// IAudioCaptureClient — reads frames from the capture endpoint buffer.
[ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAudioCaptureClient
{
    [PreserveSig] int GetBuffer(out IntPtr ppData, out uint pNumFramesToRead,
                                 out uint pdwFlags, out ulong pu64DevicePosition, out ulong pu64QPCPosition);
    [PreserveSig] int ReleaseBuffer(uint NumFramesRead);
    [PreserveSig] int GetNextPacketSize(out uint pNumFramesInNextPacket);
}

internal static class Ole32
{
    [DllImport("ole32.dll")]
    internal static extern void CoTaskMemFree(IntPtr pv);
}
