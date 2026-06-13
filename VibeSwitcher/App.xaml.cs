using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using VibeSwitcher.Helpers;
using VibeSwitcher.Models;
using VibeSwitcher.NativeMethods;
using VibeSwitcher.Services;
using VibeSwitcher.Tray;

namespace VibeSwitcher;

public partial class App : Application
{
    private readonly SingleInstanceHelper _singleInstance = new();
    private IAppLogger? _logger;
    private ISessionErrorTracker? _errorTracker;
    private IConfigService? _configService;
    private IAudioService? _audioService;
    private IHotkeyService? _hotkeyService;
    private TrayService? _trayService;
    private HwndSource? _hwndSource;
    private ProfileSwitchOrchestrator? _orchestrator;
    private AppWindowManager? _windowManager;
    private ThemeService? _themeService;
    private SchedulerService? _schedulerService;
    private MuteService? _muteService;
    private DeviceTriggerService? _deviceTriggerService;
    private HidHeadsetService? _hidHeadsetService;
    private AppWatcherService? _appWatcherService;
    private AppTriggerService? _appTriggerService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _logger = new AppLogger();
        _errorTracker = new SessionErrorTracker();
        AppLog.Register(_logger);
        AppErrors.Register(_errorTracker);

        // Give the process a stable identity before any UI/notification — fixes taskbar grouping and
        // makes Windows resolve toast/notification icon attribution to our icon (not a stale cache).
        try
        {
            int hr = WinApi.SetCurrentProcessExplicitAppUserModelID("RaphaelMansour.VibeSwitcher");
            if (hr != 0)
                _logger.Warning("App.OnStartup",
                    $"SetCurrentProcessExplicitAppUserModelID returned 0x{hr:X8} — taskbar grouping / toast attribution may be affected.");
        }
        catch (Exception ex)
        {
            _logger.Warning("App.OnStartup", $"SetCurrentProcessExplicitAppUserModelID failed: {ex.Message}");
        }

        // Register the AUMID's display name + icon so toast attribution shows "VibeSwitcher" + our
        // icon instead of the raw AUMID string (the dev build has no Start Menu shortcut to resolve it).
        NotificationIdentity.Register(_logger);

        // Last-resort handler for exceptions that escape all other catch blocks on the UI thread.
        // Marking Handled=true keeps the app alive for recoverable cases (e.g. a bad tray click).
        // Truly unexpected exceptions are still logged so they appear in the error log.
        DispatcherUnhandledException += (_, args) =>
        {
            _logger.Error("DispatcherUnhandledException", args.Exception);
            args.Handled = true;
        };

