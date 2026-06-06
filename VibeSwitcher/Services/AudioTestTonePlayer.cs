using System.Runtime.InteropServices;
using VibeSwitcher.Helpers;
using VibeSwitcher.NativeMethods;

namespace VibeSwitcher.Services;

internal static class AudioTestTonePlayer
{
    internal static void Play(string deviceId)
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
                        const float frequency   = 261f;   // C4 (middle C) — warmer, less startling than 440 Hz
                        const float amplitude   = 0.22f;
                        const float durationSec = 0.5f;
                        const float attackFrac  = 0.10f;  // fade in over first 10%
                        const float releaseFrac = 0.25f;  // fade out over last 25%
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
                                float t   = (float)(written + i) / totalFrames;
                                float env = t < attackFrac ? t / attackFrac
                                          : t > (1f - releaseFrac) ? (1f - t) / releaseFrac
                                          : 1f;
                                float sample = amplitude * env * (float)Math.Sin(2.0 * Math.PI * frequency * (written + i) / sampleRate);
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
            AppLogger.Warning("AudioTestTonePlayer.Play", ex.Message);
        }
        finally { Marshal.ReleaseComObject(enumerator); }
    }

    internal static bool IsFloatFormat(WAVEFORMATEX fmt, IntPtr fmtPtr)
    {
        const ushort WAVE_FORMAT_IEEE_FLOAT = 3;
        const ushort WAVE_FORMAT_EXTENSIBLE = 0xFFFE;
        if (fmt.wFormatTag == WAVE_FORMAT_IEEE_FLOAT) return true;
        if (fmt.wFormatTag == WAVE_FORMAT_EXTENSIBLE)
        {
            // SubFormat GUID is at byte offset 24 from the start of WAVEFORMATEX.
            var subFormat = Marshal.PtrToStructure<Guid>(IntPtr.Add(fmtPtr, 24));
            return subFormat == new Guid("00000003-0000-0010-8000-00AA00389B71");
        }
        return false;
    }
}
