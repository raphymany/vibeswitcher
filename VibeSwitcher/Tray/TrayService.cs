using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VibeSwitcher.Helpers;
using VibeSwitcher.Models;
using VibeSwitcher.Services;
using H.NotifyIcon;
using H.NotifyIcon.Core;

namespace VibeSwitcher.Tray;

public class TrayService : IDisposable
{
    private readonly TaskbarIcon _taskbarIcon;
    private ContextMenu _contextMenu = new();
    private readonly IConfigService _configService;
    // Caches the ImageSource for each profile's icon so RebuildMenu never reads from disk.
    private readonly Dictionary<Guid, ImageSource> _iconCache = new();
    // Caches raw icon bytes per profile so UpdateIcon avoids disk reads on repeat switches.
    // Bytes (not Icon objects) are cached because H.NotifyIcon disposes the Icon it holds on each change.
    private readonly Dictionary<Guid, byte[]> _trayIconBytesCache = new();

    private readonly IAppLogger _logger;
    private readonly ISessionErrorTracker _errorTracker;

    // Wired up by App.xaml.cs after ProfileSwitchOrchestrator is created.
    internal Action<DeviceProfile>? SwitchRequested;

    public TrayService(IConfigService configService, IAppLogger logger, ISessionErrorTracker errorTracker)
    {
        _configService = configService;
        _logger = logger;
        _errorTracker = errorTracker;

        _taskbarIcon = new TaskbarIcon
        {
            ToolTipText = "VibeSwitcher",
        };

        _taskbarIcon.TrayLeftMouseUp += (_, _) =>
        {
            if (_configService.Current.LeftClickCyclesProfiles) CycleNextProfile();
            else Application.Current?.Dispatcher.InvokeAsync(OpenSettings);
        };

        UpdateIcon(null);
        RebuildMenu();
        // Subscribed on the icon, not the menu — RebuildMenu replaces the menu object.
        _taskbarIcon.TrayContextMenuOpen += (_, _) => RefreshMiniMenuItem();
    }

    // ── Tray menu warm-up ────────────────────────────────────────────────────
    // The menu's first-ever open in the PROCESS pays a heavy one-time cost (JIT, default
    // menu styles, popup plumbing). When that cost is paid during a real right-click, the
    // focus handoff misses: the popup window doesn't exist yet when the tray hands it
    // focus, Windows activates the app window instead, and the menu flashes and closes.
    // The warm-up below absorbs that cost by opening and closing a menu invisibly.
    //
    // Hard-won constraints — this has regressed FOUR times. Read before touching:
    // 1. The cost is PER-PROCESS, not per menu instance: before any priming existed the
    //    failure only ever hit the first click after launch, never after profile edits
    //    (which always recreate the menu). The warm-up therefore runs exactly ONCE
    //    (_menuWarmedUp); re-priming each rebuilt menu caused a flash on every
    //    profile-name keystroke / schedule / sound change.
    // 2. THE ROOT CAUSE OF EVERY FLASH (diagnosed with a window-show hook: a full-size
    //    212x444 popup appearing at (0,0)): closing a menu popup is ASYNCHRONOUS — it
    //    fades out. Earlier warm-ups opened the real menu shrunk to 0x0/transparent, then
    //    restored its size in a finally block. The restore raced the fade-out and resized
    //    the still-visible popup back to full size at the clamped position (off-screen
    //    coordinates clamp to the monitor's top-left corner). The invisible open was never
    //    the problem; the synchronous restore was.
    // 3. Therefore: warm up a SACRIFICIAL menu and never restore anything. It uses the
    //    same control types (ContextMenu, MenuItem, Separator → same template/JIT cost as
    //    the real menu), stays 0x0/transparent/shadowless through its entire async close,
    //    and is discarded. The real menu is never touched, so no restore can race.
    // 4. The warm-up MUST complete before the tray icon registers (see ShowIcon): a user
    //    can't right-click an icon that doesn't exist yet, so priming there closes the
    //    launch race that a deferred (ApplicationIdle) prime loses while startup keeps
    //    the dispatcher busy.
    private bool _menuWarmedUp;

