using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using VibeSwitcher.App.Dialogs;
using VibeSwitcher.App.Settings;
using VibeSwitcher.App.Theming;
using VibeSwitcher.Compat;
using VibeSwitcher.Helpers;
using VibeSwitcher.Models;
using VibeSwitcher.Platform;
using VibeSwitcher.Platform.Linux.Hid;
using VibeSwitcher.Services;

namespace VibeSwitcher.App.Hosting;

// The running app — what App.xaml.cs's OnStartup / OnExit do on Windows, in the same order: the log
// and error list, the config, the theme, audio (with re-binding), hotkeys, the tray / menu-bar icon,
// the switch path, mute banners, the Settings window, the scheduler (a due schedule beats restoring
// the last profile), wake from sleep, device auto-switch, wireless headsets, app launch triggers,
// then the splash, after which the icon appears and Settings opens (unless starting minimized).
//
// One copy runs at a time (Program takes the single-instance lock first); later launches and the
// Linux desktop's shortcuts send their command here (HandleRemoteAsync). Everything else runs on the
// UI thread.
internal sealed class AppHost : IDisposable
{
    // The app's host (null while tests run the dialogs on their own, or in the dialog preview).
    public static AppHost? Current { get; set; }

    private readonly IHostPlatform _platform;
    private readonly AppPaths _paths;
    private readonly AppCommand _launchCommand;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _disposed;

    // launchCommand: what this launch was asked to do (Open unless the command line said otherwise).
    public AppHost(IHostPlatform platform, AppPaths paths, AppCommand launchCommand)
    {
        _platform = platform;
        _paths = paths;
        _launchCommand = launchCommand;
    }

    // The services (internal for tests).
    internal AppLogger Logger { get; private set; } = null!;
    internal SessionErrorTracker Errors { get; private set; } = null!;
    internal ConfigService Config { get; private set; } = null!;
    internal ThemeService Theme { get; private set; } = null!;
    internal IStartupService Startup { get; private set; } = null!;
    internal IPlatformAudioService Audio { get; private set; } = null!;
    internal DeviceRebindService Rebind { get; private set; } = null!;
    internal IGlobalKeyGrabber Grabber { get; private set; } = null!;
    internal HotkeyService Hotkeys { get; private set; } = null!;
    internal IDesktopNotifier? Notifier { get; private set; }
    internal TrayService Tray { get; private set; } = null!;
    internal ProfileSwitchOrchestrator Orchestrator { get; private set; } = null!;
    internal MuteService Mute { get; private set; } = null!;
    internal AppWindowManager Windows { get; private set; } = null!;
    internal SchedulerService Scheduler { get; private set; } = null!;
    internal ResumeDetector Resume { get; private set; } = null!;
    internal DeviceTriggerService DeviceTriggers { get; private set; } = null!;
    internal HidHeadsetService Headsets { get; private set; } = null!;
    internal AppWatcherService AppWatcher { get; private set; } = null!;
    internal AppTriggerService AppTriggers { get; private set; } = null!;

    internal Task Ready => _ready.Task;
    internal bool TrayAvailable { get; private set; } = true;
    // Whether there's a tray, once it's settled (true at once when there is one).
    internal Task<bool> TrayKnown { get; private set; } = Task.FromResult(true);
    internal static TimeSpan TrayRecheck { get; set; } = TimeSpan.FromSeconds(2);
    internal const int TrayRechecks = 10;

    private async Task<bool> WatchForTrayAsync()
    {
        for (int i = 0; i < TrayRechecks && !_disposed; i++)
        {
            await Task.Delay(TrayRecheck);
            if (await _platform.IsTrayAvailableAsync() == true)
            {
                TrayAvailable = true;
                if (Windows?.Window is { } window) window.TrayAvailable = true;
                if (Windows != null) Windows.CanHide = true;
                return true;
            }
        }
        if (!_disposed) Logger.Warning("App.Start", "This desktop shows no tray icons: closing Settings quits VibeSwitcher.");
        return false;
    }
    internal string LogPath => Path.Combine(_paths.LogDir, Path.GetFileName(AppLogger.LogPath));

