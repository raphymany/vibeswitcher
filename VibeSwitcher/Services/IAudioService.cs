using VibeSwitcher.Models;

namespace VibeSwitcher.Services;

public interface IAudioService : IDisposable
{
    event Action? DevicesChanged;
    IReadOnlyList<AudioDeviceInfo> GetPlaybackDevices();
    IReadOnlyList<AudioDeviceInfo> GetRecordingDevices();
    Task<ProfileSwitchResult> ApplyProfileAsync(DeviceProfile profile);
    Task TestSoundAsync(string deviceId);
}
