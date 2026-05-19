using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using VibeSwitcher.Helpers;
using VibeSwitcher.Services;
using VibeSwitcher.Tray;
using VibeSwitcher.ViewModels;

namespace VibeSwitcher.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;
    private readonly TrayService _trayService;
    private readonly IConfigService _configService;
    private readonly IHotkeyService _hotkeyService;
    private readonly EventHandler _errorAddedHandler;

    public SettingsWindow(
        IConfigService configService,
        IAudioService audioService,
        IHotkeyService hotkeyService,
        TrayService trayService)
    {
        InitializeComponent();
        _trayService = trayService;
        _configService = configService;
        _hotkeyService = hotkeyService;

        var startupService = new StartupService();
        var dialogService = new DialogService();
        _viewModel = new SettingsViewModel(
            configService,
            audioService,
            hotkeyService,
            startupService,
            dialogService,
            onProfilesChanged: () =>
            {
                trayService.ClearIconCache();
                trayService.RebuildMenu();
                var active = configService.Current.Profiles
                    .FirstOrDefault(p => p.Id == configService.Current.ActiveProfileId);
                trayService.UpdateIcon(active);
            },
            onHotkeyConflict: ex =>
            {
                SessionErrorTracker.Record(ErrorCode.HotkeyConflict, "Hotkey Conflict",
                    $"Could not register '{ex.Hotkey.ToDisplayString()}' — another app is using it.");
                trayService.ShowBalloon(
                    "Hotkey Conflict",
                    $"Could not register '{ex.Hotkey.ToDisplayString()}' — another app is using it.");
            });

        DataContext = _viewModel;
        RestoreWindowBounds();
        try { HeaderIcon.Source = IconHelper.GetAppIconImageSource(); } catch { }

        _errorAddedHandler = (_, _) => Dispatcher.InvokeAsync(UpdateLogsButton);

        // Subscribe only while visible so hide-and-reshow cycles don't accumulate handlers.
        IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue)
            {
                SessionErrorTracker.ErrorAdded += _errorAddedHandler;
                UpdateLogsButton();
            }
            else
            {
                SessionErrorTracker.ErrorAdded -= _errorAddedHandler;
            }
        };
    }

    private void UpdateLogsButton()
    {
        var count = SessionErrorTracker.Count;
        if (count == 0)
        {
            LogsButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            LogsButtonText.Text = $"⚠ {count} Error{(count == 1 ? "" : "s")} This Session";
            LogsButton.Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0x50, 0x00));
            LogsButton.Visibility = Visibility.Visible;
        }
    }

    private void LogsButton_Click(object sender, RoutedEventArgs e)
    {
        new SessionLogWindow { Owner = this }.ShowDialog();
    }

    private void RestoreWindowBounds()
    {
        var cfg = _configService.Current;

        if (cfg.WindowWidth >= 200)  Width  = cfg.WindowWidth;
        if (cfg.WindowHeight >= 200) Height = cfg.WindowHeight;

        if (cfg.WindowLeft.HasValue && cfg.WindowTop.HasValue)
        {
            var vsl = SystemParameters.VirtualScreenLeft;
            var vst = SystemParameters.VirtualScreenTop;
            var vsw = SystemParameters.VirtualScreenWidth;
            var vsh = SystemParameters.VirtualScreenHeight;

            // Clamp so the entire window stays within the virtual screen even if the
            // monitor it was saved on is no longer connected or has a different resolution.
            var left = Math.Clamp(cfg.WindowLeft.Value, vsl, Math.Max(vsl, vsl + vsw - Width));
            var top  = Math.Clamp(cfg.WindowTop.Value,  vst, Math.Max(vst, vst + vsh - Height));

            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top  = top;
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
        if (e.Key == Key.Escape)
        {
            // Close any open icon-info popup first; only close the window if none were open.
            if (CloseOpenIconPopups())
            {
                e.Handled = true;
                return;
            }
            Close();
        }
    }

    private bool CloseOpenIconPopups()
    {
        var closed = false;
        foreach (var toggle in FindVisualChildren<ToggleButton>(this))
        {
            if (toggle.Name == "IconInfoToggle" && toggle.IsChecked == true)
            {
                toggle.IsChecked = false;
                closed = true;
            }
        }
        return closed;
    }

    private static IEnumerable<T> FindVisualChildren<T>(System.Windows.DependencyObject parent) where T : System.Windows.DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T t) yield return t;
            foreach (var grandchild in FindVisualChildren<T>(child))
                yield return grandchild;
        }
    }

    private void OpenSoundSettings_Click(object sender, RoutedEventArgs e)
    {
        var useLegacy = _configService.Current.UseLegacySoundPanel;
        if (!useLegacy)
        {
            try
            {
                Process.Start(new ProcessStartInfo("ms-settings:sound") { UseShellExecute = true });
                return;
            }
            catch { }
        }
        try { Process.Start("control.exe", "/name Microsoft.Sound"); }
        catch (Exception ex)
        {
            AppLogger.Warning("SettingsWindow.OpenSoundSettings", ex.Message);
            SessionErrorTracker.Record(ErrorCode.SoundSettingsOpenFailed, "Sound Settings Could Not Open",
                $"Could not open Windows Sound settings: {ex.Message}");
        }
    }

    private void SettingsHotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        // Unregister all hotkeys so profile hotkeys can't fire while the dialog is open.
        _hotkeyService.UnregisterAll();
        var dialog = new HotkeyCaptureDialog(_viewModel.SettingsHotkey) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.CapturedHotkey != null)
            _viewModel.SettingsHotkey = dialog.CapturedHotkey;
        // Always re-register everything (profiles + Settings hotkey) after the dialog closes.
        _viewModel.ReregisterHotkeys();
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLogger.Warning("SettingsWindow.Hyperlink", ex.Message);
            SessionErrorTracker.Record(ErrorCode.HyperlinkOpenFailed, "Link Could Not Be Opened",
                $"Could not open '{e.Uri.AbsoluteUri}': {ex.Message}");
        }
        e.Handled = true;
    }
}
