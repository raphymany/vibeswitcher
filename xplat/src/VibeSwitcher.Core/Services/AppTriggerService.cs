using VibeSwitcher.Helpers;
using VibeSwitcher.Models;
using VibeSwitcher.Platform;

namespace VibeSwitcher.Services;

// FORK of VibeSwitcher/Services/AppTriggerService.cs (tracked in xplat/forks.txt). Same behaviour:
// keeps the watcher's list in sync with every profile's AppTriggers, handles a launch on the UI
// thread, and switches to the first profile that lists the app unless it's already active. What
// changed: the watcher reports the trigger itself, so the profile is found by that exact trigger
// first — Windows reduces both sides to a file name, which here would mix up e.g. two Flatpak apps
// from the same publisher — and only then by program name, as Windows does.
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
        var allTriggers = _configService.Current.Profiles
            .SelectMany(p => p.AppTriggers)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _watcher.UpdateWatchList(allTriggers);
    }

    // Fires on a ThreadPool thread. Marshal the whole handler to the UI thread so the profile/
    // trigger-list lookup reads the config on the same thread that mutates it (no torn read).
    private void OnProcessLaunched(string trigger)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null)
            dispatcher.InvokeAsync(() => HandleLaunch(trigger));
        else
            HandleLaunch(trigger);
    }

    internal void HandleLaunch(string trigger)
    {
        var profiles = _configService.Current.Profiles;
        var name = AppTriggerMatch.TriggerName(trigger);
        var profile = profiles.FirstOrDefault(p => p.AppTriggers.Any(t => string.Equals(t, trigger, StringComparison.OrdinalIgnoreCase)))
            ?? (name.Length == 0 ? null : profiles.FirstOrDefault(p =>
                p.AppTriggers.Any(t => string.Equals(AppTriggerMatch.TriggerName(t), name, StringComparison.OrdinalIgnoreCase))));

        if (profile == null) return;
        if (_configService.Current.ActiveProfileId == profile.Id) return;

        _logger.Info("AppTriggerService", $"'{AppTriggerMatch.DisplayName(trigger)}' launched — switching to '{profile.Name}'.");
        _switchCallback(profile);
    }

    public void Dispose()
    {
        _watcher.ProcessLaunched -= OnProcessLaunched;
    }
}
