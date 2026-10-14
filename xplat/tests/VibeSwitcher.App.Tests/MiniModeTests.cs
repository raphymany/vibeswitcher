using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VibeSwitcher.App.Controls;
using VibeSwitcher.App.Dialogs;
using VibeSwitcher.App.Settings;
using VibeSwitcher.ViewModels;
using static VibeSwitcher.App.Tests.Ui;

namespace VibeSwitcher.App.Tests;

// Mini Mode: the Settings window shrunk into a small profile switcher.
public class MiniModeTests
{
    private static SettingsWindow Open(SettingsCatalog.Setup setup)
    {
        var window = setup.Window();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        SettingsCatalog.WaitForDevices(window);
        return window;
    }

    // Profiles set up, the welcome already seen.
    private static SettingsCatalog.Setup Ready()
    {
        var setup = SettingsCatalog.WithProfiles();
        setup.Config.Current.CompactIntroShown = true;
        return setup;
    }

    private static List<string> MiniNames(SettingsWindow w) => w.MiniItems.Select(p => p.Name).ToList();

    [AvaloniaFact]
    public void Entering_ShrinksTheWindowIntoTheProfileList_AndLeavingPutsItBack()
    {
        var setup = Ready();
        var window = Open(setup);
        var (width, height) = (window.Width, window.Height);

        Click(Named<Button>(window, "TitleShrinkBtn"));
        Assert.True(window.IsCompact);
        Assert.True(setup.Config.Current.CompactMode);
        Assert.Equal("MiniPanel", window.CurrentPanel);
        Assert.Equal(300, window.Width);
        Assert.False(window.CanResize);
        Assert.Equal(SizeToContent.Height, window.SizeToContent);
        Assert.False(Named<Border>(window, "NavBar").IsVisible);
        Assert.Equal(0, Named<Grid>(window, "Root").RowDefinitions[1].Height.Value);
        Assert.True(Named<StackPanel>(window, "MiniTitleGroup").IsVisible);
        Assert.False(Named<StackPanel>(window, "FullTitleGroup").IsVisible);
        Assert.True(Named<Button>(window, "TitlePinBtn").IsVisible);
        Assert.True(Named<Button>(window, "TitleExpandBtn").IsVisible);
        Assert.False(Named<Button>(window, "TitleShrinkBtn").IsVisible);
        Assert.False(Named<Button>(window, "TitleMaxBtn").IsVisible);
        Assert.False(Named<ResizeGrip>(window, "Grip").IsVisible);
        Assert.Equal(3, Named<ItemsControl>(window, "MiniList").ItemCount);
        // As tall as its list (title bar + three rows), not the full window's height.
        Dispatcher.UIThread.RunJobs();
        Assert.InRange(window.ClientSize.Height, 100, 250);

        Click(Named<Button>(window, "TitleExpandBtn"));
        Assert.False(window.IsCompact);
        Assert.False(setup.Config.Current.CompactMode);
        Assert.Equal("ProfilesPanel", window.CurrentPanel);
        Assert.Equal((width, height), (window.Width, window.Height));
        Assert.True(window.CanResize);
        Assert.Equal(SizeToContent.Manual, window.SizeToContent);
        Assert.True(Named<Border>(window, "NavBar").IsVisible);
        Assert.Equal(54, Named<Grid>(window, "Root").RowDefinitions[1].Height.Value);
        Assert.True(Named<Button>(window, "TitleShrinkBtn").IsVisible);
        Assert.False(Named<Button>(window, "TitlePinBtn").IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void MiniModeKeepsAPlaceOfItsOwn()
    {
        var setup = Ready();
        var window = Open(setup);
        var cfg = setup.Config.Current;
        window.Position = new Avalonia.PixelPoint(200, 150);
        window.EnterCompact();
        window.Position = new Avalonia.PixelPoint(40, 60);
        window.ExitCompact();

        // (The config keeps device-independent units, as on Windows; the headless screen is 1:1.)
        Assert.Equal(40, cfg.CompactWindowLeft);
        Assert.Equal(60, cfg.CompactWindowTop);
        Assert.Equal(200, cfg.WindowLeft);
        Assert.Equal(150, cfg.WindowTop);
        Assert.Equal(new Avalonia.PixelPoint(200, 150), window.Position);

        window.EnterCompact();
        Assert.Equal(new Avalonia.PixelPoint(40, 60), window.Position);
        window.Close();
    }

    [AvaloniaFact]
    public void WithNoProfiles_ItExplainsInsteadOfShrinking()
    {
        var setup = new SettingsCatalog.Setup();
        var window = Open(setup);
        string? message = null;
        ClickLater(window, "OK", d => message = string.Join(" ", Texts(d)));
        Click(Named<Button>(window, "TitleShrinkBtn"));
        Assert.False(window.IsCompact);
        Assert.Contains("Mini Mode needs a profile", message);
        Assert.False(setup.Config.Current.CompactMode);
        window.Close();
    }

    [AvaloniaFact]
    public void TheFirstTime_ItWelcomes_AndCanGoStraightToCustomize()
    {
        var setup = SettingsCatalog.WithProfiles();
        var window = Open(setup);

        ClickLater(window, "Customize First", d => Assert.Contains("Welcome to Mini Mode", Texts(d)));
        ClickLater(window, "Save", d =>
        {
            Click(Named<ToggleButton>(d, "LayoutGrid"));
            Assert.Equal(3, Named<ItemsControl>(d, "ProfileChecklist").ItemCount);
        });
        window.EnterCompact();

        Assert.True(setup.Config.Current.CompactIntroShown);
        Assert.True(window.IsCompact);
        Assert.Equal("Grid", setup.Config.Current.CompactLayout);
        Assert.True(Named<ItemsControl>(window, "MiniGrid").IsVisible);
        Assert.False(Named<ItemsControl>(window, "MiniList").IsVisible);

        // Only once.
        window.ExitCompact();
        window.EnterCompact();
        Assert.True(window.IsCompact);
        Assert.Empty(window.OwnedWindows);
        window.Close();
    }

    [AvaloniaFact]
    public void TheList_IsInTrayOrder_AndShowsTheChosenProfiles()
    {
        var setup = Ready();
        var window = Open(setup);
        var cfg = setup.Config.Current;
        // Gaming (pinned), Work, Calls.
        Assert.Equal(new[] { "Gaming", "Work", "Calls" }, MiniNames(window));

        window.ViewModel.Profiles.Single(p => p.Name == "Calls").IsPinned = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[] { "Gaming", "Calls", "Work" }, MiniNames(window));

        cfg.CompactProfileIds = [window.ViewModel.Profiles.Single(p => p.Name == "Work").Id];
        window.RefreshMiniList();
        Assert.Equal(new[] { "Work" }, MiniNames(window));

        // A selection whose profiles are all gone shows everything again.
        cfg.CompactProfileIds = [Guid.NewGuid()];
        window.RefreshMiniList();
        Assert.Equal(3, MiniNames(window).Count);
        window.Close();
    }

    [AvaloniaFact]
    public void TheActiveProfile_IsMarked_AndAClickActivatesAnother()
    {
        var setup = Ready();
        var switched = new List<string>();
        var window = new SettingsWindow(setup.Config, setup.Audio, setup.Hotkeys, setup.Startup, setup.Logger, setup.Errors,
            _ => { }, switchProfile: p => switched.Add(p.Name));
        window.Show();
        SettingsCatalog.WaitForDevices(window);
        window.EnterCompact();
        Dispatcher.UIThread.RunJobs();

        var rows = Named<ItemsControl>(window, "MiniList").GetRealizedContainers()
            .Select(c => c.GetVisualDescendants().OfType<Button>().First()).ToList();
        Assert.Equal(3, rows.Count);
        Assert.Contains("active", rows[0].Classes); // Gaming
        Assert.DoesNotContain("active", rows[1].Classes);
        Click(rows[1]);
        Assert.Equal(new[] { "Work" }, switched);
        window.Close();
    }

    [AvaloniaFact]
    public void AlwaysOnTop_AndThePin_OnlyApplyInMiniMode()
    {
        var setup = Ready();
        setup.Config.Current.CompactAlwaysOnTop = true;
        var window = Open(setup);
        Assert.False(window.Topmost);
        window.EnterCompact();
        Assert.True(window.Topmost);

        Click(Named<Button>(window, "TitlePinBtn"));
        Assert.False(setup.Config.Current.CompactAlwaysOnTop);
        Assert.False(window.Topmost);
        Assert.DoesNotContain("on", Named<Button>(window, "TitlePinBtn").Classes);
        Click(Named<Button>(window, "TitlePinBtn"));
        Assert.True(window.Topmost);
        Assert.Contains("on", Named<Button>(window, "TitlePinBtn").Classes);

        window.ExitCompact();
        Assert.False(window.Topmost);
        window.Close();
    }

    [AvaloniaFact]
    public void Translucent_FadesWhileAnotherAppIsInFront()
    {
        var setup = Ready();
        setup.Config.Current.CompactTranslucent = true;
        var window = Open(setup);
        window.CanFade = () => true; // (a headless window can't be see-through)

        window.OnDeactivated();
        Assert.Equal(1.0, window.OpacityTarget); // (only in Mini Mode)
        window.EnterCompact();
        window.OnDeactivated();
        Assert.Equal(0.65, window.OpacityTarget);
        window.OnPointerEnteredWindow();
        Assert.Equal(1.0, window.OpacityTarget);
        window.OnPointerExitedWindow(); // (the window is still the active one: it stays solid)
        Assert.Equal(1.0, window.OpacityTarget);

        // Another app in front: the pointer passing over the mini window brings it back only while
        // it's there.
        window.HasFocus = () => false;
        window.OnDeactivated();
        Assert.Equal(0.65, window.OpacityTarget);
        window.OnPointerEnteredWindow();
        Assert.Equal(1.0, window.OpacityTarget);
        window.OnPointerExitedWindow();
        Assert.Equal(0.65, window.OpacityTarget);
        window.OnActivated();
        Assert.Equal(1.0, window.OpacityTarget);

        // Turning it off brings the window back at once.
        window.OnDeactivated();
        window.ViewModel.CompactTranslucent = false;
        Assert.Equal(1.0, window.OpacityTarget);
        window.OnPointerExitedWindow();
        Assert.Equal(1.0, window.OpacityTarget);

        window.ViewModel.CompactTranslucent = true;
        window.OnDeactivated();
        window.ExitCompact();
        Assert.Equal(1.0, window.OpacityTarget);
        Assert.Equal(1.0, window.Opacity);
        window.Close();
    }

    [AvaloniaFact]
    public void ItComesBackInMiniMode_WhenItWasLeftThere()
    {
        var setup = SettingsCatalog.WithProfiles();
        setup.Config.Current.CompactMode = true; // (the welcome isn't shown for a restore)
        var window = Open(setup);
        Assert.True(window.IsCompact);
        Assert.Empty(window.OwnedWindows);
        Assert.False(setup.Config.Current.CompactIntroShown);

        // The pages behind the full window's buttons bring it back first.
        window.OpenAboutPanel();
        Assert.False(window.IsCompact);
        // (Entered by hand for the first time now: the welcome shows — "Cancel" goes straight in.)
        ClickLater(window, "Cancel");
        window.EnterCompact();
        Assert.True(window.IsCompact);
        Assert.True(setup.Config.Current.CompactIntroShown);
        window.ExpandSettings();
        Assert.False(window.IsCompact);
        Assert.Equal("SettingsBodyScrollViewer", window.CurrentPanel);
        window.Close();
    }

    [AvaloniaFact]
    public void TheSettingsPageAndFaq_StartMiniMode()
    {
        var window = Open(Ready());
        window.ExpandSettings();
        window.ViewModel.SelectedCategory = "compact";
        Dispatcher.UIThread.RunJobs();
        var tryIt = Named<StackPanel>(window, "CompactSection").GetVisualDescendants().OfType<Button>()
            .Single(b => b.Content as string == "Switch to Mini Mode now");
        Click(tryIt);
        Assert.True(window.IsCompact);

        window.OpenFaqPanel();
        Assert.False(window.IsCompact);
        var faqLink = Named<ScrollViewer>(window, "FaqPanel").GetLogicalDescendants().OfType<Button>()
            .Single(b => b.Tag as string == "miniTry");
        Click(faqLink);
        Assert.True(window.IsCompact);
        window.Close();
    }

    [AvaloniaFact]
    public void WhereTheSystemCantShowItSeeThrough_ItStaysSolid()
    {
        var setup = Ready();
        setup.Config.Current.CompactTranslucent = true;
        var window = Open(setup);
        window.CanFade = () => false;
        window.HasFocus = () => false;
        window.EnterCompact();
        window.OnDeactivated();
        window.OnPointerExitedWindow();
        Assert.Equal(1.0, window.OpacityTarget);
        window.Close();
    }

    [AvaloniaFact]
    public void FromAMaximizedWindow_TheFullSizeIsTheLastNormalOne()
    {
        var setup = Ready();
        var cfg = setup.Config.Current;
        (cfg.WindowWidth, cfg.WindowHeight, cfg.WindowLeft, cfg.WindowTop) = (800, 600, 100, 80);
        var window = Open(setup);
        window.WindowState = WindowState.Maximized;
        Dispatcher.UIThread.RunJobs();

        window.EnterCompact();
        Assert.Equal(WindowState.Normal, window.WindowState);
        Assert.Equal(300, window.Width);
        Assert.Equal((800d, 600d), (cfg.WindowWidth, cfg.WindowHeight)); // (the maximized size isn't saved)

        window.ExitCompact();
        Assert.Equal((800d, 600d), (window.Width, window.Height));
        Assert.Equal(new Avalonia.PixelPoint(100, 80), window.Position);
        window.Close();
    }

    [AvaloniaFact]
    public async Task MovingTheMiniWindow_SavesOnlyItsOwnPlace()
    {
        var setup = Ready();
        var cfg = setup.Config.Current;
        var window = Open(setup);
        window.Position = new Avalonia.PixelPoint(150, 120);
        window.EnterCompact();
        var full = (cfg.WindowWidth, cfg.WindowHeight, cfg.WindowLeft, cfg.WindowTop);

        window.Position = new Avalonia.PixelPoint(30, 40); // dragged
        await Until(() => cfg.CompactWindowLeft == 30); // (the save waits for the move to settle)
        Assert.Equal(30, cfg.CompactWindowLeft);
        Assert.Equal(40, cfg.CompactWindowTop);
        Assert.Equal(full, (cfg.WindowWidth, cfg.WindowHeight, cfg.WindowLeft, cfg.WindowTop));
        window.Close();
    }

    [AvaloniaFact]
    public void RestoredAtStartUp_ItOpensWhereTheMiniWindowWas()
    {
        var setup = SettingsCatalog.WithProfiles();
        setup.Config.Current.CompactMode = true;
        (setup.Config.Current.CompactWindowLeft, setup.Config.Current.CompactWindowTop) = (60, 70);
        var window = Open(setup);
        Assert.Equal(new Avalonia.PixelPoint(60, 70), window.Position);
        Assert.Equal(WindowStartupLocation.Manual, window.WindowStartupLocation);
        window.Close();
    }

    [AvaloniaFact]
    public void AProfileGoingAway_LeavesTheListAndTheWindowFits()
    {
        var setup = Ready();
        var window = Open(setup);
        window.EnterCompact();
        Dispatcher.UIThread.RunJobs();
        var before = window.ClientSize.Height;

        var calls = window.ViewModel.Profiles.Single(p => p.Name == "Calls");
        setup.Config.Current.CompactProfileIds = [calls.Id];
        window.RefreshMiniList();
        Assert.Equal(new[] { "Calls" }, MiniNames(window));

        // The only picked profile is deleted: every other profile shows again, and the card's
        // changes aren't followed any more.
        window.ViewModel.Profiles.Remove(calls);
        Assert.Equal(new[] { "Gaming", "Work" }, MiniNames(window));
        calls.IsPinned = true;
        Assert.Equal(new[] { "Gaming", "Work" }, MiniNames(window));
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.ClientSize.Height < before, $"{window.ClientSize.Height} vs {before}");
        window.Close();
    }

