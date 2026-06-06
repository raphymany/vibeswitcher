using System.Windows;
using VibeSwitcher.Helpers;
using VibeSwitcher.Services;
using VibeSwitcher.Tray;
using VibeSwitcher.Views;

namespace VibeSwitcher;

public class AppWindowManager
{
    private readonly IConfigService _configService;
    private readonly IAudioService _audioService;
    private readonly IHotkeyService _hotkeyService;
    private readonly TrayService _trayService;
    private readonly IAppLogger _logger;
    private readonly ISessionErrorTracker _errorTracker;
    private readonly Action<string> _applyTheme;
    private readonly Action<Models.DeviceProfile>? _switchProfile;
    private readonly Action? _onReschedule;
    private readonly Action? _onAppTriggersChanged;

    public AppWindowManager(
        IConfigService configService,
        IAudioService audioService,
        IHotkeyService hotkeyService,
        TrayService trayService,
        IAppLogger logger,
        ISessionErrorTracker errorTracker,
        Action<string> applyTheme,
        Action<Models.DeviceProfile>? switchProfile = null,
        Action? onReschedule = null,
        Action? onAppTriggersChanged = null)
    {
        _configService        = configService;
        _audioService         = audioService;
        _hotkeyService        = hotkeyService;
        _trayService          = trayService;
        _logger               = logger;
        _errorTracker         = errorTracker;
        _applyTheme           = applyTheme;
        _switchProfile        = switchProfile;
        _onReschedule         = onReschedule;
        _onAppTriggersChanged = onAppTriggersChanged;
    }

    public void OpenSettingsWindow(bool expandSettings = false)
    {
        var existing = Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault();
        if (existing != null)
        {
            if (existing.IsVisible && existing.IsActive)
            {
                existing.Hide();
                return;
            }
            existing.Show();
            existing.WindowState = WindowState.Normal;
            existing.Activate();
            if (expandSettings) existing.ExpandSettings();
            return;
        }

        var window = new SettingsWindow(_configService, _audioService, _hotkeyService, _trayService,
            _logger, _errorTracker, _applyTheme, _switchProfile, _onReschedule, _onAppTriggersChanged);
        window.Show();
        if (expandSettings) window.ExpandSettings();
    }

    public void NotifyProfileSwitched()
    {
        var window = System.Windows.Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault();
        window?.RefreshActiveStates();
    }

    public void OpenAboutWindow()
    {
        var owner = Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault();
        var profileCount = _configService.Current.Profiles.Count;
        var about = new AboutWindow(profileCount, _logger, _errorTracker);
        if (owner != null) about.Owner = owner;
        about.ShowDialog();
    }
}
