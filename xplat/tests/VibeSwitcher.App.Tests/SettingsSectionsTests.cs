using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VibeSwitcher.App.Settings;
using VibeSwitcher.Models;
using VibeSwitcher.Platform;
using VibeSwitcher.Services;
using VibeSwitcher.Tests;

using static VibeSwitcher.App.Tests.Ui;

namespace VibeSwitcher.App.Tests;

// The Settings page (sidebar and sections), About and the FAQ, around the shared view model.
[Collection("Shared UI settings")] // (changes Wording.Mac; so does ScreenshotTests)
public class SettingsSectionsTests
{
    private static SettingsWindow Open(SettingsCatalog.Setup setup, bool mac = false)
    {
        var old = Wording.Mac;
        Wording.Mac = mac;
        try
        {
            var window = setup.Window();
            window.Show();
            Dispatcher.UIThread.RunJobs();
            SettingsCatalog.WaitForDevices(window);
            return window;
        }
        finally { Wording.Mac = old; }
    }

    private static readonly string[] Sections =
        ["StartupSection", "AppearSection", "NotifSection", "DevicesSection", "TraySection", "ShortcutsSection", "CompactSection", "LogsSection", "UninstallSection"];

    private static readonly HotkeyDefinition CtrlAltV = new() { VirtualKeyCode = 0x56, UseCtrl = true, UseAlt = true };
    private static readonly HotkeyDefinition CtrlAltG = new() { VirtualKeyCode = 0x47, UseCtrl = true, UseAlt = true };

    [AvaloniaFact]
    public void TheSidebar_ShowsOneSectionAtATime_AndMarksIt()
    {
        var window = Open(new SettingsCatalog.Setup());
        window.ExpandSettings();
        Assert.Equal("SettingsBodyScrollViewer", window.CurrentPanel);

        var navs = new[] { "NavStartup", "NavAppear", "NavNotif", "NavDevices", "NavTray", "NavShortcuts", "NavCompact", "NavLogs", "UninstallNavBtn" };
        for (int i = 0; i < navs.Length; i++)
        {
            Click(Named<Button>(window, navs[i]));
            for (int j = 0; j < Sections.Length; j++)
            {
                Assert.Equal(i == j, Named<StackPanel>(window, Sections[j]).IsVisible);
                Assert.Equal(i == j, Named<Button>(window, navs[j]).Classes.Contains("selected"));
            }
        }

        Click(Named<Button>(window, "SettingsBackBtn"));
        Assert.Equal("ProfilesPanel", window.CurrentPanel);
        window.Close();
    }

    [AvaloniaFact]
    public void Wording_FollowsTheSystem()
    {
        var linux = Open(new SettingsCatalog.Setup(), mac: false);
        var mac = Open(new SettingsCatalog.Setup(), mac: true);
        foreach (var w in new[] { linux, mac }) { w.ExpandSettings(); w.ViewModel.SelectedCategory = "startup"; }
        Dispatcher.UIThread.RunJobs();

        var linuxTexts = Texts(Named<StackPanel>(linux, "StartupSection")).ToList();
        var macTexts = Texts(Named<StackPanel>(mac, "StartupSection")).ToList();
        Assert.Contains("Start at login", linuxTexts);
        Assert.Contains("Close to tray", linuxTexts);
        Assert.Contains("Open at login", macTexts);
        Assert.Contains("Close to the menu bar", macTexts);
        Assert.DoesNotContain(linuxTexts.Concat(macTexts), t => t?.Contains("Windows") == true || t?.Contains(" PC ") == true);

        Assert.Contains("Tray", Texts(Named<Button>(linux, "NavTray")));
        Assert.Contains("Menu Bar", Texts(Named<Button>(mac, "NavTray")));
        Assert.Equal("Follow system", Named<RadioButton>(mac, "ThemeAuto").Content);
        Assert.Equal("Show in Finder", Named<Button>(mac, "BrowseInstallBtn").Content);
        linux.Close();
        mac.Close();
    }

