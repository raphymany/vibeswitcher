using System.Diagnostics;
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
    private CancellationTokenSource? _flashCts;

    // Wired up by App.xaml.cs after ProfileSwitchOrchestrator is created.
    internal Action<DeviceProfile>? SwitchRequested;

    public TrayService(IConfigService configService)
    {
        _configService = configService;

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
            AppLogger.Error("TrayService", ex);
            SessionErrorTracker.Record(ErrorCode.TrayIconCreateFailed, "Tray Icon Could Not Be Created",
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

    public void ClearIconCache() => _iconCache.Clear();

    public void UpdateIcon(DeviceProfile? activeProfile)
    {
        var iconPath = activeProfile?.IconPath;
        var icon = IconHelper.LoadIcon(iconPath, _configService.IconsDir);
        _taskbarIcon.Icon = icon;
        _taskbarIcon.ToolTipText = BuildTooltip(activeProfile);
    }

    private string BuildTooltip(DeviceProfile? activeProfile)
    {
        var header = activeProfile != null ? $"VibeSwitcher — {activeProfile.Name}" : "VibeSwitcher";
        if (header.Length > 127) return header[..127];

        var profilesWithHotkeys = _configService.Current.Profiles
            .OrderBy(p => p.SortOrder)
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
        var profiles = _configService.Current.Profiles.OrderBy(p => p.SortOrder).ToList();
        if (profiles.Count <= 1) return;

        var activeId = _configService.Current.ActiveProfileId;
        var currentIndex = profiles.FindIndex(p => p.Id == activeId);
        if (currentIndex == -1)
            AppLogger.Warning("TrayService.CycleNextProfile", "Active profile not found — cycling from first profile.");
        var nextIndex = (currentIndex + 1) % profiles.Count;
        SwitchRequested?.Invoke(profiles[nextIndex]);
    }

    public void FlashSwitch(DeviceProfile restoredProfile)
    {
        var old = _flashCts;
        var cts = new CancellationTokenSource();
        _flashCts = cts;
        old?.Cancel();
        old?.Dispose();

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(80, cts.Token);
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher == null || cts.IsCancellationRequested) return;

                await dispatcher.InvokeAsync(() =>
                {
                    if (cts.IsCancellationRequested) return;
                    _taskbarIcon.Icon = IconHelper.LoadIcon(null, _configService.IconsDir);
                });

                await Task.Delay(320, cts.Token);
                if (cts.IsCancellationRequested) return;

                await dispatcher.InvokeAsync(() =>
                {
                    if (cts.IsCancellationRequested) return;
                    UpdateIcon(restoredProfile);
                });
            }
            catch (OperationCanceledException) { }
        });
    }

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
        catch (Exception ex) { AppLogger.Warning("TrayService.RebuildMenu", ex.Message); }

        var activeId = _configService.Current.ActiveProfileId;
        var profiles = _configService.Current.Profiles.OrderBy(p => p.SortOrder).ToList();

        if (profiles.Count > 0)
        {
            foreach (var profile in profiles)
            {
                var item = new MenuItem
                {
                    Header = BuildProfileHeader(profile),
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

        var soundSettingsItem = new MenuItem { Header = BuildActionHeader("🔊", "Open Sound Settings"), Padding = new Thickness(12, 8, 16, 8) };
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
                AppLogger.Warning("TrayService.SoundSettings", ex.Message);
                SessionErrorTracker.Record(ErrorCode.SoundSettingsOpenFailed, "Sound Settings Could Not Open",
                    $"Could not open Windows Sound settings: {ex.Message}");
            }
        };
        _contextMenu.Items.Add(soundSettingsItem);

        var aboutItem = new MenuItem { Header = BuildActionHeader("ℹ", "About"), Padding = new Thickness(12, 8, 16, 8) };
        aboutItem.Click += (_, _) => OpenAbout();
        _contextMenu.Items.Add(aboutItem);

        _contextMenu.Items.Add(BuildSeparator());

        var exitItem = new MenuItem { Header = BuildActionHeader("✕", "Exit"), Padding = new Thickness(12, 8, 16, 8) };
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

    public void ShowBalloon(string title, string message)
    {
        _taskbarIcon.ShowNotification(
            title,
            message,
            icon: NotificationIcon.None,
            customIconHandle: IconHelper.GetBalloonIconHandle(),
            largeIcon: true);
    }

    public void RecreateIcon()
    {
        try
        {
            _taskbarIcon.ForceCreate(false);
        }
        catch (Exception ex)
        {
            AppLogger.Error("TrayService.RecreateIcon", ex);
            SessionErrorTracker.Record(ErrorCode.TrayIconCreateFailed, "Tray Icon Could Not Be Restored",
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

    private static void OpenAbout()
    {
        if (Application.Current is App app)
            app.OpenAboutWindow();
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
    private UIElement BuildProfileHeader(DeviceProfile profile)
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
            iconElement = new System.Windows.Controls.Image
            {
                Source = src,
                Width = 20,
                Height = 20,
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
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

        var nameBlock = new TextBlock { Text = profile.Name, FontSize = 13, FontWeight = FontWeights.SemiBold };
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
        sp.Children.Add(new System.Windows.Controls.Image
        {
            Source = appIconSource,
            Width = 20,
            Height = 20,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        });
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
    private static UIElement BuildActionHeader(string icon, string label)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        var iconBlock = new TextBlock
        {
            Text = icon,
            Width = 22,
            FontSize = 13,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        iconBlock.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
        var labelBlock = new TextBlock { Text = label, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
        labelBlock.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryText");
        sp.Children.Add(iconBlock);
        sp.Children.Add(labelBlock);
        return sp;
    }

    public void Dispose()
    {
        _flashCts?.Cancel();
        _flashCts?.Dispose();
        _taskbarIcon.Dispose();
    }
}
