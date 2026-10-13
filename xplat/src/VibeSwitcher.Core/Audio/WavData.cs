using System.Buffers.Binary;
using System.IO;

namespace VibeSwitcher.Audio;

public enum WavSampleFormat { U8, S16, S24, S32, F32 }

// A parsed in-memory .wav file: its format plus the raw sample bytes (a slice of the original array).
// Accepts what Windows' SoundPlayer plays — uncompressed PCM (8/16/24/32-bit) and 32-bit float,
// including WAVE_FORMAT_EXTENSIBLE — and throws InvalidDataException for anything else, just as
// SoundPlayer throws, so callers log the same way on every OS.
public sealed record WavData(WavSampleFormat Format, int Channels, int SampleRate, ArraySegment<byte> Samples)
{
    public int BytesPerSample => Format switch
    {
        WavSampleFormat.U8 => 1,
        WavSampleFormat.S16 => 2,
        WavSampleFormat.S24 => 3,
        _ => 4,
    };

    public static WavData Parse(byte[] wav)
    {
        ArgumentNullException.ThrowIfNull(wav);
        if (wav.Length < 12 || !Tag(wav, 0, "RIFF") || !Tag(wav, 8, "WAVE"))
            throw new InvalidDataException("Not a WAV file (missing RIFF/WAVE header).");

        WavSampleFormat? format = null;
        int channels = 0, rate = 0;
        int pos = 12;
        while (pos + 8 <= wav.Length)
        {
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(wav.AsSpan(pos + 4));
            int body = pos + 8;
            // Clamp an over-long size to the bytes present (common in streamed/truncated files).
            int len = (int)Math.Min(size, (uint)(wav.Length - body));

            if (Tag(wav, pos, "fmt "))
            {
                if (len < 16) throw new InvalidDataException("WAV 'fmt ' chunk is too short.");
                var f = wav.AsSpan(body, len);
                ushort tag = BinaryPrimitives.ReadUInt16LittleEndian(f);
                channels = BinaryPrimitives.ReadUInt16LittleEndian(f[2..]);
                rate = (int)BinaryPrimitives.ReadUInt32LittleEndian(f[4..]);
                ushort bits = BinaryPrimitives.ReadUInt16LittleEndian(f[14..]);
                if (tag == 0xFFFE) // WAVE_FORMAT_EXTENSIBLE: the real tag is the first 2 bytes of SubFormat
                {
                    if (len < 40) throw new InvalidDataException("WAV extensible format chunk is too short.");
                    tag = BinaryPrimitives.ReadUInt16LittleEndian(f[24..]);
                }
                format = (tag, bits) switch
                {
                    (1, 8) => WavSampleFormat.U8,
                    (1, 16) => WavSampleFormat.S16,
                    (1, 24) => WavSampleFormat.S24,
                    (1, 32) => WavSampleFormat.S32,
                    (3, 32) => WavSampleFormat.F32,
                    _ => throw new InvalidDataException($"Unsupported WAV encoding (format {tag}, {bits}-bit)."),
                };
                if (channels < 1 || channels > 32) throw new InvalidDataException($"Unsupported WAV channel count ({channels}).");
                if (rate < 1000 || rate > 384000) throw new InvalidDataException($"Unsupported WAV sample rate ({rate} Hz).");
            }
            else if (Tag(wav, pos, "data"))
            {
                if (format == null) throw new InvalidDataException("WAV 'data' chunk comes before 'fmt '.");
                var data = new WavData(format.Value, channels, rate, default);
                int frame = data.BytesPerSample * channels;
                // Drop a trailing partial frame so players never get a misaligned buffer.
                return data with { Samples = new ArraySegment<byte>(wav, body, len - len % frame) };
            }

            // Chunks are word-aligned: an odd-sized chunk is followed by one pad byte.
            long next = (long)body + size + (size & 1);
            if (next > wav.Length) break;
            pos = (int)next;
        }
        throw new InvalidDataException(format == null ? "WAV file has no 'fmt ' chunk." : "WAV file has no 'data' chunk.");
    }

    private static bool Tag(byte[] b, int at, string tag) =>
        at + 4 <= b.Length && b[at] == tag[0] && b[at + 1] == tag[1] && b[at + 2] == tag[2] && b[at + 3] == tag[3];
}
