using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Path = Avalonia.Controls.Shapes.Path;
using VibeSwitcher.App.Controls;
using VibeSwitcher.Models;

namespace VibeSwitcher.App.Hosting;

// One line of the tray / menu-bar menu, whichever way it's drawn.
internal enum TrayEntryKind { Header, Separator, Profile, Action }

internal sealed record TrayEntry(TrayEntryKind Kind, string Label = "", Action? Run = null,
    DeviceProfile? Profile = null, bool Pinned = false, bool IsChecked = false, string? IconKey = null);

// The macOS menu-bar menu, drawn like the Windows tray menu (Tray/TrayService.cs: its headers, built
// the same way, in the app's ContextMenu / MenuItem styles): the app header, two- or three-line
// profile items with their icons (the active one marked), and action items with their icons. A
// press on the menu-bar item opens it under the item — from a tiny see-through window there, as a
// menu needs a window to belong to — and it closes on a choice, Escape, a click elsewhere (the
// menu's own light dismiss), or another press on the item.
internal sealed class StyledTrayMenu : IDisposable
{
    private readonly Func<DeviceProfile, Bitmap?> _profileIcon;
    private Window? _anchor;
    private ContextMenu? _menu;

    public StyledTrayMenu(Func<DeviceProfile, Bitmap?> profileIcon) => _profileIcon = profileIcon;

    internal ContextMenu? OpenMenu => _menu is { IsOpen: true } ? _menu : null;

    // Opens the menu, or closes it when it's open.
    public void Toggle(IReadOnlyList<TrayEntry> entries, (double X, double Y)? at)
    {
        if (OpenMenu is { } open) open.Close();
        else Open(entries, at);
    }

    // Opens the menu at `at` (points from the main display's top-left, as macOS gives them), or by
    // the top-right corner of the main screen when that's unknown. The menu keeps itself on screen.
    public void Open(IReadOnlyList<TrayEntry> entries, (double X, double Y)? at)
    {
        _menu?.Close();
        _anchor ??= CreateAnchor();
        _anchor.Position = ScreenPoint(_anchor.Screens.Primary, _anchor.DesktopScaling, at);
        _anchor.Show();
        _anchor.Activate();
        var menu = Build(entries);
        menu.Placement = Avalonia.Controls.PlacementMode.BottomEdgeAlignedLeft;
        menu.Closed += (_, _) =>
        {
            if (_menu == menu) _anchor?.Hide();
        };
        _menu = menu;
        menu.Open((Control)_anchor.Content!);
    }

    // Points → a window position. (Avalonia places macOS windows in points: its desktop scaling there
    // is 1, whatever the screen's Retina factor.)
    internal static PixelPoint ScreenPoint(Avalonia.Platform.Screen? primary, double desktopScaling, (double X, double Y)? at)
    {
        if (at is { } p) return new PixelPoint((int)Math.Round(p.X * desktopScaling), (int)Math.Round(p.Y * desktopScaling));
        return primary is { } main
            ? new PixelPoint(main.WorkingArea.Right - (int)Math.Round(280 * desktopScaling), main.WorkingArea.Y)
            : new PixelPoint(0, 0);
    }

    private Window CreateAnchor()
    {
        var anchor = new Window
        {
            Width = 1, Height = 1, SystemDecorations = SystemDecorations.None, CanResize = false,
            ShowInTaskbar = false, Topmost = true, Background = Brushes.Transparent,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
            Content = new Panel(),
        };
        // (No closing on the anchor losing focus here: the menu's own window taking it would close the
        // menu at once. The menu closes itself on a click elsewhere.)
        return anchor;
    }

    // The menu's controls (also what the screenshot shows).
    internal ContextMenu Build(IReadOnlyList<TrayEntry> entries)
    {
        var menu = new ContextMenu();
        foreach (var entry in entries) menu.Items.Add(Item(entry, menu));
        return menu;
    }

