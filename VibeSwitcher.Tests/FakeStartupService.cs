using VibeSwitcher.Services;

namespace VibeSwitcher.Tests;

internal sealed class FakeStartupService : IStartupService
{
    public bool StartupEnabled { get; private set; }
    public bool IsStartupEnabled() => StartupEnabled;
    public void Enable() => StartupEnabled = true;
    public void Disable() => StartupEnabled = false;
    public void RefreshRegistryPath() { }
}
