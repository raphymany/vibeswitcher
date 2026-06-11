using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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
    private bool _filterBarOpen = false;

    // Drop-target highlight brush for profile card drag-and-drop
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
        _viewModel.ProfileDeletedOrCloned += CloseProfileDetailOverlay;
        RestoreWindowBounds();
        try
        {
            var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            AboutVersionText.Text = v != null ? $"Version {v.Major}.{v.Minor}.{v.Build} — Windows 10/11" : "Windows 10/11";
        }
        catch { }

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        SizeChanged     += OnBoundsChanged;
        LocationChanged += OnBoundsChanged;

        // Mini-mode lists share the card view-models but use tray ordering:
        // pinned profiles first, then the user's sort order.
        _miniRowsView = MakeMiniView();
        _miniGridView = MakeMiniView();
        MiniList.ItemsSource = _miniRowsView;
        MiniGrid.ItemsSource = _miniGridView;
        RefreshMiniList();

        Activated   += (_, _) => FadeOpacityTo(1.0);
        Deactivated += (_, _) =>
        {
            if (IsCompact && _configService.Current.CompactTranslucent && !IsMouseOver)
                FadeOpacityTo(0.65);
        };
        // Hovering the faded mini window brings it back to solid before any click.
        MouseEnter += (_, _) => { if (IsCompact) FadeOpacityTo(1.0); };
        MouseLeave += (_, _) =>
        {
            if (IsCompact && !IsActive && _configService.Current.CompactTranslucent)
                FadeOpacityTo(0.65);
        };

        if (_configService.Current.CompactMode)
            EnterCompact();

        // Uninstalling only applies to copies installed by the setup program; the card
        // stays visible for portable copies with the button disabled and a hint instead.
        if (!IsInstalledCopy())
        {
            UninstallBtn.IsEnabled = false;
            PortableUninstallNote.Visibility = Visibility.Visible;
        }
        var dataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VibeSwitcher");
        InstallLocationText.Text = AppContext.BaseDirectory;
        DataLocationText.Text    = dataFolder;
        InstallSizeText.Text     = FormatFolderSize(AppContext.BaseDirectory);
        DataSizeText.Text        = FormatFolderSize(dataFolder);
        try
        {
            var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            InstallVersionText.Text = v != null ? $"{v.Major}.{v.Minor}.{v.Build}" : "—";
        }
        catch { InstallVersionText.Text = "—"; }

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

    public void ExpandSettings()
    {
        if (IsCompact) ExitCompact();
        _viewModel.SettingsCardExpanded = true;
        ShowPanel(SettingsBodyScrollViewer);
    }

    public void OpenAboutPanel() { if (IsCompact) ExitCompact(); ShowPanel(AboutPanel); }
    public void OpenFaqPanel()   { if (IsCompact) ExitCompact(); ShowPanel(FaqPanel); }

    // ── Panel navigation ────────────────────────────────────────────

    private void ShowPanel(UIElement panel)
    {
        ProfileDetailOverlay.Visibility    = Visibility.Collapsed;
        CloseFilterBar();
        ProfilesPanel.Visibility          = Visibility.Collapsed;
        SettingsBodyScrollViewer.Visibility = Visibility.Collapsed;
        AboutPanel.Visibility             = Visibility.Collapsed;
        FaqPanel.Visibility               = Visibility.Collapsed;
        MiniPanel.Visibility              = Visibility.Collapsed;
        panel.Visibility                  = Visibility.Visible;
    }

    private void NavProfiles_Click(object sender, RoutedEventArgs e) => ShowPanel(ProfilesPanel);
    private void NavLogo_Click(object sender, RoutedEventArgs e)    => ShowPanel(ProfilesPanel);
    private void NavSettings_Click(object sender, RoutedEventArgs e) { _viewModel.SettingsCardExpanded = true; ShowPanel(SettingsBodyScrollViewer); }
    private void NavAbout_Click(object sender, RoutedEventArgs e)    => ShowPanel(AboutPanel);
    private void NavFaq_Click(object sender, RoutedEventArgs e)      => ShowPanel(FaqPanel);

    private void LinkChip_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as System.Windows.Controls.Button)?.Tag is not string url) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { _logger.Warning("LinkChip_Click", ex.Message); }
    }

    // ── Title bar controls ───────────────────────────────────────────

    private void TitleMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void TitleMaximize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void TitleClose_Click(object sender, RoutedEventArgs e) => Close();

    // ── Filter bar toggle ────────────────────────────────────────────

    private void FiltersBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_filterBarOpen) { CloseFilterBar(); return; }

        _filterBarOpen = true;
        FilterBarBorder.IsEnabled = true;   // re-enter tab order while open

        // Measure the actual content height so the bar never clips, regardless of how many
        // rows the chips wrap into or whether the day-chips row is showing.
        var content = FilterBarBorder.Child as FrameworkElement;
        content?.Measure(new Size(FilterBarBorder.ActualWidth, double.PositiveInfinity));
        double target = content?.DesiredSize.Height ?? 130;

        var anim = new DoubleAnimation
        {
            To = target,
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        // Once open, release the cap so dynamic content (e.g. day chips when "Scheduled"
        // is toggled) can grow the bar without being clipped.
        anim.Completed += (_, _) =>
        {
            if (!_filterBarOpen) return;
            FilterBarBorder.BeginAnimation(MaxHeightProperty, null);
            FilterBarBorder.MaxHeight = double.PositiveInfinity;
        };
        FilterBarBorder.BeginAnimation(MaxHeightProperty, anim);
    }

    private void CloseFilterBar()
    {
        if (!_filterBarOpen) return;
        _filterBarOpen = false;
        FilterBarBorder.IsEnabled = false;  // drop chips out of the tab order while collapsed

        // Pin the current rendered height first (the cap may have been released to infinity),
        // so the collapse animates smoothly from the real height down to 0.
        double from = FilterBarBorder.ActualHeight;
        FilterBarBorder.MaxHeight = from;
        FilterBarBorder.BeginAnimation(MaxHeightProperty, new DoubleAnimation
        {
            From = from,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        });
    }

    // ── Profile detail overlay ───────────────────────────────────────

    private void ProfileCard_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ProfileCardViewModel vm)
        {
            ProfileDetailCard.MinHeight = 0;
            vm.NotifyDetailModalOpened();
            // When the user picks a chip the VM collapses the row and fires this
            // callback so we release the MinHeight lock and let the card shrink.
            vm.OnSuggestionSelected = () => ProfileDetailCard.MinHeight = 0;
            ProfileDetailContent.DataContext = vm;
            ProfileDetailOverlay.Visibility  = Visibility.Visible;
            // Lock MinHeight after layout so typing/chip-hide can't shrink the dialog.
            Dispatcher.InvokeAsync(() =>
            {
                ProfileDetailCard.UpdateLayout();
                if (ProfileDetailCard.ActualHeight > 0)
                    ProfileDetailCard.MinHeight = ProfileDetailCard.ActualHeight;
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private void CloseProfileDetailOverlay()
    {
        ProfileDetailCard.MinHeight = 0;
        ProfileDetailOverlay.Visibility = Visibility.Collapsed;
    }

    private void OverlayClose_Click(object sender, RoutedEventArgs e)
        => CloseProfileDetailOverlay();

    private void ChipButton_Click(object sender, RoutedEventArgs e)
        => e.Handled = true;

    private void OverlayBackdrop_MouseDown(object sender, MouseButtonEventArgs e)
        => CloseProfileDetailOverlay();

    // ── Bounds tracking ─────────────────────────────────────────────

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
        if (e.PropertyName == nameof(SettingsViewModel.SettingsCardExpanded))
        {
            if (_viewModel.SettingsCardExpanded)
                ShowPanel(SettingsBodyScrollViewer);
        }
        else if (e.PropertyName == nameof(SettingsViewModel.CompactAlwaysOnTop))
        {
            if (IsCompact) Topmost = _viewModel.CompactAlwaysOnTop;
        }
        else if (e.PropertyName == nameof(SettingsViewModel.CompactTranslucent))
        {
            if (!_viewModel.CompactTranslucent) FadeOpacityTo(1.0);
        }
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

    // ── In-app uninstall (installed copies only) ────────────────────

    private static bool IsInstalledCopy()
    {
        var installRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "VibeSwitcher");
        var baseDir = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
        return baseDir.Equals(Path.GetFullPath(installRoot).TrimEnd(Path.DirectorySeparatorChar),
                              StringComparison.OrdinalIgnoreCase)
            && File.Exists(Path.Combine(baseDir, "unins000.exe"));
    }

    private void BrowseInstallFolder_Click(object sender, RoutedEventArgs e) =>
        OpenFolderInExplorer(AppContext.BaseDirectory);

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e) =>
        OpenFolderInExplorer(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VibeSwitcher"));

    private static string FormatFolderSize(string path)
    {
        try
        {
            if (!Directory.Exists(path)) return "—";
            long bytes = new DirectoryInfo(path)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(f => f.Length);
            return bytes switch
            {
                >= 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024 * 1024):0.##} GB",
                >= 1024L * 1024        => $"{bytes / (1024.0 * 1024):0.#} MB",
                >= 1024                => $"{bytes / 1024.0:0.#} KB",
                _                      => $"{bytes} B",
            };
        }
        catch { return "—"; }
    }

    private void OpenFolderInExplorer(string path)
    {
        try
        {
            if (!Directory.Exists(path)) return;
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.Warning("SettingsWindow.OpenFolder", ex.Message);
        }
    }

    private void UninstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (!IsInstalledCopy()) return;

        var dialog = new UninstallDialog { Owner = this };
        if (dialog.ShowDialog() != true) return;

        var uninstaller = Path.Combine(AppContext.BaseDirectory, "unins000.exe");
        var args = "/SILENT" + (dialog.DeleteData ? " /DELETEDATA=1" : "");

        // Launch through cmd with a short delay so this process can exit and release
        // the installer-detection mutex before the uninstaller checks for it.
        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c timeout /t 2 /nobreak >nul & \"{uninstaller}\" {args}",
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        Application.Current.Shutdown();
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
        if (IsCompact)
        {
            // Never let mini geometry overwrite the full-window slots.
            cfg.CompactWindowLeft = Left;
            cfg.CompactWindowTop  = Top;
        }
        else
        {
            cfg.WindowWidth  = Width;
            cfg.WindowHeight = Height;
            cfg.WindowLeft   = Left;
            cfg.WindowTop    = Top;
        }
        _ = Task.Run(_configService.SaveImmediate);
    }

    // ── Mini (compact) mode ─────────────────────────────────────────

    public bool IsCompact { get; private set; }
    private double _fullWidth, _fullHeight, _fullMinWidth, _fullMinHeight;
    private const double CompactWidth = 300;

    public void ToggleCompact()
    {
        if (IsCompact) ExitCompact();
        else EnterCompact();
    }

    public void EnterCompact()
    {
        if (IsCompact) return;

        // First-time guidance and empty-state guard. Skipped during the startup
        // restore (window not loaded yet) — CompactMode=true implies prior use.
        if (IsLoaded)
        {
            if (_viewModel.Profiles.Count == 0)
            {
                new AlertDialog("Mini Mode needs a profile",
                    "Mini Mode shrinks VibeSwitcher into a compact profile switcher. " +
                    "Create at least one profile first, then come back to set it up.")
                { Owner = this }.ShowDialog();
                return;
            }

            var cfg0 = _configService.Current;
            if (!cfg0.CompactIntroShown)
            {
                cfg0.CompactIntroShown = true;
                _ = Task.Run(_configService.SaveImmediate);

                bool customize = new ConfirmDialog(
                    "Welcome to Mini Mode",
                    "Mini Mode shrinks VibeSwitcher into a small, always-handy profile switcher. " +
                    "You can choose which profiles appear and pick between a row list or an icon grid — " +
                    "or skip this and start with the defaults.",
                    "Customize First",
                    subtitle: "You can change all of this later in Settings → Mini Window.",
                    iconGeometry: "IcoCompact",
                    iconBgResource: "Accent")
                { Owner = this }.ShowDialog() == true;
                if (customize)
                    CompactCustomize_Click(this, new RoutedEventArgs());
            }

            // The dialogs above pump messages — the global hotkey may have already
            // toggled mini mode while one of them was open.
            if (IsCompact) return;
        }

        // Flush the full-window bounds before any geometry changes so nothing mini leaks into them.
        _boundsTimer?.Stop();
        if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;
        if (IsLoaded) SaveWindowBounds();

        _fullWidth     = Width;
        _fullHeight    = Height;
        _fullMinWidth  = MinWidth;
        _fullMinHeight = MinHeight;

        IsCompact = true;

        RefreshMiniList();
        ShowPanel(MiniPanel);
        NavBar.Visibility         = Visibility.Collapsed;
        NavRowDef.Height          = new GridLength(0);   // collapse the fixed 54px nav row, not just its content
        FullTitleGroup.Visibility = Visibility.Collapsed;
        MiniTitleGroup.Visibility = Visibility.Visible;
        TitleShrinkBtn.Visibility = Visibility.Collapsed;
        TitleMaxBtn.Visibility    = Visibility.Collapsed;
        TitlePinBtn.Visibility    = Visibility.Visible;
        TitleExpandBtn.Visibility = Visibility.Visible;

        ResizeMode = ResizeMode.NoResize;
        MaxHeight  = SystemParameters.WorkArea.Height * 0.85;
        MinHeight  = 0;
        MinWidth   = CompactWidth;
        MaxWidth   = CompactWidth;
        Width      = CompactWidth;
        SizeToContent = SizeToContent.Height;

        var cfg = _configService.Current;
        if (cfg.CompactWindowLeft.HasValue && cfg.CompactWindowTop.HasValue)
        {
            var vsl = SystemParameters.VirtualScreenLeft;
            var vst = SystemParameters.VirtualScreenTop;
            var vsw = SystemParameters.VirtualScreenWidth;
            var vsh = SystemParameters.VirtualScreenHeight;
            WindowStartupLocation = WindowStartupLocation.Manual; // keep the pre-show restore from being recentered
            Left = Math.Clamp(cfg.CompactWindowLeft.Value, vsl, Math.Max(vsl, vsl + vsw - CompactWidth));
            Top  = Math.Clamp(cfg.CompactWindowTop.Value,  vst, Math.Max(vst, vst + vsh - 200));
        }

        Topmost = cfg.CompactAlwaysOnTop;
        UpdateMiniMuteBadge(_trayService.MuteState.Mic, _trayService.MuteState.Speakers);

        if (!cfg.CompactMode)
        {
            cfg.CompactMode = true;
            _ = Task.Run(_configService.SaveImmediate);
        }
    }

    public void ExitCompact()
    {
        if (!IsCompact) return;

        var cfg = _configService.Current;
        cfg.CompactWindowLeft = Left;
        cfg.CompactWindowTop  = Top;

        IsCompact = false;

        SizeToContent = SizeToContent.Manual;
        MaxWidth   = double.PositiveInfinity;
        MaxHeight  = double.PositiveInfinity;
        MinWidth   = _fullMinWidth;
        MinHeight  = _fullMinHeight;
        Width      = _fullWidth;
        Height     = _fullHeight;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        Topmost    = false;
        BeginAnimation(OpacityProperty, null); // release any fade hold before the direct set
        Opacity    = 1.0;

        NavBar.Visibility         = Visibility.Visible;
        NavRowDef.Height          = new GridLength(54);
        FullTitleGroup.Visibility = Visibility.Visible;
        MiniTitleGroup.Visibility = Visibility.Collapsed;
        TitleShrinkBtn.Visibility = Visibility.Visible;
        TitleMaxBtn.Visibility    = Visibility.Visible;
        TitlePinBtn.Visibility    = Visibility.Collapsed;
        TitleExpandBtn.Visibility = Visibility.Collapsed;

        ShowPanel(ProfilesPanel);

        // Put the full window back where it last lived (clamped to the visible screen).
        if (cfg.WindowLeft.HasValue && cfg.WindowTop.HasValue)
        {
            var vsl = SystemParameters.VirtualScreenLeft;
            var vst = SystemParameters.VirtualScreenTop;
            var vsw = SystemParameters.VirtualScreenWidth;
            var vsh = SystemParameters.VirtualScreenHeight;
            Left = Math.Clamp(cfg.WindowLeft.Value, vsl, Math.Max(vsl, vsl + vsw - Width));
            Top  = Math.Clamp(cfg.WindowTop.Value,  vst, Math.Max(vst, vst + vsh - Height));
        }

        if (cfg.CompactMode)
        {
            cfg.CompactMode = false;
            _ = Task.Run(_configService.SaveImmediate);
        }
    }

    private void TitleShrink_Click(object sender, RoutedEventArgs e) => EnterCompact();
    private void TitleExpand_Click(object sender, RoutedEventArgs e) => ExitCompact();
    private void TitlePin_Click(object sender, RoutedEventArgs e) =>
        _viewModel.CompactAlwaysOnTop = !_viewModel.CompactAlwaysOnTop;

    private void FadeOpacityTo(double target)
    {
        var anim = new DoubleAnimation(target, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        BeginAnimation(OpacityProperty, anim);
    }

    // Mirrors the tray mute badge inside the mini title bar (the tray is hidden under fullscreen apps).
    public void UpdateMiniMuteBadge(bool micMuted, bool speakersMuted)
    {
        if (!micMuted && !speakersMuted)
        {
            MiniMuteDot.Visibility = Visibility.Collapsed;
            return;
        }
        var (color, tip) = (micMuted, speakersMuted) switch
        {
            (true, true)  => (Color.FromRgb(150, 70, 230), "Mic + Speakers muted"),
            (true, false) => (Color.FromRgb(225, 55, 55),  "Mic muted"),
            _             => (Color.FromRgb(45, 120, 230), "Speakers muted"),
        };
        MiniMuteDot.Fill = new SolidColorBrush(color);
        MiniMuteDot.ToolTip = tip;
        MiniMuteDot.Visibility = Visibility.Visible;
    }

    private System.Windows.Data.ListCollectionView _miniRowsView = null!;
    private System.Windows.Data.ListCollectionView _miniGridView = null!;

    private System.Windows.Data.ListCollectionView MakeMiniView()
    {
        var view = new System.Windows.Data.ListCollectionView(_viewModel.Profiles)
        {
            IsLiveSorting = true,
        };
        view.SortDescriptions.Add(new SortDescription(nameof(ProfileCardViewModel.IsPinned), ListSortDirection.Descending));
        view.SortDescriptions.Add(new SortDescription(nameof(ProfileCardViewModel.SortOrder), ListSortDirection.Ascending));
        view.LiveSortingProperties.Add(nameof(ProfileCardViewModel.IsPinned));
        view.LiveSortingProperties.Add(nameof(ProfileCardViewModel.SortOrder));
        return view;
    }

    // Applies the user's mini-mode profile selection and layout choice.
    private void RefreshMiniList()
    {
        var cfg = _configService.Current;

        Predicate<object>? filter = null;
        var ids = cfg.CompactProfileIds;
        // A selection only applies while at least one selected profile still exists;
        // otherwise (or with no selection) every profile is shown.
        if (ids.Count > 0 && _viewModel.Profiles.Any(p => ids.Contains(p.Id)))
            filter = o => o is ProfileCardViewModel card && ids.Contains(card.Id);
        _miniRowsView.Filter = filter;
        _miniGridView.Filter = filter;

        bool grid = cfg.CompactLayout == "Grid";
        MiniList.Visibility = grid ? Visibility.Collapsed : Visibility.Visible;
        MiniGrid.Visibility = grid ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CompactCustomize_Click(object sender, RoutedEventArgs e)
    {
        var cfg = _configService.Current;
        var selected = cfg.CompactProfileIds.ToHashSet();

        var choices = _viewModel.Profiles
            .OrderByDescending(p => p.IsPinned).ThenBy(p => p.SortOrder)
            .Select(p => new MiniModeSetupDialog.ProfileChoice
            {
                Id = p.Id,
                Name = p.Name,
                Icon = p.IconPreview,
                IsSelected = selected.Contains(p.Id),
            })
            .ToList();

        var dialog = new MiniModeSetupDialog(cfg.CompactLayout, choices) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        cfg.CompactLayout = dialog.SelectedLayout;
        cfg.CompactProfileIds = dialog.SelectedProfileIds;
        _ = Task.Run(_configService.SaveImmediate);
        RefreshMiniList();
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
                e.Handled = true;
                return;
            }
            if (CloseOpenIconPopups()) { e.Handled = true; return; }
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

    private void CompactHotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        _hotkeyService.UnregisterAll();

        var dialogSeed = _viewModel.CompactHotkey;
        while (true)
        {
            var dialog = new HotkeyCaptureDialog(dialogSeed, "Press any key combination to assign a shortcut for toggling Mini Mode") { Owner = this };
            if (dialog.ShowDialog() != true || dialog.CapturedHotkey == null) break;

            var captured = dialog.CapturedHotkey;
            if (!captured.IsEmpty)
            {
                var conflict = FindHotkeyConflict(captured, excludeScope: null, excludeCompactHotkey: true);
                if (conflict != null)
                {
                    bool retry = new ConflictRetryDialog("Hotkey Already in Use",
                        $"'{captured.ToDisplayString()}' is already assigned to {conflict}.")
                    { Owner = this }.ShowDialog() == true;
                    if (retry) { dialogSeed = captured; continue; }
                    break;
                }
            }

            _viewModel.CompactHotkey = captured;
            break;
        }

        _viewModel.ReregisterHotkeys();
    }

    private void CompactHotkeyClear_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.CompactHotkey = new HotkeyDefinition();
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

    private string? FindHotkeyConflict(HotkeyDefinition captured, VibeSwitcher.Models.MuteScope? excludeScope, bool excludeSettingsHotkey = false, bool excludeCompactHotkey = false)
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

        if (!excludeCompactHotkey)
        {
            var compactHk = _configService.Current.CompactHotkey;
            if (compactHk != null && !compactHk.IsEmpty && captured.Matches(compactHk))
                return "\"Toggle Mini Mode\"";
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

    // FAQ inline actions: jump straight to the place the answer talks about.
    private void FaqAction_Click(object sender, RoutedEventArgs e)
    {
        var tag = (sender as System.Windows.Documents.Hyperlink)?.Tag as string;
        switch (tag)
        {
            case "newProfile":
                ShowPanel(ProfilesPanel);
                _viewModel.AddProfileCommand.Execute(null);
                break;
            case "profiles":
                ShowPanel(ProfilesPanel);
                break;
            case "filters":
                ShowPanel(ProfilesPanel);
                if (!_filterBarOpen) FiltersBtn_Click(this, new RoutedEventArgs());
                break;
            case "shortcuts":
                ExpandSettings();
                _viewModel.SelectedCategory = "shortcuts";
                break;
            case "notifications":
                ExpandSettings();
                _viewModel.SelectedCategory = "notif";
                break;
            case "backup":
                ExpandSettings();
                _viewModel.SelectedCategory = "devices";
                break;
            case "miniSettings":
                ExpandSettings();
                _viewModel.SelectedCategory = "compact";
                break;
            case "miniTry":
                EnterCompact();
                break;
        }
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

    private void Card_DragEnter(object sender, DragEventArgs e)
    {
        if (sender is Border border && e.Data.GetDataPresent(typeof(ProfileCardViewModel)))
            border.BorderBrush = _dropTargetBorder;
    }

    private void Card_DragLeave(object sender, DragEventArgs e)
    {
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
