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
    private readonly ISwitchSoundService _switchSoundService;
    private readonly Action<string> _applyTheme;
    private readonly Action<Models.DeviceProfile>? _switchProfile;

    public AppWindowManager(
        IConfigService configService,
        IAudioService audioService,
        IHotkeyService hotkeyService,
        TrayService trayService,
        ISwitchSoundService switchSoundService,
        Action<string> applyTheme,
        Action<Models.DeviceProfile>? switchProfile = null)
    {
        _configService     = configService;
        _audioService      = audioService;
        _hotkeyService     = hotkeyService;
        _trayService       = trayService;
        _switchSoundService = switchSoundService;
        _applyTheme        = applyTheme;
        _switchProfile     = switchProfile;
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

        var window = new SettingsWindow(_configService, _audioService, _hotkeyService, _trayService, _switchSoundService, _applyTheme, _switchProfile);
        window.Show();
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
