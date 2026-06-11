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
    private readonly IAppLogger _logger;
    private readonly ISessionErrorTracker _errorTracker;

    public string IconsDir { get; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private volatile AppConfig _config = new();
    private readonly object _saveLock = new();

    public ConfigService(IAppLogger logger, ISessionErrorTracker errorTracker, string? baseDir = null)
    {
        _logger = logger;
        _errorTracker = errorTracker;
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
            _logger.Error("ConfigService.Load", ex);
            _errorTracker.Record(ErrorCode.ConfigDirCreateFailed, "Config Directory Error",
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

        _logger.Warning("ConfigService.Load", "Primary config corrupted, trying backup");

        if (File.Exists(_configBakPath) && TryLoad(_configBakPath, out loaded))
        {
            _config = loaded!;
            _config.Profiles ??= new();
            Migrate(_config);
            _logger.Info("ConfigService.Load", "Recovered from backup config");
            return;
        }

        IsFirstRun = true;
        _config = new AppConfig();
        _logger.Warning("ConfigService.Load", "Both config and backup failed — starting fresh");
    }

    private bool TryLoad(string path, out AppConfig? config)
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
            _logger.Error("ConfigService.TryLoad", ex);
            _errorTracker.Record(ErrorCode.ConfigLoadFailed, "Config Load Failed",
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

        // Clamp/validate values that come straight from JSON, so a hand-edited or imported
        // config with out-of-range numbers can't produce a broken enum or a schedule that
        // silently never fires.
        foreach (var profile in config.Profiles)
        {
            if (!Enum.IsDefined(typeof(ProfileMode), profile.Mode))
                profile.Mode = ProfileMode.Both;

            profile.Schedules ??= new();
            foreach (var s in profile.Schedules)
            {
                s.Hour   = Math.Clamp(s.Hour, 0, 23);
                s.Minute = Math.Clamp(s.Minute, 0, 59);
                s.ReminderMinutes = Math.Clamp(s.ReminderMinutes, 0, 24 * 60 - 1);
            }
        }

        // A dangling ActiveProfileId (e.g. from an imported config) is reset so the tray
        // doesn't show a stale/wrong state.
        if (config.ActiveProfileId.HasValue &&
            !config.Profiles.Any(p => p.Id == config.ActiveProfileId.Value))
            config.ActiveProfileId = null;
    }

    // Fully synchronous: serialize + write on the calling thread. Used by startup, import,
    // and tests that need the file on disk before returning.
    public void SaveImmediate()
    {
        Save(_config);
    }

    // UI-thread callers: serialize a consistent snapshot on the CURRENT thread (where the model
    // isn't being mutated concurrently), then write the bytes on a background thread. This avoids
    // "collection was modified" races from serializing the live object graph on a pool thread,
    // which previously caused saves to be silently skipped.
    public void SaveDeferred()
    {
        string json;
        try
        {
            json = JsonSerializer.Serialize(_config, JsonOptions);
        }
        catch (Exception ex)
        {
            LogSaveError(ex);
            return;
        }
        _ = Task.Run(() => WriteJson(json));
    }

    public void ExportTo(string destinationPath)
    {
        lock (_saveLock)
        {
            try
            {
                var json = JsonSerializer.Serialize(_config, JsonOptions);
                File.WriteAllText(destinationPath, json);
            }
            catch (Exception ex)
            {
                _logger.Error("ConfigService.ExportTo", ex);
                _errorTracker.Record(ErrorCode.ConfigSaveFailed, "Export Failed", ex.Message);
                throw;
            }
        }
    }

    public bool TryImport(string sourcePath, out string? error)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            error = "No file path was provided.";
            return false;
        }
        if (!LooksLikeVibeSwitcherConfig(sourcePath))
        {
            error = "This file isn't a VibeSwitcher configuration.";
            return false;
        }
        if (!TryLoad(sourcePath, out var config) || config == null)
        {
            error = "The file could not be read or is not a valid VibeSwitcher configuration.";
            return false;
        }
        config.Profiles ??= new();
        Migrate(config);
        _config = config;
        SaveImmediate();
        error = null;
        return true;
    }

    // Guards against importing arbitrary well-formed JSON (e.g. {} or an unrelated file), which
    // would otherwise deserialize to a defaults-only AppConfig and silently wipe the user's setup.
    // Requires a JSON object carrying at least one recognizable VibeSwitcher marker.
    private bool LooksLikeVibeSwitcherConfig(string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
            foreach (var marker in new[] { "ConfigVersion", "Profiles" })
            {
                foreach (var prop in doc.RootElement.EnumerateObject())
                    if (string.Equals(prop.Name, marker, StringComparison.OrdinalIgnoreCase))
                        return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    public void Save(AppConfig config)
    {
        string json;
        try
        {
            json = JsonSerializer.Serialize(config, JsonOptions);
        }
        catch (Exception ex)
        {
            LogSaveError(ex);
            return;
        }
        WriteJson(json);
    }

    private void WriteJson(string json)
    {
        lock (_saveLock)
        {
            try
            {
                Directory.CreateDirectory(_configDir);

                if (File.Exists(_configPath))
                    File.Copy(_configPath, _configBakPath, overwrite: true);

                File.WriteAllText(_configTmpPath, json);
                File.Move(_configTmpPath, _configPath, overwrite: true);
            }
            catch (Exception ex)
            {
                LogSaveError(ex);
            }
        }
    }

    private void LogSaveError(Exception ex)
    {
        _logger.Error("ConfigService.Save", ex);
        _errorTracker.Record(ErrorCode.ConfigSaveFailed, "Config Save Failed", ex.Message);
    }
}
