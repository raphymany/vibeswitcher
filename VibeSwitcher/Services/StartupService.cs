using Microsoft.Win32;
using VibeSwitcher.Helpers;

namespace VibeSwitcher.Services;

public class StartupService
{
    private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "VibeSwitcher";

    public bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
        return key?.GetValue(ValueName) != null;
    }

    public void Enable()
    {
        try
        {
            var exePath = Environment.ProcessPath ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
            using var key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath, writable: true);
            key.SetValue(ValueName, $"\"{exePath}\"");
        }
        catch (Exception ex)
        {
            AppLogger.Error("StartupService.Enable", ex);
            SessionErrorTracker.Record(ErrorCode.StartupRegistryFailed, "Startup Registry Failed",
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
            AppLogger.Error("StartupService.Disable", ex);
            SessionErrorTracker.Record(ErrorCode.StartupRegistryFailed, "Startup Registry Failed",
                $"Could not disable start with Windows: {ex.Message}");
        }
    }
}
