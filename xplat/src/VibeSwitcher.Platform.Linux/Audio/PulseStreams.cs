using VibeSwitcher.Audio;
using VibeSwitcher.Compat;
using VibeSwitcher.Platform.Linux.Processes;

namespace VibeSwitcher.Platform.Linux.Audio;

internal delegate void SampleBlockHandler(ReadOnlySpan<float> block);

// Playback and capture through `pacat` / `parec` (both ship with pactl in pulseaudio-utils and work
// against PipeWire too). Raw PCM in and out, so nothing depends on libsndfile.
internal sealed class PulseStreams(IProcessRunner runner)
{
    internal const string PlayTool = "pacat";
    internal const string RecordTool = "parec";

    // How long past the sound's own length playback may take before pacat is killed.
    internal TimeSpan PlaybackSlack { get; init; } = TimeSpan.FromSeconds(5);

    // Plays raw interleaved PCM and returns once it has finished playing (pacat drains on EOF).
    // device null = the default output. Throws on failure, like SoundPlayer.PlaySync.
    internal void PlayPcm(string? device, ArraySegment<byte> pcm, WavSampleFormat format, int rate, int channels, string streamName)
    {
        var args = new List<string> { "--playback", "--raw", $"--format={PulseFormat(format)}", $"--rate={rate}", $"--channels={channels}",
            "--client-name=VibeSwitcher", $"--stream-name={streamName}" };
        if (device != null) args.Add($"--device={device}");

        using var p = Start(PlayTool, args);
        // Written on its own task: if the server stalls, pacat stops reading and a write bigger than
        // the pipe buffer would block forever. One deadline covers writing and playing; killing pacat
        // when it passes also unblocks the writer.
        var writer = Task.Run(() =>
        {
            try
            {
                p.StandardInput.Write(pcm);
                p.StandardInput.Flush();
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException) { /* pacat exited — its exit code says why */ }
            finally
            {
                try { p.StandardInput.Dispose(); } catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
            }
        });

        double seconds = pcm.Count / (double)(rate * channels * BytesPerSample(format));
        if (!p.WaitForExit(TimeSpan.FromSeconds(seconds) + PlaybackSlack))
        {
            p.Kill();
            writer.Wait(TimeSpan.FromSeconds(2));
            throw new TimeoutException("Audio playback did not finish.");
        }
        writer.Wait(TimeSpan.FromSeconds(2));
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"pacat failed (exit {p.ExitCode}): {p.StandardError.Trim()}");
    }

    // Reads mono float samples from a source until ct is cancelled, handing each ~10 ms block to onBlock.
    internal void Capture(string device, int rate, CancellationToken ct, SampleBlockHandler onBlock)
    {
        var args = new List<string> { "--record", "--raw", "--format=float32le", $"--rate={rate}", "--channels=1",
            "--latency-msec=20", "--client-name=VibeSwitcher", "--stream-name=Mic test", $"--device={device}" };

        using var p = Start(RecordTool, args);
        using var reg = ct.Register(p.Kill);
        int blockSamples = Math.Max(rate / 100, 1);
        var bytes = new byte[blockSamples * 4];
        var samples = new float[blockSamples];
        var stdout = p.StandardOutput;
        while (!ct.IsCancellationRequested)
        {
            int filled = 0;
            while (filled < bytes.Length)
            {
                int n;
                try { n = stdout.Read(bytes, filled, bytes.Length - filled); }
                catch (Exception) when (ct.IsCancellationRequested) { return; }
                if (n == 0)
                {
                    if (ct.IsCancellationRequested) return;
                    p.WaitForExit(TimeSpan.FromSeconds(2));
                    throw new InvalidOperationException($"parec stopped: {p.StandardError.Trim()}");
                }
                filled += n;
            }
            Buffer.BlockCopy(bytes, 0, samples, 0, bytes.Length);
            onBlock(samples);
        }
    }

    private IRunningProcess Start(string tool, IReadOnlyList<string> args)
    {
        try { return runner.Start(tool, args); }
        catch (ToolNotFoundException ex)
        {
            throw new SoundServerUnavailableException(
                $"The '{tool}' tool isn't installed. Install the 'pulseaudio-utils' package and restart VibeSwitcher.", ex);
        }
    }

    internal static string PulseFormat(WavSampleFormat f) => f switch
    {
        WavSampleFormat.U8 => "u8",
        WavSampleFormat.S16 => "s16le",
        WavSampleFormat.S24 => "s24le",
        WavSampleFormat.S32 => "s32le",
        _ => "float32le",
    };

    private static int BytesPerSample(WavSampleFormat f) => f switch
    {
        WavSampleFormat.U8 => 1,
        WavSampleFormat.S16 => 2,
        WavSampleFormat.S24 => 3,
        _ => 4,
    };

    // RMS of a block, the same measure the Windows mic meter shows.
    internal static float Rms(ReadOnlySpan<float> block) => AudioLevel.Rms(block);
}

// Plays the app's in-memory sounds (switch sounds, mute blips) on the default output — what
// System.Media.SoundPlayer does on Windows. Registered as WavPlayback.Player at startup.
public sealed class PulseWavPlayer : IWavPlayer
{
    private readonly PulseStreams _streams;

    public PulseWavPlayer() : this(ProcessRunner.Instance) { }

    public PulseWavPlayer(IProcessRunner runner) => _streams = new PulseStreams(runner);

    public void PlayWav(byte[] wav)
    {
        var data = WavData.Parse(wav);
        _streams.PlayPcm(null, data.Samples, data.Format, data.SampleRate, data.Channels, "VibeSwitcher sound");
    }
}
