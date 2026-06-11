using VibeSwitcher.Models;
using VibeSwitcher.Services;

namespace VibeSwitcher.Tests;

internal sealed class FakeConfigService : IConfigService
{
    private AppConfig _config = new();

    public AppConfig Current => _config;
    public bool IsFirstRun { get; set; }
    public string IconsDir { get; set; } =
        Path.Combine(Path.GetTempPath(), "VibeSwitcherTests", "Icons");

    public void Load() { }
    public void SaveImmediate() { }
    public void SaveDeferred() { }
    public void ExportTo(string destinationPath) { }
    public bool TryImport(string sourcePath, out string? error) { error = null; return false; }

    public void SetConfig(AppConfig config) => _config = config;
}
