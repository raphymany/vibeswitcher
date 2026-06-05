using System.IO;
using VibeSwitcher.Helpers;
using VibeSwitcher.Models;

namespace VibeSwitcher.Services;

public sealed class AppTriggerService : IDisposable
{
    private readonly IConfigService _configService;
    private readonly AppWatcherService _watcher;
    private readonly Action<DeviceProfile> _switchCallback;

    public AppTriggerService(
        IConfigService configService,
        AppWatcherService watcher,
        Action<DeviceProfile> switchCallback)
    {
        _configService = configService;
        _watcher = watcher;
        _switchCallback = switchCallback;
        _watcher.ProcessLaunched += OnProcessLaunched;
        RefreshWatchList();
    }

    // Call after any profile's AppTriggers list changes so the watcher stays in sync.
    public void RefreshWatchList()
    {
        var allPaths = _configService.Current.Profiles
            .SelectMany(p => p.AppTriggers)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _watcher.UpdateWatchList(allPaths);
    }

    private void OnProcessLaunched(string exePath)
    {
        var exeName = Path.GetFileNameWithoutExtension(exePath);

        var profile = _configService.Current.Profiles.FirstOrDefault(p =>
            p.AppTriggers.Any(t =>
                string.Equals(Path.GetFileNameWithoutExtension(t), exeName, StringComparison.OrdinalIgnoreCase)));

        if (profile == null) return;
        if (_configService.Current.ActiveProfileId == profile.Id) return;

        AppLogger.Info("AppTriggerService", $"'{exeName}' launched — switching to '{profile.Name}'.");

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null)
            dispatcher.InvokeAsync(() => _switchCallback(profile));
        else
            _switchCallback(profile);
    }

    public void Dispose()
    {
        _watcher.ProcessLaunched -= OnProcessLaunched;
    }
}