    [AvaloniaFact]
    public void Switches_ChangeTheSettings()
    {
        var setup = new SettingsCatalog.Setup();
        var window = Open(setup);
        window.ExpandSettings();
        window.ViewModel.SelectedCategory = "startup";
        Dispatcher.UIThread.RunJobs();

        var startAtLogin = Named<CheckBox>(window, "StartAtLoginToggle");
        startAtLogin.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(setup.Startup.StartupEnabled);
        startAtLogin.IsChecked = false;
        Dispatcher.UIThread.RunJobs();
        Assert.False(setup.Startup.StartupEnabled);

        // The Settings › Devices rows Windows has but macOS and Linux don't are left out.
        Assert.DoesNotContain(Texts(Named<StackPanel>(window, "DevicesSection")), t => t?.Contains("legacy") == true || t?.Contains("disabled") == true);
        window.Close();
    }

    [AvaloniaFact]
    public void ThemeAndLogoPickers_FollowAndSetTheSetting()
    {
        var setup = new SettingsCatalog.Setup();
        setup.Config.Current.Theme = "Auto";
        var window = Open(setup);
        window.ExpandSettings();
        window.ViewModel.SelectedCategory = "appear";
        Dispatcher.UIThread.RunJobs();

        Assert.True(Named<RadioButton>(window, "ThemeAuto").IsChecked);
        Named<RadioButton>(window, "ThemeDark").IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Dark", setup.Config.Current.Theme);
        Assert.Equal(new[] { "Dark" }, setup.AppliedThemes);
        Assert.False(Named<RadioButton>(window, "ThemeAuto").IsChecked);

        // Changed elsewhere (an import, Reset): the pickers follow.
        window.ViewModel.Theme = "Follow Windows";
        Dispatcher.UIThread.RunJobs();
        Assert.True(Named<RadioButton>(window, "ThemeAuto").IsChecked);
        Assert.False(Named<RadioButton>(window, "ThemeDark").IsChecked);
        Assert.Equal("Auto", setup.Config.Current.Theme);

        var oldMode = VibeSwitcher.Controls.LogoAnimator.Mode;
        try
        {
            Named<RadioButton>(window, "LogoReduced").IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Reduced", setup.Config.Current.LogoAnimation);
            Assert.Equal(VibeSwitcher.Controls.LogoAnimator.AnimMode.Reduced, VibeSwitcher.Controls.LogoAnimator.Mode);
        }
        finally { VibeSwitcher.Controls.LogoAnimator.Mode = oldMode; }
        window.Close();
    }

    [AvaloniaFact]
    public void SettingAShortcut_TurnsHotkeysOffWhileAsking_ThenOnAgain()
    {
        var setup = SettingsCatalog.WithProfiles();
        var window = Open(setup);
        window.ExpandSettings();
        window.ViewModel.SelectedCategory = "shortcuts";
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("unset", Named<TextBlock>(window, "SettingsHotkeyText").Classes);
        Assert.False(Named<Button>(window, "SettingsHotkeyClearBtn").IsVisible);

        string? subtitle = null;
        int registeredWhileAsking = -1;
        window.CaptureHotkey = (seed, sub) =>
        {
            subtitle = sub;
            registeredWhileAsking = setup.Hotkeys.RegisteredProfiles.Count;
            return CtrlAltV;
        };
        Click(Named<Button>(window, "SettingsHotkeySetBtn"));

        Assert.Equal("Press any key combination to assign a shortcut for opening Settings", subtitle);
        Assert.Equal(0, registeredWhileAsking);
        Assert.Equal("Gaming", Assert.Single(setup.Hotkeys.RegisteredProfiles).Name); // back on afterwards (the one with a hotkey)
        Assert.True(CtrlAltV.Matches(setup.Config.Current.SettingsHotkey!));
        Assert.DoesNotContain("unset", Named<TextBlock>(window, "SettingsHotkeyText").Classes);
        Assert.True(Named<Button>(window, "SettingsHotkeyClearBtn").IsVisible);

        Click(Named<Button>(window, "SettingsHotkeyClearBtn"));
        Assert.True(setup.Config.Current.SettingsHotkey!.IsEmpty);
        Assert.Equal("Not set", Named<TextBlock>(window, "SettingsHotkeyText").Text);
        window.Close();
    }

