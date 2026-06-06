using System.Runtime.InteropServices;
using VibeSwitcher.Helpers;
using VibeSwitcher.NativeMethods;

namespace VibeSwitcher.Services;

internal static class AudioMicMonitor
{
    internal static void RunMicLevelMonitor(string deviceId, CancellationToken ct, Action<float> onLevel, IAppLogger logger)
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
                    bool isFloat = AudioTestTonePlayer.IsFloatFormat(fmt, fmtPtr);
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
            logger.Warning("AudioMicMonitor.RunMicLevelMonitor", ex.Message);
        }
        finally { Marshal.ReleaseComObject(enumerator); }
    }
}
