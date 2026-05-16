using System.IO;
using VibeSwitcher.Helpers;
using VibeSwitcher.Models;
using Newtonsoft.Json;

namespace VibeSwitcher.Services;

public class ConfigService
{
    private static readonly string ConfigDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VibeSwitcher");

    public static readonly string IconsDir = Path.Combine(ConfigDir, "Icons");

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");
    private static readonly string ConfigTmpPath = ConfigPath + ".tmp";

    private AppConfig _config = new();
    private readonly object _saveLock = new();

    public AppConfig Current => _config;
    public bool IsFirstRun { get; private set; }

    public void Load()
    {
        Directory.CreateDirectory(ConfigDir);

        if (!File.Exists(ConfigPath))
        {
            IsFirstRun = true;
            _config = new AppConfig();
            return;
        }

        try
        {
            var json = File.ReadAllText(ConfigPath);
            var loaded = JsonConvert.DeserializeObject<AppConfig>(json);
            _config = loaded ?? new AppConfig();
            _config.Profiles ??= new();
        }
        catch (Exception ex)
        {
            IsFirstRun = true;
            _config = new AppConfig();
            AppLogger.Error("ConfigService.Load", ex);
        }
    }

    public void SaveImmediate()
    {
        Save(_config);
    }

    public void Save(AppConfig config)
    {
        lock (_saveLock)
        {
            try
            {
                Directory.CreateDirectory(ConfigDir);
                var json = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(ConfigTmpPath, json);
                File.Move(ConfigTmpPath, ConfigPath, overwrite: true);
            }
            catch (Exception ex)
            {
                AppLogger.Error("ConfigService.Save", ex);
            }
        }
    }
}
