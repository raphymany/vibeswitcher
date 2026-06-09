using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Navigation;
using VibeSwitcher.Helpers;
using VibeSwitcher.Models;
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
    private readonly IAppLogger _logger;
    private readonly ISessionErrorTracker _errorTracker;
    private readonly EventHandler _errorAddedHandler;

    private System.Windows.Threading.DispatcherTimer? _boundsTimer;

    private enum ActivePanel { Profiles, Settings, About }
    private bool _filtersOpen = false;
    private ProfileCardViewModel? _detailVm;

    public SettingsWindow(
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
        InitializeComponent();
        _trayService = trayService;
        _configService = configService;
        _hotkeyService = hotkeyService;
        _logger = logger;
        _errorTracker = errorTracker;

        var startupService = new StartupService(logger, errorTracker);
        var dialogService = new DialogService(logger);
        _viewModel = new SettingsViewModel(
            configService,
            audioService,
            hotkeyService,
            startupService,
            dialogService,
            logger,
            errorTracker,
            onProfilesChanged: () =>
            {
                trayService.ClearIconCache();
                trayService.RebuildMenu();
                var active = configService.Current.Profiles
                    .FirstOrDefault(p => p.Id == configService.Current.ActiveProfileId);
                trayService.UpdateIcon(active);
                onReschedule?.Invoke();
            },
            onHotkeyConflict: ex =>
            {
                errorTracker.Record(ErrorCode.HotkeyConflict, "Hotkey Conflict",
                    $"Could not register '{ex.Hotkey.ToDisplayString()}' — another app is using it.");
                trayService.ShowBalloon(
                    "Hotkey Conflict",
                    $"Could not register '{ex.Hotkey.ToDisplayString()}' — another app is using it.");
            },
            applyTheme: applyTheme,
            switchProfile: switchProfile,
            onAppTriggersChanged: onAppTriggersChanged);

        DataContext = _viewModel;
        RestoreWindowBounds();

        SizeChanged     += OnBoundsChanged;
        LocationChanged += OnBoundsChanged;

        _errorAddedHandler = (_, _) => Dispatcher.InvokeAsync(UpdateLogsButton);

        IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue)
            {
                _errorTracker.ErrorAdded += _errorAddedHandler;
                UpdateLogsButton();
                _viewModel.RefreshActiveStates();
            }
            else
            {
                _errorTracker.ErrorAdded -= _errorAddedHandler;
            }
        };
    }

    public void RefreshActiveStates() => _viewModel.RefreshActiveStates();

    public void ExpandSettings() => ShowPanel(ActivePanel.Settings);

    // ── Panel navigation ──────────────────────────────────────────────────

    private void ShowPanel(ActivePanel panel)
    {
        ProfileGridView.Visibility = panel == ActivePanel.Profiles ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility   = panel == ActivePanel.Settings  ? Visibility.Visible : Visibility.Collapsed;
        AboutPanel.Visibility      = panel == ActivePanel.About     ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowProfiles_Click(object sender, RoutedEventArgs e) => ShowPanel(ActivePanel.Profiles);
    private void ShowSettings_Click(object sender, RoutedEventArgs e) => ShowPanel(ActivePanel.Settings);
    private void ShowAbout_Click(object sender, RoutedEventArgs e)    => ShowPanel(ActivePanel.About);

    // ── Filter bar animation ──────────────────────────────────────────────

    private void FiltersToggle_Click(object sender, RoutedEventArgs e)
    {
        _filtersOpen = !_filtersOpen;
        var anim = new DoubleAnimation(
            _filtersOpen ? 52 : 0,
            TimeSpan.FromMilliseconds(300));
        anim.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut };
        FilterBarBorder.BeginAnimation(MaxHeightProperty, anim);
    }

    // ── Profile detail modal ──────────────────────────────────────────────

    private void ProfileCard_Clicked(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not ProfileCardView card) return;
        _detailVm = card.DataContext as ProfileCardViewModel;
        if (_detailVm == null) return;

        ProfileDetailCard.DataContext = _detailVm;

        // Animate open: scale 0.88→1, translateY 16→0, opacity 0→1
        ModalScale.ScaleX = 0.88;
        ModalScale.ScaleY = 0.88;
        ModalTranslate.Y  = 16;
        ProfileDetailCard.Opacity = 0;

        ProfileDetailOverlay.Visibility = Visibility.Visible;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(220);

        var scaleAnim = new DoubleAnimation(1, duration) { EasingFunction = ease };
        ModalScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
        ModalScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);

        var translateAnim = new DoubleAnimation(0, duration) { EasingFunction = ease };
        ModalTranslate.BeginAnimation(TranslateTransform.YProperty, translateAnim);

        var opacityAnim = new DoubleAnimation(1, duration);
        ProfileDetailCard.BeginAnimation(OpacityProperty, opacityAnim);
    }

    private void ProfileDetail_Close(object sender, RoutedEventArgs e)
    {
        ProfileDetailOverlay.Visibility = Visibility.Collapsed;
        _detailVm = null;
    }

    private void ProfileDetail_BackdropClick(object sender, MouseButtonEventArgs e)
    {
        ProfileDetailOverlay.Visibility = Visibility.Collapsed;
        _detailVm = null;
    }

    private void ProfileDetail_Save(object sender, RoutedEventArgs e)
    {
        // Two-way bindings already committed changes; just close the overlay.
        ProfileDetailOverlay.Visibility = Visibility.Collapsed;
        _detailVm = null;
    }

    // ── Window controls (frameless) ───────────────────────────────────────

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    // ── Bounds persistence ────────────────────────────────────────────────

    private void OnBoundsChanged(object? sender, EventArgs e)
    {
        if (_boundsTimer == null)
        {
            _boundsTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(400)
            };
            _boundsTimer.Tick += (_, _) => { _boundsTimer.Stop(); SaveWindowBounds(); };
        }
        _boundsTimer.Stop();
        _boundsTimer.Start();
    }

    private void UpdateLogsButton()
    {
        var count = _errorTracker.Count;
        if (count == 0)
        {
            LogsButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            LogsButtonText.Text = $"⚠ {count} Error{(count == 1 ? "" : "s")} This Session";
            LogsButton.Visibility = Visibility.Visible;
        }
    }

    private void LogsButton_Click(object sender, RoutedEventArgs e)
    {
        new SessionLogWindow(_logger, _errorTracker) { Owner = this }.ShowDialog();
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
        _ = Task.Run(_configService.SaveImmediate);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _boundsTimer?.Stop();
        SaveWindowBounds();

        if (_viewModel.CloseToTray)
        {
            e.Cancel = true;
            Hide();
        }
        else
        {
            Application.Current.Shutdown();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void ManageAliasesButton_Click(object sender, RoutedEventArgs e)
    {
        var vm = (ViewModels.SettingsViewModel)DataContext;
        var dialog = new DeviceAliasesDialog(vm.DeviceAliases) { Owner = this };
        dialog.ShowDialog();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (ProfileDetailOverlay.Visibility == Visibility.Visible)
            {
                ProfileDetailOverlay.Visibility = Visibility.Collapsed;
                _detailVm = null;
                e.Handled = true;
                return;
            }
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
            _logger.Warning("SettingsWindow.OpenSoundSettings", ex.Message);
            _errorTracker.Record(ErrorCode.SoundSettingsOpenFailed, "Sound Settings Could Not Open",
                $"Could not open Windows Sound settings: {ex.Message}");
        }
    }

    private void SettingsHotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        _hotkeyService.UnregisterAll();

        var dialogSeed = _viewModel.SettingsHotkey;
        while (true)
        {
            var dialog = new HotkeyCaptureDialog(dialogSeed, "Press any key combination to assign a shortcut for opening Settings") { Owner = this };
            if (dialog.ShowDialog() != true || dialog.CapturedHotkey == null) break;

            var captured = dialog.CapturedHotkey;
            if (!captured.IsEmpty)
            {
                var conflict = FindHotkeyConflict(captured, excludeScope: null, excludeSettingsHotkey: true);
                if (conflict != null)
                {
                    bool retry = new ConflictRetryDialog("Hotkey Already in Use",
                        $"'{captured.ToDisplayString()}' is already assigned to {conflict}.")
                    { Owner = this }.ShowDialog() == true;
                    if (retry) { dialogSeed = captured; continue; }
                    break;
                }
            }

            _viewModel.SettingsHotkey = captured;
            break;
        }

        _viewModel.ReregisterHotkeys();
    }

    private void SettingsHotkeyClear_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SettingsHotkey = new HotkeyDefinition();
    }

    private void MuteHotkeyClear_Click(object sender, RoutedEventArgs e)
    {
        var tag = (sender as FrameworkElement)?.Tag as string;
        var scope = tag switch
        {
            "Mic"      => VibeSwitcher.Models.MuteScope.Mic,
            "Speakers" => VibeSwitcher.Models.MuteScope.Speakers,
            _          => VibeSwitcher.Models.MuteScope.Both,
        };
        _viewModel.SetMuteHotkeyFromDialog(scope, new HotkeyDefinition());
    }

    private void MuteHotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        var tag = (sender as FrameworkElement)?.Tag as string;
        var scope = tag switch
        {
            "Mic"      => VibeSwitcher.Models.MuteScope.Mic,
            "Speakers" => VibeSwitcher.Models.MuteScope.Speakers,
            _          => VibeSwitcher.Models.MuteScope.Both,
        };

        _hotkeyService.UnregisterAll();

        var dialogSeed = scope switch
        {
            VibeSwitcher.Models.MuteScope.Mic      => _viewModel.MuteMicHotkey,
            VibeSwitcher.Models.MuteScope.Speakers => _viewModel.MuteSpeakersHotkey,
            _                                      => _viewModel.MuteBothHotkey,
        };

        var muteSubtitle = scope switch
        {
            VibeSwitcher.Models.MuteScope.Mic      => "Press any key combination to assign a shortcut for muting the microphone",
            VibeSwitcher.Models.MuteScope.Speakers => "Press any key combination to assign a shortcut for muting speakers",
            _                                      => "Press any key combination to assign a shortcut for muting all audio",
        };

        while (true)
        {
            var dialog = new HotkeyCaptureDialog(dialogSeed, muteSubtitle) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.CapturedHotkey == null) break;

            var captured = dialog.CapturedHotkey;
            if (!captured.IsEmpty)
            {
                var conflict = FindHotkeyConflict(captured, excludeScope: scope);
                if (conflict != null)
                {
                    bool retry = new ConflictRetryDialog("Hotkey Already in Use",
                        $"'{captured.ToDisplayString()}' is already assigned to {conflict}.")
                    { Owner = this }.ShowDialog() == true;
                    if (retry) { dialogSeed = captured; continue; }
                    break;
                }
            }

            _viewModel.SetMuteHotkeyFromDialog(scope, captured);
            break;
        }

        _viewModel.ReregisterHotkeys();
    }

    private string? FindHotkeyConflict(HotkeyDefinition captured, VibeSwitcher.Models.MuteScope? excludeScope, bool excludeSettingsHotkey = false)
    {
        var profileOwner = _configService.Current.Profiles
            .FirstOrDefault(p => !p.Hotkey.IsEmpty && captured.Matches(p.Hotkey))?.Name;
        if (profileOwner != null) return $"\"{profileOwner}\"";

        if (!excludeSettingsHotkey)
        {
            var settingsHk = _configService.Current.SettingsHotkey;
            if (settingsHk != null && !settingsHk.IsEmpty && captured.Matches(settingsHk))
                return "\"Open / Close VibeSwitcher\"";
        }

        var muteChecks = new[]
        {
            (VibeSwitcher.Models.MuteScope.Mic,      _configService.Current.MuteMicHotkey,      "Mute Microphone"),
            (VibeSwitcher.Models.MuteScope.Speakers, _configService.Current.MuteSpeakersHotkey, "Mute Speakers"),
            (VibeSwitcher.Models.MuteScope.Both,     _configService.Current.MuteBothHotkey,      "Mute Mic + Speakers"),
        };
        foreach (var (muteScope, muteHk, label) in muteChecks)
        {
            if (muteScope == excludeScope) continue;
            if (muteHk != null && !muteHk.IsEmpty && captured.Matches(muteHk))
                return $"\"{label}\"";
        }

        return null;
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.Warning("SettingsWindow.Hyperlink", ex.Message);
            _errorTracker.Record(ErrorCode.HyperlinkOpenFailed, "Link Could Not Be Opened",
                $"Could not open '{e.Uri.AbsoluteUri}': {ex.Message}");
        }
        e.Handled = true;
    }

    private void ThemeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Primitives.ToggleButton rb && rb.Tag is string tag)
            _viewModel.Theme = tag;
    }

    private void HelpButton_Click(object sender, RoutedEventArgs e)
    {
        new HelpDialog { Owner = this }.ShowDialog();
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = "Export VibeSwitcher Config",
            Filter = "JSON Files (*.json)|*.json",
            FileName = "vibeswitcher-backup.json",
            DefaultExt = ".json"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            _viewModel.ExportConfig(dlg.FileName);
            new AlertDialog("Export Successful", $"Configuration exported to:\n{dlg.FileName}") { Owner = this }.ShowDialog();
        }
        catch (Exception ex)
        {
            new AlertDialog("Export Failed", $"Could not export configuration:\n{ex.Message}") { Owner = this }.ShowDialog();
        }
    }

    private void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Import VibeSwitcher Config",
            Filter = "JSON Files (*.json)|*.json",
            CheckFileExists = true
        };
        if (dlg.ShowDialog() != true) return;

        var confirm = new ConfirmDialog(
            "Replace Configuration?",
            $"Importing '{System.IO.Path.GetFileName(dlg.FileName)}' will replace all current profiles and settings.",
            "Import",
            subtitle: "Your current profiles and settings will be replaced.")
        { Owner = this };
        if (confirm.ShowDialog() != true) return;

        if (!_viewModel.ImportConfig(dlg.FileName, out var error))
            new AlertDialog("Import Failed", error ?? "The configuration could not be imported.") { Owner = this }.ShowDialog();
    }
}
