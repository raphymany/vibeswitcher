namespace VibeSwitcher.Audio;

public static class AudioLevel
{
    // RMS of a block of float samples (-1..1): the level the Windows mic meter shows (AudioMicMonitor).
    public static float Rms(ReadOnlySpan<float> block)
    {
        if (block.Length == 0) return 0;
        double sum = 0;
        foreach (var s in block) sum += s * s;
        return (float)Math.Sqrt(sum / block.Length);
    }

    // For interleaved multi-channel audio: the RMS of the loudest channel. (An RMS over every channel
    // would read about √N too low when only one of N inputs has a microphone on it.)
    public static float LoudestChannelRms(ReadOnlySpan<float> interleaved, int channels)
    {
        if (channels <= 1) return Rms(interleaved);
        int frames = interleaved.Length / channels;
        if (frames == 0) return 0;
        double loudest = 0;
        for (int ch = 0; ch < channels; ch++)
        {
            double sum = 0;
            for (int f = 0; f < frames; f++)
            {
                float s = interleaved[f * channels + ch];
                sum += s * s;
            }
            loudest = Math.Max(loudest, sum / frames);
        }
        return (float)Math.Sqrt(loudest);
    }
}
