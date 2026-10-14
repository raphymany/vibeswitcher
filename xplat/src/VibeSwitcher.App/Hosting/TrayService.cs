using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using VibeSwitcher.Helpers;
using VibeSwitcher.Models;
using VibeSwitcher.Platform;
using VibeSwitcher.Services;

namespace VibeSwitcher.App.Hosting;

// What the rest of the app needs from the tray / menu-bar icon (the orchestrator, the scheduler, the
// mute banners); tests stand in for it.
internal interface ITrayService
{
    void UpdateIcon(DeviceProfile? activeProfile);
    void SetActiveProfile(Guid activeProfileId);
    void SetSwitchingTooltip(string profileName);
    void ShowBalloon(string title, string message, bool sound = true);
    void RebuildMenu();
    void ClearIconCache();
}

// What the menu's items do (the Windows TrayService reaches these through App).
internal sealed record TrayActions(
    Action OpenSettings, Action OpenSettingsExpanded, Action OpenAbout, Action OpenFaq,
    Action ToggleMiniMode, Func<bool> IsMiniModeActive, Action Quit);

// Fork of Tray/TrayService.cs on Avalonia's TrayIcon: the Linux tray (StatusNotifierItem) and the
// macOS menu bar. Same behaviour as Windows: the icon is the active profile's, the tooltip lists the
// profile shortcuts, and the menu has the app header, the profiles (pinned first, ★), About, Help,
// Settings, Mini Mode, Sound Settings and Quit, following Settings › Tray (BuildEntries).
//   Linux  the menu is the desktop's own (it can't take the Windows styling: each profile is one
//          line, with its icon); a left click cycles profiles or opens Settings, as on Windows
//   macOS  the app's own menu-bar item (Avalonia's never reports a click there): a press opens the
//          Windows-styled menu (StyledTrayMenu) under it — the menu bar's way: there's no separate
//          left-click action there. If that item can't be made, Avalonia's icon with the system menu.
// Banners go to the system's notifications (IDesktopNotifier); the Windows balloon sound has no
// counterpart (the notification server decides).
internal sealed class TrayService : ITrayService, IDisposable
{
    private readonly IConfigService _configService;
    private readonly IAppLogger _logger;
    private readonly ISessionErrorTracker _errorTracker;
    private readonly IDesktopNotifier? _notifier;
    private readonly TrayActions _actions;
    private readonly TrayIcon? _trayIcon;  // Linux (and the macOS fallback)
    private readonly IMenuBarItem? _menuBar; // macOS
    // Each profile's icon, decoded once (RebuildMenu and UpdateIcon never read from disk again).
    private readonly Dictionary<Guid, byte[]> _iconBytes = new();
    private readonly Dictionary<Guid, Bitmap> _menuIcons = new();
    private readonly Dictionary<Guid, NativeMenuItem> _profileItems = new();
    private NativeMenuItem? _miniItem;
    // macOS: the styled menu, and the item's pictures (PNG, decoded once per profile).
    private readonly StyledTrayMenu? _styled;
    private readonly Dictionary<Guid, byte[]> _menuBarPngs = new();

    // Wired up by the host once the orchestrator exists.
    internal Action<DeviceProfile>? SwitchRequested;

    public TrayService(IConfigService configService, IAppLogger logger, ISessionErrorTracker errorTracker,
        IDesktopNotifier? notifier, TrayActions actions, IMenuBarItem? menuBarItem = null)
    {
        _configService = configService;
        _logger = logger;
        _errorTracker = errorTracker;
        _notifier = notifier;
        _actions = actions;

        if (menuBarItem != null)
        {
            _menuBar = menuBarItem;
            _styled = new StyledTrayMenu(MenuIcon);
            _menuBar.Clicked += OpenStyledMenu;
        }
        else
        {
            _trayIcon = new TrayIcon { ToolTipText = "VibeSwitcher", IsVisible = false, Menu = new NativeMenu() };
            _trayIcon.Clicked += (_, _) =>
            {
                if (_configService.Current.LeftClickCyclesProfiles) CycleNextProfile();
                else _actions.OpenSettings();
            };
            if (Application.Current is { } app) TrayIcon.SetIcons(app, [_trayIcon]); // (the app removes it on exit)
        }

        UpdateIcon(null);
        RebuildMenu();
    }

