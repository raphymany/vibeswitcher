using VibeSwitcher.Models;
using VibeSwitcher.Services;

namespace VibeSwitcher.Tests;

internal sealed class FakeAudioService : IAudioService
{
    public event Action? DevicesChanged;

    public IReadOnlyList<AudioDeviceInfo> PlaybackResult { get; set; } = [];
    public IReadOnlyList<AudioDeviceInfo> RecordingResult { get; set; } = [];
    public ProfileSwitchResult SwitchResult { get; set; } = new(true, true, null, null);

    public IReadOnlyList<AudioDeviceInfo> GetPlaybackDevices() => PlaybackResult;
    public IReadOnlyList<AudioDeviceInfo> GetRecordingDevices() => RecordingResult;
    public Task<ProfileSwitchResult> ApplyProfileAsync(DeviceProfile profile) =>
        Task.FromResult(SwitchResult);

    public void RaiseDevicesChanged() => DevicesChanged?.Invoke();
    public void Dispose() { }
}
