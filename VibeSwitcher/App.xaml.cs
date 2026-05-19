using System.IO;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using VibeSwitcher.Helpers;
using VibeSwitcher.NativeMethods;
using VibeSwitcher.Services;
using VibeSwitcher.Tray;

namespace VibeSwitcher;

public partial class App : Application
{
    private readonly SingleInstanceHelper _singleInstance = new();
    private IConfigService? _configService;
    private IAudioService? _audioService;
    private IHotkeyService? _hotkeyService;
    private TrayService? _trayService;
    private HwndSource? _hwndSource;
    private ProfileSwitchOrchestrator? _orchestrator;
    private AppWindowManager? _windowManager;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Portable mode: if portable.txt exists next to the exe, store config and logs there
        // instead of %APPDATA%\VibeSwitcher. Detected once here; null = use default AppData path.
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        string? portableBaseDir = File.Exists(Path.Combine(exeDir, "portable.txt")) ? exeDir : null;
        if (portableBaseDir != null)
            AppLogger.Initialize(portableBaseDir);

        AppLogger.StartSession();

        // Last-resort handler for exceptions that escape all other catch blocks on the UI thread.
        // Marking Handled=true keeps the app alive for recoverable cases (e.g. a bad tray click).
        // Truly unexpected exceptions are still logged so they appear in the error log.
        DispatcherUnhandledException += (_, args) =>
        {
            AppLogger.Error("DispatcherUnhandledException", args.Exception);
            args.Handled = true;
        };

        // Logs fatal exceptions on non-UI threads. Cannot prevent process termination in .NET Core.
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                AppLogger.Error("UnhandledException", ex);
        };

        // Catches exceptions from fire-and-forget Tasks that were never awaited.
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLogger.Error("UnobservedTaskException", args.Exception);
            args.SetObserved();
        };

        // 1. Single-instance guard
        if (!_singleInstance.TryAcquire())
        {
            Shutdown();
            return;
        }

        // 2. Load configuration (portableBaseDir is null in normal mode → uses %APPDATA% default)
        _configService = new ConfigService(portableBaseDir);
        _configService.Load();

        // 2a. First-time portable activation: offer to import existing profiles from %APPDATA%.
        // Only shown when portable mode just activated and no portable config exists yet.
        if (_configService.IsPortable && _configService.IsFirstRun)
            TryOfferPortableImport(portableBaseDir!);

        // 2b. Self-correct the startup registry path if the exe was moved since last enable.
        // Skip in portable mode — the exe path is not stable (e.g. USB drive letter can change).
        if (!_configService.IsPortable)
            new StartupService().RefreshRegistryPath();

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
        _audioService = new AudioService();
        _hotkeyService = new HotkeyService(_hwndSource.Handle);
        _trayService = new TrayService(_configService);

        // 5. Initialise orchestrators
        _orchestrator = new ProfileSwitchOrchestrator(_configService, _audioService, _trayService, Dispatcher);
        _windowManager = new AppWindowManager(_configService, _audioService, _hotkeyService, _trayService);

        // Wire tray-menu profile clicks through the orchestrator so there is a single switch path.
        _trayService.SwitchRequested = _orchestrator.SwitchToProfile;

        // 6. Register hotkeys
        RegisterHotkeys();

        // 7. Restore last active profile via the orchestrator so the single switch path is always used.
        // Guard: if ActiveProfileId is set but no matching profile exists (e.g. profile was deleted
        // outside the app), reset it so the tray icon does not show a stale or wrong state.
        if (_configService.Current.ActiveProfileId.HasValue &&
            !_configService.Current.Profiles.Any(p => p.Id == _configService.Current.ActiveProfileId))
        {
            AppLogger.Warning("App.OnStartup", "ActiveProfileId in config does not match any profile — resetting.");
            _configService.Current.ActiveProfileId = null;
            _configService.SaveImmediate();
        }

        var activeProfile = _configService.Current.Profiles
            .FirstOrDefault(p => p.Id == _configService.Current.ActiveProfileId);
        if (activeProfile != null)
            _orchestrator.SwitchToProfile(activeProfile);

        // 8. Refresh tray
        _trayService.UpdateIcon(activeProfile);
        _trayService.RebuildMenu();

        // 9. Re-apply active profile when the PC wakes from sleep/hibernate
        SystemEvents.PowerModeChanged += _orchestrator.OnPowerModeChanged;

        // 10. Open settings on first run, or if the user has turned off start-minimized
        if (_configService.IsFirstRun || !_configService.Current.StartMinimized)
            OpenSettingsWindow();
    }

    private void TryOfferPortableImport(string portableBaseDir)
    {
        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VibeSwitcher");
        var appDataConfig = Path.Combine(appDataDir, "config.json");

        if (!File.Exists(appDataConfig)) return;

        var result = MessageBox.Show(
            "Portable mode is active.\n\n" +
            "In portable mode, VibeSwitcher stores your profiles and settings right here, " +
            "next to the exe — not in the usual Windows location. This means you can copy " +
            "this folder to a USB drive or another PC and everything travels with it.\n\n" +
            "Existing profiles were found on this PC. What would you like to do?\n\n" +
            "  Yes — import your current profiles and settings into this folder\n" +
            "  No  — start fresh with no profiles",
            "VibeSwitcher — Portable Mode",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information,
            MessageBoxResult.Yes);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            File.Copy(appDataConfig, Path.Combine(portableBaseDir, "config.json"), overwrite: true);

            var srcIcons = Path.Combine(appDataDir, "Icons");
            if (Directory.Exists(srcIcons))
            {
                var dstIcons = Path.Combine(portableBaseDir, "Icons");
                Directory.CreateDirectory(dstIcons);
                foreach (var file in Directory.GetFiles(srcIcons))
                    File.Copy(file, Path.Combine(dstIcons, Path.GetFileName(file)), overwrite: true);
            }

            _configService!.Load();
            AppLogger.Info("App.TryOfferPortableImport", "Profiles imported from AppData to portable location.");
        }
        catch (Exception ex)
        {
            AppLogger.Error("App.TryOfferPortableImport", ex);
            MessageBox.Show(
                $"Import failed: {ex.Message}\n\nThe app will continue with a fresh start.",
                "VibeSwitcher — Import Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void RegisterHotkeys()
    {
        var conflicts = _hotkeyService!.RegisterAll(_configService!.Current.Profiles);
        foreach (var ex in conflicts)
        {
            SessionErrorTracker.Record(ErrorCode.HotkeyConflict, "Hotkey Conflict",
                $"Could not register '{ex.Hotkey.ToDisplayString()}' — another app is using it.");
            _trayService!.ShowBalloon(
                "Hotkey Conflict",
                $"Could not register '{ex.Hotkey.ToDisplayString()}' — another app is using it.");
        }
    }

    private static readonly int WM_TASKBARCREATED = WinApi.RegisterWindowMessage("TaskbarCreated");

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WinApi.WM_HOTKEY)
        {
            ushort atomId = (ushort)wParam.ToInt32();
            var profile = _hotkeyService!.HandleHotkey(atomId);
            if (profile != null)
            {
                _orchestrator!.SwitchToProfile(profile);
                handled = true;
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

    public void OpenAboutWindow() => _windowManager?.OpenAboutWindow();

    protected override void OnExit(ExitEventArgs e)
    {
        // _orchestrator is null when a second instance exits early via Shutdown() before OnStartup completes.
        if (_orchestrator != null)
            SystemEvents.PowerModeChanged -= _orchestrator.OnPowerModeChanged;
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
