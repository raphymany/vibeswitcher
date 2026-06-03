using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using System.Windows.Controls;
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

    private System.Windows.Threading.DispatcherTimer? _boundsTimer;

    // Drag-and-drop state for profile card reordering
    private Point _dragStart;
    private ProfileCardViewModel? _dragSource;
    private static readonly Brush _dropTargetBorder = MakeFrozenBrush(0xFF, 0x80, 0x00);
    private static SolidColorBrush MakeFrozenBrush(byte r, byte g, byte b)
    {
        var b2 = new SolidColorBrush(Color.FromRgb(r, g, b));
        b2.Freeze();
        return b2;
    }

    public SettingsWindow(
        IConfigService configService,
        IAudioService audioService,
        IHotkeyService hotkeyService,
        TrayService trayService,
        Action<string> applyTheme,
        Action<Models.DeviceProfile>? switchProfile = null)
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
            },
            applyTheme: applyTheme,
            switchProfile: switchProfile);

        DataContext = _viewModel;
        RestoreWindowBounds();
        try
        {
            var icon = IconHelper.GetAppIconImageSource();
            AppTitleBar.IconSource = icon;
            AppHeaderIcon.Source   = icon;
        }
        catch { }

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        SizeChanged     += OnBoundsChanged;
        LocationChanged += OnBoundsChanged;

        _errorAddedHandler = (_, _) => Dispatcher.InvokeAsync(UpdateLogsButton);

        // Subscribe only while visible so hide-and-reshow cycles don't accumulate handlers.
        IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue)
            {
                SessionErrorTracker.ErrorAdded += _errorAddedHandler;
                UpdateLogsButton();
                _viewModel.RefreshActiveStates();
            }
            else
            {
                SessionErrorTracker.ErrorAdded -= _errorAddedHandler;
            }
        };
    }

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

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.SettingsCardExpanded) && _viewModel.SettingsCardExpanded)
            Dispatcher.InvokeAsync(EnsureFooterVisible, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void EnsureFooterVisible()
    {
        if (WindowState != WindowState.Normal) return;
        var footerBottom = FooterGrid.TranslatePoint(new Point(0, FooterGrid.ActualHeight), this).Y;
        // MainGrid has Margin="18" so its bottom edge sits 18px above the window bottom.
        var overflow = footerBottom - (ActualHeight - 18);
        if (overflow <= 0) return;
        var newHeight = Math.Ceiling(ActualHeight + overflow + 12);
        Height = Math.Min(newHeight, SystemParameters.WorkArea.Height - 40);
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
        _boundsTimer?.Stop();
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

    private void ManageAliasesButton_Click(object sender, RoutedEventArgs e)
    {
        var vm = (ViewModels.SettingsViewModel)DataContext;
        var dialog = new DeviceAliasesDialog(vm.DeviceAliases) { Owner = this };
        dialog.ShowDialog();
    }

    private void AboutButton_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current is App app)
            app.OpenAboutWindow();
    }

    private void FilterNameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !string.IsNullOrEmpty(_viewModel.NameFilter))
        {
            _viewModel.NameFilter = "";
            e.Handled = true;
        }
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

        var dialogSeed = _viewModel.SettingsHotkey;
        while (true)
        {
            var dialog = new HotkeyCaptureDialog(dialogSeed) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.CapturedHotkey == null) break;

            var captured = dialog.CapturedHotkey;
            if (!captured.IsEmpty)
            {
                var ownerName = _configService.Current.Profiles
                    .FirstOrDefault(p => !p.Hotkey.IsEmpty && captured.Matches(p.Hotkey))?.Name;
                if (ownerName != null)
                {
                    bool retry = new ConflictRetryDialog("Hotkey Already in Use",
                        $"'{captured.ToDisplayString()}' is already assigned to \"{ownerName}\".")
                    { Owner = this }.ShowDialog() == true;
                    if (retry) { dialogSeed = captured; continue; }
                    break;
                }
            }

            _viewModel.SettingsHotkey = captured;
            break;
        }

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

    private void DragGrip_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragSource = (sender as FrameworkElement)?.DataContext as ProfileCardViewModel;
    }

    private void DragGrip_MouseUp(object sender, MouseButtonEventArgs e)
    {
        // Clear stale drag source if the user released without crossing the drag threshold.
        _dragSource = null;
    }

    private void DragGrip_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragSource == null) return;
        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStart.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(pos.Y - _dragStart.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            DragDrop.DoDragDrop((DependencyObject)sender, _dragSource, DragDropEffects.Move);
            _dragSource = null;
        }
    }

    private void Card_DragEnter(object sender, DragEventArgs e)
    {
        if (sender is Border border && e.Data.GetDataPresent(typeof(ProfileCardViewModel)))
            border.BorderBrush = _dropTargetBorder;
    }

    private void Card_DragLeave(object sender, DragEventArgs e)
    {
        // Only reset when the drag has truly left the card bounds, not just crossed into a child element.
        if (sender is not Border border) return;
        var pos = e.GetPosition(border);
        if (pos.X < 0 || pos.Y < 0 || pos.X > border.ActualWidth || pos.Y > border.ActualHeight)
            border.ClearValue(Border.BorderBrushProperty);
    }

    private void Card_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(ProfileCardViewModel))
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void Card_Drop(object sender, DragEventArgs e)
    {
        if (sender is Border border)
            border.ClearValue(Border.BorderBrushProperty);

        var target = (sender as FrameworkElement)?.DataContext as ProfileCardViewModel;
        var source = e.Data.GetData(typeof(ProfileCardViewModel)) as ProfileCardViewModel;
        if (source == null || target == null || source == target) return;
        _viewModel.MoveProfile(source, target);
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
