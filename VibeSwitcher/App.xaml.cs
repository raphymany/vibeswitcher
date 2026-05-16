using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using VibeSwitcher.Helpers;
using VibeSwitcher.NativeMethods;
using VibeSwitcher.Services;
using VibeSwitcher.Tray;
using VibeSwitcher.Views;
using H.NotifyIcon;
using H.NotifyIcon.Core;

namespace VibeSwitcher;

public partial class App : Application
{
    private readonly SingleInstanceHelper _singleInstance = new();
    private ConfigService? _configService;
    private AudioService? _audioService;
    private HotkeyService? _hotkeyService;
    private TrayService? _trayService;
    private HwndSource? _hwndSource;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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

        // 2. Load configuration
        _configService = new ConfigService();
        _configService.Load();

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
        _trayService = new TrayService(_configService, _audioService, _hotkeyService);

        // 5. Register hotkeys
        RegisterHotkeys();

        // 6. Restore last active profile (fire-and-forget, non-blocking)
        var activeProfile = _configService.Current.Profiles
            .FirstOrDefault(p => p.Id == _configService.Current.ActiveProfileId);
        if (activeProfile != null)
            _ = _audioService.ApplyProfileAsync(activeProfile); // already async, no Task.Run needed

        // 7. Refresh tray
        _trayService.UpdateIcon(activeProfile);
        _trayService.RebuildMenu();

        // 8. Re-apply active profile when the PC wakes from sleep/hibernate
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        // 9. Open settings on first run, or if the user has turned off start-minimized
        if (_configService.IsFirstRun || !_configService.Current.StartMinimized)
            OpenSettingsWindow();
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume) return;
        var activeProfile = _configService!.Current.Profiles
            .FirstOrDefault(p => p.Id == _configService.Current.ActiveProfileId);
        if (activeProfile != null)
            SwitchToProfile(activeProfile);
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
                $"Could not register '{ex.Hotkey.ToDisplayString()}' — another app is using it.",
                NotificationIcon.Warning);
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
                SwitchToProfile(profile);
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

    // async void is intentional: called as fire-and-forget from WndProc and PowerModeChanged.
    // The try/catch ensures exceptions are always handled, so the async void is safe.
    private async void SwitchToProfile(Models.DeviceProfile profile)
    {
        try
        {
            // ApplyProfileAsync already dispatches to an STA background thread internally —
            // no outer Task.Run needed here.
            var result = await _audioService!.ApplyProfileAsync(profile);
            await Dispatcher.InvokeAsync(() =>
            {
                _configService!.Current.ActiveProfileId = profile.Id;
                _configService.SaveImmediate();
                _trayService!.UpdateIcon(profile);
                _trayService.RebuildMenu();

                if (result.MissingPlaybackId != null)
                {
                    var msg = $"Playback device for '{profile.Name}' is disconnected.";
                    AppLogger.Warning("SwitchToProfile", msg);
                    SessionErrorTracker.Record(ErrorCode.PlaybackDeviceUnavailable, "Device Unavailable", msg);
                }
                if (result.MissingRecordingId != null)
                {
                    var msg = $"Recording device for '{profile.Name}' is disconnected.";
                    AppLogger.Warning("SwitchToProfile", msg);
                    SessionErrorTracker.Record(ErrorCode.RecordingDeviceUnavailable, "Device Unavailable", msg);
                }

                if (_configService.Current.ShowNotifications)
                {
                    if (result.MissingPlaybackId == null && result.MissingRecordingId == null)
                    {
                        _trayService.ShowBalloon("VibeSwitcher", $"Switched to {profile.Name}");
                    }
                    else
                    {
                        if (result.MissingPlaybackId != null)
                            _trayService.ShowBalloon("Device Unavailable",
                                $"Playback device for '{profile.Name}' is disconnected.", NotificationIcon.Warning);
                        if (result.MissingRecordingId != null)
                            _trayService.ShowBalloon("Device Unavailable",
                                $"Recording device for '{profile.Name}' is disconnected.", NotificationIcon.Warning);
                    }
                }
            });
        }
        catch (Exception ex)
        {
            AppLogger.Error("SwitchToProfile", ex);
            var detail = ex.InnerException?.Message ?? ex.Message;
            SessionErrorTracker.Record(ErrorCode.ProfileSwitchFailed, "Profile Switch Failed",
                $"Could not switch to '{profile.Name}': {detail}");
            await Dispatcher.InvokeAsync(() =>
                new ErrorDialog(ErrorCode.ProfileSwitchFailed, "Profile Switch Failed",
                    $"Could not switch to '{profile.Name}': {detail}").ShowDialog());
        }
    }

    public void OpenSettingsWindow()
    {
        var existing = Windows.OfType<SettingsWindow>().FirstOrDefault();
        if (existing != null)
        {
            existing.Show();
            existing.WindowState = WindowState.Normal;
            existing.Activate();
            return;
        }

        var window = new SettingsWindow(_configService!, _audioService!, _hotkeyService!, _trayService!);
        window.Show();
    }

    public void OpenAboutWindow()
    {
        var owner = Windows.OfType<SettingsWindow>().FirstOrDefault();
        var about = new AboutWindow();
        if (owner != null) about.Owner = owner;
        about.ShowDialog();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _hotkeyService?.UnregisterAll();
        _hwndSource?.RemoveHook(WndProc);
        _hwndSource?.Dispose();
        _trayService?.Dispose();

        _singleInstance.Dispose();
        base.OnExit(e);
    }
}
