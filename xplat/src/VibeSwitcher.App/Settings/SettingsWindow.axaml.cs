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
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using VibeSwitcher.App.Controls;
using VibeSwitcher.App.Dialogs;
using VibeSwitcher.App.Threading;
using VibeSwitcher.Helpers;
using VibeSwitcher.Models;
using VibeSwitcher.Platform;
using VibeSwitcher.Services;
using VibeSwitcher.ViewModels;

namespace VibeSwitcher.App.Settings;

// Port of Views/SettingsWindow.xaml.cs: the window around the shared SettingsViewModel — panel
// navigation, the title bar, the sliding filter bar, the profile popup, remembering the window's
// size and place, closing to the tray, the Settings / About / FAQ pages, and Mini Mode.
public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;
    private readonly IConfigService _configService;
    private readonly IHotkeyService _hotkeyService;
    private readonly IAppLogger _logger;
    private readonly ISessionErrorTracker _errorTracker;
    private readonly DialogService _dialogs;
    private readonly EventHandler _errorAddedHandler;

    // What the Settings › Install & Updates page describes: this copy of the app and the data folder
    // (the config folder: the one holding Icons/). Settable for tests, followed by RefreshStorageInfo.
    internal InstallInfo Install { get; set; } = InstallInfo.Current();
    internal string DataFolder { get; set; } = "";
    internal string Version { get; set; } = Cli.Version; // (with -preview, as --version prints it)

    // Uninstalling this copy (and its data, when asked) once the user has confirmed — set by the
    // app (next steps); until then it only logs.
    internal Func<InstallInfo, bool, Task>? Uninstall { get; set; }

    // The shortcut capture and the backup file pickers (tests stand in for them).
    internal Func<HotkeyDefinition, string, HotkeyDefinition?> CaptureHotkey { get; set; }
    internal Func<Task<string?>> PickExportPath { get; set; }
    internal Func<Task<string?>> PickImportPath { get; set; }

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
        _hotkeyService = hotkeyService;
        _logger = logger;
        _errorTracker = errorTracker;
        _dialogs = new DialogService(() => this)
        {
            Audio = audioService as IPlatformAudioService, Logger = logger, Config = configService,
            Apps = PlatformApps.Catalog(), AppIcon = Imaging.AppIcons.Load, Headsets = PlatformApps.HeadsetAccess(),
        };
        CaptureHotkey = _dialogs.ShowHotkeyCapture;
        HasFocus = () => IsActive;
        CanFade = () => ActualTransparencyLevel != WindowTransparencyLevel.None;
        PickExportPath = () => _dialogs.PickSaveJsonAsync("Export VibeSwitcher Config", "vibeswitcher-backup.json");
        PickImportPath = () => _dialogs.PickOpenJsonAsync("Import VibeSwitcher Config");
        Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://VibeSwitcher/Assets/Icons/vs-icon-256.png")));
        ApplyChrome();

        if (configService == null) // previewer
        {
            _viewModel = null!;
            _errorAddedHandler = (_, _) => { };
            return;
        }

        _viewModel = new SettingsViewModel(
            configService, audioService, hotkeyService, startupService, _dialogs, logger, errorTracker,
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

        // (The Install page is filled in each time the window shows, below.)
        DataFolder = System.IO.Path.GetDirectoryName(configService.IconsDir) ?? "";
        Loaded += (_, _) => AlignFaqLinks();

        WatchMiniList();
        Activated += (_, _) => OnActivated();
        Deactivated += (_, _) => OnDeactivated();
        PointerEntered += (_, _) => OnPointerEnteredWindow();
        PointerExited += (_, _) => OnPointerExitedWindow();
        if (configService.Current.CompactMode)
            EnterCompact();

        _errorAddedHandler = (_, _) => Dispatcher.UIThread.Post(UpdateLogsButton);
        this.GetObservable(IsVisibleProperty).Subscribe(new Observer<bool>(visible =>
        {
            if (visible)
            {
                _errorTracker.ErrorAdded += _errorAddedHandler;
                UpdateLogsButton();
                _viewModel.RefreshActiveStates();
                RefreshStorageInfo();
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
        if (IsCompact) ExitCompact();
        _viewModel.SettingsCardExpanded = true;
        ShowPanel("SettingsBodyScrollViewer");
    }

    public void OpenAboutPanel() { if (IsCompact) ExitCompact(); ShowPanel("AboutPanel"); }
    public void OpenFaqPanel() { if (IsCompact) ExitCompact(); ShowPanel("FaqPanel"); }

    private void NavProfiles_Click(object? sender, RoutedEventArgs e) => ShowPanel("ProfilesPanel");
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
        UpdateGrip();
    }

    // (macOS windows are resized from their edges by the system, with no grip; Mini Mode doesn't resize.)
    private void UpdateGrip() =>
        this.FindControl<ResizeGrip>("Grip")!.IsVisible =
            WindowState == WindowState.Normal && WindowChrome.Style != ChromeStyle.Mac && !IsCompact;

    // ── Mini Mode ──────────────────────────────────────────────────────────────────────────────────

    // The window shrinks into a small profile switcher: 300 px wide, as tall as its list (at most
    // 85% of the screen), no top bar, no resizing; it keeps a place of its own, separate from the
    // full window's, and can stay on top and fade while another app is in front.
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

        // First-time guidance and empty-state guard. Skipped during the start-up restore (window
        // not shown yet) — CompactMode=true implies prior use.
        if (IsLoaded)
        {
            if (_viewModel.Profiles.Count == 0)
            {
                ((IDialogService)_dialogs).ShowAlert("Mini Mode needs a profile",
                    "Mini Mode shrinks VibeSwitcher into a compact profile switcher. " +
                    "Create at least one profile first, then come back to set it up.");
                return;
            }

            var cfg0 = _configService.Current;
            if (!cfg0.CompactIntroShown)
            {
                cfg0.CompactIntroShown = true;
                _configService.SaveDeferred();

                bool customize = DialogService.Wait(_dialogs.ShowAsync(new ConfirmDialog(
                    "Welcome to Mini Mode",
                    "Mini Mode shrinks VibeSwitcher into a small, always-handy profile switcher. " +
                    "You can choose which profiles appear and pick between a row list or an icon grid — " +
                    "or skip this and start with the defaults.",
                    "Customize First",
                    subtitle: "You can change all of this later in Settings → Mini Window.",
                    iconGeometry: "IcoCompact",
                    iconBgResource: "Accent")));
                if (customize)
                    CompactCustomize_Click(this, new RoutedEventArgs());
            }

            // The dialogs above keep the app running — the global hotkey may have already toggled
            // Mini Mode while one of them was open.
            if (IsCompact) return;
        }

        // Flush the full-window bounds before any geometry changes so nothing mini leaks into them.
        _boundsTimer?.Stop();
        if (WindowState != WindowState.Normal)
        {
            // Maximized, or full screen on macOS. Avalonia's Width/Height are the maximized size until
            // the system has restored the window (later, on macOS and Linux), so the full window's
            // size comes from its last saved normal bounds (never saved while maximized) instead.
            _restoringBounds = true;
            try { WindowState = WindowState.Normal; }
            finally { _restoringBounds = false; }
            var saved = _configService.Current;
            _fullWidth = saved.WindowWidth >= 200 ? saved.WindowWidth : Width;
            _fullHeight = saved.WindowHeight >= 200 ? saved.WindowHeight : Height;
        }
        else
        {
            if (IsLoaded) SaveWindowBounds();
            _fullWidth = Width;
            _fullHeight = Height;
        }
        _fullMinWidth = MinWidth;
        _fullMinHeight = MinHeight;

        IsCompact = true;

        RefreshMiniList();
        ShowPanel("MiniPanel");
        SetCompactChrome(true);

        _restoringBounds = true; // (the size and place below aren't the full window's)
        try
        {
            CanResize = false;
            MaxHeight = WorkAreaHeight() * 0.85;
            MinHeight = 0;
            MinWidth = CompactWidth;
            MaxWidth = CompactWidth;
            Width = CompactWidth;
            // (In Avalonia a set Height wins over SizeToContent; in WPF it doesn't.)
            Height = double.NaN;
            SizeToContent = SizeToContent.Height;

            var cfg = _configService.Current;
            if (cfg.CompactWindowLeft.HasValue && cfg.CompactWindowTop.HasValue)
            {
                var scale = Scaling;
                var area = VirtualScreen(scale);
                WindowStartupLocation = WindowStartupLocation.Manual; // keep the pre-show restore from being recentred
                var left = Math.Clamp(cfg.CompactWindowLeft.Value, area.X, Math.Max(area.X, area.Right - CompactWidth));
                var top = Math.Clamp(cfg.CompactWindowTop.Value, area.Y, Math.Max(area.Y, area.Bottom - 200));
                Position = new PixelPoint((int)Math.Round(left * scale), (int)Math.Round(top * scale));
            }

            Topmost = cfg.CompactAlwaysOnTop;
            // (So the fade below can show what's behind the window, not just darken it.)
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent];

            if (!cfg.CompactMode)
            {
                cfg.CompactMode = true;
                _configService.SaveDeferred();
            }
        }
        finally { _restoringBounds = false; }
    }

    public void ExitCompact()
    {
        if (!IsCompact) return;

        _boundsTimer?.Stop(); // (a save started by moving the mini window mustn't run after this)
        var cfg = _configService.Current;
        cfg.CompactWindowLeft = Position.X / Scaling;
        cfg.CompactWindowTop = Position.Y / Scaling;

        IsCompact = false;

        _restoringBounds = true;
        try
        {
            SizeToContent = SizeToContent.Manual;
            MaxWidth = double.PositiveInfinity;
            MaxHeight = double.PositiveInfinity;
            MinWidth = _fullMinWidth;
            MinHeight = _fullMinHeight;
            Width = _fullWidth;
            Height = _fullHeight;
            CanResize = true;
            Topmost = false;
            SetOpacityNow(1.0); // (any fade released)
            TransparencyLevelHint = [];

            SetCompactChrome(false);
            ShowPanel("ProfilesPanel");

            // Put the full window back where it last lived (clamped to the visible screen).
            if (cfg.WindowLeft.HasValue && cfg.WindowTop.HasValue)
            {
                var scale = Scaling;
                var area = VirtualScreen(scale);
                var left = Math.Clamp(cfg.WindowLeft.Value, area.X, Math.Max(area.X, area.Right - Width));
                var top = Math.Clamp(cfg.WindowTop.Value, area.Y, Math.Max(area.Y, area.Bottom - Height));
                Position = new PixelPoint((int)Math.Round(left * scale), (int)Math.Round(top * scale));
            }
        }
        finally { _restoringBounds = false; }

        if (cfg.CompactMode)
        {
            cfg.CompactMode = false;
            _configService.SaveDeferred();
        }
    }

    // The title bar and top bar for Mini Mode (logo + shortcut, pin and expand) or the full window.
    private void SetCompactChrome(bool compact)
    {
        this.FindControl<Border>("NavBar")!.IsVisible = !compact;
        // (The fixed 54 px row itself collapses, not just its content.)
        this.FindControl<Grid>("Root")!.RowDefinitions[1].Height = new GridLength(compact ? 0 : 54);
        this.FindControl<StackPanel>("FullTitleGroup")!.IsVisible = !compact;
        this.FindControl<StackPanel>("MiniTitleGroup")!.IsVisible = compact;
        this.FindControl<Button>("TitleShrinkBtn")!.IsVisible = !compact;
        this.FindControl<Button>("TitleMaxBtn")!.IsVisible = !compact;
        this.FindControl<Button>("TitlePinBtn")!.IsVisible = compact;
        this.FindControl<Button>("TitleExpandBtn")!.IsVisible = compact;
        UpdateGrip();
    }

    // The height of the screen the window is on, without the taskbar / Dock / panels (WPF's
    // SystemParameters.WorkArea), in device-independent pixels.
    private double WorkAreaHeight()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        return screen == null ? 720 : screen.WorkingArea.Height / (screen.Scaling > 0 ? screen.Scaling : 1);
    }

    private void TitleShrink_Click(object? sender, RoutedEventArgs e) => EnterCompact();
    private void TitleExpand_Click(object? sender, RoutedEventArgs e) => ExitCompact();
    private void TitlePin_Click(object? sender, RoutedEventArgs e) =>
        _viewModel.CompactAlwaysOnTop = !_viewModel.CompactAlwaysOnTop;

    // Translucent when inactive: the mini window fades to 65% while another app has focus and the
    // pointer isn't over it; it's solid again as soon as the pointer comes back or it's activated.
    internal void OnActivated()
    {
        FadeOpacityTo(1.0);
    }

    internal void OnDeactivated()
    {
        if (IsCompact && _configService.Current.CompactTranslucent && !IsPointerOver) FadeOpacityTo(0.65);
    }

    internal void OnPointerEnteredWindow()
    {
        if (IsCompact) FadeOpacityTo(1.0);
    }

    internal void OnPointerExitedWindow()
    {
        if (IsCompact && !HasFocus() && _configService.Current.CompactTranslucent) FadeOpacityTo(0.65);
    }

    // Whether this window is the one in front (tests stand in for it: a headless window always is).
    internal Func<bool> HasFocus { get; set; }

    // Whether the system shows this window see-through (Mini Mode asks for it). Where it can't — e.g.
    // Linux without a desktop compositor — a faded window would only look darker, so it stays solid.
    internal Func<bool> CanFade { get; set; }

    // Where the fade is heading (Opacity itself is mid-fade for a moment).
    internal double OpacityTarget { get; private set; } = 1.0;

    private readonly DoubleTransition _fade =
        new() { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(180), Easing = new CubicEaseInOut() };

    private void FadeOpacityTo(double target)
    {
        if (target == OpacityTarget) return;
        if (target < 1.0 && !CanFade()) return;
        Transitions ??= [];
        if (!Transitions.Contains(_fade)) Transitions.Add(_fade);
        OpacityTarget = target;
        Opacity = target;
    }

    private void SetOpacityNow(double value)
    {
        Transitions?.Remove(_fade);
        OpacityTarget = value;
        Opacity = value;
    }

    // Mini Mode lists the profiles in tray order: pinned first, then the user's order — only those
    // picked in Customize, while at least one of them still exists (otherwise, or with none picked,
    // every profile). Rebuilt whenever profiles come, go, move or are pinned.
    private readonly HashSet<ProfileCardViewModel> _miniWatched = [];

    private void WatchMiniList()
    {
        _viewModel.Profiles.CollectionChanged += OnProfilesChanged;
        RefreshMiniList();
    }

    private void OnProfilesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) =>
        RefreshMiniList();

    private void OnCardChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProfileCardViewModel.IsPinned) or nameof(ProfileCardViewModel.SortOrder) or null or "")
            RefreshMiniList();
    }

    internal IReadOnlyList<ProfileCardViewModel> MiniItems { get; private set; } = [];

    internal void RefreshMiniList()
    {
        // Follow the cards that exist now (and only those).
        foreach (var gone in _miniWatched.Except(_viewModel.Profiles).ToList())
        {
            gone.PropertyChanged -= OnCardChanged;
            _miniWatched.Remove(gone);
        }
        foreach (var card in _viewModel.Profiles.Where(_miniWatched.Add))
            card.PropertyChanged += OnCardChanged;

        var cfg = _configService.Current;
        var ids = cfg.CompactProfileIds;
        bool filtered = ids.Count > 0 && _viewModel.Profiles.Any(p => ids.Contains(p.Id));
        MiniItems = _viewModel.Profiles
            .Where(p => !filtered || ids.Contains(p.Id))
            .OrderByDescending(p => p.IsPinned).ThenBy(p => p.SortOrder)
            .ToList();

        // Only the list on show, and only in Mini Mode (entering refreshes it): a reorder changes
        // every card's place, one at a time.
        if (!IsCompact) return;
        bool grid = cfg.CompactLayout == "Grid";
        var rows = this.FindControl<ItemsControl>("MiniList")!;
        var icons = this.FindControl<ItemsControl>("MiniGrid")!;
        rows.ItemsSource = grid ? null : MiniItems;
        icons.ItemsSource = grid ? MiniItems : null;
        rows.IsVisible = !grid;
        icons.IsVisible = grid;
        // The window fits the list again (Avalonia keeps the height it last measured otherwise).
        Height = double.NaN;
    }

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

    // The session log (the log file sits in the data folder, under AppLogger's file name).
    private void LogsButton_Click(object? sender, RoutedEventArgs e) =>
        DialogService.Wait(_dialogs.ShowAsync(
            new SessionLogWindow(_logger, _errorTracker,
                System.IO.Path.Combine(DataFolder, System.IO.Path.GetFileName(AppLogger.LogPath)))));

    private void OpenSoundSettings_Click(object? sender, RoutedEventArgs e)
    {
        if (!SystemSoundSettings.Open(out var error))
        {
            _logger.Warning("SettingsWindow.OpenSoundSettings", error);
            _errorTracker.Record(ErrorCode.SoundSettingsOpenFailed, "Sound Settings Could Not Open",
                $"Could not open the system's sound settings: {error}");
        }
    }

    // ── Settings › Shortcuts and Mini Window ───────────────────────────────────────────────────────

    // Each "Set" asks for a key combination (every hotkey is off meanwhile, so pressing one doesn't
    // fire it), refuses one that's already in use with the option to try again, then registers
    // everything again — as on Windows.
    private void SettingsHotkeyButton_Click(object? sender, RoutedEventArgs e) =>
        AssignHotkey(_viewModel.SettingsHotkey, "Press any key combination to assign a shortcut for opening Settings",
            captured => FindHotkeyConflict(captured, excludeScope: null, excludeSettingsHotkey: true),
            captured => _viewModel.SettingsHotkey = captured);

    private void SettingsHotkeyClear_Click(object? sender, RoutedEventArgs e) =>
        _viewModel.SettingsHotkey = new HotkeyDefinition();

    private void CompactHotkeyButton_Click(object? sender, RoutedEventArgs e) =>
        AssignHotkey(_viewModel.CompactHotkey, "Press any key combination to assign a shortcut for toggling Mini Mode",
            captured => FindHotkeyConflict(captured, excludeScope: null, excludeCompactHotkey: true),
            captured => _viewModel.CompactHotkey = captured);

    private void CompactHotkeyClear_Click(object? sender, RoutedEventArgs e) =>
        _viewModel.CompactHotkey = new HotkeyDefinition();

    private static MuteScope ScopeOf(object? sender) => ((sender as Control)?.Tag as string) switch
    {
        "Mic" => MuteScope.Mic,
        "Speakers" => MuteScope.Speakers,
        _ => MuteScope.Both,
    };

    private void MuteHotkeyClear_Click(object? sender, RoutedEventArgs e) =>
        _viewModel.SetMuteHotkeyFromDialog(ScopeOf(sender), new HotkeyDefinition());

    private void MuteHotkeyButton_Click(object? sender, RoutedEventArgs e)
    {
        var scope = ScopeOf(sender);
        var seed = scope switch
        {
            MuteScope.Mic => _viewModel.MuteMicHotkey,
            MuteScope.Speakers => _viewModel.MuteSpeakersHotkey,
            _ => _viewModel.MuteBothHotkey,
        };
        var subtitle = scope switch
        {
            MuteScope.Mic => "Press any key combination to assign a shortcut for muting the microphone",
            MuteScope.Speakers => "Press any key combination to assign a shortcut for muting speakers",
            _ => "Press any key combination to assign a shortcut for muting all audio",
        };
        AssignHotkey(seed, subtitle,
            captured => FindHotkeyConflict(captured, excludeScope: scope),
            captured => _viewModel.SetMuteHotkeyFromDialog(scope, captured));
    }

    private void AssignHotkey(HotkeyDefinition seed, string subtitle, Func<HotkeyDefinition, string?> conflictOf, Action<HotkeyDefinition> assign)
    {
        _hotkeyService.UnregisterAll();
        try
        {
            while (true)
            {
                var captured = CaptureHotkey(seed, subtitle);
                if (captured == null) break;

                if (!captured.IsEmpty && conflictOf(captured) is { } conflict)
                {
                    bool retry = ((IDialogService)_dialogs).ShowHotkeyConflictRetry("Hotkey Already in Use",
                        $"'{captured.ToDisplayString()}' is already assigned to {conflict}.");
                    if (retry) { seed = captured; continue; }
                    break;
                }

                assign(captured);
                break;
            }
        }
        finally
        {
            // (Also when something above failed: the hotkeys must never stay off.)
            _viewModel.ReregisterHotkeys();
        }
    }

    // Who already uses this key combination: a profile, the Settings or Mini Mode shortcut, or a
    // mute shortcut (other than the one being set) — the Windows wording, quotes included.
    internal string? FindHotkeyConflict(HotkeyDefinition captured, MuteScope? excludeScope,
        bool excludeSettingsHotkey = false, bool excludeCompactHotkey = false)
    {
        var cfg = _configService.Current;
        var profileOwner = cfg.Profiles.FirstOrDefault(p => !p.Hotkey.IsEmpty && captured.Matches(p.Hotkey))?.Name;
        if (profileOwner != null) return $"\"{profileOwner}\"";

        if (!excludeSettingsHotkey && cfg.SettingsHotkey is { IsEmpty: false } settingsHk && captured.Matches(settingsHk))
            return "\"Open / Close VibeSwitcher\"";

        if (!excludeCompactHotkey && cfg.CompactHotkey is { IsEmpty: false } compactHk && captured.Matches(compactHk))
            return "\"Toggle Mini Mode\"";

        var muteChecks = new[]
        {
            (MuteScope.Mic, cfg.MuteMicHotkey, "Mute Microphone"),
            (MuteScope.Speakers, cfg.MuteSpeakersHotkey, "Mute Speakers"),
            (MuteScope.Both, cfg.MuteBothHotkey, "Mute Mic + Speakers"),
        };
        foreach (var (muteScope, muteHk, label) in muteChecks)
        {
            if (muteScope == excludeScope) continue;
            if (muteHk is { IsEmpty: false } && captured.Matches(muteHk)) return $"\"{label}\"";
        }
        return null;
    }

    // Mini Mode's profiles and layout.
    private void CompactCustomize_Click(object? sender, RoutedEventArgs e)
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

        var dialog = new MiniModeSetupDialog(cfg.CompactLayout, choices);
        if (!DialogService.Wait(_dialogs.ShowAsync(dialog))) return;

        cfg.CompactLayout = dialog.SelectedLayout;
        cfg.CompactProfileIds = dialog.SelectedProfileIds;
        _configService.SaveDeferred();
        RefreshMiniList();
    }

    // ── Settings › Devices ─────────────────────────────────────────────────────────────────────────

    private void ManageAliasesButton_Click(object? sender, RoutedEventArgs e) =>
        _dialogs.ShowDeviceAliases(_viewModel.DeviceAliases);

    private async void ExportButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var path = await PickExportPath();
            if (path == null) return;
            string title, message;
            try
            {
                _viewModel.ExportConfig(path);
                (title, message) = ("Export Successful", $"Configuration exported to:\n{path}");
            }
            catch (Exception ex)
            {
                (title, message) = ("Export Failed", $"Could not export configuration:\n{ex.Message}");
            }
            await _dialogs.ShowAlertAsync(title, message);
        }
        catch (Exception ex)
        {
            _logger.Warning("SettingsWindow.Export", ex.Message);
        }
    }

    private async void ImportButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var path = await PickImportPath();
            if (path == null) return;

            var confirmed = await _dialogs.ShowAsync(new ConfirmDialog(
                "Replace Configuration?",
                $"Importing '{System.IO.Path.GetFileName(path)}' will replace all current profiles and settings.",
                "Import",
                subtitle: "Your current profiles and settings will be replaced."));
            if (!confirmed) return;

            if (!_viewModel.ImportConfig(path, out var error))
                await _dialogs.ShowAlertAsync("Import Failed", error ?? "The configuration could not be imported.");
        }
        catch (Exception ex)
        {
            _logger.Warning("SettingsWindow.Import", ex.Message);
        }
    }

    // ── Settings › Install & Updates ───────────────────────────────────────────────────────────────

    // The Install page (and the version on About). Recomputed on every show (the window is hidden,
    // not destroyed, on close).
    internal void RefreshStorageInfo()
    {
        this.FindControl<TextBlock>("AboutVersionText")!.Text = $"Version {Version} — {Wording.Platform}";
        this.FindControl<TextBlock>("InstallVersionText")!.Text = Version;
        this.FindControl<TextBlock>("InstallLocationText")!.Text = Install.Location;
        this.FindControl<TextBlock>("InstallSizeText")!.Text = FormatSize(Install.Location);
        this.FindControl<TextBlock>("DataLocationText")!.Text = DataFolder;
        this.FindControl<TextBlock>("DataSizeText")!.Text = FormatSize(DataFolder);

        this.FindControl<TextBlock>("UninstallDetailText")!.Text = Wording.UninstallDetail(Install.Kind);
        var blocked = Wording.UninstallBlocked(Install);
        var note = this.FindControl<TextBlock>("UninstallBlockedNote")!;
        note.Text = blocked;
        note.IsVisible = blocked != null;
        this.FindControl<Button>("UninstallBtn")!.IsEnabled = blocked == null && !_uninstalling;
    }

    // A folder's (or a single file's: the AppImage) size on disk. Links aren't followed, so a link
    // to somewhere big (or back up the tree) doesn't count.
    internal static string FormatSize(string path)
    {
        try
        {
            long bytes;
            if (File.Exists(path)) bytes = new FileInfo(path).Length;
            else if (Directory.Exists(path))
                bytes = new DirectoryInfo(path).EnumerateFiles("*", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint,
                }).Sum(f => f.Length);
            else return "—";
            return bytes switch
            {
                >= 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024 * 1024):0.##} GB",
                >= 1024L * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
                >= 1024 => $"{bytes / 1024.0:0.#} KB",
                _ => $"{bytes} B",
            };
        }
        catch { return "—"; }
    }

    private void BrowseInstallFolder_Click(object? sender, RoutedEventArgs e)
    {
        // The app bundle or AppImage file is shown in its folder; an installed or build folder opens.
        var location = Install.Location;
        var ok = Install.Kind is InstallKind.MacApp || File.Exists(location)
            ? SystemOpener.Reveal(location, out var error)
            : SystemOpener.OpenFolder(location, out error);
        if (!ok) _logger.Warning("SettingsWindow.OpenFolder", error);
    }

    private void OpenDataFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (!Directory.Exists(DataFolder)) return;
        if (!SystemOpener.OpenFolder(DataFolder, out var error)) _logger.Warning("SettingsWindow.OpenFolder", error);
    }

    private async void UninstallButton_Click(object? sender, RoutedEventArgs e)
    {
        var button = this.FindControl<Button>("UninstallBtn")!;
        try
        {
            if (Wording.UninstallBlocked(Install) != null) return;
            var text = Install.Kind switch
            {
                InstallKind.MacApp => UninstallText.Mac,
                InstallKind.AppImage => UninstallText.LinuxAppImage,
                _ => UninstallText.LinuxPackage,
            };
            var dialog = new UninstallDialog(text.WithDataFolder(DataFolder));
            if (!await _dialogs.ShowAsync(dialog)) return;
            if (Uninstall == null)
            {
                _logger.Warning("SettingsWindow.Uninstall", "Uninstalling from the app isn't available here.");
                return;
            }
            // (Off while it runs — also when Settings is reopened: a password prompt can stay open
            // for minutes.)
            _uninstalling = true;
            ShowShortcutsInfo(_shortcutsInfo);
            button.IsEnabled = false;
            button.Content = "Uninstalling…";
            await Uninstall(Install, dialog.DeleteData);
        }
        catch (Exception ex)
        {
            _logger.Warning("SettingsWindow.Uninstall", ex.Message);
        }
        finally
        {
            if (_uninstalling)
            {
                _uninstalling = false;
                ShowShortcutsInfo(_shortcutsInfo);
                button.IsEnabled = Wording.UninstallBlocked(Install) == null;
                button.Content = "Uninstall VibeSwitcher…";
            }
        }
    }

    private bool _uninstalling;

    // ── Shortcuts: how they work on this desktop (Linux) ───────────────────────────────────────────

    // Allowing (true) or stopping GNOME keyboard shortcuts — set by the app.
    internal Func<bool, Task>? SetGnomeShortcuts { get; set; }

    // What the Shortcuts page says about it (null: nothing — the card is hidden). The app sets it
    // again whenever it changes (an approval in the desktop's dialog, say).
    internal ShortcutsInfo? Shortcuts
    {
        get => _shortcutsInfo;
        set => ShowShortcutsInfo(value);
    }

    private ShortcutsInfo? _shortcutsInfo;
    private bool _changingShortcuts;

    internal void ShowShortcutsInfo(ShortcutsInfo? info)
    {
        _shortcutsInfo = info;
        this.FindControl<Border>("ShortcutsWayCard")!.IsVisible = info != null;
        if (info == null) return;
        this.FindControl<TextBlock>("ShortcutsWayNote")!.Text = info.Note;
        var keys = this.FindControl<TextBlock>("ShortcutsWayKeys")!;
        keys.Text = string.Join("\n", info.Keys);
        keys.IsVisible = info.Keys.Count > 0;
        var button = this.FindControl<Button>("ShortcutsWayBtn")!;
        button.IsVisible = info.Choice != GnomeChoice.None;
        button.Content = info.ChoiceText;
        button.IsEnabled = !_changingShortcuts && !_uninstalling; // (not during an uninstall: its shortcuts are going)
        // (Allowing is the step forward: the primary style; stopping is an ordinary button.)
        if (this.TryFindResource(info.Choice == GnomeChoice.Allow ? "PrimaryButton" : "ActionButton", out var theme) && theme is ControlTheme t)
            button.Theme = t;
    }

    private async void ShortcutsWayButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_shortcutsInfo is not { Choice: not GnomeChoice.None } info || SetGnomeShortcuts == null || _changingShortcuts || _uninstalling) return;
        var button = this.FindControl<Button>("ShortcutsWayBtn")!;
        _changingShortcuts = true;
        button.IsEnabled = false;
        try { await SetGnomeShortcuts(info.Choice == GnomeChoice.Allow); }
        catch (Exception ex) { _logger.Warning("SettingsWindow.Shortcuts", ex.Message); }
        finally
        {
            _changingShortcuts = false;
            button.IsEnabled = !_uninstalling;
        }
    }

    // ── About and FAQ ──────────────────────────────────────────────────────────────────────────────

    private void LinkChip_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is not string url) return;
        if (!SystemOpener.OpenUrl(url, out var error)) _logger.Warning("LinkChip_Click", error);
    }

    // A web link inside the FAQ's text (Windows: Hyperlink_RequestNavigate, which also lists the failure
    // among the session's errors).
    private void FaqUrl_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is not string url) return;
        if (SystemOpener.OpenUrl(url, out var error)) return;
        _logger.Warning("SettingsWindow.Hyperlink", error);
        _errorTracker.Record(ErrorCode.HyperlinkOpenFailed, "Link Could Not Be Opened", $"Could not open '{url}': {error}");
    }

    // FAQ inline actions: jump straight to the place the answer talks about.
    private void FaqAction_Click(object? sender, RoutedEventArgs e)
    {
        switch ((sender as Control)?.Tag as string)
        {
            case "newProfile":
                ShowPanel("ProfilesPanel");
                _viewModel.AddProfileCommand.Execute(null);
                break;
            case "profiles":
                ShowPanel("ProfilesPanel");
                break;
            case "filters":
                ShowPanel("ProfilesPanel");
                if (!_filterBarOpen) FiltersBtn_Click(this, new RoutedEventArgs());
                break;
            case "miniTry":
                TitleShrink_Click(this, new RoutedEventArgs());
                break;
            case var tag when tag != null && FaqCategories.TryGetValue(tag, out var category):
                ExpandSettings();
                _viewModel.SelectedCategory = category;
                break;
        }
    }

    // The FAQ's links sit inside its sentences (InlineUIContainer). Avalonia puts a control's bottom on
    // the line's baseline unless the control says where its own baseline is: a link's is its text's
    // ascent, so the link reads on the same line as the words around it.
    private void AlignFaqLinks()
    {
        var theme = this.FindResource("FaqLink");
        foreach (var link in this.FindControl<ScrollViewer>("FaqPanel")!.GetLogicalDescendants().OfType<Button>())
        {
            if (!ReferenceEquals(link.Theme, theme)) continue;
            var metrics = new Typeface(link.FontFamily, link.FontStyle, link.FontWeight).GlyphTypeface.Metrics;
            TextBlock.SetBaselineOffset(link, Math.Abs(metrics.Ascent) * link.FontSize / metrics.DesignEmHeight);
        }
    }

    private static readonly Dictionary<string, string> FaqCategories = new()
    {
        ["shortcuts"] = "shortcuts",
        ["notifications"] = "notif",
        ["backup"] = "devices",
        ["miniSettings"] = "compact",
        ["traySettings"] = "tray",
        ["startupSettings"] = "startup",
        ["deviceAliases"] = "devices",
        ["installSettings"] = "uninstall",
    };

    // ── Window size and place ──────────────────────────────────────────────────────────────────────

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.SettingsCardExpanded))
        {
            if (_viewModel.SettingsCardExpanded) ShowPanel("SettingsBodyScrollViewer");
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
        if (IsCompact)
        {
            // Never let mini geometry overwrite the full-window slots.
            cfg.CompactWindowLeft = Position.X / Scaling;
            cfg.CompactWindowTop = Position.Y / Scaling;
        }
        else
        {
            cfg.WindowWidth = Width;
            cfg.WindowHeight = Height;
            cfg.WindowLeft = Position.X / Scaling;
            cfg.WindowTop = Position.Y / Scaling;
        }
        _configService.SaveDeferred();
    }

    // ── Closing ────────────────────────────────────────────────────────────────────────────────────

    // Close hides to the tray / menu bar when that's on; otherwise it quits the app (Windows).
    private bool _quitAfterClose;

    // False on a Linux desktop that shows no tray icons (stock GNOME without the AppIndicator
    // extension): hidden in a tray that isn't there, the app couldn't be reached.
    internal bool TrayAvailable { get; set; } = true;

    // Closing just closes this window — no hiding, no quitting (the diagnostics' pictures).
    internal bool ClosesForGood { get; set; }

    // (For tests: whether closing this window is quitting the app.)
    internal bool QuitsWhenClosed => _quitAfterClose;

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_viewModel == null) return;
        _boundsTimer?.Stop();
        SaveWindowBounds();

        // The app or the system is quitting (Quit, logging out): let it (WPF ignores a cancel then).
        if (e.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown) return;

        // (Only where there's a tray to come back from: on a desktop without one, closing quits.)
        if (ClosesForGood) return;
        if (_viewModel.CloseToTray && TrayAvailable)
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
            _viewModel.Profiles.CollectionChanged -= OnProfilesChanged;
            foreach (var card in _miniWatched) card.PropertyChanged -= OnCardChanged;
            _miniWatched.Clear();
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
