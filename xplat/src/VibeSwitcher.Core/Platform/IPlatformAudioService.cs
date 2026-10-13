namespace VibeSwitcher.Services;

// What the macOS and Linux audio backends provide on top of the Windows-shaped IAudioService. On
// Windows these live in separate WASAPI/COM helpers (AudioMicMonitor, MuteService's endpoint-volume
// calls); here each OS backend owns them, so the app layer talks to one object per platform.
public interface IPlatformAudioService : IAudioService
{
    // Mic Test dialog: calls onLevel with the RMS level (0..1, same scale as Windows) of the device's
    // captured audio, roughly every 10 ms, until ct is cancelled. Blocks the calling thread; returns
    // early (after logging) if the device can't be opened.
    void RunMicLevelMonitor(string deviceId, CancellationToken ct, Action<float> onLevel);

    // Mute hotkeys act on the current default output (playback = true) or input device.
    // Get returns null when the state can't be read; Set returns false when it didn't take.
    bool? GetDefaultDeviceMute(bool playback);
    bool SetDefaultDeviceMute(bool playback, bool mute);
}