    private void WarmUpMenuInfrastructure()
    {
        if (_menuWarmedUp) return;
        _menuWarmedUp = true;
        var dummy = new ContextMenu
        {
            Opacity = 0,
            MaxWidth = 0,
            MaxHeight = 0,
            HasDropShadow = false,
            Placement = System.Windows.Controls.Primitives.PlacementMode.AbsolutePoint,
            HorizontalOffset = -32000,
            VerticalOffset = -32000,
        };
        dummy.Items.Add(new MenuItem { Header = "" });
        dummy.Items.Add(BuildSeparator());
        dummy.IsOpen = true;
        dummy.UpdateLayout();
        dummy.IsOpen = false;
        // Deliberately no property restore: the dummy remains invisible while its popup
        // finishes closing, then it is garbage — that is the entire point (see note 2/3).
    }

    // The icon is registered with the shell only once the app is ready (the splash
    // animation has completed), so users can't interact with a half-started app.
    private bool _iconShown;
    private readonly List<(string Title, string Message, bool Sound)> _pendingBalloons = new();

    public void ShowIcon()
    {
        if (_iconShown) return;
        _iconShown = true;

        // Warm the menu machinery BEFORE the icon becomes clickable — the user can't
        // right-click an icon that isn't registered yet, so the very first click is
        // guaranteed to hit a warm process (the at-idle prime alone loses this race).
        try { WarmUpMenuInfrastructure(); }
        catch (Exception ex) { _logger.Warning("TrayService.PrimeMenu", ex.Message); }

        try
        {
            // Required when creating TaskbarIcon programmatically (not via XAML)
            // to trigger Shell_NotifyIcon registration with the system tray.
            _taskbarIcon.ForceCreate(false);
        }
        catch (Exception ex)
        {
            _logger.Error("TrayService.ShowIcon", ex);
            _errorTracker.Record(ErrorCode.TrayIconCreateFailed, "Tray Icon Could Not Be Created",
                $"The system tray icon failed to register: {ex.Message}");
            _pendingBalloons.Clear(); // icon never registered — don't try to show balloons on it
            return;
        }

        foreach (var (title, message, sound) in _pendingBalloons)
            ShowBalloon(title, message, sound);
        _pendingBalloons.Clear();
    }

    public void ClearIconCache()
    {
        _trayIconBytesCache.Clear();
        _iconCache.Clear();
    }

    public void UpdateIcon(DeviceProfile? activeProfile)
    {
        Icon icon;
        if (activeProfile == null || string.IsNullOrEmpty(activeProfile.IconPath))
        {
            // LoadIcon with null path returns CopyIcon(GetDefaultIcon()) — no disk I/O.
            // A fresh copy is required because H.NotifyIcon disposes the icon it previously held.
            icon = IconHelper.LoadIcon(null, _configService.IconsDir);
        }
        else if (_trayIconBytesCache.TryGetValue(activeProfile.Id, out var cachedBytes))
        {
            // Reconstruct from cached bytes — no disk I/O.
            using var ms = new MemoryStream(cachedBytes, writable: false);
            icon = new Icon(ms);
        }
        else
        {
            // First load: read from disk once and cache the bytes.
            icon = IconHelper.LoadIcon(activeProfile.IconPath, _configService.IconsDir);
            using var ms = new MemoryStream();
            icon.Save(ms);
            _trayIconBytesCache[activeProfile.Id] = ms.ToArray();
        }
        _taskbarIcon.Icon = icon;
        _taskbarIcon.ToolTipText = BuildTooltip(activeProfile);
    }

    private string BuildTooltip(DeviceProfile? activeProfile)
    {
        var header = activeProfile != null ? $"VibeSwitcher — {activeProfile.Name}" : "VibeSwitcher";
        if (header.Length > 127) return header[..127];

        var profilesWithHotkeys = _configService.Current.Profiles
            .OrderByDescending(p => p.IsPinned).ThenBy(p => p.SortOrder)
            .Where(p => !p.Hotkey.IsEmpty)
            .Select(p => $"{p.Name}: {p.Hotkey.ToDisplayString()}")
            .ToList();

        if (profilesWithHotkeys.Count == 0) return header;

        var lines = new System.Text.StringBuilder(header);
        foreach (var line in profilesWithHotkeys)
        {
            if (lines.Length + 1 + line.Length > 127) break;
            lines.Append('\n');
            lines.Append(line);
        }
        return lines.ToString();
    }

    private void CycleNextProfile()
    {
        var profiles = _configService.Current.Profiles
            .OrderByDescending(p => p.IsPinned).ThenBy(p => p.SortOrder).ToList();
        if (profiles.Count <= 1) return;

        var activeId = _configService.Current.ActiveProfileId;
        var currentIndex = profiles.FindIndex(p => p.Id == activeId);
        if (currentIndex == -1)
            _logger.Warning("TrayService.CycleNextProfile", "Active profile not found — cycling from first profile.");
        var nextIndex = (currentIndex + 1) % profiles.Count;
        SwitchRequested?.Invoke(profiles[nextIndex]);
    }