    [AvaloniaFact]
    public void AShortcutInUse_IsRefused_WithAChanceToTryAgain()
    {
        var setup = SettingsCatalog.WithProfiles(); // "Gaming" has Ctrl+Alt+G
        var window = Open(setup);
        window.ExpandSettings();
        window.ViewModel.SelectedCategory = "shortcuts";
        Dispatcher.UIThread.RunJobs();

        var seeds = new List<HotkeyDefinition>();
        window.CaptureHotkey = (seed, _) =>
        {
            seeds.Add(seed);
            return seeds.Count == 1 ? CtrlAltG : null; // the second time: cancelled
        };
        string? message = null;
        ClickLater(window, "Try Again", d => message = string.Join(" ", Texts(d)));
        Click(Named<Button>(window, "MuteMicSetBtn"));
        Assert.Equal(2, seeds.Count);
        Assert.True(seeds[1].Matches(CtrlAltG)); // tries again from the refused keys
        Assert.Contains("is already assigned to \"Gaming\"", message);
        Assert.True(setup.Config.Current.MuteMicHotkey is null or { IsEmpty: true });
        Assert.Single(setup.Hotkeys.RegisteredProfiles);
        window.Close();
    }

    [AvaloniaFact]
    public void HotkeysComeBackOn_EvenWhenAskingFails()
    {
        var setup = SettingsCatalog.WithProfiles();
        var window = Open(setup);
        window.CaptureHotkey = (_, _) => throw new InvalidOperationException("capture failed");
        // (The XAML-wired handler's exception arrives wrapped.)
        var thrown = Assert.ThrowsAny<Exception>(() => Click(Named<Button>(window, "CompactHotkeySetBtn")));
        Assert.IsType<InvalidOperationException>(thrown.GetBaseException());
        Assert.Single(setup.Hotkeys.RegisteredProfiles);
        Assert.True(setup.Config.Current.CompactHotkey is null or { IsEmpty: true });
        window.Close();
    }

    [Fact]
    public void TheConfigPath_IsShownFromTheHomeFolder()
    {
        Assert.Equal("~/.config/VibeSwitcher/config.json", Wording.Home("/home/ann/.config/VibeSwitcher/config.json", "/home/ann"));
        Assert.Equal("~/x/VibeSwitcher/config.json", Wording.Home("/home/ann/x/VibeSwitcher/config.json", "/home/ann/"));
        Assert.Equal("/srv/cfg/VibeSwitcher/config.json", Wording.Home("/srv/cfg/VibeSwitcher/config.json", "/home/ann"));
        Assert.Equal("/home/annie/c.json", Wording.Home("/home/annie/c.json", "/home/ann"));
    }

    [AvaloniaFact]
    public void FindHotkeyConflict_NamesTheOwner_LeavingOutTheOneBeingSet()
    {
        var setup = SettingsCatalog.WithProfiles();
        var cfg = setup.Config.Current;
        cfg.SettingsHotkey = CtrlAltV;
        cfg.MuteBothHotkey = new HotkeyDefinition { VirtualKeyCode = 0x4D, UseCtrl = true };
        var window = Open(setup);

        Assert.Equal("\"Gaming\"", window.FindHotkeyConflict(CtrlAltG, excludeScope: null));
        Assert.Equal("\"Open / Close VibeSwitcher\"", window.FindHotkeyConflict(CtrlAltV, excludeScope: null));
        Assert.Null(window.FindHotkeyConflict(CtrlAltV, excludeScope: null, excludeSettingsHotkey: true));
        Assert.Equal("\"Mute Mic + Speakers\"", window.FindHotkeyConflict(cfg.MuteBothHotkey, excludeScope: MuteScope.Mic));
        Assert.Null(window.FindHotkeyConflict(cfg.MuteBothHotkey, excludeScope: MuteScope.Both));
        Assert.Null(window.FindHotkeyConflict(new HotkeyDefinition { VirtualKeyCode = 0x50, UseAlt = true }, excludeScope: null));
        window.Close();
    }

    [AvaloniaFact]
    public void MuteShortcuts_SetAndClearPerScope()
    {
        var setup = SettingsCatalog.WithProfiles();
        var window = Open(setup);
        window.CaptureHotkey = (_, sub) => sub.Contains("speakers") ? new HotkeyDefinition { VirtualKeyCode = 0x53, UseCtrl = true, UseShift = true } : null;
        Click(Named<Button>(window, "MuteSpeakersSetBtn"));
        Assert.False(setup.Config.Current.MuteSpeakersHotkey!.IsEmpty);
        Assert.True(window.ViewModel.MuteSpeakersHotkeyIsSet);
        Assert.False(window.ViewModel.MuteMicHotkeyIsSet);
        window.Close();
    }