    [AvaloniaFact]
    public void TheHotkeyTogglingMiniModeWhileTheWelcomeIsOpen_DoesntEnterTwice()
    {
        var setup = SettingsCatalog.WithProfiles();
        var window = Open(setup);
        ClickLater(window, "Cancel", _ => window.EnterCompact()); // (the welcome is shown once: this goes straight in)
        window.EnterCompact();
        Assert.True(window.IsCompact);
        Assert.Equal(300, window.Width);
        window.ExitCompact();
        Assert.False(window.IsCompact);
        window.Close();
    }

    [AvaloniaFact]
    public void Customize_ChoosesTheLayoutAndProfiles()
    {
        var setup = Ready();
        var window = Open(setup);
        window.ExpandSettings();
        window.ViewModel.SelectedCategory = "compact";
        Dispatcher.UIThread.RunJobs();
        var customize = Named<StackPanel>(window, "CompactSection").GetVisualDescendants().OfType<Button>()
            .Single(b => b.Content as string == "Customize…");

        // Cancel changes nothing.
        ClickLater(window, "Cancel", d => Click(Named<ToggleButton>(d, "LayoutGrid")));
        Click(customize);
        Assert.Equal("Rows", setup.Config.Current.CompactLayout);

        ClickLater(window, "Save", d =>
        {
            var boxes = Named<ItemsControl>(d, "ProfileChecklist").GetVisualDescendants().OfType<CheckBox>().ToList();
            boxes[1].IsChecked = true; // Work
        });
        Click(customize);
        var work = window.ViewModel.Profiles.Single(p => p.Name == "Work").Id;
        Assert.Equal(new[] { work }, setup.Config.Current.CompactProfileIds);
        Assert.Equal(new[] { "Work" }, MiniNames(window));
        window.Close();
    }

