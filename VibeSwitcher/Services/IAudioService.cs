using VibeSwitcher.Models;

namespace VibeSwitcher.Services;

public interface IAudioService : IDisposable
{
    event Action? DevicesChanged;
    event Action<string>? DevicePropertyChanged;
    IReadOnlyList<AudioDeviceInfo> GetPlaybackDevices();
    IReadOnlyList<AudioDeviceInfo> GetRecordingDevices();
    Task<ProfileSwitchResult> ApplyProfileAsync(DeviceProfile profile);
    Task TestSoundAsync(string deviceId);
    string? GetAudioEndpointPath(string audioDeviceId);
}