    [AvaloniaFact]
    public async Task ExportAndImport_UseTheConfigFile_AndSayHowItWent()
    {
        var dir = Directory.CreateTempSubdirectory("vs-settings-").FullName;
        try
        {
            var logger = new FakeAppLogger();
            var errors = new FakeSessionErrorTracker();
            var config = new ConfigService(logger, errors, dir);
            config.Load();
            config.Current.Profiles.Add(new DeviceProfile { Name = "Exported" });
            var window = new SettingsWindow(config, new FakeAudioService(), new FakeHotkeyService(), new FakeStartupService(), logger, errors, _ => { });
            window.Show();

            var backup = Path.Combine(dir, "backup.json");
            window.PickExportPath = () => Task.FromResult<string?>(backup);
            string? alert = null;
            ClickLater(window, "OK", d => alert = string.Join(" ", Texts(d)));
            Click(Named<Button>(window, "ExportBtn"));
            await Until(() => alert != null);
            Assert.True(File.Exists(backup));
            Assert.Contains("Export Successful", alert);

            // A folder that isn't there: the export says it failed, and why.
            alert = null;
            window.PickExportPath = () => Task.FromResult<string?>(Path.Combine(dir, "missing", "backup.json"));
            ClickLater(window, "OK", d => alert = string.Join(" ", Texts(d)));
            Click(Named<Button>(window, "ExportBtn"));
            await Until(() => alert != null);
            Assert.Contains("Export Failed", alert);

            // Import asks first: Cancel leaves everything as it is; Import replaces it.
            config.Current.Profiles.Clear();
            window.PickImportPath = () => Task.FromResult<string?>(backup);
            ClickLater(window, "Cancel", d => Assert.Contains(Texts(d), t => t?.Contains("Importing 'backup.json'") == true));
            Click(Named<Button>(window, "ImportBtn"));
            await Until(() => window.OwnedWindows.Count == 0);
            Assert.Empty(config.Current.Profiles);

            ClickLater(window, "Import");
            Click(Named<Button>(window, "ImportBtn"));
            await Until(() => config.Current.Profiles.Any(p => p.Name == "Exported"));
            Assert.Contains(config.Current.Profiles, p => p.Name == "Exported");
            Assert.Contains(window.ViewModel.Profiles, p => p.Name == "Exported");

            // A broken file: nothing changes, and the import says why.
            await File.WriteAllTextAsync(backup, "{ not json");
            alert = null;
            ClickLater(window, "Import", _ => ClickLater(window, "OK", d => alert = string.Join(" ", Texts(d))));
            Click(Named<Button>(window, "ImportBtn"));
            await Until(() => alert != null);
            Assert.Contains("Import Failed", alert);
            Assert.Contains(config.Current.Profiles, p => p.Name == "Exported");

            // Cancelling the picker does nothing.
            window.PickImportPath = () => Task.FromResult<string?>(null);
            Click(Named<Button>(window, "ImportBtn"));
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(window.OwnedWindows);
            window.Close();
        }
        finally { await DeleteSoon(dir); }
    }

