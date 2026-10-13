namespace VibeSwitcher.Audio;

// The "Test" button's tone, rendered as 16-bit PCM for the macOS/Linux players. Same sound as the
// Windows app (AudioTestTonePlayer — forked, tracked in forks.txt): middle C, 0.5 s, 10% fade-in,
// 25% fade-out, amplitude 0.22.
public static class TestTone
{
    public const float Frequency = 261f;
    public const float Amplitude = 0.22f;
    public const float DurationSec = 0.5f;
    private const float AttackFrac = 0.10f;
    private const float ReleaseFrac = 0.25f;

    // Interleaved signed 16-bit little-endian samples, every channel carrying the same signal.
    public static byte[] RenderPcm16(int sampleRate, int channels)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);

        int totalFrames = (int)(sampleRate * DurationSec);
        var pcm = new byte[totalFrames * channels * 2];
        for (int i = 0; i < totalFrames; i++)
        {
            float t = (float)i / totalFrames;
            float env = t < AttackFrac ? t / AttackFrac
                      : t > (1f - ReleaseFrac) ? (1f - t) / ReleaseFrac
                      : 1f;
            float sample = Amplitude * env * (float)Math.Sin(2.0 * Math.PI * Frequency * i / sampleRate);
            short s = (short)(sample * 32767);
            for (int ch = 0; ch < channels; ch++)
            {
                int o = (i * channels + ch) * 2;
                pcm[o] = (byte)s;
                pcm[o + 1] = (byte)(s >> 8);
            }
        }
        return pcm;
    }
}