    private MenuItem Item(TrayEntry entry, ContextMenu menu)
    {
        if (entry.Kind == TrayEntryKind.Separator)
        {
            var line = new Border { Height = 1, Margin = new Thickness(8, 4, 8, 4) };
            Res.Bind(line, Border.BackgroundProperty, "TrayMenuSeparatorBrush");
            return new MenuItem { Tag = "sep", IsEnabled = false, IsHitTestVisible = false, Header = line };
        }
        var item = new MenuItem
        {
            Header = entry.Kind switch
            {
                TrayEntryKind.Header => AppHeader(),
                TrayEntryKind.Profile => ProfileHeader(entry.Profile!, entry.Pinned),
                _ => ActionHeader(entry.IconKey ?? "IcoSettings", entry.Label),
            },
            Padding = entry.Kind == TrayEntryKind.Header ? new Thickness(12, 6, 16, 6) : new Thickness(12, 8, 16, 8),
        };
        if (entry.Kind == TrayEntryKind.Profile)
        {
            item.ToggleType = MenuItemToggleType.Radio;
            item.IsChecked = entry.IsChecked;
        }
        // The choice first, then the menu goes: hiding the menu's window first could hand the focus
        // back to the app behind before Settings comes forward.
        item.Click += (_, _) =>
        {
            try { entry.Run?.Invoke(); }
            finally { menu.Close(); }
        };
        return item;
    }

    // [app icon] VibeSwitcher — the wordmark in the brand orange, as on Windows.
    private static Control AppHeader()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new Image
        {
            Source = AppIcon.Value, Width = 20, Height = 20, Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0),
        });
        var label = new TextBlock { Text = "VibeSwitcher", FontSize = 13, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        Res.Bind(label, TextBlock.ForegroundProperty, "Accent");
        panel.Children.Add(label);
        return panel;
    }

    // [profile icon]  Name / mode / shortcut.
    private Control ProfileHeader(DeviceProfile profile, bool pinned)
    {
        var modeLabel = profile.Mode switch
        {
            ProfileMode.Playback => "Playback Only",
            ProfileMode.Recording => "Recording Only",
            ProfileMode.Both => "Both Devices",
            _ => "",
        };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("28,*") };
        Control icon = _profileIcon(profile) is { } bitmap
            ? new Image { Source = bitmap, Width = 20, Height = 20, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center }
            : new TextBlock { Text = "•", FontSize = 15, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        grid.Children.Add(icon);

        var stack = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
        var name = new TextBlock { Text = pinned ? $"★ {profile.Name}" : profile.Name, FontSize = 13, FontWeight = FontWeight.SemiBold };
        Res.Bind(name, TextBlock.ForegroundProperty, "PrimaryText");
        var mode = new TextBlock { Text = modeLabel, FontSize = 11, Margin = new Thickness(0, 1, 0, 0) };
        Res.Bind(mode, TextBlock.ForegroundProperty, "SecondaryText");
        stack.Children.Add(name);
        stack.Children.Add(mode);
        if (!profile.Hotkey.IsEmpty)
        {
            var hotkey = new TextBlock { Text = profile.Hotkey.ToDisplayString(), FontSize = 10, Margin = new Thickness(0, 1, 0, 0) };
            Res.Bind(hotkey, TextBlock.ForegroundProperty, "TertiaryText");
            stack.Children.Add(hotkey);
        }
        Grid.SetColumn(stack, 1);
        grid.Children.Add(stack);
        return grid;
    }

    // [outline icon]  Label.
    private static Control ActionHeader(string geometryKey, string label)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var host = new Grid { Width = 20, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        var path = new Path
        {
            Width = 15, Height = 15, Stretch = Stretch.Uniform, Fill = Brushes.Transparent, StrokeThickness = 1.4,
            StrokeJoin = PenLineJoin.Round, StrokeLineCap = PenLineCap.Round,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        Res.Bind(path, Path.DataProperty, geometryKey);
        Res.Bind(path, Shape.StrokeProperty, "SecondaryText");
        host.Children.Add(path);
        var text = new TextBlock { Text = label, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
        Res.Bind(text, TextBlock.ForegroundProperty, "PrimaryText");
        panel.Children.Add(host);
        panel.Children.Add(text);
        return panel;
    }

    private static readonly Lazy<Bitmap?> AppIcon = new(() =>
    {
        try { return new Bitmap(Avalonia.Platform.AssetLoader.Open(new Uri("avares://VibeSwitcher/Assets/Icons/vs-icon-32.png"))); }
        catch (Exception) { return null; }
    });

    public void Dispose()
    {
        _menu?.Close();
        _anchor?.Close();
        _anchor = null;
    }
}
