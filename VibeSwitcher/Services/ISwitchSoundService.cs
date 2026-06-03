using VibeSwitcher.Models;

namespace VibeSwitcher.Services;

public interface ISwitchSoundService
{
    Task PlayAsync(DeviceProfile profile, AppConfig config);
    Task TestAsync(string tone, string? customPath, int volume);
}
