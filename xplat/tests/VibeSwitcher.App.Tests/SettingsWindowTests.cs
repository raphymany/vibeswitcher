using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VibeSwitcher.App.Controls;
using VibeSwitcher.App.Settings;
using VibeSwitcher.Controls;
using VibeSwitcher.ViewModels;

namespace VibeSwitcher.App.Tests;

// The Settings window around the shared Windows view model: the Profiles page, filters, the profile
// popup, navigation, the window's place, and closing to the tray.
public class SettingsWindowTests
{
    private static SettingsWindow Open(SettingsCatalog.Setup setup)
    {
        var window = setup.Window();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        SettingsCatalog.WaitForDevices(window);
        return window;
    }

    private static T Named<T>(Window w, string name) where T : Control => w.FindControl<T>(name)!;

    private static IEnumerable<Control> Cards(Window w) =>
        Named<ItemsControl>(w, "ProfileCards").GetRealizedContainers()
            .Select(c => c.GetVisualDescendants().OfType<Grid>().First())
            .Where(g => g.IsVisible);

    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static Button ButtonWithTip(Visual v, string tip) =>
        v.GetVisualDescendants().OfType<Button>().First(b => ToolTip.GetTip(b) as string == tip);

    private static void Press(Window w, Key key) =>
        w.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });

    [AvaloniaFact]
    public void ShowsACardPerProfile_WithItsDetails()
    {
        var setup = SettingsCatalog.WithProfiles();
        var window = Open(setup);
        Assert.True(setup.Errors.Count == 0, string.Join("; ", setup.Errors.Errors.Select(e => $"{e.Title}: {e.Message}")));
        var cards = Cards(window).ToList();
        Assert.Equal(3, cards.Count);
        var texts = cards[0].GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
        Assert.Contains("Gaming", texts);
        Assert.Contains(SettingsCatalog.Headset.FriendlyName, texts);
        Assert.Contains(SettingsCatalog.HeadsetMic.FriendlyName, texts);
        Assert.Contains(texts, t => t?.Contains('G') == true && t.Contains("Alt")); // the hotkey chip
        Assert.False(Named<Button>(window, "LogsButton").IsVisible); // no errors this session
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "3"); // PROFILES count
        window.Close();
    }

    [AvaloniaFact]
    public void WithNoProfiles_ItSaysHowToStart()
    {
        var window = Open(new SettingsCatalog.Setup());
        Assert.Empty(Cards(window));
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),
            t => t.IsEffectivelyVisible && t.Text == "No profiles yet — click + New Profile to get started.");
        window.Close();
    }

    [AvaloniaFact]
    public void Searching_HidesOtherCards_AndSaysWhenNothingMatches()
    {
        var window = Open(SettingsCatalog.WithProfiles());
        var vm = (SettingsViewModel)window.DataContext!;
        vm.SearchText = "wor";
        Dispatcher.UIThread.RunJobs();
        Assert.Single(Cards(window));

        vm.SearchText = "nothing like this";
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(Cards(window));
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),
            t => t.IsEffectivelyVisible && t.Text == "No profiles match your filters.");
        window.Close();
    }

    [AvaloniaFact]
    public async Task TheFiltersButton_SlidesTheFilterBarOpenAndClosed()
    {
        var window = Open(SettingsCatalog.WithProfiles());
        var bar = Named<Border>(window, "FilterBarBorder");
        Assert.Equal(0, bar.Bounds.Height);
        Assert.False(bar.IsEnabled);

        Click(Named<Button>(window, "FiltersToggleBtn"));
        await Task.Delay(450);
        Dispatcher.UIThread.RunJobs();
        Assert.True(bar.IsEnabled);
        Assert.True(double.IsPositiveInfinity(bar.MaxHeight));
        window.UpdateLayout();
        Assert.True(bar.Bounds.Height > 40);

        // A filter chip filters the cards; the count badge shows on the button.
        var vm = (SettingsViewModel)window.DataContext!;
        vm.PinnedFilter = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Single(Cards(window));
        Assert.True(Named<Border>(window, "FilterCountBadge").IsVisible);

        Click(Named<Button>(window, "FiltersToggleBtn"));
        await Task.Delay(350);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Assert.False(bar.IsEnabled);
        Assert.Equal(0, bar.Bounds.Height);
        window.Close();
    }

    [AvaloniaFact]
    public void ClickingACard_OpensItsPopup_AndEscapeOrDoneClosesIt()
    {
        var window = Open(SettingsCatalog.WithProfiles());
        var overlay = Named<Grid>(window, "ProfileDetailOverlay");
        Click(ButtonWithTip(Cards(window).ElementAt(1), "Open profile settings"));
        Dispatcher.UIThread.RunJobs();
        Assert.True(overlay.IsVisible);
        var card = Assert.IsType<ProfileCardViewModel>(Named<Grid>(window, "ProfileDetailContent").DataContext);
        Assert.Equal("Work", card.Name);
        Assert.Equal("Work", Named<TextBox>(window, "ProfileNameBox").Text);

        Press(window, Key.Escape);
        Assert.False(overlay.IsVisible);
        Assert.True(window.IsVisible); // Escape closed the popup, not the window

        Click(ButtonWithTip(Cards(window).First(), "Open profile settings"));
        Dispatcher.UIThread.RunJobs();
        Click(overlay.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Done"));
        Assert.False(overlay.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void ThePopup_EditsTheProfile()
    {
        var setup = SettingsCatalog.WithProfiles();
        var window = Open(setup);
        Click(ButtonWithTip(Cards(window).ElementAt(1), "Open profile settings"));
        Dispatcher.UIThread.RunJobs();
        var name = Named<TextBox>(window, "ProfileNameBox");
        name.Text = "Office";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Office", setup.Config.Current.Profiles[1].Name);
        Assert.Contains(Cards(window).ElementAt(1).GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Office");
        window.Close();
    }

    [AvaloniaFact]
    public void CardButtons_ActOnTheProfile()
    {
        var setup = SettingsCatalog.WithProfiles();
        var window = Open(setup);
        var work = Cards(window).ElementAt(1);

        var pin = work.GetVisualDescendants().OfType<ToggleButton>().Single(b => ToolTip.GetTip(b) as string == "Pin to top of list");
        pin.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(setup.Config.Current.Profiles.Single(p => p.Name == "Work").IsPinned);

        // Auto-switch asks first (the supported-headsets dialog). Answered "no", the toggle must flip
        // back — the shared view model's revert works here.
        var auto = work.GetVisualDescendants().OfType<ToggleButton>().Single(b => ToolTip.GetTip(b) as string == "Auto-switch on device connect");
        Assert.NotNull(Avalonia.Data.BindingOperations.GetBindingExpressionBase(auto, ToggleButton.IsCheckedProperty));
        Ui.ClickLater(window, "Close");
        auto.IsChecked = true; // (what a click does, then Click — after the view model has answered)
        auto.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        var cardVm = (ProfileCardViewModel)work.DataContext!;
        Assert.False(cardVm.TriggerOnConnect);
        Assert.False(auto.IsChecked);
        Assert.False(setup.Config.Current.Profiles.Single(p => p.Name == "Work").TriggerOnConnect);

        // Answered "yes", it's on.
        Ui.ClickLater(window, "Enable Auto-Switch");
        auto.IsChecked = true;
        auto.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(cardVm.TriggerOnConnect);
        Assert.True(auto.IsChecked);
        Assert.True(setup.Config.Current.Profiles.Single(p => p.Name == "Work").TriggerOnConnect);

        // Recording-only profiles can't auto-switch: the button is dimmed and off.
        var calls = Cards(window).ElementAt(2);
        var callsAuto = calls.GetVisualDescendants().OfType<ToggleButton>().Single(b => ToolTip.GetTip(b) as string == "Auto-switch on device connect");
        Assert.False(callsAuto.IsEnabled);
        window.Close();
    }

    [AvaloniaFact]
    public void TheNavButtons_SwitchPages()
    {
        var window = Open(SettingsCatalog.WithProfiles());
        Click(Named<Button>(window, "NavAboutBtn"));
        Assert.Equal("AboutPanel", window.CurrentPanel);
        Assert.False(Named<ScrollViewer>(window, "ProfilesPanel").IsVisible);
        Click(Named<Button>(window, "NavFaqBtn"));
        Assert.Equal("FaqPanel", window.CurrentPanel);
        Click(Named<Button>(window, "NavSettingsBtn"));
        Assert.Equal("SettingsBodyScrollViewer", window.CurrentPanel);
        Assert.True(((SettingsViewModel)window.DataContext!).SettingsCardExpanded);
        window.GetVisualDescendants().OfType<Button>().First(b => b.Theme == window.FindResource("NavLogoBtn")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("ProfilesPanel", window.CurrentPanel);
        window.Close();
    }

    [AvaloniaFact]
    public void Closing_HidesToTheTray_WhenThatsOn()
    {
        var setup = SettingsCatalog.WithProfiles();
        setup.Config.Current.CloseToTray = true;
        var window = Open(setup);
        window.Close();
        Assert.False(window.IsVisible);
        Assert.NotNull(window.DataContext); // hidden, not closed: it opens again as it was
        setup.Config.Current.CloseToTray = false;
        ((SettingsViewModel)window.DataContext!).CloseToTray = false;
        window.Show();
        window.Close();
    }

    [AvaloniaFact]
    public void TheWindowsSizeAndPlace_AreRememberedInWindowsUnits()
    {
        var setup = SettingsCatalog.WithProfiles();
        setup.Config.Current.WindowWidth = 1000;
        setup.Config.Current.WindowHeight = 650;
        var window = Open(setup);
        Assert.Equal(1000, window.Width);
        Assert.Equal(650, window.Height);
        window.Close();
    }

    [AvaloniaFact]
    public void TheErrorsButton_ShowsThisSessionsErrorCount()
    {
        var setup = SettingsCatalog.WithProfiles();
        var window = Open(setup);
        setup.Errors.Record(Helpers.ErrorCode.ProfileSwitchFailed, "Switch failed", "details");
        setup.Errors.Record(Helpers.ErrorCode.ProfileSwitchFailed, "Switch failed", "details");
        Dispatcher.UIThread.RunJobs();
        Assert.True(Named<Button>(window, "LogsButton").IsVisible);
        Assert.Equal("⚠ 2 Errors This Session", Named<TextBlock>(window, "LogsButtonText").Text);
        window.Close();
    }

    [Fact]
    public void LogoFrames_FollowTheWindowsStoryboards()
    {
        Assert.Equal(0, LogoIcon.VBreath.At(0), 6);
        Assert.Equal(-5, LogoIcon.VBreath.At(0.54), 6);
        Assert.Equal(2.5, LogoIcon.VBreath.At(1.26), 6);
        Assert.Equal(0, LogoIcon.VBreath.At(1.8), 6);
        Assert.Equal(-2.5, LogoIcon.VBreath.At(0.27), 6); // eased half-way
        Assert.Equal(1.75, LogoIcon.Bars[2].At(0.18), 6);
        // Bar 1 starts 0.22 s into its cycle (WPF BeginTime -0.22 s), so its peak (0.34 s) comes at 0.12 s.
        Assert.Equal(2.2, LogoIcon.Bars[0].At(0.12), 6);
        // As in WPF, a bar with a negative BeginTime repeats every Duration + BeginTime (bar 4: 0.44 s).
        Assert.Equal(LogoIcon.Bars[3].At(0.1), LogoIcon.Bars[3].At(0.1 + 0.44 * 3), 6);
    }

    [AvaloniaFact]
    public void TheLogoAnimates_OnlyWhenShown_AndFollowsTheSetting()
    {
        var before = LogoAnimator.Mode;
        try
        {
            LogoAnimator.Mode = LogoAnimator.AnimMode.Full;
            int before0 = LogoIcon.Clock.ShownCount;
            var window = new Window { Content = new LogoIcon() };
            window.Show();
            Assert.True(LogoIcon.Clock.IsRunning);
            LogoAnimator.Mode = LogoAnimator.AnimMode.Static;
            Assert.False(LogoIcon.Clock.IsRunning);
            LogoAnimator.Mode = LogoAnimator.AnimMode.Reduced;
            Assert.True(LogoIcon.Clock.IsRunning);
            Assert.Equal(before0 + 1, LogoIcon.Clock.ShownCount);
            window.Close();
            Assert.Equal(before0, LogoIcon.Clock.ShownCount); // off screen: not animated
            Assert.Equal(before0 > 0, LogoIcon.Clock.IsRunning);
        }
        finally { LogoAnimator.Mode = before; }
    }

    [AvaloniaFact]
    public void CenteredWrapPanel_CentresEachRow()
    {
        var panel = new CenteredWrapPanel { Width = 100 };
        for (int i = 0; i < 3; i++) panel.Children.Add(new Border { Width = 40, Height = 10 });
        var window = new Window { Content = panel, Width = 100, Height = 100 };
        window.Show();
        window.UpdateLayout();
        Assert.Equal(new Rect(10, 0, 40, 10), panel.Children[0].Bounds);
        Assert.Equal(new Rect(50, 0, 40, 10), panel.Children[1].Bounds);
        Assert.Equal(new Rect(30, 10, 40, 10), panel.Children[2].Bounds); // second row, centred
        window.Close();
    }

    [Fact]
    public void SoundSettings_OpensTheDesktopsOwnSettingsFirst()
    {
        Assert.Equal(("/usr/bin/open", "x-apple.systempreferences:com.apple.Sound-Settings.extension"),
            SystemSoundSettings.Candidates(macOS: true, null).Select(c => (c.Program, c.Args[0])).Single());
        Assert.Equal("systemsettings", SystemSoundSettings.Candidates(false, "KDE").First().Program);
        Assert.Equal("gnome-control-center", SystemSoundSettings.Candidates(false, "ubuntu:GNOME").First().Program);
        Assert.Equal("cinnamon-settings", SystemSoundSettings.Candidates(false, "X-Cinnamon").First().Program);
        Assert.Equal("pavucontrol", SystemSoundSettings.Candidates(false, "XFCE").First().Program);
    }

    [AvaloniaFact]
    public void FramelessWindows_ResizeFromTheirEdges()
    {
        var window = new Window { Width = 400, Height = 300, CanResize = true };
        window.Show();
        Assert.Equal(WindowEdge.NorthWest, WindowChrome.EdgeAt(window, new Point(2, 2)));
        Assert.Equal(WindowEdge.East, WindowChrome.EdgeAt(window, new Point(397, 150)));
        Assert.Equal(WindowEdge.South, WindowChrome.EdgeAt(window, new Point(200, 296)));
        Assert.Null(WindowChrome.EdgeAt(window, new Point(200, 150)));
        window.WindowState = WindowState.Maximized;
        Assert.Null(WindowChrome.EdgeAt(window, new Point(2, 2))); // not while maximized
        window.Close();
    }

    [AvaloniaFact]
    public void ACardsChip_OpensItsOwnDialog_NotThePopup()
    {
        var window = Open(SettingsCatalog.WithProfiles());
        var chip = Cards(window).First().GetVisualDescendants().OfType<Button>()
            .First(b => ToolTip.GetTip(b) as string == "Click to view or edit schedules");
        Click(chip);
        Dispatcher.UIThread.RunJobs();
        Assert.False(Named<Grid>(window, "ProfileDetailOverlay").IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void Escape_WithNoPopup_ClosesTheWindow_ToTheTray()
    {
        var setup = SettingsCatalog.WithProfiles();
        setup.Config.Current.CloseToTray = true;
        var window = Open(setup);
        Press(window, Key.Escape);
        Assert.False(window.IsVisible);
        ((SettingsViewModel)window.DataContext!).CloseToTray = false;
        window.Show();
        window.Close();
    }

    [AvaloniaFact]
    public void ClickingOutsideThePopup_ClosesIt()
    {
        var window = Open(SettingsCatalog.WithProfiles());
        window.Width = 920; window.Height = 720;
        Click(ButtonWithTip(Cards(window).First(), "Open profile settings"));
        Dispatcher.UIThread.RunJobs();
        var overlay = Named<Grid>(window, "ProfileDetailOverlay");
        Assert.True(overlay.IsVisible);
        window.MouseDown(new Point(40, 400), MouseButton.Right); // (right clicks don't)
        window.MouseUp(new Point(40, 400), MouseButton.Right);
        Assert.True(overlay.IsVisible);
        window.MouseDown(new Point(40, 400), MouseButton.Left);
        window.MouseUp(new Point(40, 400), MouseButton.Left);
        Assert.False(overlay.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void ThePopup_SwitchingBetweenProfiles_KeepsEachOnesDevices()
    {
        var setup = SettingsCatalog.WithProfiles();
        var window = Open(setup);
        var profiles = setup.Config.Current.Profiles;
        foreach (var i in new[] { 0, 1, 2, 0, 1 })
        {
            window.OpenProfileDetail(window.ViewModel.Profiles[i]);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.Equal(SettingsCatalog.Headset.Id, profiles[0].PlaybackDeviceId);
        Assert.Equal(SettingsCatalog.HeadsetMic.Id, profiles[0].RecordingDeviceId);
        Assert.Equal(SettingsCatalog.Speakers.Id, profiles[1].PlaybackDeviceId);
        Assert.Equal(SettingsCatalog.HeadsetMic.Id, profiles[2].RecordingDeviceId);
        window.Close();
    }

    [AvaloniaFact]
    public void Maximizing_SwapsTheButtonsSymbol_AndHidesTheGrip()
    {
        var old = WindowChrome.Style;
        WindowChrome.Style = ChromeStyle.Custom;
        try
        {
            var window = Open(SettingsCatalog.WithProfiles());
            var glyph = window.FindControl<Avalonia.Controls.Shapes.Path>("MaxGlyph")!;
            var normal = glyph.Data;
            Assert.True(Named<ResizeGrip>(window, "Grip").IsVisible);
            window.WindowState = WindowState.Maximized;
            Assert.NotSame(normal, glyph.Data);
            Assert.False(Named<ResizeGrip>(window, "Grip").IsVisible);
            window.WindowState = WindowState.Normal;
            Assert.Same(normal, glyph.Data);
            window.Close();
        }
        finally { WindowChrome.Style = old; }
    }

    [AvaloniaFact]
    public void Static_ShowsTheLogoAtRest()
    {
        var before = LogoAnimator.Mode;
        try
        {
            LogoAnimator.Mode = LogoAnimator.AnimMode.Full;
            var logo = new LogoIcon();
            var window = new Window { Content = logo };
            window.Show();
            logo.Show(TimeSpan.FromSeconds(0.3)); // mid-animation
            Assert.NotEqual(1, logo.BarScale(0));
            LogoAnimator.Mode = LogoAnimator.AnimMode.Static;
            Assert.Equal(0, logo.RestY);
            for (int i = 0; i < 5; i++) Assert.Equal(1, logo.BarScale(i));
            window.Close();
        }
        finally { LogoAnimator.Mode = before; }
    }

    [AvaloniaFact]
    public void AHiddenWindowsLogos_StopAnimating()
    {
        var before = LogoAnimator.Mode;
        try
        {
            LogoAnimator.Mode = LogoAnimator.AnimMode.Full;
            int baseline = LogoIcon.Clock.ShownCount;
            var window = new Window { Content = new LogoIcon() };
            window.Show();
            Assert.Equal(baseline + 1, LogoIcon.Clock.ShownCount);
            window.Hide();
            Assert.Equal(baseline, LogoIcon.Clock.ShownCount);
            window.Show();
            Assert.Equal(baseline + 1, LogoIcon.Clock.ShownCount);
            window.Close();
        }
        finally { LogoAnimator.Mode = before; }
    }
}
