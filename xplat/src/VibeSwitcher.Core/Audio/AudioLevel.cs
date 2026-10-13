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
}
