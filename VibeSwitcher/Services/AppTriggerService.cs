using System.IO;
using VibeSwitcher.Helpers;
using VibeSwitcher.Models;

namespace VibeSwitcher.Services;

public sealed class AppTriggerService : IDisposable
{
    private readonly IConfigService _configService;
    private readonly AppWatcherService _watcher;
    private readonly Action<DeviceProfile> _switchCallback;
    private readonly IAppLogger _logger;

    public AppTriggerService(
        IConfigService configService,
        AppWatcherService watcher,
        Action<DeviceProfile> switchCallback,
        IAppLogger logger)
    {
        _configService = configService;
        _watcher = watcher;
        _switchCallback = switchCallback;
        _logger = logger;
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

    // Fires on a ThreadPool thread. Marshal the whole handler to the UI thread so the profile/
    // trigger-list lookup reads the config on the same thread that mutates it (no torn read).
    private void OnProcessLaunched(string exePath)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null)
            dispatcher.InvokeAsync(() => HandleLaunch(exePath));
        else
            HandleLaunch(exePath);
    }

    private void HandleLaunch(string exePath)
    {
        var exeName = Path.GetFileNameWithoutExtension(exePath);

        var profile = _configService.Current.Profiles.FirstOrDefault(p =>
            p.AppTriggers.Any(t =>
                string.Equals(Path.GetFileNameWithoutExtension(t), exeName, StringComparison.OrdinalIgnoreCase)));

        if (profile == null) return;
        if (_configService.Current.ActiveProfileId == profile.Id) return;

        _logger.Info("AppTriggerService", $"'{exeName}' launched — switching to '{profile.Name}'.");
        _switchCallback(profile);
    }

    public void Dispose()
    {
        _watcher.ProcessLaunched -= OnProcessLaunched;
    }
}
