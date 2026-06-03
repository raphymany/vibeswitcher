using System.Media;
using System.IO;
using VibeSwitcher.Models;

namespace VibeSwitcher.Services;

public class SwitchSoundService : ISwitchSoundService
{
    public Task PlayAsync(DeviceProfile profile, AppConfig config)
    {
        var resolved = Resolve(profile, config);
        if (resolved is null) return Task.CompletedTask;
        var (tone, customPath, volume) = resolved.Value;
        return Task.Run(() => PlaySync(tone, customPath, volume));
    }

    public Task TestAsync(string tone, string? customPath, int volume)
        => Task.Run(() => PlaySync(tone, customPath, volume));

    // Returns null when no sound should play.
    internal static (string tone, string? customPath, int volume)? Resolve(DeviceProfile profile, AppConfig config)
    {
        if (!config.SwitchSoundEnabled) return null;
        if (profile.SoundMuted) return null;  // mute is independent of override

        if (profile.SoundOverride)
        {
            return (
                profile.SoundTone       ?? config.SwitchSoundTone,
                profile.SoundCustomPath ?? config.SwitchSoundCustomPath,
                profile.SoundVolume     ?? config.SwitchSoundVolume
            );
        }

        return (config.SwitchSoundTone, config.SwitchSoundCustomPath, config.SwitchSoundVolume);
    }

    private static void PlaySync(string tone, string? customPath, int volume)
    {
        try
        {
            byte[] wav = tone == "Custom" && !string.IsNullOrEmpty(customPath)
                ? LoadAndScaleWav(customPath, volume)
                : GenerateTone(tone, volume);

            using var ms = new MemoryStream(wav);
            using var player = new SoundPlayer(ms);
            player.PlaySync();
        }
        catch (Exception ex)
        {
            Helpers.AppLogger.Warning("SwitchSoundService.PlaySync", ex.Message);
        }
    }

    // ── WAV generation ────────────────────────────────────────────────────────

    private const int SampleRate = 44100;

    private static byte[] GenerateTone(string tone, int volume)
    {
        float amplitude = Math.Clamp(volume / 100f, 0f, 1f) * 0.5f;

        short[] samples = tone switch
        {
            "Chime" => GenerateChime(amplitude),
            "Blip"  => GenerateBlip(amplitude),
            "Bell"  => GenerateBell(amplitude),
            "Alert" => GenerateAlert(amplitude),
            _       => GenerateClick(amplitude),
        };

        return BuildWav(samples);
    }

    private static short[] GenerateClick(float amplitude)
    {
        // 800 Hz sine, 0.09 s, linear fade-out
        int frames = (int)(SampleRate * 0.09);
        var s = new short[frames];
        for (int i = 0; i < frames; i++)
        {
            float env = 1f - (float)i / frames;
            float sample = amplitude * env * (float)Math.Sin(2 * Math.PI * 800.0 * i / SampleRate);
            s[i] = (short)(sample * short.MaxValue);
        }
        return s;
    }

    private static short[] GenerateChime(float amplitude)
    {
        // 880 Hz sine, 0.45 s, exponential decay
        int frames = (int)(SampleRate * 0.45);
        var s = new short[frames];
        for (int i = 0; i < frames; i++)
        {
            float env = (float)Math.Exp(-4.0 * i / frames);
            float sample = amplitude * env * (float)Math.Sin(2 * Math.PI * 880.0 * i / SampleRate);
            s[i] = (short)(sample * short.MaxValue);
        }
        return s;
    }

    private static short[] GenerateBlip(float amplitude)
    {
        // Two-tone ascending sweep: 600 Hz → 900 Hz over 0.18 s
        int frames = (int)(SampleRate * 0.18);
        var s = new short[frames];
        double phase = 0;
        for (int i = 0; i < frames; i++)
        {
            float t = (float)i / frames;
            double freq = 600 + 300 * t;
            float env = t < 0.5f ? t * 2 : (1f - t) * 2; // triangle envelope
            phase += 2 * Math.PI * freq / SampleRate;
            float sample = amplitude * env * (float)Math.Sin(phase);
            s[i] = (short)(sample * short.MaxValue);
        }
        return s;
    }

