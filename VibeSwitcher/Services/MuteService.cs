using System.Media;
using System.Runtime.InteropServices;
using VibeSwitcher.Helpers;
using VibeSwitcher.Models;
using VibeSwitcher.NativeMethods;

namespace VibeSwitcher.Services;

public class MuteService
{
    private readonly HashSet<MuteScope> _activeMutes = new();

    public bool IsAnyMuteActive => _activeMutes.Count > 0;

    public event Action? MuteStateChanged;

    public void Toggle(MuteScope scope)
    {
        bool muting = !_activeMutes.Contains(scope);
        if (muting) Mute(scope); else Unmute(scope);
        MuteStateChanged?.Invoke();
        Task.Run(() => PlaySound(scope, muting));
    }

    private void Mute(MuteScope scope)
    {
        if (scope == MuteScope.Mic || scope == MuteScope.Both)
            SetDeviceMute(EDataFlow.Capture, true);
        if (scope == MuteScope.Speakers || scope == MuteScope.Both)
            SetDeviceMute(EDataFlow.Render, true);
        _activeMutes.Add(scope);
    }

    private void Unmute(MuteScope scope)
    {
        if (scope == MuteScope.Mic || scope == MuteScope.Both)
            SetDeviceMute(EDataFlow.Capture, false);
        if (scope == MuteScope.Speakers || scope == MuteScope.Both)
            SetDeviceMute(EDataFlow.Render, false);
        _activeMutes.Remove(scope);
    }

    private static void SetDeviceMute(EDataFlow flow, bool mute)
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            enumerator.GetDefaultAudioEndpoint(flow, ERole.Console, out device);
            if (device == null) return;

            var volumeGuid = typeof(IAudioEndpointVolume).GUID;
            device.Activate(ref volumeGuid, 23, IntPtr.Zero, out var obj);
            if (obj is not IAudioEndpointVolume vol) return;

            var ctx = Guid.Empty;
            vol.SetMute(mute, ref ctx);
            Marshal.ReleaseComObject(vol);
        }
        catch (Exception ex)
        {
            AppLogger.Warning("MuteService.SetDeviceMute", ex.Message);
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