    [AvaloniaFact]
    public async Task TheSetupDialog_ShowAllTurnsOnWhenEverythingIsChecked()
    {
        var choices = new[] { "A", "B" }.Select(n => new MiniModeSetupDialog.ProfileChoice { Id = Guid.NewGuid(), Name = n }).ToList();
        var dialog = new MiniModeSetupDialog("Grid", choices);
        dialog.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.True(Named<ToggleButton>(dialog, "LayoutGrid").IsChecked);
        Assert.False(Named<ToggleButton>(dialog, "LayoutRows").IsChecked);
        var all = Named<CheckBox>(dialog, "AllToggle");
        var host = Named<Border>(dialog, "ChecklistHost");
        Assert.True(host.IsEnabled);

        foreach (var box in Named<ItemsControl>(dialog, "ProfileChecklist").GetVisualDescendants().OfType<CheckBox>())
            box.IsChecked = true;
        await Until(() => all.IsChecked == true);
        Assert.True(all.IsChecked);
        Assert.False(host.IsEnabled);
        Assert.Equal(0.45, host.Opacity);

        // "Show all" saves as no selection (every profile, now and later).
        Click(dialog, "Save");
        Assert.True(dialog.Result);
        Assert.Empty(dialog.SelectedProfileIds);
        Assert.Equal("Grid", dialog.SelectedLayout);
    }
}