    private static short[] GenerateBell(float amplitude)
    {
        // 440 Hz fundamental + 1320 Hz third harmonic, 0.7 s exponential decay
        int frames = (int)(SampleRate * 0.7);
        var s = new short[frames];
        for (int i = 0; i < frames; i++)
        {
            float env    = (float)Math.Exp(-3.5 * i / frames);
            float fund   = (float)Math.Sin(2 * Math.PI * 440.0  * i / SampleRate);
            float over   = 0.35f * (float)Math.Sin(2 * Math.PI * 1320.0 * i / SampleRate);
            float sample = amplitude * env * (fund + over);
            s[i] = (short)(sample * short.MaxValue);
        }
        return s;
    }

    private static short[] GenerateAlert(float amplitude)
    {
        // Two 660 Hz pulses (0.12 s each) with a 0.05 s silent gap between them
        int pulseLen = (int)(SampleRate * 0.12);
        int gapLen   = (int)(SampleRate * 0.05);
        var s = new short[2 * pulseLen + gapLen];
        for (int pass = 0; pass < 2; pass++)
        {
            int offset = pass * (pulseLen + gapLen);
            for (int i = 0; i < pulseLen; i++)
            {
                float t   = (float)i / pulseLen;
                float env = t < 0.2f ? t / 0.2f : 1f - (t - 0.2f) / 0.8f; // quick attack, longer decay
                float sample = amplitude * env * (float)Math.Sin(2 * Math.PI * 660.0 * i / SampleRate);
                s[offset + i] = (short)(sample * short.MaxValue);
            }
        }
        return s;
    }

    private static byte[] BuildWav(short[] samples)
    {
        int dataBytes = samples.Length * 2;
        using var ms = new MemoryStream(44 + dataBytes);
        using var w = new BinaryWriter(ms);

        w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + dataBytes);
        w.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        w.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        w.Write(16);          // chunk size
        w.Write((short)1);    // PCM
        w.Write((short)1);    // mono
        w.Write(SampleRate);
        w.Write(SampleRate * 2); // byte rate
        w.Write((short)2);    // block align
        w.Write((short)16);   // bits per sample
        w.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        w.Write(dataBytes);
        foreach (var s in samples)
            w.Write(s);

        return ms.ToArray();
    }

    // ── Custom WAV loading ────────────────────────────────────────────────────

    private static byte[] LoadAndScaleWav(string path, int volume)
    {
        byte[] raw = File.ReadAllBytes(path);

        // Find the "fmt " chunk and verify it is 16-bit PCM.
        int fmtOffset = FindChunk(raw, "fmt ");
        if (fmtOffset < 0) return raw; // unknown format — play as-is

        int audioFormat   = BitConverter.ToInt16(raw, fmtOffset + 8);
        int channels      = BitConverter.ToInt16(raw, fmtOffset + 10);
        int bitsPerSample = BitConverter.ToInt16(raw, fmtOffset + 22);

        if (audioFormat != 1 || bitsPerSample != 16) return raw; // not PCM-16 — play as-is

        int dataOffset = FindChunk(raw, "data");
        if (dataOffset < 0) return raw;

        int dataSize = BitConverter.ToInt32(raw, dataOffset + 4);
        int sampleStart = dataOffset + 8;

        float scale = Math.Clamp(volume / 100f, 0f, 1f);
        byte[] result = (byte[])raw.Clone();

        int sampleCount = dataSize / (bitsPerSample / 8 * channels);
        for (int i = 0; i < sampleCount * channels; i++)
        {
            int bytePos = sampleStart + i * 2;
            if (bytePos + 2 > result.Length) break;
            short s = BitConverter.ToInt16(result, bytePos);
            short scaled = (short)(s * scale);
            result[bytePos]     = (byte)(scaled & 0xFF);
            result[bytePos + 1] = (byte)((scaled >> 8) & 0xFF);
        }

        return result;
    }

    private static int FindChunk(byte[] data, string id)
    {
        byte[] tag = System.Text.Encoding.ASCII.GetBytes(id);
        for (int i = 12; i < data.Length - 8; i++)
        {
            if (data[i] == tag[0] && data[i + 1] == tag[1] &&
                data[i + 2] == tag[2] && data[i + 3] == tag[3])
                return i;
            int chunkSize = BitConverter.ToInt32(data, i + 4);
            if (chunkSize < 0) return -1; // malformed file — bail out
            // RIFF pads odd-length chunks with a zero byte; account for it.
            int pad = chunkSize % 2;
            i += 7 + chunkSize + pad; // 8-byte header - 1 for loop increment + optional pad
        }
        return -1;
    }
}
