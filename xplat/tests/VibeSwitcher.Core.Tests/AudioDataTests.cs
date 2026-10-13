using VibeSwitcher.Audio;

namespace VibeSwitcher.Core.Tests;

public class TestToneTests
{
    [Fact]
    public void Length_IsHalfASecond()
    {
        Assert.Equal(48000 / 2 * 2 * 2, TestTone.RenderPcm16(48000, 2).Length);
        Assert.Equal(44100 / 2 * 1 * 2, TestTone.RenderPcm16(44100, 1).Length);
    }

    [Fact]
    public void FadesInAndOut_PeakNearAmplitude_ChannelsIdentical()
    {
        var pcm = TestTone.RenderPcm16(48000, 2);
        short Sample(int frame, int ch) => BitConverter.ToInt16(pcm, (frame * 2 + ch) * 2);

        int frames = pcm.Length / 4;
        Assert.Equal(0, Sample(0, 0));                       // silent start
        Assert.True(Math.Abs((int)Sample(frames - 1, 0)) < 200); // almost silent end
        int peak = Enumerable.Range(0, frames).Max(i => Math.Abs((int)Sample(i, 0)));
        Assert.InRange(peak, (int)(0.21 * 32767), (int)(0.221 * 32767)); // amplitude 0.22
        Assert.All(Enumerable.Range(0, frames), i => Assert.Equal(Sample(i, 0), Sample(i, 1)));
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(48000, 0)]
    public void InvalidFormat_Throws(int rate, int channels) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => TestTone.RenderPcm16(rate, channels));
}

public class AudioLevelTests
{
    [Fact]
    public void Rms_MatchesTheWindowsMeter()
    {
        Assert.Equal(0f, AudioLevel.Rms([]));
        Assert.Equal(0f, AudioLevel.Rms([0f, 0f]));
        Assert.Equal(0.5f, AudioLevel.Rms([0.5f, -0.5f, 0.5f, -0.5f]), 5);
        // A full-scale sine has an RMS of 1/√2.
        var sine = Enumerable.Range(0, 4800).Select(i => (float)Math.Sin(2 * Math.PI * 440 * i / 48000)).ToArray();
        Assert.Equal(1 / MathF.Sqrt(2), AudioLevel.Rms(sine), 3);
    }

    [Fact]
    public void LoudestChannelRms_OneMicOnAManyInputInterface_ReadsFullStrength()
    {
        // 16 interleaved channels, signal (0.5) only on channel 2.
        var block = new float[16 * 100];
        for (int f = 0; f < 100; f++) block[f * 16 + 1] = f % 2 == 0 ? 0.5f : -0.5f;
        Assert.Equal(0.5f, AudioLevel.LoudestChannelRms(block, 16), 4);
        Assert.Equal(0.125f, AudioLevel.Rms(block), 4); // what an all-channel RMS would show
        Assert.Equal(AudioLevel.Rms([0.25f, -0.25f]), AudioLevel.LoudestChannelRms([0.25f, -0.25f], 1));
        Assert.Equal(0f, AudioLevel.LoudestChannelRms([0.5f], 2)); // less than one frame
    }
}

