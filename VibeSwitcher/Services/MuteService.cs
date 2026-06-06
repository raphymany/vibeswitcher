using System.Media;
using System.Runtime.InteropServices;
using VibeSwitcher.Helpers;
using VibeSwitcher.Models;
using VibeSwitcher.NativeMethods;

namespace VibeSwitcher.Services;

public class MuteService
{
    private bool _micMuted = false;
    private bool _speakersMuted = false;

    public bool IsAnyMuteActive   => _micMuted || _speakersMuted;
    public bool IsMicMuted        => _micMuted;
    public bool IsSpeakersMuted   => _speakersMuted;

    public event Action? MuteStateChanged;

    public void Toggle(MuteScope scope)
    {
        bool muting;
        switch (scope)
        {
            case MuteScope.Mic:
                muting = !_micMuted;
                if (SetDeviceMute(EDataFlow.Capture, muting))
                    _micMuted = muting;
                else
                    muting = _micMuted; // COM failed — keep current state for sound
                break;
            case MuteScope.Speakers:
                muting = !_speakersMuted;
                if (SetDeviceMute(EDataFlow.Render, muting))
                    _speakersMuted = muting;
                else
                    muting = _speakersMuted;
                break;
            default: // Both
                muting = !(_micMuted && _speakersMuted);
                bool micOk = SetDeviceMute(EDataFlow.Capture, muting);
                bool spkOk = SetDeviceMute(EDataFlow.Render, muting);
                if (micOk) _micMuted = muting;
                if (spkOk) _speakersMuted = muting;
                break;
        }
        MuteStateChanged?.Invoke();
        _ = Task.Run(() => PlaySound(scope, muting));
    }

    private static bool SetDeviceMute(EDataFlow flow, bool mute)
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            enumerator.GetDefaultAudioEndpoint(flow, ERole.Console, out device);
            if (device == null) return false;

            var volumeGuid = typeof(IAudioEndpointVolume).GUID;
            device.Activate(ref volumeGuid, 23, IntPtr.Zero, out var obj);
            if (obj is not IAudioEndpointVolume vol) return false;

            try
            {
                var ctx = Guid.Empty;
                vol.SetMute(mute, ref ctx);
                return true;
            }
            finally
            {
                Marshal.ReleaseComObject(vol);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warning("MuteService.SetDeviceMute", ex.Message);
            return false;
        }
        finally
        {
            if (device != null) Marshal.ReleaseComObject(device);
            if (enumerator != null) Marshal.ReleaseComObject(enumerator);
        }
    }

    // ── Sounds ───────────────────────────────────────────────────────────────

    private const int SampleRate = 44100;
    private const float Amplitude = 0.35f;

    private static void PlaySound(MuteScope scope, bool muting)
    {
        // When muting speakers the output is silenced immediately — no point playing a sound nobody hears.
        // On unmute the speakers are already restored before this runs, so that case is fine to play.
        if (muting && (scope == MuteScope.Speakers || scope == MuteScope.Both)) return;

        try
        {
            byte[] wav = scope switch
            {
                MuteScope.Mic => muting ? BuildMicMuteWav() : BuildMicUnmuteWav(),
                _             => BuildBothUnmuteWav(),   // Both unmute — speakers restored, so audible
            };
            using var ms = new System.IO.MemoryStream(wav);
            using var player = new SoundPlayer(ms);
            player.PlaySync();
        }
        catch (Exception ex)
        {
            AppLogger.Warning("MuteService.PlaySound", ex.Message);
        }
    }

    // Mic mute: two descending blips — 440 Hz then 280 Hz, deep and short
    private static byte[] BuildMicMuteWav()
        => BuildWav(Concat(Blip(440, 0.09, fadeDown: true), Blip(280, 0.09, fadeDown: true)));

    // Mic unmute: two ascending blips — 280 Hz then 440 Hz
    private static byte[] BuildMicUnmuteWav()
        => BuildWav(Concat(Blip(280, 0.09, fadeDown: false), Blip(440, 0.09, fadeDown: false)));

    // Both unmute: deeper sweep up then ascending blips (speakers are audible again by the time this plays)
    private static byte[] BuildBothUnmuteWav()
        => BuildWav(Concat(Sweep(160, 320, 0.20), Blip(280, 0.08, fadeDown: false), Blip(440, 0.08, fadeDown: false)));

    // Single tone at a fixed frequency with a linear fade-in (fadeDown=false) or fade-out (fadeDown=true)
    private static short[] Blip(double freq, double durationSec, bool fadeDown)
    {
        int frames = (int)(SampleRate * durationSec);
        var s = new short[frames];
        for (int i = 0; i < frames; i++)
        {
            float env = fadeDown ? 1f - (float)i / frames : (float)i / frames;
            // Apply a slight smoothing to avoid clicks at start/end
            env = env * env;
            float sample = Amplitude * env * (float)Math.Sin(2 * Math.PI * freq * i / SampleRate);
            s[i] = (short)(sample * short.MaxValue);
        }
        return s;
    }

    // Frequency sweep from startHz to endHz over durationSec with an envelope shaped for naturalness
    private static short[] Sweep(double startHz, double endHz, double durationSec)
    {
        int frames = (int)(SampleRate * durationSec);
        var s = new short[frames];
        double phase = 0;
        for (int i = 0; i < frames; i++)
        {
            float t = (float)i / frames;
            // Bell-shaped envelope: ramp up briefly then fade out
            float env = t < 0.1f ? t / 0.1f : 1f - (t - 0.1f) / 0.9f;
            env = env * env * Amplitude;
            double freq = startHz + (endHz - startHz) * t;
            phase += 2 * Math.PI * freq / SampleRate;
            s[i] = (short)(env * Math.Sin(phase) * short.MaxValue);
        }
        return s;
    }

    private static short[] Concat(params short[][] parts)
    {
        int total = 0;
        foreach (var p in parts) total += p.Length;
        var result = new short[total];
        int pos = 0;
        foreach (var p in parts) { Array.Copy(p, 0, result, pos, p.Length); pos += p.Length; }
        return result;
    }

    private static byte[] BuildWav(short[] samples)
    {
        int dataBytes = samples.Length * 2;
        using var ms = new System.IO.MemoryStream(44 + dataBytes);
        using var w = new System.IO.BinaryWriter(ms);
        w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + dataBytes);
        w.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        w.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(SampleRate); w.Write(SampleRate * 2); w.Write((short)2); w.Write((short)16);
        w.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        w.Write(dataBytes);
        foreach (var s in samples) w.Write(s);
        return ms.ToArray();
    }
}
