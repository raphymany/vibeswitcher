using System.Diagnostics;
using System.Runtime.InteropServices;
using VibeSwitcher.Audio;
using VibeSwitcher.Platform.MacOS.Interop;
using static VibeSwitcher.Platform.MacOS.Interop.AudioToolbox;

namespace VibeSwitcher.Platform.MacOS.Audio;

// Playback and capture through Audio Queues (AudioToolbox), the standard macOS way to play or record
// raw PCM on a chosen device. Callbacks run on the queue's own thread (no run loop needed).
internal sealed class AudioQueueStreams : IMacAudioStreams
{
    private static readonly int ByteSizeOffset = Marshal.OffsetOf<AudioQueueBuffer>(nameof(AudioQueueBuffer.AudioDataByteSize)).ToInt32();

    // Output buffers are freed with the queue, so the "buffer done" callback has nothing to do — but
    // Audio Queues require one. Static, so it can never be collected while a queue might call it.
    private static readonly OutputCallback NoOp = (_, _, _) => { };
    private static readonly IntPtr NoOpPtr = Marshal.GetFunctionPointerForDelegate(NoOp);

    // Silence appended to every sound: the queue reports "stopped" when its buffers are consumed,
    // which can be a little before the device (especially Bluetooth) has actually played them.
    private static readonly TimeSpan Tail = TimeSpan.FromMilliseconds(150);

    // How long past the sound's own length playback may take before it's abandoned.
    internal TimeSpan PlaybackSlack { get; init; } = TimeSpan.FromSeconds(5);

    // An input that delivers nothing for this long has stopped (e.g. the device was unplugged).
    internal TimeSpan InputStallTimeout { get; init; } = TimeSpan.FromSeconds(3);

    public void PlayPcm(string? deviceUid, ArraySegment<byte> pcm, WavSampleFormat format, int rate, int channels)
    {
        if (pcm.Count == 0) return;
        int frameBytes = BytesPerSample(format) * channels;
        int tailBytes = (int)(rate * Tail.TotalSeconds) * frameBytes;
        int total = pcm.Count + tailBytes;
        var asbd = Describe(format, rate, channels);

        Check(AudioQueueNewOutput(ref asbd, NoOpPtr, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out var queue), "AudioQueueNewOutput");
        try
        {
            if (deviceUid != null) SetDevice(queue, deviceUid);

            Check(AudioQueueAllocateBuffer(queue, (uint)total, out var buffer), "AudioQueueAllocateBuffer");
            var b = Marshal.PtrToStructure<AudioQueueBuffer>(buffer);
            Marshal.Copy(pcm.Array!, pcm.Offset, b.AudioData, pcm.Count);
            // The tail is silence: zero for signed/float PCM, the midpoint (0x80) for unsigned 8-bit.
            var silence = new byte[tailBytes];
            if (format == WavSampleFormat.U8) Array.Fill(silence, (byte)0x80);
            Marshal.Copy(silence, 0, b.AudioData + pcm.Count, tailBytes);
            Marshal.WriteInt32(buffer, ByteSizeOffset, total);
            Check(AudioQueueEnqueueBuffer(queue, buffer, 0, IntPtr.Zero), "AudioQueueEnqueueBuffer");

            Check(AudioQueueStart(queue, IntPtr.Zero), "AudioQueueStart");
            // A non-immediate stop takes effect once everything queued has played.
            Check(AudioQueueStop(queue, 0), "AudioQueueStop");

            // "Running" is reported asynchronously: wait until it has been seen and then cleared, so
            // a short sound on a device that's slow to start isn't thrown away before it plays.
            var deadline = TimeSpan.FromSeconds(total / (double)(rate * frameBytes)) + PlaybackSlack;
            var sw = Stopwatch.StartNew();
            bool seenRunning = false;
            while (true)
            {
                bool running = IsRunning(queue);
                if (running) seenRunning = true;
                else if (seenRunning) break;
                if (sw.Elapsed > deadline)
                {
                    AudioQueueStop(queue, 1);
                    throw new TimeoutException(seenRunning ? "Audio playback did not finish." : "Audio playback never started.");
                }
                Thread.Sleep(5);
            }
        }
        finally { AudioQueueDispose(queue, 1); }
    }