    [AvaloniaFact]
    public void TheInstallPage_DescribesThisCopy_AndOnlyOffersUninstallWhereItCan()
    {
        var window = Open(new SettingsCatalog.Setup());
        var dir = Directory.CreateTempSubdirectory("vs-install-").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "VibeSwitcher"), new byte[3000]);
            window.Install = new InstallInfo(InstallKind.Package, dir, "2.0.1");
            window.DataFolder = Path.Combine(dir, "missing");
            window.Version = "2.0.1-preview";
            window.RefreshStorageInfo();
            Assert.Equal("2.0.1-preview", Named<TextBlock>(window, "InstallVersionText").Text);
            Assert.Equal(dir, Named<TextBlock>(window, "InstallLocationText").Text);
            Assert.Equal("2.9 KB", Named<TextBlock>(window, "InstallSizeText").Text);
            Assert.Equal("—", Named<TextBlock>(window, "DataSizeText").Text);
            Assert.True(Named<Button>(window, "UninstallBtn").IsEnabled);
            Assert.False(Named<TextBlock>(window, "UninstallBlockedNote").IsVisible);
            Assert.Contains("asked for your password", Named<TextBlock>(window, "UninstallDetailText").Text);
            Assert.Equal($"Version 2.0.1-preview — {Wording.Platform}", Named<TextBlock>(window, "AboutVersionText").Text);

            window.Install = new InstallInfo(InstallKind.Development, dir, "2.0.1");
            window.RefreshStorageInfo();
            Assert.False(Named<Button>(window, "UninstallBtn").IsEnabled);
            Assert.Contains("development build", Named<TextBlock>(window, "UninstallBlockedNote").Text);

            window.Install = new InstallInfo(InstallKind.MacApp, "/private/var/folders/x/AppTranslocation/1/d/VibeSwitcher.app", "2.0.1", Translocated: true);
            window.RefreshStorageInfo();
            Assert.False(Named<Button>(window, "UninstallBtn").IsEnabled);
            Assert.Contains("Applications folder", Named<TextBlock>(window, "UninstallBlockedNote").Text);
        }
        finally
        {
            window.Close();
            Directory.Delete(dir, recursive: true);
        }
    }

    [AvaloniaFact]
    public void FormatSize_CountsFilesNotLinks()
    {
        var dir = Directory.CreateTempSubdirectory("vs-size-").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "a"), new byte[512]);
            Directory.CreateDirectory(Path.Combine(dir, "sub"));
            File.WriteAllBytes(Path.Combine(dir, "sub", "b"), new byte[1536]);
            Assert.Equal("2 KB", SettingsWindow.FormatSize(dir));
            Assert.Equal("512 B", SettingsWindow.FormatSize(Path.Combine(dir, "a")));
            Assert.Equal("—", SettingsWindow.FormatSize(Path.Combine(dir, "none")));
            if (!OperatingSystem.IsWindows())
            {
                // A link back up the tree isn't followed (no endless loop, no double counting).
                File.CreateSymbolicLink(Path.Combine(dir, "sub", "loop"), dir);
                Assert.Equal("2 KB", SettingsWindow.FormatSize(dir));
            }
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [AvaloniaFact]
    public async Task Uninstalling_AsksFirst_ThenHandsOverWithTheDataChoice()
    {
        var window = Open(new SettingsCatalog.Setup());
        var install = new InstallInfo(InstallKind.AppImage, "/home/alex/VibeSwitcher.AppImage", "2.0.1");
        window.Install = install;
        window.RefreshStorageInfo();
        (InstallInfo Install, bool DeleteData)? asked = null;
        window.Uninstall = (i, deleteData) => asked = (i, deleteData);

        ClickLater(window, "Cancel");
        Click(Named<Button>(window, "UninstallBtn"));
        await Until(() => window.OwnedWindows.Count == 0);
        Assert.Null(asked);

        ClickLater(window, "Uninstall", d =>
        {
            Assert.Contains(Texts(d), t => t?.Contains("AppImage file will be deleted") == true);
            d.GetVisualDescendants().OfType<CheckBox>().Single().IsChecked = true;
        });
        Click(Named<Button>(window, "UninstallBtn"));
        await Until(() => asked != null);
        Assert.Equal((install, true), asked);
        window.Close();
    }

    [AvaloniaFact]
    public void FaqLinks_JumpToWhatTheyTalkAbout()
    {
        var window = Open(SettingsCatalog.WithProfiles());
        window.OpenFaqPanel();
        Dispatcher.UIThread.RunJobs();
        var links = Named<ScrollViewer>(window, "FaqPanel").GetLogicalDescendants().OfType<Button>()
            .Where(b => b.Theme == window.FindResource("FaqLink")).ToList();
        Assert.Equal(20, links.Count);
        // Each sits on its sentence's line: it says where its text's baseline is (about 4/5 of the way down).
        Assert.All(links, l => Assert.InRange(TextBlock.GetBaselineOffset(l), 9, 15));

        Button Link(string tag) => links.First(b => b.Tag as string == tag);

        Click(Link("shortcuts"));
        Assert.Equal("SettingsBodyScrollViewer", window.CurrentPanel);
        Assert.True(window.ViewModel.IsShortcutsSelected);

        window.OpenFaqPanel();
        Click(Link("installSettings"));
        Assert.True(window.ViewModel.IsUninstallSelected);

        window.OpenFaqPanel();
        Click(Link("filters"));
        Assert.Equal("ProfilesPanel", window.CurrentPanel);
        Assert.True(window.FilterBarOpen);

        window.OpenFaqPanel();
        Click(Link("profiles"));
        Assert.Equal("ProfilesPanel", window.CurrentPanel);
        Assert.False(window.FilterBarOpen);
        window.Close();
    }

    [AvaloniaFact]
    public void TheFaqAndAbout_SpeakForTheSystem()
    {
        var linux = Open(new SettingsCatalog.Setup(), mac: false);
        var mac = Open(new SettingsCatalog.Setup(), mac: true);

        string Faq(Window w) => string.Join(" ", w.FindControl<ScrollViewer>("FaqPanel")!.GetLogicalDescendants().OfType<TextBlock>()
            .Select(t => t.Inlines is { Count: > 0 } inlines
                ? string.Concat(inlines.Select(i => i is Run r ? r.Text : i is InlineUIContainer { Child: Button b } ? b.Content as string : ""))
                : t.Text));

        var linuxFaq = Faq(linux);
        var macFaq = Faq(mac);
        Assert.Contains("VibeSwitcher is a Linux system tray app that lets you", linuxFaq);
        Assert.Contains("VibeSwitcher is a macOS menu bar app that lets you", macFaq);
        Assert.Contains("~/.config/VibeSwitcher/config.json", linuxFaq);
        Assert.Contains("~/Library/Application Support/VibeSwitcher/config.json", macFaq);
        Assert.Contains("Settings → Menu Bar", macFaq);
        Assert.Contains("Click + New Profile to create a profile.", linuxFaq); // the link sits in the sentence
        foreach (var faq in new[] { linuxFaq, macFaq })
            Assert.DoesNotContain("Windows", faq);

        var rows = mac.FindControl<ScrollViewer>("AboutPanel")!.GetLogicalDescendants().OfType<ItemsControl>().Single().ItemCount;
        Assert.Equal(6, rows);
        Assert.EndsWith("— macOS 14 or later", mac.FindControl<TextBlock>("AboutVersionText")!.Text);
        linux.Close();
        mac.Close();
    }

    [Fact]
    public void Opener_UsesTheSystemsTools_AndOnlyOpensWebLinks()
    {
        var reveal = SystemOpener.RevealCommand(true, "/Applications/VibeSwitcher.app");
        Assert.Equal("/usr/bin/open", reveal.Program);
        Assert.Equal(["-R", "/Applications/VibeSwitcher.app"], reveal.Args);
        Assert.Equal(["/home/a/Apps"], SystemOpener.RevealCommand(false, "/home/a/Apps/VibeSwitcher.AppImage").Args);
        Assert.Equal("xdg-open", SystemOpener.FolderCommand(false, "/opt/vibeswitcher").Program);
        Assert.Equal("xdg-open", SystemOpener.UrlCommand(false, "https://github.com").Program);

        Assert.True(SystemOpener.IsWebUrl("https://github.com/raphymany/vibeswitcher"));
        Assert.False(SystemOpener.IsWebUrl("file:///etc/passwd"));
        Assert.False(SystemOpener.IsWebUrl("javascript:alert(1)"));
        Assert.False(SystemOpener.IsWebUrl("/usr/bin/xterm"));
        Assert.False(SystemOpener.OpenUrl("smb://server/share", out var error));
        Assert.Contains("isn't a web address", error);
    }

    [AvaloniaFact]
    public void ScrollBars_TakeTheirOwnRoom_AndMenuItemsMarkTheActiveOne()
    {
        var setup = SettingsCatalog.WithProfiles();
        var window = Open(setup);
        window.OpenFaqPanel();
        Dispatcher.UIThread.RunJobs();
        var faq = Named<ScrollViewer>(window, "FaqPanel");
        var bar = faq.GetVisualDescendants().OfType<ScrollBar>().Single(b => b.Orientation == Avalonia.Layout.Orientation.Vertical);
        Assert.True(bar.IsVisible);
        Assert.Equal(20, bar.Bounds.Width);
        var presenter = faq.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ScrollContentPresenter>().First();
        Assert.True(presenter.Bounds.Right <= bar.Bounds.Left + 0.5, "the content runs under the scroll bar");
        window.Close();

        var item = new MenuItem { Header = "Gaming", IsChecked = true };
        var host = new Window { Content = new StackPanel { Children = { item } } };
        host.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.True(item.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Rectangle>().Single(r => r.Name == "ActiveBar").IsVisible);
        item.IsChecked = false;
        Dispatcher.UIThread.RunJobs();
        Assert.False(item.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Rectangle>().Single(r => r.Name == "ActiveBar").IsVisible);
        host.Close();
    }
}
