using Microsoft.Win32;
using VibeSwitcher.Helpers;

namespace VibeSwitcher.Services;

public class StartupService : IStartupService
{
    private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "VibeSwitcher";

    private readonly IAppLogger _logger;
    private readonly ISessionErrorTracker _errorTracker;

    public StartupService(IAppLogger logger, ISessionErrorTracker errorTracker)
    {
        _logger = logger;
        _errorTracker = errorTracker;
    }

    public bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
            return key?.GetValue(ValueName) != null;
        }
        catch (Exception ex)
        {
            _logger.Error("StartupService.IsStartupEnabled", ex);
            _errorTracker.Record(ErrorCode.StartupRegistryReadFailed, "Startup Registry Read Failed",
                $"Could not read the startup registry key: {ex.Message}");
            return false;
        }
    }

    public void Enable()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
            {
                _logger.Error("StartupService.Enable", "Could not resolve executable path");
                _errorTracker.Record(ErrorCode.StartupPathResolutionFailed, "Startup Path Unavailable",
                    "Could not determine the application path — 'Start with Windows' was not enabled.");
                return;
            }

            using var key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath, writable: true);
            key.SetValue(ValueName, $"\"{exePath}\"");
        }
        catch (Exception ex)
        {
            _logger.Error("StartupService.Enable", ex);
            _errorTracker.Record(ErrorCode.StartupRegistryFailed, "Startup Registry Failed",
                $"Could not enable start with Windows: {ex.Message}");
        }
    }

    public void Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            _logger.Error("StartupService.Disable", ex);
            _errorTracker.Record(ErrorCode.StartupRegistryFailed, "Startup Registry Failed",
                $"Could not disable start with Windows: {ex.Message}");
        }
    }

    // Called on every app launch. If the startup entry exists but points to the old path
    // (e.g. user moved VibeSwitcher.exe), silently update it to the current location.
    public void RefreshRegistryPath()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
            if (key?.GetValue(ValueName) is not string stored) return;

            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return;

            var expected = $"\"{exePath}\"";
            if (!string.Equals(stored, expected, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Info("StartupService.RefreshRegistryPath",
                    $"Startup registry path outdated; updating to current exe location.");
                Enable();
            }
        }
        catch (Exception ex)
        {
            _logger.Warning("StartupService.RefreshRegistryPath", ex.Message);
            _errorTracker.Record(ErrorCode.StartupRegistryFailed, "Startup Registry Failed",
                $"Could not refresh the startup registry path: {ex.Message}");
        }
    }
}