    public void Capture(string deviceUid, int rate, int channels, CancellationToken ct, Action<float[]> onBlock)
    {
        channels = Math.Clamp(channels, 1, 32);
        int blockFrames = Math.Max(rate / 100, 1);
        var asbd = Describe(WavSampleFormat.F32, rate, channels);
        Exception? callbackError = null;
        // Not disposed: a late callback must never touch a disposed object from a native thread.
        var failed = new ManualResetEventSlim();
        long lastCallback = Stopwatch.GetTimestamp();

        InputCallback callback = (_, queue, buffer, _, _, _) =>
        {
            try
            {
                Volatile.Write(ref lastCallback, Stopwatch.GetTimestamp());
                var b = Marshal.PtrToStructure<AudioQueueBuffer>(buffer);
                if (b.AudioDataByteSize >= 4 && !ct.IsCancellationRequested)
                {
                    var samples = new float[b.AudioDataByteSize / 4];
                    Marshal.Copy(b.AudioData, samples, 0, samples.Length);
                    onBlock(samples);
                }
            }
            catch (Exception ex)
            {
                callbackError ??= ex; // never let an exception reach native code
                failed.Set();
            }
            finally
            {
                if (!ct.IsCancellationRequested) AudioQueueEnqueueBuffer(queue, buffer, 0, IntPtr.Zero);
            }
        };

        Check(AudioQueueNewInput(ref asbd, Marshal.GetFunctionPointerForDelegate(callback), IntPtr.Zero,
            IntPtr.Zero, IntPtr.Zero, 0, out var q), "AudioQueueNewInput");
        try
        {
            SetDevice(q, deviceUid);
            for (int i = 0; i < 3; i++)
            {
                Check(AudioQueueAllocateBuffer(q, (uint)(blockFrames * channels * 4), out var buffer), "AudioQueueAllocateBuffer");
                Check(AudioQueueEnqueueBuffer(q, buffer, 0, IntPtr.Zero), "AudioQueueEnqueueBuffer");
            }
            Check(AudioQueueStart(q, IntPtr.Zero), "AudioQueueStart");
            Volatile.Write(ref lastCallback, Stopwatch.GetTimestamp());

            while (!ct.IsCancellationRequested)
            {
                if (WaitHandle.WaitAny([ct.WaitHandle, failed.WaitHandle], 250) == 1)
                    throw new InvalidOperationException("Mic level callback failed.", callbackError);
                if (Stopwatch.GetElapsedTime(Volatile.Read(ref lastCallback)) > InputStallTimeout && !ct.IsCancellationRequested)
                    throw new InvalidOperationException("The input device stopped delivering audio (was it unplugged?).");
            }
        }
        finally
        {
            AudioQueueStop(q, 1);
            AudioQueueDispose(q, 1);
            GC.KeepAlive(callback);
        }
    }

    private static void SetDevice(IntPtr queue, string uid)
    {
        var cf = CoreFoundation.Create(uid);
        if (cf == IntPtr.Zero) throw new InvalidOperationException($"Could not pass the device id '{uid}' to Core Audio.");
        var holder = Marshal.AllocHGlobal(IntPtr.Size);
        try
        {
            Marshal.WriteIntPtr(holder, cf);
            int status = AudioQueueSetProperty(queue, QueuePropertyCurrentDevice, holder, (uint)IntPtr.Size);
            if (status != 0) throw new InvalidOperationException($"Audio device '{uid}' is not available (OSStatus {status}).");
        }
        finally
        {
            Marshal.FreeHGlobal(holder);
            CoreFoundation.CFRelease(cf);
        }
    }

    private static bool IsRunning(IntPtr queue)
    {
        var buf = Marshal.AllocHGlobal(4);
        try
        {
            uint size = 4;
            return AudioQueueGetProperty(queue, QueuePropertyIsRunning, buf, ref size) == 0 && Marshal.ReadInt32(buf) != 0;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    // Packed, interleaved, native-endian (little-endian on every Mac) linear PCM.
    internal static AudioStreamBasicDescription Describe(WavSampleFormat format, int rate, int channels)
    {
        uint bytes = (uint)BytesPerSample(format);
        uint flags = FormatFlagIsPacked | format switch
        {
            WavSampleFormat.F32 => FormatFlagIsFloat,
            WavSampleFormat.U8 => 0u,
            _ => FormatFlagIsSignedInteger,
        };
        return new AudioStreamBasicDescription
        {
            SampleRate = rate,
            FormatID = FormatLinearPCM,
            FormatFlags = flags,
            BytesPerPacket = bytes * (uint)channels,
            FramesPerPacket = 1,
            BytesPerFrame = bytes * (uint)channels,
            ChannelsPerFrame = (uint)channels,
            BitsPerChannel = bytes * 8,
        };
    }

    private static int BytesPerSample(WavSampleFormat f) => f switch
    {
        WavSampleFormat.U8 => 1,
        WavSampleFormat.S16 => 2,
        WavSampleFormat.S24 => 3,
        _ => 4,
    };

    private static void Check(int status, string what)
    {
        if (status != 0) throw new InvalidOperationException($"{what} failed (OSStatus {status} '{FourCC.ToText(unchecked((uint)status))}').");
    }
}
