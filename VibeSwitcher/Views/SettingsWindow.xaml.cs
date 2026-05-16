using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;
using VibeSwitcher.Services;
using VibeSwitcher.Tray;
using VibeSwitcher.ViewModels;

namespace VibeSwitcher.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;
    private readonly TrayService _trayService;
    private readonly ConfigService _configService;

    public SettingsWindow(
        ConfigService configService,
        AudioService audioService,
        HotkeyService hotkeyService,
        TrayService trayService)
    {
        InitializeComponent();
        _trayService = trayService;
        _configService = configService;

        var startupService = new StartupService();
        _viewModel = new SettingsViewModel(
            configService,
            audioService,
            hotkeyService,
            startupService,
            onProfilesChanged: () =>
            {
                trayService.RebuildMenu();
                var active = configService.Current.Profiles
                    .FirstOrDefault(p => p.Id == configService.Current.ActiveProfileId);
                trayService.UpdateIcon(active);
            });

        DataContext = _viewModel;
        RestoreWindowBounds();
    }

    private void RestoreWindowBounds()
    {
        var cfg = _configService.Current;

        if (cfg.WindowWidth >= 200)  Width  = cfg.WindowWidth;
        if (cfg.WindowHeight >= 200) Height = cfg.WindowHeight;

        // Only restore position if it's within the virtual screen (guards against disconnected monitors)
        if (cfg.WindowLeft >= SystemParameters.VirtualScreenLeft &&
            cfg.WindowLeft <  SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth &&
            cfg.WindowTop  >= SystemParameters.VirtualScreenTop &&
            cfg.WindowTop  <  SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = cfg.WindowLeft;
            Top  = cfg.WindowTop;
        }
    }

    private void SaveWindowBounds()
    {
        if (WindowState != WindowState.Normal) return;
        var cfg = _configService.Current;
        cfg.WindowWidth  = Width;
        cfg.WindowHeight = Height;
        cfg.WindowLeft   = Left;
        cfg.WindowTop    = Top;
        _configService.SaveImmediate();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        SaveWindowBounds();

        if (_viewModel.CloseToTray)
        {
            // Hide instead of close — app stays alive in tray
            e.Cancel = true;
            Hide();
        }
        else
        {
            Application.Current.Shutdown();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void AboutButton_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current is App app)
            app.OpenAboutWindow();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