    // Errors that escape everything else are logged and the app keeps running (Windows: the three
    // last-resort handlers in OnStartup). Not for tests: they'd outlive the test.
    public void InstallCrashLogging()
    {
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            Logger?.Error("DispatcherUnhandledException", e.Exception);
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) Logger?.Error("UnhandledException", ex);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            // (Avalonia offers its windows' menus to the desktop's global menu bar, which most Linux
            // desktops don't have: that call's "no such service" ends up here on every launch.)
            if (e.Exception.InnerExceptions.All(IsMissingDesktopService))
                Logger?.Debug("UnobservedTaskException", e.Exception.InnerException?.Message ?? e.Exception.Message);
            else
                Logger?.Error("UnobservedTaskException", e.Exception);
            e.SetObserved();
        };
    }

    private static bool IsMissingDesktopService(Exception ex) =>
        ex.Message.Contains("org.freedesktop.DBus.Error.ServiceUnknown", StringComparison.Ordinal);

    // showSplash: false shows the icon (and Settings) straight away (tests). A start-up that fails also
    // fails the commands other launches are waiting to send.
    public async Task StartAsync(bool showSplash = true)
    {
        try
        {
            await StartCoreAsync(showSplash);
        }
        catch (Exception)
        {
            _ready.TrySetException(new InvalidOperationException("VibeSwitcher couldn't start."));
            throw;
        }
    }

    private async Task StartCoreAsync(bool showSplash)
    {
        Dispatcher.UIThread.VerifyAccess();

        // 1. The log (only now: Program has made sure this is the one running copy — a new log empties
        //    the file) and the session's error list.
        Logger = new AppLogger(_paths.LogDir);
        Errors = new SessionErrorTracker();
        AppLog.Register(Logger);
        AppErrors.Register(Errors);
        Logger.Info("App", $"VibeSwitcher {Cli.Version} starting ({Environment.OSVersion}).");

        // 2. The config, the logo animation and the theme (before any window).
        Config = new ConfigService(Logger, Errors, _paths.ConfigDir);
        Config.Load();
        global::VibeSwitcher.Controls.LogoAnimator.Mode = global::VibeSwitcher.Controls.LogoAnimator.Parse(Config.Current.LogoAnimation);
        Theme = new ThemeService(Config);
        Theme.Apply();
        Theme.StartListening();

        // 2b. Start at login points at this copy, if the app moved since it was turned on.
        Startup = _platform.CreateStartup(Logger, Errors);
        Startup.RefreshRegistryPath();

        // 3. Sounds (switch sounds, mute blips) and audio, re-binding profiles whose device came back
        //    under a new ID (before anything else listens for device changes; once now, for anything
        //    that changed while the app wasn't running).
        WavPlayback.Player = _platform.CreateWavPlayer();
        Audio = _platform.CreateAudio(Logger, Errors);
        Rebind = new DeviceRebindService(Audio, Config, Logger);
        Audio.DevicesChanged += () => Dispatcher.UIThread.Post(() => { if (!_disposed) Rebind.Reconcile(); });
        Rebind.DevicesRebound += () => Windows?.NotifyDevicesRebound();
        Rebind.Reconcile();

        // 4. Global shortcuts (Linux may ask the desktop: off the UI thread), the icon and banners.
        Grabber = await _platform.CreateKeyGrabberAsync(Logger);
        Grabber.Pressed += id => Dispatcher.UIThread.Post(() => OnHotkey(id));
        Hotkeys = new HotkeyService(Grabber, Logger, Errors);
        Notifier = _platform.CreateNotifier(Logger);
        Tray = new TrayService(Config, Logger, Errors, Notifier, new TrayActions(
            OpenSettings: () => Windows.OpenSettingsWindow(),
            OpenSettingsExpanded: () => Windows.OpenSettingsWindow(expandSettings: true),
            OpenAbout: () => Windows.OpenAbout(),
            OpenFaq: () => Windows.OpenFaq(),
            ToggleMiniMode: () => { Windows.ToggleCompactMode(); Tray.RefreshMiniMenuItem(); },
            IsMiniModeActive: () => Windows?.IsMiniModeActive() ?? false, // (asked while the menu is first built)
            Quit: Quit), menuBarItem: _platform.CreateMenuBarItem());
        Theme.ThemeApplied += Tray.RebuildMenu;
        // A Linux desktop without a tray (stock GNOME): the icon can't show, so Settings mustn't hide
        // into it — see AfterSplash.
        // (Started at login, the app can come up before the desktop's panel: it's looked for again
        // for a while before deciding there's none — TrayKnown.)
        TrayAvailable = await _platform.IsTrayAvailableAsync() != false;
        TrayKnown = TrayAvailable ? Task.FromResult(true) : WatchForTrayAsync();

        // 5. The switch path, mute banners and the Settings window.
        Orchestrator = new ProfileSwitchOrchestrator(Config, Audio, Tray, new SwitchSoundService(Logger, Config.SoundsDir),
            Logger, Errors, ShowSwitchError, rebindDevices: Rebind.Reconcile);
        Mute = new MuteService(Logger, Audio);
        Mute.MuteToggled += OnMuteToggled;
        Windows = new AppWindowManager(CreateSettingsWindow) { CanHide = TrayAvailable };
        Tray.SwitchRequested = p => Orchestrator.SwitchToProfile(p);
        Orchestrator.ProfileSwitched += () => Windows.NotifyProfileSwitched();

        // 6. Hotkeys.
        RegisterHotkeys();
        RegisterSettingsHotkey();
        RegisterCompactHotkey();
        RegisterMuteHotkeys();

        // 7. A remembered active profile that no longer exists is forgotten.
        if (Config.Current.ActiveProfileId.HasValue && !Config.Current.Profiles.Any(p => p.Id == Config.Current.ActiveProfileId))
        {
            Logger.Warning("App.Start", "ActiveProfileId in config does not match any profile — resetting.");
            Config.Current.ActiveProfileId = null;
            Config.SaveImmediate();
        }
        var activeProfile = Config.Current.Profiles.FirstOrDefault(p => p.Id == Config.Current.ActiveProfileId);

        // 8–10. Schedules first (one that came due while the app wasn't running beats the restore),
        //       then the last profile, then the icon (a fired schedule sets it itself).
        Scheduler = new SchedulerService(Config,
            (profile, silent) => Orchestrator.SwitchToProfile(profile, silent),
            (title, message) => Tray.ShowBalloon(title, message));
        bool scheduleFired = Scheduler.EvaluateNow();
        // (A launch that switches profile — a desktop shortcut while the app wasn't running — replaces the
        // restore: it runs once the app is up.)
        bool launchSwitches = _launchCommand.Kind == AppCommandKind.Switch && FindProfile(_launchCommand.Argument) != null;
        if (!scheduleFired && !launchSwitches && activeProfile != null) Orchestrator.SwitchToProfile(activeProfile);
        if (!scheduleFired) Tray.UpdateIcon(activeProfile);
        Tray.RebuildMenu();

        // 11. Wake from sleep: schedules first, then the active profile again.
        Resume = new ResumeDetector();
        Resume.Resumed += _ => Dispatcher.UIThread.Post(OnResumed);

        // 12–14. Device auto-switch, wireless headsets, app launch triggers.
        DeviceTriggers = new DeviceTriggerService(Audio, Config, profile => Orchestrator.SwitchToProfile(profile), Logger);
        Headsets = new HidHeadsetService(Logger);
        Headsets.DeviceMonitoringStarted += d => DeviceTriggers.RegisterHidDescriptor(d);
        Headsets.WirelessConnected += d => DeviceTriggers.OnHidWirelessConnected(d);
        Headsets.WirelessDisconnected += d => DeviceTriggers.OnHidWirelessDisconnected(d);
        if (_platform.WatchesHeadsets)
            try { Headsets.Start(); }
            catch (Exception ex) { Logger.Warning("App.Start", $"Wireless headset detection didn't start: {ex.Message}"); }
        AppWatcher = new AppWatcherService(Logger, _platform.CreateProcesses());
        AppTriggers = new AppTriggerService(Config, AppWatcher, profile => Orchestrator.SwitchToProfile(profile), Logger);

        // Started to run a command (a desktop shortcut while the app wasn't running): that now, without
        // waiting for the splash — and instead of opening Settings after it.
        if (_launchCommand.Kind != AppCommandKind.Open) RunLaunchCommand();

        // 15. The splash, then the icon and Settings. (If the splash closes early, never finishes, or
        //     can't show, the same happens anyway: nothing may leave the app running with no way to
        //     reach it.)
        if (!showSplash)
        {
            AfterSplash();
            return;
        }
        var splash = new SplashWindow();
        Splash = splash;
        splash.AnimationComplete += (_, _) => AfterSplash();
        splash.Closed += (_, _) => AfterSplash();
        try { splash.Show(); }
        catch (Exception ex)
        {
            Logger.Warning("App.Start", $"The splash couldn't show: {ex.Message}");
            AfterSplash();
        }
        DispatcherTimer.RunOnce(AfterSplash, TimeSpan.FromSeconds(6));
    }

    internal SplashWindow? Splash { get; private set; }

    private void RunLaunchCommand()
    {
        try { Run(_launchCommand); }
        catch (Exception ex)
        {
            // (e.g. "No profile named 'X'." — said where it can be seen, once the icon is up.)
            Logger.Warning("App.Start", ex.Message);
            Tray.ShowBalloon("VibeSwitcher", ex.Message);
        }
    }

    private bool _afterSplashDone;

    internal void AfterSplash()
    {
        if (_afterSplashDone || _disposed) return;
        _afterSplashDone = true;
        Tray.ShowIcon();
        RunMenuBarCheck();
        OfferHeadsetAccess();
        var opened = _launchCommand.Kind == AppCommandKind.Open && (Config.IsFirstRun || !Config.Current.StartMinimized);
        if (opened) Windows.OpenSettingsWindow();
        // With no tray (still none once the desktop has settled), Settings is the only way in: it
        // opens, whatever started the app — and says why, once.
        if (!TrayAvailable) _ = WhenNoTrayAsync();
        // Only now do other launches' commands run: one arriving during the splash ("open Settings")
        // would otherwise be undone by the opening above, which hides a window that's in front.
        _ready.TrySetResult();
    }

    // CI on a real Mac (VS_MENUBAR_CHECK=<file>): a press on the menu-bar item, as the user's, then
    // whether the styled menu opened and where, written to the file — and the app quits.
    private void RunMenuBarCheck()
    {
        if (Environment.GetEnvironmentVariable("VS_MENUBAR_CHECK") is not { Length: > 0 } file) return;
        DispatcherTimer.RunOnce(() =>
        {
            string result;
            try
            {
                Tray.PressMenuBarItem();
                result = !Tray.HasMenuBarItem ? "no menu-bar item"
                    : Tray.Styled?.OpenMenu != null ? $"menu opened at {Tray.MenuBarAnchor}"
                    : "menu not opened";
            }
            catch (Exception ex) { result = $"failed: {ex.Message}"; }
            DispatcherTimer.RunOnce(() =>
            {
                if (result.StartsWith("menu opened") && Tray.Styled?.OpenMenu == null) result = "menu closed by itself";
                try { File.WriteAllText(file, result); } catch (Exception) { }
                Quit();
            }, TimeSpan.FromSeconds(1));
        }, TimeSpan.FromSeconds(2));
    }

    internal string NoTrayMarker => Path.Combine(_paths.ConfigDir, "no-tray-explained");

    private async Task WhenNoTrayAsync()
    {
        if (await TrayKnown || _disposed) return;
        Windows.ShowSettingsWindow();
        ExplainNoTray();
    }

    private void ExplainNoTray()
    {
        if (File.Exists(NoTrayMarker)) return;
        try { File.WriteAllText(NoTrayMarker, ""); }
        catch (Exception ex) { Logger.Warning("App.NoTray", ex.Message); }
        _ = new DialogService(() => Windows.Window is { IsVisible: true } w ? w : null).ShowAlertAsync("No Tray on This Desktop",
            "This desktop doesn't show tray icons, so VibeSwitcher's icon can't appear. On GNOME, install the \"AppIndicator and KStatusNotifierItem Support\" extension and log in again (Ubuntu has it already).\n\n"
            + "Until then, closing this window quits VibeSwitcher — start it again to come back. Your hotkeys, schedules and auto-switching work while it's open.");
    }

    // Linux: when a profile switches on a headset's power and its receiver can't be read, explain it
    // once (the same dialog turning auto-switch on offers it again). Remembered in a file beside the
    // config — the config itself is shared with the Windows app.
    internal string HeadsetOfferMarker => Path.Combine(_paths.ConfigDir, "headset-access-offered");

    private void OfferHeadsetAccess()
    {
        if (_platform.HeadsetAccess() is not { } access || !Config.Current.Profiles.Any(p => p.TriggerOnConnect)
            || File.Exists(HeadsetOfferMarker)) return;
        _ = Task.Run(access.Check).ContinueWith(t => Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || !t.IsCompletedSuccessfully) return;
            if (t.Result is not (HeadsetAccess.NeedsRule or HeadsetAccess.NeedsReconnect)) return;
            try { File.WriteAllText(HeadsetOfferMarker, ""); }
            catch (Exception ex) { Logger.Warning("App.HeadsetAccess", ex.Message); }
            _ = new DialogService(() => Windows.Window is { IsVisible: true } w ? w : null)
                .ShowAsync(new HeadsetAccessDialog(t.Result, access.Install, access.Check));
        }), TaskScheduler.Default);
    }

    private SettingsWindow CreateSettingsWindow() =>
        new SettingsWindow(Config, Audio, Hotkeys, Startup, Logger, Errors, Theme.Apply,
            onProfilesChanged: () =>
            {
                Tray.ClearIconCache();
                Tray.RebuildMenu();
                var active = Config.Current.Profiles.FirstOrDefault(p => p.Id == Config.Current.ActiveProfileId);
                Tray.UpdateIcon(active);
                Scheduler.Reschedule();
            },
            onHotkeyConflict: ex =>
            {
                Errors.Record(ErrorCode.HotkeyConflict, "Hotkey Conflict",
                    $"Could not register '{ex.Hotkey.ToDisplayString()}' — another app is using it.");
                Tray.ShowBalloon("Hotkey Conflict", $"Could not register '{ex.Hotkey.ToDisplayString()}' — another app is using it.");
            },
            switchProfile: profile => Orchestrator.SwitchToProfile(profile),
            onAppTriggersChanged: () => AppTriggers.RefreshWatchList()) { TrayAvailable = TrayAvailable };

    // The "Profile Switch Failed" dialog, on Settings when it's showing, else on its own.
    private void ShowSwitchError(string message)
    {
        var dialogs = new DialogService(() => Windows.Window is { IsVisible: true } w ? w : null);
        _ = dialogs.ShowAsync(new ErrorDialog(ErrorCode.ProfileSwitchFailed, "Profile Switch Failed", message, LogPath, Logger, Errors));
    }

    private void OnMuteToggled(MuteScope scope, bool muted)
    {
        var cfg = Config.Current;
        bool silent = scope switch
        {
            MuteScope.Mic => cfg.MuteMicSilent,
            MuteScope.Speakers => cfg.MuteSpeakersSilent,
            _ => cfg.MuteBothSilent,
        };
        if (silent) return;
        string what = scope switch
        {
            MuteScope.Mic => "Microphone",
            MuteScope.Speakers => "Speakers",
            _ => "Mic + Speakers",
        };
        Tray.ShowBalloon("VibeSwitcher", $"{what} {(muted ? "muted" : "unmuted")}", sound: false);
    }

    internal void OnResumed()
    {
        if (_disposed) return;
        bool fired = Scheduler.EvaluateNow();
        if (fired) return;
        var active = Config.Current.Profiles.FirstOrDefault(p => p.Id == Config.Current.ActiveProfileId);
        if (active != null) Orchestrator.SwitchToProfile(active);
    }

    // ── Hotkeys (as App.xaml.cs) ───────────────────────────────────────────────────────────────────

    private void RegisterHotkeys()
    {
        foreach (var ex in Hotkeys.RegisterAll(Config.Current.Profiles))
        {
            Errors.Record(ErrorCode.HotkeyConflict, "Hotkey Conflict", $"Could not register '{ex.Hotkey.ToDisplayString()}' — another app is using it.");
            Tray.ShowBalloon("Hotkey Conflict", $"Could not register '{ex.Hotkey.ToDisplayString()}' — another app is using it.");
        }
    }

    private void RegisterSettingsHotkey()
    {
        if (!Config.Current.SettingsHotkeyEnabled || Config.Current.SettingsHotkey is not { IsEmpty: false } hotkey) return;
        if (Hotkeys.RegisterSettingsHotkey(hotkey) is { } conflict)
        {
            Errors.Record(ErrorCode.HotkeyConflict, "Hotkey Conflict",
                $"Could not register Settings hotkey '{conflict.Hotkey.ToDisplayString()}' — another app is using it.");
            Tray.ShowBalloon("Hotkey Conflict", $"Settings hotkey '{conflict.Hotkey.ToDisplayString()}' is in use by another app.");
        }
    }

    private void RegisterCompactHotkey()
    {
        if (!Config.Current.CompactHotkeyEnabled || Config.Current.CompactHotkey is not { IsEmpty: false } hotkey) return;
        if (Hotkeys.RegisterCompactHotkey(hotkey) is { } conflict)
        {
            Errors.Record(ErrorCode.HotkeyConflict, "Hotkey Conflict",
                $"Could not register Mini Mode hotkey '{conflict.Hotkey.ToDisplayString()}' — another app is using it.");
            Tray.ShowBalloon("Hotkey Conflict", $"Mini Mode hotkey '{conflict.Hotkey.ToDisplayString()}' is in use by another app.");
        }
    }

    private void RegisterMuteHotkeys()
    {
        var cfg = Config.Current;
        RegisterOneMuteHotkey(MuteScope.Mic, cfg.MuteMicHotkey, cfg.MuteMicHotkeyEnabled);
        RegisterOneMuteHotkey(MuteScope.Speakers, cfg.MuteSpeakersHotkey, cfg.MuteSpeakersHotkeyEnabled);
        RegisterOneMuteHotkey(MuteScope.Both, cfg.MuteBothHotkey, cfg.MuteBothHotkeyEnabled);
    }

    private void RegisterOneMuteHotkey(MuteScope scope, HotkeyDefinition? hotkey, bool enabled)
    {
        if (!enabled || hotkey == null || hotkey.IsEmpty) return;
        if (Hotkeys.RegisterMuteHotkey(scope, hotkey) is { } conflict)
        {
            Errors.Record(ErrorCode.HotkeyConflict, "Hotkey Conflict",
                $"Could not register mute hotkey '{conflict.Hotkey.ToDisplayString()}' — another app is using it.");
            Tray.ShowBalloon("Hotkey Conflict", $"Mute hotkey '{conflict.Hotkey.ToDisplayString()}' is in use by another app.");
        }
    }

    // A held key repeats: one press per 500 ms counts (a quick deliberate re-press still works).
    private readonly Dictionary<ushort, long> _hotkeyLastFired = new();
    private const long HotkeyDebounceTicks = 500 * TimeSpan.TicksPerMillisecond;

    internal void OnHotkey(ushort id)
    {
        if (_disposed) return;
        var now = DateTime.UtcNow.Ticks;
        if (_hotkeyLastFired.TryGetValue(id, out var last) && now - last < HotkeyDebounceTicks) return;
        _hotkeyLastFired[id] = now;

        if (Hotkeys.IsSettingsHotkey(id)) Windows.OpenSettingsWindow();
        else if (Hotkeys.IsCompactHotkey(id)) { Windows.ToggleCompactMode(); Tray.RefreshMiniMenuItem(); }
        else if (Hotkeys.IsMuteHotkey(id, out var scope)) Mute.Toggle(scope);
        else if (Hotkeys.HandleHotkey(id) is { } profile) Orchestrator.SwitchToProfile(profile);
    }

    // ── Commands from other launches and the Linux desktop's shortcuts ────────────────────────────

    // Called on a background thread by SingleInstance; waits for start-up, then runs on the UI thread.
    // An error's message is sent back to the launch that asked ("error: …").
    public async Task<string> HandleRemoteAsync(AppCommand command)
    {
        await _ready.Task;
        return await Dispatcher.UIThread.InvokeAsync(() => Run(command));
    }

    internal string Run(AppCommand command)
    {
        if (_disposed) throw new InvalidOperationException("VibeSwitcher is quitting.");
        switch (command.Kind)
        {
            case AppCommandKind.Switch:
                var profile = FindProfile(command.Argument)
                    ?? throw new InvalidOperationException($"No profile named '{command.Argument}'.");
                _ = Orchestrator.SwitchAsync(profile, waitTurn: true);
                return $"Switching to {profile.Name}.";
            case AppCommandKind.Mute:
                Mute.Toggle(AppCommand.ParseMuteScope(command.Argument) ?? MuteScope.Mic);
                return "ok";
            case AppCommandKind.MiniMode:
                Windows.ToggleCompactMode();
                Tray.RefreshMiniMenuItem();
                return "ok";
            default:
                // (Windows: a second launch opens Settings — and, like the shortcut, hides it again
                // when it's already in front.)
                Windows.OpenSettingsWindow();
                return "ok";
        }
    }

    // macOS: the app was opened again while running (Finder, Launchpad, Spotlight) — Settings shows,
    // once the app is up.
    public void Reopen()
    {
        if (_disposed || !_ready.Task.IsCompletedSuccessfully) return;
        Windows.ShowSettingsWindow();
    }

    // A profile by name (any case), or by id.
    private DeviceProfile? FindProfile(string? nameOrId)
    {
        if (string.IsNullOrWhiteSpace(nameOrId)) return null;
        var profiles = Config.Current.Profiles;
        return profiles.FirstOrDefault(p => string.Equals(p.Name, nameOrId.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? (Guid.TryParse(nameOrId, out var id) ? profiles.FirstOrDefault(p => p.Id == id) : null);
    }

    // ── Quitting ───────────────────────────────────────────────────────────────────────────────────

    // The menu's Quit: the app's lifetime ends, which disposes the host.
    private void Quit()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.Shutdown();
        else Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Pending config changes are written now (a setting changed just before Quit isn't lost).
        try { Config?.SaveImmediate(); } catch (Exception) { /* best effort on exit */ }
        Scheduler?.Dispose();
        Resume?.Dispose();
        Headsets?.Dispose();
        DeviceTriggers?.Dispose();
        AppTriggers?.Dispose();
        AppWatcher?.Dispose();
        Theme?.StopListening();
        Hotkeys?.UnregisterAll();
        Grabber?.Dispose();
        Tray?.Dispose();
        (Notifier as IDisposable)?.Dispose();
        Audio?.Dispose();
        Orchestrator?.Dispose();
        _ready.TrySetException(new InvalidOperationException("VibeSwitcher is quitting."));
    }
}
