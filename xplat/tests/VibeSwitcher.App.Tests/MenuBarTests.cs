using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VibeSwitcher.App.Hosting;
using VibeSwitcher.App.Settings;
using VibeSwitcher.Models;
using VibeSwitcher.Tests;
using Screen = Avalonia.Platform.Screen;
using static VibeSwitcher.App.Tests.Ui;

namespace VibeSwitcher.App.Tests;

// macOS: the menu-bar icon's Windows-styled menu, the app's own banners, reopening, the wording.
[Collection("Shared UI settings")] // (changes Wording.Mac)
public class MenuBarTests
{
    // The menu-bar item, as the app sees it: what it's been given, and a press.
    internal sealed class FakeMenuBarItem((double X, double Y)? anchor) : IMenuBarItem
    {
        public event Action? Clicked;
        public byte[]? Image { get; private set; }
        public string? ToolTip { get; private set; }
        public bool Visible { get; private set; }
        public bool Disposed { get; private set; }
        public void SetImage(byte[] png) => Image = png;
        public void SetToolTip(string text) => ToolTip = text;
        public void SetVisible(bool visible) => Visible = visible;
        public (double X, double Y)? MenuAnchor() => anchor;
        public void PerformClick() => Clicked?.Invoke();
        public void Dispose() => Disposed = true;
    }

    private static (TrayService Tray, FakeConfigService Config, List<string> Done, FakeMenuBarItem Item) StyledTray((double X, double Y)? anchor = null)
    {
        var item = new FakeMenuBarItem(anchor);
        var config = new FakeConfigService();
        var gaming = new DeviceProfile { Name = "Gaming", IsPinned = true, SortOrder = 0, Mode = ProfileMode.Both, Hotkey = new HotkeyDefinition { VirtualKeyCode = 0x47, UseCtrl = true, UseAlt = true } };
        var work = new DeviceProfile { Name = "Work", SortOrder = 1, Mode = ProfileMode.Playback };
        config.Current.Profiles.AddRange([gaming, work]);
        config.Current.ActiveProfileId = gaming.Id;
        var done = new List<string>();
        var tray = new TrayService(config, new FakeAppLogger(), new FakeSessionErrorTracker(), null, new TrayActions(
            OpenSettings: () => done.Add("settings"), OpenSettingsExpanded: () => done.Add("settings+"),
            OpenAbout: () => done.Add("about"), OpenFaq: () => done.Add("faq"), ToggleMiniMode: () => done.Add("mini"),
            IsMiniModeActive: () => false, Quit: () => done.Add("quit")), menuBarItem: item);
        tray.SwitchRequested = p => done.Add(p.Name);
        return (tray, config, done, item);
    }

    // What each line of the styled menu says.
    internal static List<string> Lines(ContextMenu menu) =>
        menu.Items.OfType<MenuItem>().Select(i => i.Tag as string == "sep" ? "—" : string.Join(" / ", Texts(i.Header as Control))).ToList();

    private static IEnumerable<string> Texts(Control? c) =>
        c == null ? [] : (c is TextBlock t ? [t.Text ?? ""] : c.GetLogicalDescendants().OfType<TextBlock>().Select(x => x.Text ?? ""));

    [AvaloniaFact]
    public void APress_OpensTheStyledMenu_WithTheWindowsLines()
    {
        var (tray, _, done, item) = StyledTray((400, 24));
        // The item shows the icon (a PNG), the shortcut list, and appears with the app.
        Assert.NotNull(item.Image);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, item.Image![..4]);
        Assert.StartsWith("VibeSwitcher — Gaming", item.ToolTip);
        Assert.False(item.Visible);
        tray.ShowIcon();
        Assert.True(item.Visible);

        item.PerformClick();
        var menu = tray.Styled!.OpenMenu;
        Assert.NotNull(menu);
        Assert.Equal(new[]
        {
            "VibeSwitcher", "—", $"★ Gaming / Both Devices / {new HotkeyDefinition { VirtualKeyCode = 0x47, UseCtrl = true, UseAlt = true }.ToDisplayString()}",
            "—", "Work / Playback Only", "—", "About", "Help & FAQ", "Settings", "Mini Mode", "Open Sound Settings", "—", "Quit",
        }, Lines(menu!));
        var items = menu!.Items.OfType<MenuItem>().ToList();
        Assert.True(items[2].IsChecked); // the active profile (the orange bar)
        Assert.False(items[4].IsChecked);

        // A choice closes the menu (and its anchor) and does what it says.
        items[4].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[] { "Work" }, done);
        Assert.Null(tray.Styled.OpenMenu);

        // A second press on the item closes the menu, as menu-bar menus do.
        item.PerformClick();
        Assert.NotNull(tray.Styled.OpenMenu);
        item.PerformClick();
        Assert.Null(tray.Styled.OpenMenu);
        tray.Dispose();
        Assert.True(item.Disposed);
    }

    [AvaloniaFact]
    public void TheMenu_GoesUnderTheItem_InWindowUnits()
    {
        // macOS places windows in points (desktop scaling 1, even on a Retina screen).
        var retina = new Screen(2, new PixelRect(0, 0, 1440, 900), new PixelRect(0, 25, 1440, 875), true);
        Assert.Equal(new PixelPoint(1200, 24), StyledTrayMenu.ScreenPoint(retina, 1, (1200, 24)));
        // (Pixels where windows are placed in pixels.)
        Assert.Equal(new PixelPoint(2400, 48), StyledTrayMenu.ScreenPoint(retina, 2, (1200, 24)));
        // Unknown: by the main screen's top-right corner.
        Assert.Equal(new PixelPoint(1440 - 280, 25), StyledTrayMenu.ScreenPoint(retina, 1, null));
    }

    [AvaloniaFact]
    public async Task Banners_ShowOneAtATime_ThenGo()
    {
        var notifier = new BannerNotifier(TimeSpan.FromMilliseconds(300));
        Assert.True(await notifier.ShowAsync("VibeSwitcher", "Switched to Gaming"));
        var first = notifier.Current!;
        Assert.Equal("VibeSwitcher", first.TitleShown);
        Assert.Equal("Switched to Gaming", first.MessageShown);
        Assert.False(first.ShowActivated); // (never takes the focus)
        // At the top right of the main screen's working area: in points on macOS, pixels on Linux.
        Assert.Equal(new PixelPoint(1440 - 344, 25), BannerWindow.TopRight(new PixelRect(0, 25, 1440, 875), 344, 1));
        Assert.Equal(new PixelPoint(2880 - 688, 50), BannerWindow.TopRight(new PixelRect(0, 50, 2880, 1750), 344, 2));

        Assert.True(await notifier.ShowAsync("Device Unavailable", "Playback device for 'Gaming' is disconnected."));
        Assert.False(first.IsVisible); // replaced
        var second = notifier.Current!;
        Assert.Equal("Device Unavailable", second.TitleShown);
        await Until(() => !second.IsVisible);
        Assert.False(second.IsVisible);
        Assert.Null(notifier.Current);
    }

    [AvaloniaFact]
    public void Wording_HasNoLeftClickChoice_OnTheMenuBar()
    {
        var old = Wording.Mac;
        try
        {
            Wording.Mac = true;
            Assert.False(Wording.LeftClickOption);
            Assert.Contains("Click the icon for the menu", Wording.LivesIn);
            Assert.DoesNotContain("right-click", Wording.LivesInRest);
            Wording.Mac = false;
            Assert.True(Wording.LeftClickOption);
            Assert.Contains("right-click for the full menu", Wording.LivesInRest);
        }
        finally { Wording.Mac = old; }
    }
}