    // For tests: what the icon shows.
    internal TrayIcon Icon => _trayIcon!;
    internal NativeMenu Menu => _trayIcon!.Menu!;
    internal StyledTrayMenu? Styled => _styled;
    internal string ToolTip { get; private set; } = "VibeSwitcher";

    // macOS: the styled menu, built fresh (the Mini Mode item says what it will do now) under the
    // item — or by the top-right corner of the screen when that can't be told. A press while it's
    // open closes it, as menu-bar menus do.
    internal void OpenStyledMenu() => _styled?.Toggle(BuildEntries(), _menuBar?.MenuAnchor());

    // (The CI check on a real Mac: a press on the item, as the user's, and where the menu goes.)
    internal bool HasMenuBarItem => _menuBar != null;
    internal void PressMenuBarItem() => _menuBar?.PerformClick();
    internal (double X, double Y)? MenuBarAnchor => _menuBar?.MenuAnchor();

    private void SetToolTip(string text)
    {
        ToolTip = text;
        if (_trayIcon != null) _trayIcon.ToolTipText = text;
        _menuBar?.SetToolTip(text);
    }

    // The icon appears only once the app is ready (the splash has finished), so a click can't land
    // on a half-started app; banners raised before then (start-up hotkey conflicts) wait for it.
    private bool _iconShown;
    private readonly List<(string Title, string Message)> _pendingBalloons = new();

    internal bool IsShown => _iconShown;

    public void ShowIcon()
    {
        if (_iconShown) return;
        _iconShown = true;
        try
        {
            if (_trayIcon != null) _trayIcon.IsVisible = true;
            _menuBar?.SetVisible(true);
        }
        catch (Exception ex)
        {
            _logger.Error("TrayService.ShowIcon", ex);
            _errorTracker.Record(ErrorCode.TrayIconCreateFailed, "Tray Icon Could Not Be Created",
                $"The tray icon failed to appear: {ex.Message}");
        }
        foreach (var (title, message) in _pendingBalloons) ShowBalloon(title, message);
        _pendingBalloons.Clear();
    }

    // (The menu's pictures aren't disposed: the menu showing now may still be using them.)
    public void ClearIconCache()
    {
        _iconBytes.Clear();
        _menuIcons.Clear();
        _menuBarPngs.Clear();
    }

    public void UpdateIcon(DeviceProfile? activeProfile)
    {
        try
        {
            if (_trayIcon != null) _trayIcon.Icon = new WindowIcon(new MemoryStream(IconBytes(activeProfile), writable: false));
            _menuBar?.SetImage(MenuBarPng(activeProfile));
        }
        catch (Exception ex) { _logger.Warning("TrayService.UpdateIcon", ex.Message); }
        SetToolTip(BuildTooltip(activeProfile));
    }

    // The icon as a 36-pixel PNG (18 points on a Retina menu bar).
    private byte[] MenuBarPng(DeviceProfile? profile)
    {
        var key = profile is { IconPath.Length: > 0 } ? profile.Id : Guid.Empty;
        if (_menuBarPngs.TryGetValue(key, out var cached)) return cached;
        using var source = new MemoryStream(IconBytes(profile), writable: false);
        using var bitmap = Bitmap.DecodeToWidth(source, 36, BitmapInterpolationMode.HighQuality);
        using var png = new MemoryStream();
        bitmap.Save(png);
        return _menuBarPngs[key] = png.ToArray();
    }

    // The profile's .ico (or the app icon), read once per profile.
    private byte[] IconBytes(DeviceProfile? profile)
    {
        if (profile == null || string.IsNullOrEmpty(profile.IconPath))
        {
            using var fallback = IconHelper.GetDefaultIcon();
            return fallback.Data;
        }
        if (_iconBytes.TryGetValue(profile.Id, out var cached)) return cached;
        using var icon = IconHelper.LoadIcon(profile.IconPath, _configService.IconsDir);
        return _iconBytes[profile.Id] = icon.Data;
    }

