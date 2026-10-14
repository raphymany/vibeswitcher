using System.ComponentModel;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Shapes;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using VibeSwitcher.App.Controls;
using VibeSwitcher.App.Dialogs;
using VibeSwitcher.App.Threading;
using VibeSwitcher.Helpers;
using VibeSwitcher.Models;
using VibeSwitcher.Services;
using VibeSwitcher.ViewModels;

namespace VibeSwitcher.App.Settings;

// Port of Views/SettingsWindow.xaml.cs: the window around the shared SettingsViewModel — panel
// navigation, the title bar, the sliding filter bar, the profile popup, remembering the window's
// size and place, and closing to the tray. The Settings / About / FAQ pages and Mini Mode follow
// in the next steps.
public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;
    private readonly IConfigService _configService;
    private readonly IAppLogger _logger;
    private readonly ISessionErrorTracker _errorTracker;
    private readonly EventHandler _errorAddedHandler;

    private DispatcherTimer? _boundsTimer;
    private bool _filterBarOpen;
    private bool _restoringBounds;

    // (For the XAML previewer.)
    public SettingsWindow() : this(null!, null!, null!, null!, null!, null!, _ => { }) { }

    public SettingsWindow(
        IConfigService configService,
        IAudioService audioService,
        IHotkeyService hotkeyService,
        IStartupService startupService,
        IAppLogger logger,
        ISessionErrorTracker errorTracker,
        Action<string> applyTheme,
        Action? onProfilesChanged = null,
        Action<HotkeyConflictException>? onHotkeyConflict = null,
        Action<DeviceProfile>? switchProfile = null,
        Action? onAppTriggersChanged = null)
    {
        AvaloniaXamlLoader.Load(this);
        _configService = configService;
        _logger = logger;
        _errorTracker = errorTracker;
        Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://VibeSwitcher/Assets/Icons/vs-icon-256.png")));
        ApplyChrome();

        if (configService == null) // previewer
        {
            _viewModel = null!;
            _errorAddedHandler = (_, _) => { };
            return;
        }

        var dialogService = new DialogService(() => this);
        _viewModel = new SettingsViewModel(
            configService, audioService, hotkeyService, startupService, dialogService, logger, errorTracker,
            onProfilesChanged: onProfilesChanged ?? (() => { }),
            onHotkeyConflict: onHotkeyConflict ?? (_ => { }),
            applyTheme: applyTheme,
            switchProfile: switchProfile,
            onAppTriggersChanged: onAppTriggersChanged);

        DataContext = _viewModel;
        _viewModel.ProfileDeletedOrCloned += CloseProfileDetailOverlay;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        RestoreWindowBounds();

        PositionChanged += (_, _) => OnBoundsChanged();
        this.GetObservable(ClientSizeProperty).Subscribe(new Observer<Size>(_ => OnBoundsChanged()));
        this.GetObservable(WindowStateProperty).Subscribe(new Observer<WindowState>(OnWindowStateChanged));

        this.FindControl<Grid>("TitleBarGrid")!.PointerPressed += TitleBar_PointerPressed;
        this.FindControl<Border>("OverlayBackdrop")!.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) CloseProfileDetailOverlay();
        };
        KeyDown += Window_KeyDown;
        AvaloniaUiThread.RequeryCommandsOnInput(this);

        _errorAddedHandler = (_, _) => Dispatcher.UIThread.Post(UpdateLogsButton);
        this.GetObservable(IsVisibleProperty).Subscribe(new Observer<bool>(visible =>
        {
            if (visible)
            {
                _errorTracker.ErrorAdded += _errorAddedHandler;
                UpdateLogsButton();
                _viewModel.RefreshActiveStates();
            }
            else
            {
                _errorTracker.ErrorAdded -= _errorAddedHandler;
            }
        }));
    }

    internal SettingsViewModel ViewModel => _viewModel;

    public void RefreshActiveStates() => _viewModel.RefreshActiveStates();
    public void RefreshDevices() => _viewModel.RefreshDevices();

    // The system frame: macOS keeps its own buttons (the title moves right of them); Linux draws the
    // Windows title bar and is resized from its edges.
    private void ApplyChrome()
    {
        WindowChrome.Apply(this);
        if (WindowChrome.Style == ChromeStyle.Mac)
        {
            ExtendClientAreaTitleBarHeightHint = 30;
            this.FindControl<StackPanel>("WindowButtons")!.IsVisible = false;
            this.FindControl<StackPanel>("FullTitleGroup")!.Margin = new Thickness(WindowChrome.MacButtonsWidth, 0, 0, 0);
            this.FindControl<StackPanel>("MiniTitleGroup")!.Margin = new Thickness(WindowChrome.MacButtonsWidth, 0, 0, 0);
        }
        else
        {
            WindowChrome.EnableEdgeResize(this);
        }
    }

    // ── Panel navigation ───────────────────────────────────────────────────────────────────────────

    internal string CurrentPanel { get; private set; } = "ProfilesPanel";

    private void ShowPanel(string panel)
    {
        this.FindControl<Grid>("ProfileDetailOverlay")!.IsVisible = false;
        CloseFilterBar();
        foreach (var name in new[] { "ProfilesPanel", "SettingsBodyScrollViewer", "AboutPanel", "FaqPanel", "MiniPanel" })
            this.FindControl<Control>(name)!.IsVisible = name == panel;
        CurrentPanel = panel;
    }

    public void ExpandSettings()
    {
        _viewModel.SettingsCardExpanded = true;
        ShowPanel("SettingsBodyScrollViewer");
    }

    public void OpenAboutPanel() => ShowPanel("AboutPanel");
    public void OpenFaqPanel() => ShowPanel("FaqPanel");

    private void NavLogo_Click(object? sender, RoutedEventArgs e) => ShowPanel("ProfilesPanel");
    private void NavSettings_Click(object? sender, RoutedEventArgs e) => ExpandSettings();
    private void NavAbout_Click(object? sender, RoutedEventArgs e) => ShowPanel("AboutPanel");
    private void NavFaq_Click(object? sender, RoutedEventArgs e) => ShowPanel("FaqPanel");

    // ── Title bar ──────────────────────────────────────────────────────────────────────────────────

    // Dragging the title bar moves the window; a double click maximizes it (Linux — macOS handles
    // its own title bar). The buttons on it take their own clicks.
    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.ClickCount == 2)
        {
            // (macOS zooms on a title-bar double click; this window's title bar takes the click.)
            if (CanResize) ToggleMaximize();
            return;
        }
        BeginMoveDrag(e);
    }

    private void TitleMinimize_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void TitleMaximize_Click(object? sender, RoutedEventArgs e) => ToggleMaximize();
    private void TitleClose_Click(object? sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    // The maximize symbol shows "restore" while maximized (Windows: □ ↔ ❐).
    private static readonly Geometry RestoreGlyph = StreamGeometry.Parse("M2.5,2.5 H9.5 V9.5 H2.5 Z M2.5,2.5 V0.5 H11.5 V7.5 H9.5");
    private static readonly Geometry MaximizeGlyph = StreamGeometry.Parse("M0.5,0.5 H9.5 V9.5 H0.5 Z");

    private void OnWindowStateChanged(WindowState state)
    {
        this.FindControl<Path>("MaxGlyph")!.Data = state == WindowState.Maximized ? RestoreGlyph : MaximizeGlyph;
        // (macOS windows are resized from their edges by the system, with no grip.)
        this.FindControl<ResizeGrip>("Grip")!.IsVisible = state == WindowState.Normal && WindowChrome.Style != ChromeStyle.Mac;
    }

    // Mini Mode (next step).
    private void TitleShrink_Click(object? sender, RoutedEventArgs e) { }
    private void TitleExpand_Click(object? sender, RoutedEventArgs e) { }
    private void TitlePin_Click(object? sender, RoutedEventArgs e) =>
        _viewModel.CompactAlwaysOnTop = !_viewModel.CompactAlwaysOnTop;

    // ── Filter bar ─────────────────────────────────────────────────────────────────────────────────

    internal bool FilterBarOpen => _filterBarOpen;

    private void FiltersBtn_Click(object? sender, RoutedEventArgs e)
    {
        if (_filterBarOpen) { CloseFilterBar(); return; }

        _filterBarOpen = true;
        var bar = this.FindControl<Border>("FilterBarBorder")!;
        bar.IsEnabled = true; // back in the tab order while open

        // Measure the content so the bar never clips, however many rows the chips wrap into.
        var content = bar.Child!;
        content.Measure(new Size(bar.Bounds.Width, double.PositiveInfinity));
        double target = content.DesiredSize.Height;

        // Once open, the cap is released so changing content (the day chips) can grow the bar.
        _ = Slide(bar, bar.Bounds.Height, target, TimeSpan.FromMilliseconds(300))
            .ContinueWith(_ => { if (_filterBarOpen) bar.MaxHeight = double.PositiveInfinity; },
                TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void CloseFilterBar()
    {
        if (!_filterBarOpen) return;
        _filterBarOpen = false;
        var bar = this.FindControl<Border>("FilterBarBorder")!;
        bar.IsEnabled = false; // out of the tab order while closed
        _ = Slide(bar, bar.Bounds.Height, 0, TimeSpan.FromMilliseconds(220));
    }

    private CancellationTokenSource? _slide;

    private Task Slide(Border bar, double from, double to, TimeSpan duration)
    {
        // A new slide replaces one still running (quick double clicks), so they can't fight.
        _slide?.Cancel();
        _slide = new CancellationTokenSource();
        bar.MaxHeight = to; // the value it rests at; the animation runs over it
        return new Animation
        {
            Duration = duration,
            Easing = new CubicEaseInOut(),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(MaxHeightProperty, from) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(MaxHeightProperty, to) } },
            },
        }.RunAsync(bar, _slide.Token);
    }

    // ── Profile popup ──────────────────────────────────────────────────────────────────────────────

    private void ProfileCard_Click(object? sender, RoutedEventArgs e)
    {
        // A click on one of the card's chips (schedule, sound) bubbles up to here too: that chip
        // opens its own dialog, not the popup (WPF: ChipButton_Click marks it handled).
        if (!ReferenceEquals(e.Source, sender)) return;
        if ((sender as Control)?.DataContext is ProfileCardViewModel vm) OpenProfileDetail(vm);
    }

    internal void OpenProfileDetail(ProfileCardViewModel vm)
    {
        var card = this.FindControl<Border>("ProfileDetailCard")!;
        card.MinHeight = 0;
        vm.NotifyDetailModalOpened();
        // Picking a name chip collapses the chips row; the popup may shrink again then.
        vm.OnSuggestionSelected = () => card.MinHeight = 0;
        // Unhooked from the previous profile first: the device lists and their selections then
        // switch over cleanly, so no selection of one profile is ever written into another.
        var content = this.FindControl<Grid>("ProfileDetailContent")!;
        content.DataContext = null;
        content.DataContext = vm;
        this.FindControl<Grid>("ProfileDetailOverlay")!.IsVisible = true;
        // After layout, typing or hiding the chips can't shrink the popup.
        Dispatcher.UIThread.Post(() =>
        {
            card.UpdateLayout();
            if (card.Bounds.Height > 0) card.MinHeight = card.Bounds.Height;
        }, DispatcherPriority.Loaded);
    }

    private void CloseProfileDetailOverlay()
    {
        this.FindControl<Border>("ProfileDetailCard")!.MinHeight = 0;
        this.FindControl<Grid>("ProfileDetailOverlay")!.IsVisible = false;
    }

    private void OverlayClose_Click(object? sender, RoutedEventArgs e) => CloseProfileDetailOverlay();

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        var overlay = this.FindControl<Grid>("ProfileDetailOverlay")!;
        if (overlay.IsVisible) { overlay.IsVisible = false; return; }
        Close();
    }

    // ── Logs, sound settings ───────────────────────────────────────────────────────────────────────

    private void UpdateLogsButton()
    {
        var button = this.FindControl<Button>("LogsButton")!;
        var count = _errorTracker.Count;
        button.IsVisible = count > 0;
        if (count > 0) this.FindControl<TextBlock>("LogsButtonText")!.Text = $"⚠ {count} Error{(count == 1 ? "" : "s")} This Session";
    }

    // The session log window comes with the remaining dialogs (next step).
    private void LogsButton_Click(object? sender, RoutedEventArgs e) =>
        _logger.Warning("SettingsWindow", "The session log isn't available on macOS/Linux yet.");

    private void OpenSoundSettings_Click(object? sender, RoutedEventArgs e)
    {
        if (!SystemSoundSettings.Open(out var error))
        {
            _logger.Warning("SettingsWindow.OpenSoundSettings", error);
            _errorTracker.Record(ErrorCode.SoundSettingsOpenFailed, "Sound Settings Could Not Open",
                $"Could not open the system's sound settings: {error}");
        }
    }

    // ── Window size and place ──────────────────────────────────────────────────────────────────────

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.SettingsCardExpanded) && _viewModel.SettingsCardExpanded)
            ShowPanel("SettingsBodyScrollViewer");
    }

    private void OnBoundsChanged()
    {
        if (_restoringBounds || !IsVisible) return; // (not before the window is first shown)
        if (_boundsTimer == null)
        {
            _boundsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _boundsTimer.Tick += (_, _) => { _boundsTimer.Stop(); SaveWindowBounds(); };
        }
        _boundsTimer.Stop();
        _boundsTimer.Start();
    }

    // The config keeps the Windows app's units (device-independent pixels), so a config moved
    // between systems puts the window in the same place. Saving and restoring use the same factor
    // (the main screen's), so the window comes back exactly where it was on mixed-scale setups.
    private double Scaling => Screens.Primary?.Scaling is > 0 and var s ? s : DesktopScaling > 0 ? DesktopScaling : 1;

    private void RestoreWindowBounds()
    {
        var cfg = _configService.Current;
        _restoringBounds = true;
        try
        {
            if (cfg.WindowWidth >= 200) Width = cfg.WindowWidth;
            if (cfg.WindowHeight >= 200) Height = cfg.WindowHeight;

            if (cfg.WindowLeft.HasValue && cfg.WindowTop.HasValue)
            {
                var scale = Scaling;
                var area = VirtualScreen(scale);
                var left = Math.Clamp(cfg.WindowLeft.Value, area.X, Math.Max(area.X, area.Right - Width));
                var top = Math.Clamp(cfg.WindowTop.Value, area.Y, Math.Max(area.Y, area.Bottom - Height));
                WindowStartupLocation = WindowStartupLocation.Manual;
                Position = new PixelPoint((int)Math.Round(left * scale), (int)Math.Round(top * scale));
            }
        }
        finally { _restoringBounds = false; }
    }

    // All screens together, in device-independent pixels (WPF's VirtualScreen*).
    private Rect VirtualScreen(double scale)
    {
        var all = Screens.All;
        if (all.Count == 0) return new Rect(0, 0, double.MaxValue / 4, double.MaxValue / 4);
        var px = all.Select(s => s.Bounds).Aggregate((a, b) => a.Union(b));
        return new Rect(px.X / scale, px.Y / scale, px.Width / scale, px.Height / scale);
    }

    private void SaveWindowBounds()
    {
        if (WindowState != WindowState.Normal || _viewModel == null) return;
        var cfg = _configService.Current;
        cfg.WindowWidth = Width;
        cfg.WindowHeight = Height;
        cfg.WindowLeft = Position.X / Scaling;
        cfg.WindowTop = Position.Y / Scaling;
        _configService.SaveDeferred();
    }

    // ── Closing ────────────────────────────────────────────────────────────────────────────────────

    // Close hides to the tray / menu bar when that's on; otherwise it quits the app (Windows).
    private bool _quitAfterClose;

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_viewModel == null) return;
        _boundsTimer?.Stop();
        SaveWindowBounds();

        // The app or the system is quitting (Quit, logging out): let it (WPF ignores a cancel then).
        if (e.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown) return;

        if (_viewModel.CloseToTray)
        {
            e.Cancel = true;
            Hide();
        }
        else
        {
            _quitAfterClose = true; // once this window has closed (quitting closes every window)
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        // Only reached on a real close (closing to the tray cancels first), e.g. quitting.
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.ProfileDeletedOrCloned -= CloseProfileDetailOverlay;
            _errorTracker.ErrorAdded -= _errorAddedHandler;
            _viewModel.Dispose();
        }
        base.OnClosed(e);
        if (_quitAfterClose && Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            Dispatcher.UIThread.Post(() => desktop.Shutdown());
    }

    private sealed class Observer<T>(Action<T> onNext) : IObserver<T>
    {
        public void OnNext(T value) => onNext(value);
        public void OnError(Exception error) { }
        public void OnCompleted() { }
    }
}
