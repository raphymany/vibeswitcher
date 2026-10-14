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

// Fork of Tray/TrayService.cs on Avalonia's TrayIcon: the Linux tray (StatusNotifierItem) and, for
// now, the macOS menu bar (the styled menu-bar popup comes next). Same behaviour as Windows: the icon
// is the active profile's, the tooltip lists the profile shortcuts, a left click cycles profiles or
// opens the window, and the menu has the app header, the profiles (pinned first, ★), About, Help,
// Settings, Mini Mode, Sound Settings and Quit, following Settings › Tray. The menu is the system's
// own, so it can't take the Windows menu's styling: each profile is one line, with its icon.
// Banners go to the system's notifications (IDesktopNotifier); the Windows balloon sound has no
// counterpart (the notification server decides).
internal sealed class TrayService : ITrayService, IDisposable
{
    private readonly IConfigService _configService;
    private readonly IAppLogger _logger;
    private readonly ISessionErrorTracker _errorTracker;
    private readonly IDesktopNotifier? _notifier;
    private readonly TrayActions _actions;
    private readonly TrayIcon _trayIcon;
    // Each profile's icon, decoded once (RebuildMenu and UpdateIcon never read from disk again).
    private readonly Dictionary<Guid, byte[]> _iconBytes = new();
    private readonly Dictionary<Guid, Bitmap> _menuIcons = new();
    private readonly Dictionary<Guid, NativeMenuItem> _profileItems = new();
    private NativeMenuItem? _miniItem;

    // Wired up by the host once the orchestrator exists.
    internal Action<DeviceProfile>? SwitchRequested;

    public TrayService(IConfigService configService, IAppLogger logger, ISessionErrorTracker errorTracker,
        IDesktopNotifier? notifier, TrayActions actions)
    {
        _configService = configService;
        _logger = logger;
        _errorTracker = errorTracker;
        _notifier = notifier;
        _actions = actions;

        _trayIcon = new TrayIcon { ToolTipText = "VibeSwitcher", IsVisible = false, Menu = new NativeMenu() };
        _trayIcon.Clicked += (_, _) =>
        {
            if (_configService.Current.LeftClickCyclesProfiles) CycleNextProfile();
            else _actions.OpenSettings();
        };
        if (Application.Current is { } app) TrayIcon.SetIcons(app, [_trayIcon]); // (the app removes it on exit)

        UpdateIcon(null);
        RebuildMenu();
    }

    // For tests: what the icon shows.
    internal TrayIcon Icon => _trayIcon;
    internal NativeMenu Menu => _trayIcon.Menu!;

    // The icon appears only once the app is ready (the splash has finished), so a click can't land
    // on a half-started app; banners raised before then (start-up hotkey conflicts) wait for it.
    private bool _iconShown;
    private readonly List<(string Title, string Message)> _pendingBalloons = new();

    internal bool IsShown => _iconShown;

    public void ShowIcon()
    {
        if (_iconShown) return;
        _iconShown = true;
        try { _trayIcon.IsVisible = true; }
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
    }

    public void UpdateIcon(DeviceProfile? activeProfile)
    {
        try { _trayIcon.Icon = new WindowIcon(new MemoryStream(IconBytes(activeProfile), writable: false)); }
        catch (Exception ex) { _logger.Warning("TrayService.UpdateIcon", ex.Message); }
        _trayIcon.ToolTipText = BuildTooltip(activeProfile);
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

    public void RebuildMenu()
    {
        var menu = new NativeMenu();
        _profileItems.Clear();

        var header = new NativeMenuItem("VibeSwitcher") { Icon = AppIconBitmap.Value };
        header.Click += (_, _) => _actions.OpenSettings();
        menu.Add(header);
        menu.Add(new NativeMenuItemSeparator());

        var activeId = _configService.Current.ActiveProfileId;
        var allProfiles = _configService.Current.Profiles.OrderBy(p => p.SortOrder).ToList();
        var pinned = allProfiles.Where(p => p.IsPinned).ToList();
        var unpinned = allProfiles.Where(p => !p.IsPinned).ToList();
        if (allProfiles.Count > 0)
        {
            foreach (var profile in pinned) menu.Add(ProfileItem(profile, pinned: true, activeId));
            if (pinned.Count > 0 && unpinned.Count > 0) menu.Add(new NativeMenuItemSeparator());
            foreach (var profile in unpinned) menu.Add(ProfileItem(profile, pinned: false, activeId));
            menu.Add(new NativeMenuItemSeparator());
        }

        var cfg = _configService.Current;
        if (cfg.TrayShowAbout) menu.Add(ActionItem("About", _actions.OpenAbout));
        if (cfg.TrayShowFaq) menu.Add(ActionItem("Help & FAQ", _actions.OpenFaq));
        menu.Add(ActionItem("Settings", _actions.OpenSettingsExpanded)); // always available
        _miniItem = ActionItem("Mini Mode", _actions.ToggleMiniMode);
        if (cfg.TrayShowMiniMode) menu.Add(_miniItem);
        if (cfg.TrayShowSoundSettings) menu.Add(ActionItem("Open Sound Settings", OpenSoundSettings));
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(ActionItem("Quit", _actions.Quit)); // (Windows: "Exit"; Settings › Tray names it Quit)

        // The Mini Mode item says what it will do, checked each time the menu opens.
        menu.Opening += (_, _) => RefreshMiniMenuItem();
        RefreshMiniMenuItem();
        _trayIcon.Menu = menu;

        // The shortcut list in the tooltip follows profile changes.
        var active = _configService.Current.Profiles.FirstOrDefault(p => p.Id == _configService.Current.ActiveProfileId);
        _trayIcon.ToolTipText = BuildTooltip(active);
    }

    private NativeMenuItem ProfileItem(DeviceProfile profile, bool pinned, Guid? activeId)
    {
        var item = new NativeMenuItem(pinned ? $"★ {profile.Name}" : profile.Name)
        {
            ToggleType = NativeMenuItemToggleType.Radio,
            IsChecked = activeId == profile.Id,
            Icon = MenuIcon(profile),
        };
        var captured = profile;
        item.Click += (_, _) => SwitchRequested?.Invoke(captured);
        _profileItems[profile.Id] = item;
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

    public void SetSwitchingTooltip(string profileName) => _trayIcon.ToolTipText = $"Switching to {profileName}...";

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
        _trayIcon.IsVisible = false;
        // (Taken off the app's list too, so its own exit doesn't dispose it a second time.)
        if (Application.Current is { } app && TrayIcon.GetIcons(app) is { } icons) icons.Remove(_trayIcon);
        _trayIcon.Dispose();
        ClearIconCache();
    }
}
