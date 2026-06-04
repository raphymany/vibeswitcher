using VibeSwitcher.Models;
using VibeSwitcher.Services;

namespace VibeSwitcher.Tests;

internal sealed class FakeAudioService : IAudioService
{
    public event Action? DevicesChanged;
    public event Action<string>? DevicePropertyChanged;

    public IReadOnlyList<AudioDeviceInfo> PlaybackResult { get; set; } = [];
    public IReadOnlyList<AudioDeviceInfo> RecordingResult { get; set; } = [];
    public ProfileSwitchResult SwitchResult { get; set; } = new(true, true, null, null);

    public List<string> TestSoundCalledWith { get; } = new();
    public Dictionary<string, string> EndpointPaths { get; } = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<AudioDeviceInfo> GetPlaybackDevices() => PlaybackResult;
    public IReadOnlyList<AudioDeviceInfo> GetRecordingDevices() => RecordingResult;
    public Task<ProfileSwitchResult> ApplyProfileAsync(DeviceProfile profile) =>
        Task.FromResult(SwitchResult);
    public Task TestSoundAsync(string deviceId)
    {
        TestSoundCalledWith.Add(deviceId);
        return Task.CompletedTask;
    }
    public string? GetAudioEndpointPath(string audioDeviceId) =>
        EndpointPaths.TryGetValue(audioDeviceId, out var path) ? path : null;

    public void RaiseDevicesChanged() => DevicesChanged?.Invoke();
    public void RaiseDevicePropertyChanged(string deviceId) => DevicePropertyChanged?.Invoke(deviceId);
    public void Dispose() { }
}
