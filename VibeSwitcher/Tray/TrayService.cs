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
    // Mute indicator state — a static colored badge is composited onto the active icon
    // (no flashing). _muted gates the overlay that UpdateIcon applies.
    private bool _muted;
    private System.Drawing.Color _muteColor;
    private string? _muteTooltip;

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

        // Required when creating TaskbarIcon programmatically (not via XAML)
        // to trigger Shell_NotifyIcon registration with the system tray.
        try
        {
            _taskbarIcon.ForceCreate(false);
        }
        catch (Exception ex)
        {
            _logger.Error("TrayService", ex);
            _errorTracker.Record(ErrorCode.TrayIconCreateFailed, "Tray Icon Could Not Be Created",
                $"The system tray icon failed to register: {ex.Message}");
        }

        _taskbarIcon.TrayLeftMouseUp += (_, _) =>
        {
            if (_configService.Current.LeftClickCyclesProfiles) CycleNextProfile();
            else Application.Current?.Dispatcher.InvokeAsync(OpenSettings);
        };

        UpdateIcon(null);
        RebuildMenu();
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
        if (_muted)
        {
            var badged = ComposeMutedIcon(icon, _muteColor);
            icon.Dispose();
            icon = badged;
        }

        _taskbarIcon.Icon = icon;
        _taskbarIcon.ToolTipText = _muted ? (_muteTooltip ?? "VibeSwitcher") : BuildTooltip(activeProfile);
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

    // Call whenever mute state changes. Shows a static colored badge on the tray icon
    // (mic-only = red, speakers-only = blue, both = purple) or removes it when nothing is muted.
    // No flashing — the badge is composited onto the current profile/app icon in UpdateIcon.
    public (bool Mic, bool Speakers) MuteState { get; private set; }

    public void UpdateMuteFlash(bool micMuted, bool speakersMuted)
    {
        MuteState = (micMuted, speakersMuted);
        if (!micMuted && !speakersMuted)
        {
            _muted = false;
            _muteTooltip = null;
            RefreshActiveIcon();
            return;
        }

        _muteColor = (micMuted, speakersMuted) switch
        {
            (true, true)  => System.Drawing.Color.FromArgb(150, 70, 230),  // purple — both
            (true, false) => System.Drawing.Color.FromArgb(225, 55, 55),   // red    — mic only
            _             => System.Drawing.Color.FromArgb(45, 120, 230),  // blue   — speakers only
        };
        _muteTooltip = (micMuted, speakersMuted) switch
        {
            (true, true)  => "VibeSwitcher — Mic + Speakers muted",
            (true, false) => "VibeSwitcher — Mic muted",
            _             => "VibeSwitcher — Speakers muted",
        };
        _muted = true;
        RefreshActiveIcon();
    }

    private void RefreshActiveIcon()
    {
        var active = _configService.Current.Profiles
            .FirstOrDefault(p => p.Id == _configService.Current.ActiveProfileId);
        UpdateIcon(active);
    }

    // Composites a small colored mute badge (white-ringed dot) onto the bottom-right
    // corner of the active icon. Keeps the brand/profile icon visible — no full-icon swap.
    private static Icon ComposeMutedIcon(Icon baseIcon, System.Drawing.Color badge)
    {
        using var bmp = new System.Drawing.Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            using (var baseBmp = baseIcon.ToBitmap())
                g.DrawImage(baseBmp, new System.Drawing.Rectangle(0, 0, 32, 32));

            const float d = 15f;
            float x = 32f - d, y = 32f - d;
            using (var ring = new System.Drawing.SolidBrush(System.Drawing.Color.White))
                g.FillEllipse(ring, x - 1.5f, y - 1.5f, d + 3f, d + 3f);
            using (var fill = new System.Drawing.SolidBrush(badge))
                g.FillEllipse(fill, x, y, d, d);
        }

        // GetHicon returns an HICON we own — Icon.FromHandle does NOT take ownership,
        // so we copy to a stream for an independent Icon then destroy the raw handle.
        var hIcon = bmp.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(hIcon);
            using var ms = new MemoryStream();
            temp.Save(ms);
            ms.Seek(0, SeekOrigin.Begin);
            return new Icon(ms);
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public void RebuildMenu()
    {
        _contextMenu = new ContextMenu();
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

        var miniItem = new MenuItem { Header = BuildActionHeader("IcoCompact", "Mini Mode"), Padding = new Thickness(12, 8, 16, 8) };
        miniItem.Click += (_, _) => OpenMiniMode();

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

        _contextMenu.Items.Add(aboutItem);
        _contextMenu.Items.Add(faqItem);
        _contextMenu.Items.Add(settingsItem);
        _contextMenu.Items.Add(miniItem);
        _contextMenu.Items.Add(soundSettingsItem);

        _contextMenu.Items.Add(BuildSeparator());

        var exitItem = new MenuItem { Header = BuildActionHeader("IcoClose", "Exit"), Padding = new Thickness(12, 8, 16, 8) };
        exitItem.Click += (_, _) => Application.Current.Shutdown();
        _contextMenu.Items.Add(exitItem);

        // Refresh tooltip so hotkey cheat-sheet stays in sync when profiles change.
        var active = _configService.Current.Profiles
            .FirstOrDefault(p => p.Id == _configService.Current.ActiveProfileId);
        _taskbarIcon.ToolTipText = BuildTooltip(active);
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
        _taskbarIcon.ShowNotification(
            title,
            message,
            icon: NotificationIcon.None,
            customIconHandle: IconHelper.GetBalloonIconHandle(),
            largeIcon: true,
            sound: sound);
    }

    public void RecreateIcon()
    {
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

    private static void OpenMiniMode()
    {
        if (Application.Current is App app)
            app.OpenMiniMode();
    }

    private static MenuItem BuildSeparator()
    {
        var line = new Border { Height = 1, Margin = new Thickness(8, 4, 8, 4) };
        line.SetResourceReference(Border.BackgroundProperty, "SeparatorBrush");
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
        label.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryText");
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
