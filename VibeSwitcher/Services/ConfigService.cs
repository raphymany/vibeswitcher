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
    // Monotonic version stamped on every save request. A background (deferred) write that finishes
    // out of order is skipped if a newer version already reached disk, so the latest snapshot always
    // wins regardless of thread-pool scheduling.
    private long _saveVersion;
    private long _lastWrittenVersion;

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

        if (config.LogoAnimation is not ("Full" or "Reduced" or "Static"))
            config.LogoAnimation = "Full";
    }

    // Fully synchronous: serialize + write on the calling thread. Used by startup, import, the exit
    // flush, and tests that need the file on disk before returning.
    public void SaveImmediate()
    {
        if (TrySerialize(out var json))
            WriteJson(json, Interlocked.Increment(ref _saveVersion));
    }

    // UI-thread callers: serialize a consistent snapshot on the CURRENT thread (where the model
    // isn't being mutated concurrently), then write the bytes on a background thread. This avoids
    // "collection was modified" races from serializing the live object graph on a pool thread,
    // which previously caused saves to be silently skipped.
    public void SaveDeferred()
    {
        if (!TrySerialize(out var json)) return;
        var version = Interlocked.Increment(ref _saveVersion);
        _ = Task.Run(() => WriteJson(json, version));
    }

    private bool TrySerialize(out string json)
    {
        try
        {
            json = JsonSerializer.Serialize(_config, JsonOptions);
            return true;
        }
        catch (Exception ex)
        {
            LogSaveError(ex);
            json = "";
            return false;
        }
    }

    // Replaces every setting with its default while preserving the user's data: profiles
    // (incl. their schedules/sounds/triggers), the active profile, and device aliases.
    public void ResetSettingsToDefaults()
    {
        var cur = _config;
        _config = new AppConfig
        {
            Profiles = cur.Profiles,
            ActiveProfileId = cur.ActiveProfileId,
            DeviceAliases = cur.DeviceAliases,
            CompactIntroShown = cur.CompactIntroShown,       // don't re-show the first-run intro
            LastSchedulerEvaluation = cur.LastSchedulerEvaluation, // keep catch-up dedup history
        };
        SaveImmediate();
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
            // Require the VibeSwitcher-specific "ConfigVersion" marker. Every config the app writes
            // includes it; keying off it (rather than the generic "Profiles") rejects unrelated JSON
            // files that merely happen to carry a "profiles" property, which would otherwise overwrite
            // the user's real config on import.
            foreach (var prop in doc.RootElement.EnumerateObject())
                if (string.Equals(prop.Name, "ConfigVersion", StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
        catch
        {
            return false;
        }
    }

    private void WriteJson(string json, long version)
    {
        lock (_saveLock)
        {
            // Skip a stale write: a newer save already reached disk while this one was queued.
            if (version < _lastWrittenVersion) return;
            try
            {
                Directory.CreateDirectory(_configDir);

                if (File.Exists(_configPath))
                    File.Copy(_configPath, _configBakPath, overwrite: true);

                // Delete any pre-existing temp file first so the write always lands in a fresh regular
                // file — if something planted a symlink at this predictable path, WriteAllText would
                // otherwise follow it and redirect the write. Deleting removes the link, not its target.
                if (File.Exists(_configTmpPath)) File.Delete(_configTmpPath);
                File.WriteAllText(_configTmpPath, json);
                File.Move(_configTmpPath, _configPath, overwrite: true);
                _lastWrittenVersion = version;
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