    public void RebuildMenu()
    {
        _contextMenu = new ContextMenu();
        // On the menu's first-ever open its popup window doesn't exist yet when the
        // tray library hands it focus, so the handoff misses, Windows activates the
        // app window instead, and the menu instantly dismisses. Re-assert foreground
        // onto the menu once it has actually opened (its window exists by then).
        _contextMenu.Opened += (_, _) =>
        {
            if (System.Windows.Interop.HwndSource.FromVisual(_contextMenu)
                is System.Windows.Interop.HwndSource src)
                NativeMethods.WinApi.SetForegroundWindow(src.Handle);
        };
        _taskbarIcon.ContextMenu = _contextMenu;

        try
        {
            var appIconSource = IconHelper.GetAppIconImageSource();
            var headerItem = new MenuItem
            {
                Header = BuildAppHeader(appIconSource),
                Padding = new Thickness(12, 6, 16, 6),
            };
            headerItem.Click += (_, _) => OpenSettings();
            _contextMenu.Items.Add(headerItem);
            _contextMenu.Items.Add(BuildSeparator());
        }
        catch (Exception ex) { _logger.Warning("TrayService.RebuildMenu", ex.Message); }

        var activeId = _configService.Current.ActiveProfileId;
        var allProfiles = _configService.Current.Profiles.OrderBy(p => p.SortOrder).ToList();
        var pinned   = allProfiles.Where(p => p.IsPinned).ToList();
        var unpinned = allProfiles.Where(p => !p.IsPinned).ToList();

        if (allProfiles.Count > 0)
        {
            foreach (var profile in pinned)
            {
                var item = new MenuItem
                {
                    Header = BuildProfileHeader(profile, pinned: true),
                    IsChecked = activeId.HasValue && profile.Id == activeId.Value,
                    Padding = new Thickness(12, 8, 16, 8),
                    Tag = profile.Id,
                };
                var capturedProfile = profile;
                item.Click += (_, _) => SwitchRequested?.Invoke(capturedProfile);
                _contextMenu.Items.Add(item);
            }

            if (pinned.Count > 0 && unpinned.Count > 0)
                _contextMenu.Items.Add(BuildSeparator());

            foreach (var profile in unpinned)
            {
                var item = new MenuItem
                {
                    Header = BuildProfileHeader(profile, pinned: false),
                    IsChecked = activeId.HasValue && profile.Id == activeId.Value,
                    Padding = new Thickness(12, 8, 16, 8),
                    Tag = profile.Id,
                };
                var capturedProfile = profile;
                item.Click += (_, _) => SwitchRequested?.Invoke(capturedProfile);
                _contextMenu.Items.Add(item);
            }

            _contextMenu.Items.Add(BuildSeparator());
        }

        var aboutItem = new MenuItem { Header = BuildActionHeader("IcoInfo", "About"), Padding = new Thickness(12, 8, 16, 8) };
        aboutItem.Click += (_, _) => OpenAbout();

        var faqItem = new MenuItem { Header = BuildActionHeader("IcoHelp", "Help & FAQ"), Padding = new Thickness(12, 8, 16, 8) };
        faqItem.Click += (_, _) => OpenFaq();

        var settingsItem = new MenuItem { Header = BuildActionHeader("IcoSettings", "Settings"), Padding = new Thickness(12, 8, 16, 8) };
        settingsItem.Click += (_, _) => OpenSettingsExpanded();

        _miniItem = new MenuItem { Header = BuildActionHeader("IcoCompact", "Mini Mode"), Padding = new Thickness(12, 8, 16, 8) };
        _miniItem.Click += (_, _) => ToggleMiniMode();

        var soundSettingsItem = new MenuItem { Header = BuildActionHeader("IcoSpeaker", "Open Sound Settings"), Padding = new Thickness(12, 8, 16, 8) };
        soundSettingsItem.Click += (_, _) =>
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
                _logger.Warning("TrayService.SoundSettings", ex.Message);
                _errorTracker.Record(ErrorCode.SoundSettingsOpenFailed, "Sound Settings Could Not Open",
                    $"Could not open Windows Sound settings: {ex.Message}");
            }
        };

        var cfg = _configService.Current;
        if (cfg.TrayShowAbout) _contextMenu.Items.Add(aboutItem);
        if (cfg.TrayShowFaq)   _contextMenu.Items.Add(faqItem);
        _contextMenu.Items.Add(settingsItem);                       // always available
        if (cfg.TrayShowMiniMode)      _contextMenu.Items.Add(_miniItem);
        if (cfg.TrayShowSoundSettings) _contextMenu.Items.Add(soundSettingsItem);

        _contextMenu.Items.Add(BuildSeparator());

        var exitItem = new MenuItem { Header = BuildActionHeader("IcoClose", "Exit"), Padding = new Thickness(12, 8, 16, 8) };
        exitItem.Click += (_, _) => Application.Current.Shutdown();
        _contextMenu.Items.Add(exitItem);

        // Refresh tooltip so hotkey cheat-sheet stays in sync when profiles change.
        var active = _configService.Current.Profiles
            .FirstOrDefault(p => p.Id == _configService.Current.ActiveProfileId);
        _taskbarIcon.ToolTipText = BuildTooltip(active);

        // Deliberately NO warm-up here: the first-open cost is per-process and already
        // absorbed once in ShowIcon. Re-priming every rebuilt menu is what used to flash
        // the top-left corner on every profile-name keystroke / schedule / sound change.
    }

    // Fast path: only flip IsChecked on profile items — no menu rebuild needed on a simple switch.
    public void SetActiveProfile(Guid activeProfileId)
    {
        foreach (var item in _contextMenu.Items.OfType<MenuItem>())
        {
            if (item.Tag is Guid id)
                item.IsChecked = id == activeProfileId;
        }
    }

    public void ShowBalloon(string title, string message, bool sound = true)
    {
        // Balloons raised before the icon exists (e.g. startup hotkey conflicts)
        // are queued and flushed when the icon appears.
        if (!_iconShown)
        {
            _pendingBalloons.Add((title, message, sound));
            return;
        }
        _taskbarIcon.ShowNotification(
            title,
            message,
            icon: NotificationIcon.None,
            customIconHandle: IconHelper.GetBalloonIconHandle(),
            largeIcon: true,
            sound: sound);

        // Known H.NotifyIcon 2.0.x bug (fixed upstream in PR #239, unreleased): the
        // notification's NIM_MODIFY omits NIF_SHOWTIP, which flips the icon out of
        // standard-tooltip mode — the hover tooltip silently stops appearing after any
        // balloon. Re-asserting the tooltip text sends a NIM_MODIFY that restores it.
        var tip = _taskbarIcon.ToolTipText;
        _taskbarIcon.ToolTipText = "";
        _taskbarIcon.ToolTipText = tip;
    }

    public void RecreateIcon()
    {
        if (!_iconShown) return; // Explorer restarted before the icon was ever shown
        try
        {
            _taskbarIcon.ForceCreate(false);
        }
        catch (Exception ex)
        {
            _logger.Error("TrayService.RecreateIcon", ex);
            _errorTracker.Record(ErrorCode.TrayIconCreateFailed, "Tray Icon Could Not Be Restored",
                $"The tray icon failed to re-register after Explorer restarted: {ex.Message}");
        }
    }

    public void SetSwitchingTooltip(string profileName)
    {
        _taskbarIcon.ToolTipText = $"Switching to {profileName}...";
    }

    private static void OpenSettings()
    {
        if (Application.Current is App app)
            app.OpenSettingsWindow();
    }

    private static void OpenSettingsExpanded()
    {
        if (Application.Current is App app)
            app.OpenSettingsWindowExpanded();
    }

    private static void OpenAbout()
    {
        if (Application.Current is App app)
            app.OpenAboutPanel();
    }

    private static void OpenFaq()
    {
        if (Application.Current is App app)
            app.OpenFaqPanel();
    }

    private static void ToggleMiniMode()
    {
        if (Application.Current is App app)
            app.ToggleMiniMode();
    }

    // The mini item flips between entering and leaving mini mode depending on the
    // window's current state, refreshed each time the menu opens.
    private MenuItem? _miniItem;

    private void RefreshMiniMenuItem()
    {
        if (_miniItem == null) return;
        bool active = Application.Current is App app && app.IsMiniModeActive;
        _miniItem.Header = active
            ? BuildActionHeader("IcoExpand", "Exit Mini Mode")
            : BuildActionHeader("IcoCompact", "Mini Mode");
    }

    // Runs the action immediately, or — if the tray menu is currently open — defers it
    // until the menu closes, so window activation can't dismiss the user's menu.
    public void RunWhenContextMenuClosed(Action action)
    {
        var menu = _contextMenu;
        if (!menu.IsOpen)
        {
            action();
            return;
        }
        void Handler(object sender, RoutedEventArgs e)
        {
            menu.Closed -= Handler;
            action();
        }
        menu.Closed += Handler;
    }

    private static MenuItem BuildSeparator()
    {
        var line = new Border { Height = 1, Margin = new Thickness(8, 4, 8, 4) };
        line.SetResourceReference(Border.BackgroundProperty, "TrayMenuSeparatorBrush");
        return new MenuItem
        {
            Tag = "sep",
            IsEnabled = false,
            IsHitTestVisible = false,
            Header = line,
        };
    }

    // Two-line profile item: [profile icon]  Name / Mode subtitle
    private UIElement BuildProfileHeader(DeviceProfile profile, bool pinned = false)
    {
        var modeLabel = profile.Mode switch
        {
            ProfileMode.Playback  => "Playback Only",
            ProfileMode.Recording => "Recording Only",
            ProfileMode.Both      => "Both Devices",
            _                     => "",
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Profile icon — loaded once per profile and cached so RebuildMenu never reads from disk.
        UIElement iconElement;
        try
        {
            if (!_iconCache.TryGetValue(profile.Id, out var src))
            {
                var ico = IconHelper.LoadIcon(profile.IconPath, _configService.IconsDir);
                src = IconHelper.ToImageSource(ico);
                ico.Dispose();
                _iconCache[profile.Id] = src;
            }
            var profileIcon = new System.Windows.Controls.Image
            {
                Source = src,
                Width = 20,
                Height = 20,
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            RenderOptions.SetBitmapScalingMode(profileIcon, BitmapScalingMode.HighQuality);
            iconElement = profileIcon;
        }
        catch
        {
            iconElement = new TextBlock
            {
                Text = "•",
                FontSize = 15,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
        }
        Grid.SetColumn(iconElement, 0);

        var nameText = pinned ? $"★ {profile.Name}" : profile.Name;
        var nameBlock = new TextBlock { Text = nameText, FontSize = 13, FontWeight = FontWeights.SemiBold };
        nameBlock.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryText");

        var subBlock = new TextBlock { Text = modeLabel, FontSize = 11, Margin = new Thickness(0, 1, 0, 0) };
        subBlock.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");

        var stack = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
        stack.Children.Add(nameBlock);
        stack.Children.Add(subBlock);
        if (!profile.Hotkey.IsEmpty)
        {
            var hkBlock = new TextBlock { Text = profile.Hotkey.ToDisplayString(), FontSize = 10, Margin = new Thickness(0, 1, 0, 0) };
            hkBlock.SetResourceReference(TextBlock.ForegroundProperty, "TertiaryText");
            stack.Children.Add(hkBlock);
        }
        Grid.SetColumn(stack, 1);

        grid.Children.Add(iconElement);
        grid.Children.Add(stack);
        return grid;
    }

    private static UIElement BuildAppHeader(ImageSource appIconSource)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        var appIcon = new System.Windows.Controls.Image
        {
            Source = appIconSource,
            Width = 20,
            Height = 20,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        RenderOptions.SetBitmapScalingMode(appIcon, BitmapScalingMode.HighQuality);
        sp.Children.Add(appIcon);
        var label = new TextBlock
        {
            Text = "VibeSwitcher",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        // The wordmark is brand-orange everywhere (title bar, About, splash) — keep the tray in step.
        label.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
        sp.Children.Add(label);
        return sp;
    }

    // Single-line action item: [icon]  Label
    private static UIElement BuildActionHeader(string geometryKey, string label)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        var iconHost = new Grid { Width = 20, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        var path = new System.Windows.Shapes.Path
        {
            Data = (System.Windows.Media.Geometry)Application.Current.FindResource(geometryKey),
            Width = 15,
            Height = 15,
            Stretch = System.Windows.Media.Stretch.Uniform,
            Fill = System.Windows.Media.Brushes.Transparent,
            StrokeThickness = 1.4,
            StrokeLineJoin = System.Windows.Media.PenLineJoin.Round,
            StrokeStartLineCap = System.Windows.Media.PenLineCap.Round,
            StrokeEndLineCap = System.Windows.Media.PenLineCap.Round,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        path.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "SecondaryText");
        iconHost.Children.Add(path);
        var labelBlock = new TextBlock { Text = label, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
        labelBlock.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryText");
        sp.Children.Add(iconHost);
        sp.Children.Add(labelBlock);
        return sp;
    }

    public void Dispose()
    {
        _taskbarIcon.Dispose();
    }
}