    // "VibeSwitcher — Gaming", then each profile with a shortcut (as on Windows, up to 127 characters).
    internal string BuildTooltip(DeviceProfile? activeProfile)
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

    internal void CycleNextProfile()
    {
        var profiles = _configService.Current.Profiles
            .OrderByDescending(p => p.IsPinned).ThenBy(p => p.SortOrder).ToList();
        if (profiles.Count <= 1) return;

        var activeId = _configService.Current.ActiveProfileId;
        var currentIndex = profiles.FindIndex(p => p.Id == activeId);
        if (currentIndex == -1)
            _logger.Warning("TrayService.CycleNextProfile", "Active profile not found — cycling from first profile.");
        SwitchRequested?.Invoke(profiles[(currentIndex + 1) % profiles.Count]);
    }

    // The menu's lines, as the Windows menu has them (Quit for Windows' "Exit", as Settings › Tray says).
    internal List<TrayEntry> BuildEntries()
    {
        var entries = new List<TrayEntry>
        {
            new(TrayEntryKind.Header, "VibeSwitcher", _actions.OpenSettings),
            new(TrayEntryKind.Separator),
        };
        var activeId = _configService.Current.ActiveProfileId;
        var allProfiles = _configService.Current.Profiles.OrderBy(p => p.SortOrder).ToList();
        var pinned = allProfiles.Where(p => p.IsPinned).ToList();
        var unpinned = allProfiles.Where(p => !p.IsPinned).ToList();
        TrayEntry Profile(DeviceProfile p, bool isPinned) =>
            new(TrayEntryKind.Profile, isPinned ? $"★ {p.Name}" : p.Name, () => SwitchRequested?.Invoke(p), p, isPinned, activeId == p.Id);
        if (allProfiles.Count > 0)
        {
            entries.AddRange(pinned.Select(p => Profile(p, true)));
            if (pinned.Count > 0 && unpinned.Count > 0) entries.Add(new(TrayEntryKind.Separator));
            entries.AddRange(unpinned.Select(p => Profile(p, false)));
            entries.Add(new(TrayEntryKind.Separator));
        }
        var cfg = _configService.Current;
        if (cfg.TrayShowAbout) entries.Add(new(TrayEntryKind.Action, "About", _actions.OpenAbout, IconKey: "IcoInfo"));
        if (cfg.TrayShowFaq) entries.Add(new(TrayEntryKind.Action, "Help & FAQ", _actions.OpenFaq, IconKey: "IcoHelp"));
        entries.Add(new(TrayEntryKind.Action, "Settings", _actions.OpenSettingsExpanded, IconKey: "IcoSettings")); // always available
        if (cfg.TrayShowMiniMode)
            entries.Add(_actions.IsMiniModeActive()
                ? new(TrayEntryKind.Action, "Exit Mini Mode", _actions.ToggleMiniMode, IconKey: "IcoExpand")
                : new(TrayEntryKind.Action, "Mini Mode", _actions.ToggleMiniMode, IconKey: "IcoCompact"));
        if (cfg.TrayShowSoundSettings) entries.Add(new(TrayEntryKind.Action, "Open Sound Settings", OpenSoundSettings, IconKey: "IcoSpeaker"));
        entries.Add(new(TrayEntryKind.Separator));
        entries.Add(new(TrayEntryKind.Action, "Quit", _actions.Quit, IconKey: "IcoClose"));
        return entries;
    }

