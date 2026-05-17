namespace VibeSwitcher.Services;

public interface IStartupService
{
    bool IsStartupEnabled();
    void Enable();
    void Disable();
    void RefreshRegistryPath();
}
