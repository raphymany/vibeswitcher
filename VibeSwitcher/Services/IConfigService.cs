using VibeSwitcher.Models;

namespace VibeSwitcher.Services;

public interface IConfigService
{
    AppConfig Current { get; }
    bool IsFirstRun { get; }
    string IconsDir { get; }
    string SoundsDir { get; }
    string IconsLibraryDir { get; }
    string SoundsLibraryDir { get; }
    void Load();
    void SaveImmediate();
    void SaveDeferred();
    void ExportTo(string destinationPath);
    bool TryImport(string sourcePath, out string? error);
    void ResetSettingsToDefaults();
}