    public void RebuildMenu()
    {
        // The shortcut list in the tooltip follows profile changes.
        var active = _configService.Current.Profiles.FirstOrDefault(p => p.Id == _configService.Current.ActiveProfileId);
        SetToolTip(BuildTooltip(active));
        if (_trayIcon == null) return; // (macOS: built each time it opens)

        var menu = new NativeMenu();
        _profileItems.Clear();
        _miniItem = null;
        foreach (var entry in BuildEntries())
        {
            switch (entry.Kind)
            {
                case TrayEntryKind.Separator:
                    menu.Add(new NativeMenuItemSeparator());
                    break;
                case TrayEntryKind.Header:
                    var header = new NativeMenuItem(entry.Label) { Icon = AppIconBitmap.Value };
                    header.Click += (_, _) => entry.Run?.Invoke();
                    menu.Add(header);
                    break;
                case TrayEntryKind.Profile:
                    menu.Add(ProfileItem(entry));
                    break;
                default:
                    var item = ActionItem(entry.Label, entry.Run!);
                    if (entry.Run == _actions.ToggleMiniMode) _miniItem = item;
                    menu.Add(item);
                    break;
            }
        }
        // The Mini Mode item says what it will do, checked each time the menu opens.
        menu.Opening += (_, _) => RefreshMiniMenuItem();
        _trayIcon.Menu = menu;
    }
    private NativeMenuItem ProfileItem(TrayEntry entry)
    {
        var item = new NativeMenuItem(entry.Label)
        {
            ToggleType = NativeMenuItemToggleType.Radio,
            IsChecked = entry.IsChecked,
            Icon = MenuIcon(entry.Profile!),
        };
        item.Click += (_, _) => entry.Run?.Invoke();
        _profileItems[entry.Profile!.Id] = item;
        return item;
    }

    private Bitmap? MenuIcon(DeviceProfile profile)
    {
        if (_menuIcons.TryGetValue(profile.Id, out var cached)) return cached;
        try
        {
            using var stream = new MemoryStream(IconBytes(profile), writable: false);
            return _menuIcons[profile.Id] = Bitmap.DecodeToWidth(stream, 32, BitmapInterpolationMode.HighQuality);
        }
        catch (Exception ex)
        {
            _logger.Warning("TrayService.MenuIcon", ex.Message);
            return null;
        }
    }

    private static NativeMenuItem ActionItem(string label, Action action)
    {
        var item = new NativeMenuItem(label);
        item.Click += (_, _) => action();
        return item;
    }

    internal void RefreshMiniMenuItem()
    {
        if (_miniItem != null) _miniItem.Header = _actions.IsMiniModeActive() ? "Exit Mini Mode" : "Mini Mode";
    }

    private void OpenSoundSettings()
    {
        if (Settings.SystemSoundSettings.Open(out var error)) return;
        _logger.Warning("TrayService.SoundSettings", error);
        _errorTracker.Record(ErrorCode.SoundSettingsOpenFailed, "Sound Settings Could Not Open",
            $"Could not open the system's sound settings: {error}");
    }

    // Fast path: only the profile items' checks change on a switch.
    public void SetActiveProfile(Guid activeProfileId)
    {
        foreach (var (id, item) in _profileItems) item.IsChecked = id == activeProfileId;
    }

    public void SetSwitchingTooltip(string profileName) => SetToolTip($"Switching to {profileName}...");

    public void ShowBalloon(string title, string message, bool sound = true)
    {
        if (!_iconShown)
        {
            _pendingBalloons.Add((title, message));
            return;
        }
        if (_notifier == null)
        {
            _logger.Info("TrayService.Banner", $"{title}: {message}");
            return;
        }
        _ = _notifier.ShowAsync(title, message); // (never throws; a banner that can't show is just dropped)
    }

    private static readonly Lazy<Bitmap?> AppIconBitmap = new(() =>
    {
        try { return new Bitmap(Avalonia.Platform.AssetLoader.Open(new Uri("avares://VibeSwitcher/Assets/Icons/vs-icon-32.png"))); }
        catch (Exception) { return null; }
    });

    public void Dispose()
    {
        _styled?.Dispose();
        _menuBar?.Dispose();
        if (_trayIcon != null)
        {
            _trayIcon.IsVisible = false;
            // (Taken off the app's list too, so its own exit doesn't dispose it a second time.)
            if (Application.Current is { } app && TrayIcon.GetIcons(app) is { } icons) icons.Remove(_trayIcon);
            _trayIcon.Dispose();
        }
        ClearIconCache();
    }
}