public class WavDataTests
{
    private static byte[] Wav(ushort tag, int bits, int channels = 2, int rate = 44100, int dataBytes = 16,
        bool extensible = false, ushort subTag = 1, byte[]? extraChunk = null)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8); w.Write(0); w.Write("WAVE"u8);
        if (extraChunk != null) { w.Write("LIST"u8); w.Write(extraChunk.Length); w.Write(extraChunk); if (extraChunk.Length % 2 == 1) w.Write((byte)0); }
        w.Write("fmt "u8); w.Write(extensible ? 40 : 16);
        w.Write(extensible ? (ushort)0xFFFE : tag); w.Write((short)channels); w.Write(rate);
        w.Write(rate * channels * bits / 8); w.Write((short)(channels * bits / 8)); w.Write((short)bits);
        if (extensible)
        {
            w.Write((short)22); w.Write((short)bits); w.Write(3); // cbSize, valid bits, channel mask
            w.Write(subTag); w.Write(new byte[] { 0, 0, 0, 0, 0x10, 0, 0x80, 0, 0, 0xAA, 0, 0x38, 0x9B, 0x71 });
        }
        w.Write("data"u8); w.Write(dataBytes); w.Write(Enumerable.Range(0, dataBytes).Select(i => (byte)i).ToArray());
        return ms.ToArray();
    }

    [Theory]
    [InlineData(1, 8, WavSampleFormat.U8)]
    [InlineData(1, 16, WavSampleFormat.S16)]
    [InlineData(1, 24, WavSampleFormat.S24)]
    [InlineData(1, 32, WavSampleFormat.S32)]
    [InlineData(3, 32, WavSampleFormat.F32)]
    public void Formats(ushort tag, int bits, WavSampleFormat expected)
    {
        var d = WavData.Parse(Wav(tag, bits, dataBytes: 48));
        Assert.Equal(expected, d.Format);
        Assert.Equal(2, d.Channels);
        Assert.Equal(44100, d.SampleRate);
        Assert.Equal(48, d.Samples.Count);
        Assert.Equal(0, d.Samples[0]);
    }

    [Fact]
    public void Extensible_UsesSubFormat()
    {
        Assert.Equal(WavSampleFormat.F32, WavData.Parse(Wav(0, 32, extensible: true, subTag: 3)).Format);
        Assert.Equal(WavSampleFormat.S16, WavData.Parse(Wav(0, 16, extensible: true, subTag: 1)).Format);
    }

    [Fact]
    public void SkipsOtherChunks_IncludingOddSizedPadding()
    {
        var d = WavData.Parse(Wav(1, 16, extraChunk: [1, 2, 3]));
        Assert.Equal(WavSampleFormat.S16, d.Format);
        Assert.Equal(16, d.Samples.Count);
    }

    [Fact]
    public void TruncatedData_ClampedToWholeFrames()
    {
        var wav = Wav(1, 16, channels: 2, dataBytes: 16);
        Array.Resize(ref wav, wav.Length - 3); // the declared size now overshoots the file
        Assert.Equal(12, WavData.Parse(wav).Samples.Count); // 13 bytes present → 3 whole 4-byte frames
    }

    private sealed class CapturingPlayer : VibeSwitcher.Compat.IWavPlayer
    {
        public readonly List<byte[]> Played = [];
        public void PlayWav(byte[] wav) => Played.Add(wav);
    }

    [Theory]
    [InlineData("Click")]
    [InlineData("Chime")]
    [InlineData("Alert")]
    public async Task TheAppsOwnSwitchSounds_Parse(string tone)
    {
        // The linked SwitchSoundService synthesizes these and hands them to the SoundPlayer shim;
        // every one must be playable by the macOS/Linux players.
        var player = new CapturingPlayer();
        VibeSwitcher.Compat.WavPlayback.Player = player;
        try
        {
            await new VibeSwitcher.Services.SwitchSoundService(new VibeSwitcher.Tests.FakeAppLogger()).TestAsync(tone, null, 80);
            var d = WavData.Parse(Assert.Single(player.Played));
            Assert.Equal(WavSampleFormat.S16, d.Format);
            Assert.Equal(1, d.Channels);
            Assert.Equal(44100, d.SampleRate);
            Assert.True(d.Samples.Count > 1000);
        }
        finally { VibeSwitcher.Compat.WavPlayback.Player = null; }
    }

    [Theory]
    [InlineData(2, 16)]  // ADPCM
    [InlineData(1, 12)]  // odd PCM width
    [InlineData(3, 64)]  // double
    public void UnsupportedEncodings_Throw(ushort tag, int bits) =>
        Assert.Throws<InvalidDataException>(() => WavData.Parse(Wav(tag, bits)));

    [Fact]
    public void Garbage_Throws()
    {
        Assert.Throws<InvalidDataException>(() => WavData.Parse([]));
        Assert.Throws<InvalidDataException>(() => WavData.Parse("RIFF\0\0\0\0WAVE"u8.ToArray()));
        Assert.Throws<InvalidDataException>(() => WavData.Parse("RIFX\0\0\0\0WAVEfmt "u8.ToArray()));
    }

    [Fact]
    public void DataBeforeFmt_Throws()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8); w.Write(0); w.Write("WAVE"u8); w.Write("data"u8); w.Write(2); w.Write((short)0);
        Assert.Throws<InvalidDataException>(() => WavData.Parse(ms.ToArray()));
    }
}
