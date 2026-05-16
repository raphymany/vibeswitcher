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

    private static readonly string ConfigPath    = Path.Combine(ConfigDir, "config.json");
    private static readonly string ConfigBakPath = Path.Combine(ConfigDir, "config.json.bak");
    private static readonly string ConfigTmpPath = Path.Combine(ConfigDir, "config.json.tmp");

    private volatile AppConfig _config = new();
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

        if (TryLoad(ConfigPath, out var loaded))
        {
            _config = loaded!;
            _config.Profiles ??= new();
            Migrate(_config);
            return;
        }

        AppLogger.Warning("ConfigService.Load", "Primary config corrupted, trying backup");

        if (File.Exists(ConfigBakPath) && TryLoad(ConfigBakPath, out loaded))
        {
            _config = loaded!;
            _config.Profiles ??= new();
            Migrate(_config);
            AppLogger.Info("ConfigService.Load", "Recovered from backup config");
            return;
        }

        IsFirstRun = true;
        _config = new AppConfig();
        AppLogger.Warning("ConfigService.Load", "Both config and backup failed — starting fresh");
    }

    private static bool TryLoad(string path, out AppConfig? config)
    {
        try
        {
            var json = File.ReadAllText(path);
            config = JsonConvert.DeserializeObject<AppConfig>(json);
            return config != null;
        }
        catch (Exception ex)
        {
            AppLogger.Error("ConfigService.TryLoad", ex);
            config = null;
            return false;
        }
    }

    private static void Migrate(AppConfig config)
    {
        // v1 used -1 as a sentinel for "window position not yet saved"; v1.0.1+ uses null.
        if (config.WindowLeft == -1) config.WindowLeft = null;
        if (config.WindowTop  == -1) config.WindowTop  = null;
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

                // Back up the current good config before overwriting
                if (File.Exists(ConfigPath))
                    File.Copy(ConfigPath, ConfigBakPath, overwrite: true);

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
