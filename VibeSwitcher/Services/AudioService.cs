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
            // Include Disabled and Unplugged so those devices appear with a red dot rather than disappearing.
            enumerator.EnumAudioEndpoints(flow, AudioDeviceState.Active | AudioDeviceState.Disabled | AudioDeviceState.Unplugged, out collection);
            collection.GetCount(out uint count);

            var results = new List<AudioDeviceInfo>((int)count);
            for (uint i = 0; i < count; i++)
            {
                collection.Item(i, out var device);
                var info = GetDeviceInfo(device, flow == EDataFlow.Render);
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

    private static AudioDeviceInfo? GetDeviceInfo(IMMDevice device, bool isPlayback)
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

    // ── Test sound (playback) ─────────────────────────────────────────────────

    public Task TestSoundAsync(string deviceId) => Task.Run(() => PlayTestTone(deviceId));

    private static void PlayTestTone(string deviceId)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        try
        {
            if (enumerator.GetDevice(deviceId, out var device) != 0) return;
            try
            {
                var audioClientId = typeof(IAudioClient).GUID;
                if (device.Activate(ref audioClientId, 23, IntPtr.Zero, out var clientObj) != 0) return;
                var client = (IAudioClient)clientObj;
                try
                {
                    if (client.GetMixFormat(out var fmtPtr) != 0) return;
                    var fmt = Marshal.PtrToStructure<WAVEFORMATEX>(fmtPtr);
                    bool isFloat = IsFloatFormat(fmt, fmtPtr);
                    // Support float32 and PCM-16; skip any other format silently.
                    if (!isFloat && fmt.wBitsPerSample != 16)
                    {
                        Ole32.CoTaskMemFree(fmtPtr);
                        return;
                    }

                    int channels   = fmt.nChannels;
                    int sampleRate = (int)fmt.nSamplesPerSec;
                    int bytesPerSample = isFloat ? 4 : 2;

                    client.GetDevicePeriod(out long defaultPeriod, out _);

                    var sessionGuid = Guid.Empty;
                    int hr = client.Initialize(0 /* SHARED */, 0, defaultPeriod * 4, 0, fmtPtr, ref sessionGuid);
                    Ole32.CoTaskMemFree(fmtPtr);
                    if (hr != 0) return;

                    client.GetBufferSize(out uint bufferFrames);

                    var rcId = typeof(IAudioRenderClient).GUID;
                    if (client.GetService(ref rcId, out var rcObj) != 0) return;
                    var renderClient = (IAudioRenderClient)rcObj;
                    try
                    {
                        const float frequency  = 440f;
                        const float amplitude  = 0.25f;
                        const float durationSec = 0.35f;
                        int totalFrames = (int)(sampleRate * durationSec);

                        client.Start();
                        int written = 0;
                        while (written < totalFrames)
                        {
                            client.GetCurrentPadding(out uint padding);
                            uint available = bufferFrames - padding;
                            if (available == 0) { Thread.Sleep(1); continue; }

                            int toWrite = (int)Math.Min(available, (uint)(totalFrames - written));
                            if (renderClient.GetBuffer((uint)toWrite, out var dataPtr) != 0) break;

                            for (int i = 0; i < toWrite; i++)
                            {
                                float sample = amplitude * (float)Math.Sin(2.0 * Math.PI * frequency * (written + i) / sampleRate);
                                for (int ch = 0; ch < channels; ch++)
                                {
                                    int byteOffset = (i * channels + ch) * bytesPerSample;
                                    if (isFloat)
                                        Marshal.WriteInt32(dataPtr, byteOffset, BitConverter.SingleToInt32Bits(sample));
                                    else
                                        Marshal.WriteInt16(dataPtr, byteOffset, (short)(sample * 32767));
                                }
                            }

                            renderClient.ReleaseBuffer((uint)toWrite, 0);
                            written += toWrite;
                        }

                        // Sleep long enough for the hardware buffer to drain before Stop().
                        int drainMs = (int)(durationSec * 1000) + (int)(defaultPeriod / 10_000) + 50;
                        Thread.Sleep(drainMs);
                        client.Stop();
                    }
                    finally { Marshal.ReleaseComObject(renderClient); }
                }
                finally { Marshal.ReleaseComObject(client); }
            }
            finally { Marshal.ReleaseComObject(device); }
        }
        catch (Exception ex)
        {
            AppLogger.Warning("AudioService.PlayTestTone", ex.Message);
        }
        finally { Marshal.ReleaseComObject(enumerator); }
    }

    private static bool IsFloatFormat(WAVEFORMATEX fmt, IntPtr fmtPtr)
    {
        const ushort WAVE_FORMAT_IEEE_FLOAT  = 3;
        const ushort WAVE_FORMAT_EXTENSIBLE  = 0xFFFE;
        if (fmt.wFormatTag == WAVE_FORMAT_IEEE_FLOAT) return true;
        if (fmt.wFormatTag == WAVE_FORMAT_EXTENSIBLE)
        {
            // SubFormat GUID is at byte offset 24 from the start of WAVEFORMATEX.
            var subFormat = Marshal.PtrToStructure<Guid>(IntPtr.Add(fmtPtr, 24));
            return subFormat == new Guid("00000003-0000-0010-8000-00AA00389B71");
        }
        return false;
    }

    // ── Mic level monitor (used by MicTestDialog) ─────────────────────────────

    internal static void RunMicLevelMonitor(string deviceId, CancellationToken ct, Action<float> onLevel)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        try
        {
            if (enumerator.GetDevice(deviceId, out var device) != 0) return;
            try
            {
                var audioClientId = typeof(IAudioClient).GUID;
                if (device.Activate(ref audioClientId, 23, IntPtr.Zero, out var clientObj) != 0) return;
                var client = (IAudioClient)clientObj;
                try
                {
                    if (client.GetMixFormat(out var fmtPtr) != 0) return;
                    var fmt = Marshal.PtrToStructure<WAVEFORMATEX>(fmtPtr);
                    bool isFloat = IsFloatFormat(fmt, fmtPtr);
                    int channels = fmt.nChannels;

                    client.GetDevicePeriod(out long defaultPeriod, out _);
                    var sessionGuid = Guid.Empty;
                    int hr = client.Initialize(0 /* SHARED */, 0, defaultPeriod * 4, 0, fmtPtr, ref sessionGuid);
                    Ole32.CoTaskMemFree(fmtPtr);
                    if (hr != 0) return;

                    var capId = typeof(IAudioCaptureClient).GUID;
                    if (client.GetService(ref capId, out var capObj) != 0) return;
                    var captureClient = (IAudioCaptureClient)capObj;
                    try
                    {
                        client.Start();
                        while (!ct.IsCancellationRequested)
                        {
                            if (captureClient.GetNextPacketSize(out uint packetSize) != 0) break;
                            if (packetSize == 0) { Thread.Sleep(10); continue; }

                            if (captureClient.GetBuffer(out var dataPtr, out uint numFrames,
                                out uint flags, out _, out _) != 0)
                            {
                                captureClient.ReleaseBuffer(0);
                                continue;
                            }

                            float level = 0;
                            const uint AUDCLNT_BUFFERFLAGS_SILENT = 0x2;
                            if ((flags & AUDCLNT_BUFFERFLAGS_SILENT) == 0 && numFrames > 0)
                            {
                                int totalSamples = (int)numFrames * channels;
                                float sumSq = 0;
                                if (isFloat)
                                {
                                    var samples = new float[totalSamples];
                                    Marshal.Copy(dataPtr, samples, 0, totalSamples);
                                    foreach (var s in samples) sumSq += s * s;
                                }
                                else if (fmt.wBitsPerSample == 16)
                                {
                                    var samples = new short[totalSamples];
                                    Marshal.Copy(dataPtr, samples, 0, totalSamples);
                                    foreach (var s in samples) sumSq += (s / 32768f) * (s / 32768f);
                                }
                                level = (float)Math.Sqrt(sumSq / Math.Max(totalSamples, 1));
                            }

                            captureClient.ReleaseBuffer(numFrames);
                            onLevel(level);
                        }
                        client.Stop();
                    }
                    finally { Marshal.ReleaseComObject(captureClient); }
                }
                finally { Marshal.ReleaseComObject(client); }
            }
            finally { Marshal.ReleaseComObject(device); }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            AppLogger.Warning("AudioService.RunMicLevelMonitor", ex.Message);
        }
        finally { Marshal.ReleaseComObject(enumerator); }
    }
}
