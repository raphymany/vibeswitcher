using System.Windows;
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
    private readonly Action<string> _applyTheme;
    private readonly Action<Models.DeviceProfile>? _switchProfile;

    public AppWindowManager(
        IConfigService configService,
        IAudioService audioService,
        IHotkeyService hotkeyService,
        TrayService trayService,
        Action<string> applyTheme,
        Action<Models.DeviceProfile>? switchProfile = null)
    {
        _configService = configService;
        _audioService  = audioService;
        _hotkeyService = hotkeyService;
        _trayService   = trayService;
        _applyTheme    = applyTheme;
        _switchProfile = switchProfile;
    }

    public void OpenSettingsWindow()
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
            return;
        }

        var window = new SettingsWindow(_configService, _audioService, _hotkeyService, _trayService, _applyTheme, _switchProfile);
        window.Show();
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
        var about = new AboutWindow(profileCount);
        if (owner != null) about.Owner = owner;
        about.ShowDialog();
    }
}
