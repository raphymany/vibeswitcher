using System.IO;
using System.Text.Json;
using VibeSwitcher.Helpers;
using VibeSwitcher.Models;

namespace VibeSwitcher.Services;

public class ConfigService : IConfigService
{
    private static readonly string DefaultConfigDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VibeSwitcher");

    private readonly string _configDir;
    private readonly string _configPath;
    private readonly string _configBakPath;
    private readonly string _configTmpPath;

    public string IconsDir { get; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private volatile AppConfig _config = new();
    private readonly object _saveLock = new();

    public ConfigService(string? baseDir = null)
    {
        _configDir    = baseDir ?? DefaultConfigDir;
        IconsDir      = Path.Combine(_configDir, "Icons");
        _configPath   = Path.Combine(_configDir, "config.json");
        _configBakPath = Path.Combine(_configDir, "config.json.bak");
        _configTmpPath = Path.Combine(_configDir, "config.json.tmp");
    }

    public AppConfig Current => _config;
    public bool IsFirstRun { get; private set; }

    public void Load()
    {
        try
        {
            Directory.CreateDirectory(_configDir);
        }
        catch (Exception ex)
        {
            AppLogger.Error("ConfigService.Load", ex);
            SessionErrorTracker.Record(ErrorCode.ConfigDirCreateFailed, "Config Directory Error",
                $"Could not create config directory at '{_configDir}': {ex.Message}");
            IsFirstRun = true;
            _config = new AppConfig();
            return;
        }

        if (!File.Exists(_configPath))
        {
            IsFirstRun = true;
            _config = new AppConfig();
            return;
        }

        if (TryLoad(_configPath, out var loaded))
        {
            _config = loaded!;
            _config.Profiles ??= new();
            Migrate(_config);
            return;
        }

        AppLogger.Warning("ConfigService.Load", "Primary config corrupted, trying backup");

        if (File.Exists(_configBakPath) && TryLoad(_configBakPath, out loaded))
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
            string json;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
                json = reader.ReadToEnd();
            config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
            return config != null;
        }
        catch (Exception ex)
        {
            AppLogger.Error("ConfigService.TryLoad", ex);
            SessionErrorTracker.Record(ErrorCode.ConfigLoadFailed, "Config Load Failed",
                $"Failed to read {Path.GetFileName(path)}: {ex.Message}");
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
                Directory.CreateDirectory(_configDir);

                if (File.Exists(_configPath))
                    File.Copy(_configPath, _configBakPath, overwrite: true);

                var json = JsonSerializer.Serialize(config, JsonOptions);
                File.WriteAllText(_configTmpPath, json);
                File.Move(_configTmpPath, _configPath, overwrite: true);
            }
            catch (Exception ex)
            {
                AppLogger.Error("ConfigService.Save", ex);
                SessionErrorTracker.Record(ErrorCode.ConfigSaveFailed, "Config Save Failed", ex.Message);
            }
        }
    }
}
