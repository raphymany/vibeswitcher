using VibeSwitcher.Models;

namespace VibeSwitcher.Services;

public interface IConfigService
{
    AppConfig Current { get; }
    bool IsFirstRun { get; }
    bool IsPortable { get; }
    string IconsDir { get; }
    void Load();
    void SaveImmediate();
}