        // Logs fatal exceptions on non-UI threads. Cannot prevent process termination in .NET Core.
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                _logger.Error("UnhandledException", ex);
        };

        // Catches exceptions from fire-and-forget Tasks that were never awaited.
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _logger.Error("UnobservedTaskException", args.Exception);
            args.SetObserved();
        };

        // 1. Single-instance guard — if another instance signals us (e.g. taskbar pin click),
        //    show the settings window. The callback fires on a background thread, so marshal
        //    back to the UI dispatcher.
        if (!_singleInstance.TryAcquire(() => Dispatcher.InvokeAsync(OpenSettingsWindow)))
        {
            Shutdown();
            return;
        }

        // 2. Load configuration
        _configService = new ConfigService(_logger, _errorTracker);
        _configService.Load();

        // Apply the saved logo-animation preference before any logo control loads.
        Controls.LogoAnimator.Mode = Controls.LogoAnimator.Parse(_configService.Current.LogoAnimation);

        // 2a. Apply theme before any UI is shown
        _themeService = new ThemeService(_configService);
        _themeService.Apply();
        _themeService.StartListening();

        // 2b. Self-correct the startup registry path if the exe was moved since last enable
        new StartupService(_logger, _errorTracker).RefreshRegistryPath();

        // 3. Dedicated message-only HwndSource for WM_HOTKEY (never shown)
        _hwndSource = new HwndSource(new HwndSourceParameters("AudioSwitcherHotkeys")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,        // WS_OVERLAPPED — minimal valid style
            ExtendedWindowStyle = 0x80, // WS_EX_TOOLWINDOW — excluded from taskbar/alt-tab
        });
        _hwndSource.AddHook(WndProc);

        // 4. Initialise services
        _audioService = new AudioService(_logger, _errorTracker);
        _hotkeyService = new HotkeyService(_hwndSource.Handle, _logger, _errorTracker);
        _trayService = new TrayService(_configService, _logger, _errorTracker);
        _themeService.ThemeApplied += () => _trayService.RebuildMenu();

        // 5. Initialise orchestrators
        var switchSoundService = new SwitchSoundService(_logger);
        _orchestrator = new ProfileSwitchOrchestrator(_configService, _audioService, _trayService, switchSoundService, Dispatcher, _logger, _errorTracker);
        _muteService = new MuteService(_logger);
        // Show a brief banner when a mute hotkey toggles, unless that shortcut is set to silent.
        _muteService.MuteToggled += (scope, muted) =>
        {
            var cfg = _configService!.Current;
            bool silent = scope switch
            {
                Models.MuteScope.Mic      => cfg.MuteMicSilent,
                Models.MuteScope.Speakers => cfg.MuteSpeakersSilent,
                _                         => cfg.MuteBothSilent,
            };
            if (silent) return;
            string what = scope switch
            {
                Models.MuteScope.Mic      => "Microphone",
                Models.MuteScope.Speakers => "Speakers",
                _                         => "Mic + Speakers",
            };
            _trayService!.ShowBalloon("VibeSwitcher", $"{what} {(muted ? "muted" : "unmuted")}", sound: false);
        };
        _windowManager = new AppWindowManager(_configService, _audioService, _hotkeyService, _trayService,
            _logger, _errorTracker, _themeService.Apply,
            switchProfile: profile => _orchestrator.SwitchToProfile(profile),
            onReschedule: () => _schedulerService?.Reschedule(),
            onAppTriggersChanged: () => _appTriggerService?.RefreshWatchList());

        // Wire tray-menu profile clicks through the orchestrator so there is a single switch path.
        _trayService.SwitchRequested = p => _orchestrator.SwitchToProfile(p);
        // Keep the settings window's active-profile indicators in sync with background switches.
        _orchestrator.ProfileSwitched += () => _windowManager!.NotifyProfileSwitched();

        // 6. Register hotkeys
        RegisterHotkeys();
        RegisterSettingsHotkey();
        RegisterCompactHotkey();
        RegisterMuteHotkeys();

        // 7. Restore last active profile via the orchestrator so the single switch path is always used.
        // Guard: if ActiveProfileId is set but no matching profile exists (e.g. profile was deleted
        // outside the app), reset it so the tray icon does not show a stale or wrong state.
        if (_configService.Current.ActiveProfileId.HasValue &&
            !_configService.Current.Profiles.Any(p => p.Id == _configService.Current.ActiveProfileId))
        {
            _logger.Warning("App.OnStartup", "ActiveProfileId in config does not match any profile — resetting.");
            _configService.Current.ActiveProfileId = null;
            _configService.SaveImmediate();
        }

        var activeProfile = _configService.Current.Profiles
            .FirstOrDefault(p => p.Id == _configService.Current.ActiveProfileId);

        // 8. Profile scheduler — created and evaluated BEFORE the restore so a schedule that was
        //    due while the app wasn't running takes precedence over re-applying the last profile
        //    (otherwise the restore would hold the switch lock and the due schedule would be dropped).
        _schedulerService = new SchedulerService(
            _configService,
            (profile, silent) => _orchestrator.SwitchToProfile(profile, silent),
            (title, msg) => _trayService!.ShowBalloon(title, msg));
        bool scheduleFired = _schedulerService.EvaluateNow();

        // 9. Restore last active profile only if no due schedule already switched us.
        if (!scheduleFired && activeProfile != null)
            _orchestrator.SwitchToProfile(activeProfile);

        // 10. Refresh tray. When a schedule fired, that switch updates the icon itself — setting the
        //     old active profile's icon here would briefly flash the wrong icon, so skip it.
        if (!scheduleFired)
            _trayService.UpdateIcon(activeProfile);
        _trayService.RebuildMenu();

        // 11. On wake from sleep/hibernate, re-evaluate schedules and re-apply the active profile.
        //     A single handler keeps the catch-up evaluation and the restore from racing the switch lock.
        SystemEvents.PowerModeChanged += OnSystemResume;

        // 12. Device trigger — auto-switch when a profile's device is connected
        _deviceTriggerService = new DeviceTriggerService(
            _audioService,
            _configService,
            profile => _orchestrator.SwitchToProfile(profile),
            _logger);

        // 13. HID headset monitor — detects wireless power-off for supported headsets
        //     and triggers the revert that the audio API can't detect on its own.
        _hidHeadsetService = new HidHeadsetService(_logger);
        _hidHeadsetService.DeviceMonitoringStarted +=
            d => _deviceTriggerService.RegisterHidDescriptor(d);
        _hidHeadsetService.WirelessConnected    +=
            d => _deviceTriggerService.OnHidWirelessConnected(d);
        _hidHeadsetService.WirelessDisconnected +=
            d => _deviceTriggerService.OnHidWirelessDisconnected(d);
        _hidHeadsetService.Start();

        // 14. App launch trigger — switches profile when a linked executable launches
        _appWatcherService = new AppWatcherService(_logger);
        _appTriggerService = new AppTriggerService(
            _configService,
            _appWatcherService,
            profile => _orchestrator.SwitchToProfile(profile),
            _logger);

        // 15. Show splash screen, then open settings if not start-minimized
        var splash = new Views.SplashWindow();
        splash.AnimationComplete += (_, _) =>
        {
            // The tray icon appears only now — once the app is fully ready — so a
            // click can never land on a half-started app.
            _trayService!.ShowIcon();
            if (!_configService!.IsFirstRun && _configService.Current.StartMinimized) return;
            // If the user is in the tray menu when the startup-open fires, opening
            // (and activating) the window would dismiss their menu — wait it out.
            _trayService.RunWhenContextMenuClosed(OpenSettingsWindow);
        };
        splash.Show();

        // Safety net: if the splash animation is interrupted (closed early, storyboard fault),
        // AnimationComplete may never fire — guarantee the tray icon still appears so the app
        // can't end up headless. ShowIcon is idempotent.
        var iconFallback = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        iconFallback.Tick += (_, _) =>
        {
            iconFallback.Stop();
            _trayService?.ShowIcon();
        };
        iconFallback.Start();
    }

    // Wake handler: SystemEvents.PowerModeChanged fires on a background thread, so marshal to the UI
    // thread before touching the scheduler/switch path. Evaluate schedules first (a slot may have come
    // due during sleep); only re-apply the last active profile if none fired — mirroring startup so the
    // two paths never race the single switch lock.
    private void OnSystemResume(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume) return;
        Dispatcher.InvokeAsync(() =>
        {
            bool fired = _schedulerService?.EvaluateNow() ?? false;
            if (fired) return;
            var active = _configService?.Current.Profiles
                .FirstOrDefault(p => p.Id == _configService.Current.ActiveProfileId);
            if (active != null) _orchestrator?.SwitchToProfile(active);
        });
    }

    private void RegisterSettingsHotkey()
    {
        if (!_configService!.Current.SettingsHotkeyEnabled) return;
        var hotkey = _configService.Current.SettingsHotkey;
        if (hotkey == null || hotkey.IsEmpty) return;
        var conflict = _hotkeyService!.RegisterSettingsHotkey(hotkey);
        if (conflict != null)
        {
            _errorTracker!.Record(ErrorCode.HotkeyConflict, "Hotkey Conflict",
                $"Could not register Settings hotkey '{conflict.Hotkey.ToDisplayString()}' — another app is using it.");
            _trayService!.ShowBalloon("Hotkey Conflict",
                $"Settings hotkey '{conflict.Hotkey.ToDisplayString()}' is in use by another app.");
        }
    }

    private void RegisterCompactHotkey()
    {
        if (!_configService!.Current.CompactHotkeyEnabled) return;
        var hotkey = _configService.Current.CompactHotkey;
        if (hotkey == null || hotkey.IsEmpty) return;
        var conflict = _hotkeyService!.RegisterCompactHotkey(hotkey);
        if (conflict != null)
        {
            _errorTracker!.Record(ErrorCode.HotkeyConflict, "Hotkey Conflict",
                $"Could not register Mini Mode hotkey '{conflict.Hotkey.ToDisplayString()}' — another app is using it.");
            _trayService!.ShowBalloon("Hotkey Conflict",
                $"Mini Mode hotkey '{conflict.Hotkey.ToDisplayString()}' is in use by another app.");
        }
    }

    private void RegisterMuteHotkeys()
    {
        var cfg = _configService!.Current;
        RegisterOneMuteHotkey(Models.MuteScope.Mic, cfg.MuteMicHotkey, cfg.MuteMicHotkeyEnabled);
        RegisterOneMuteHotkey(Models.MuteScope.Speakers, cfg.MuteSpeakersHotkey, cfg.MuteSpeakersHotkeyEnabled);
        RegisterOneMuteHotkey(Models.MuteScope.Both, cfg.MuteBothHotkey, cfg.MuteBothHotkeyEnabled);
    }

    private void RegisterOneMuteHotkey(Models.MuteScope scope, HotkeyDefinition? hotkey, bool enabled)
    {
        if (!enabled || hotkey == null || hotkey.IsEmpty) return;
        var conflict = _hotkeyService!.RegisterMuteHotkey(scope, hotkey);
        if (conflict != null)
        {
            _errorTracker!.Record(ErrorCode.HotkeyConflict, "Hotkey Conflict",
                $"Could not register mute hotkey '{conflict.Hotkey.ToDisplayString()}' — another app is using it.");
            _trayService!.ShowBalloon("Hotkey Conflict",
                $"Mute hotkey '{conflict.Hotkey.ToDisplayString()}' is in use by another app.");
        }
    }

    private void RegisterHotkeys()
    {
        var conflicts = _hotkeyService!.RegisterAll(_configService!.Current.Profiles);
        foreach (var ex in conflicts)
        {
            _errorTracker!.Record(ErrorCode.HotkeyConflict, "Hotkey Conflict",
                $"Could not register '{ex.Hotkey.ToDisplayString()}' — another app is using it.");
            _trayService!.ShowBalloon(
                "Hotkey Conflict",
                $"Could not register '{ex.Hotkey.ToDisplayString()}' — another app is using it.");
        }
    }

    private static readonly int WM_TASKBARCREATED = WinApi.RegisterWindowMessage("TaskbarCreated");

    // Debounce per-atom to suppress WM_HOTKEY auto-repeat when a key is held down.
    private readonly Dictionary<ushort, long> _hotkeyLastFired = new();
    // 500ms suppresses key auto-repeat while still allowing a deliberate quick re-press
    // (e.g. mute then immediately unmute).
    private const long HotkeyDebounceTicks = 500 * TimeSpan.TicksPerMillisecond;

    private bool ShouldHandleHotkey(ushort atomId)
    {
        var now = DateTime.UtcNow.Ticks;
        if (_hotkeyLastFired.TryGetValue(atomId, out var last) && now - last < HotkeyDebounceTicks)
            return false;
        _hotkeyLastFired[atomId] = now;
        return true;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WinApi.WM_HOTKEY)
        {
            ushort atomId = (ushort)wParam.ToInt32();
            if (!ShouldHandleHotkey(atomId))
            {
                handled = true;
                return IntPtr.Zero;
            }
            if (_hotkeyService!.IsSettingsHotkey(atomId))
            {
                OpenSettingsWindow();
                handled = true;
            }
            else if (_hotkeyService.IsCompactHotkey(atomId))
            {
                ToggleMiniMode();
                handled = true;
            }
            else if (_hotkeyService.IsMuteHotkey(atomId, out var muteScope))
            {
                _muteService!.Toggle(muteScope);
                handled = true;
            }
            else
            {
                var profile = _hotkeyService.HandleHotkey(atomId);
                if (profile != null)
                {
                    _orchestrator!.SwitchToProfile(profile);
                    handled = true;
                }
            }
        }
        else if (msg == WM_TASKBARCREATED)
        {
            // Explorer crashed and restarted — recreate the tray icon
            _trayService?.RecreateIcon();
        }
        return IntPtr.Zero;
    }

    public void OpenSettingsWindow() => _windowManager?.OpenSettingsWindow();

    public void OpenSettingsWindowExpanded() => _windowManager?.OpenSettingsWindow(expandSettings: true);

    public void OpenAboutPanel() => _windowManager?.OpenAbout();
    public void OpenFaqPanel() => _windowManager?.OpenFaq();
    public void ToggleMiniMode() => _windowManager?.ToggleCompactMode();
    public bool IsMiniModeActive => _windowManager?.IsMiniModeActive() ?? false;

    protected override void OnExit(ExitEventArgs e)
    {
        // Flush any pending config synchronously so a SaveDeferred queued just before exit
        // (e.g. a setting toggled then Exit clicked) isn't lost when the process ends.
        try { _configService?.SaveImmediate(); } catch { /* best-effort on exit */ }

        // Safe even if OnStartup exited early (second instance) — unsubscribing an unregistered handler is a no-op.
        SystemEvents.PowerModeChanged -= OnSystemResume;
        _schedulerService?.Dispose();
        _hidHeadsetService?.Dispose();
        _deviceTriggerService?.Dispose();
        _appTriggerService?.Dispose();
        _appWatcherService?.Dispose();
        _themeService?.StopListening();
        _hotkeyService?.UnregisterAll();
        _hwndSource?.RemoveHook(WndProc);
        _hwndSource?.Dispose();
        _trayService?.Dispose();
        _audioService?.Dispose();
        _orchestrator?.Dispose();

        _singleInstance.Dispose();
        base.OnExit(e);
    }
}
