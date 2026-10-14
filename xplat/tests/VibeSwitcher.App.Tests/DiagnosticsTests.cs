using System.IO.Compression;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using VibeSwitcher.App.Diagnostics;
using VibeSwitcher.App.Hosting;
using VibeSwitcher.Helpers;
using VibeSwitcher.Models;
using VibeSwitcher.Platform;
using VibeSwitcher.Services;
using VibeSwitcher.Tests;
using static VibeSwitcher.App.Tests.Ui;
using FakePlatform = VibeSwitcher.App.Tests.AppHostTests.FakePlatform;

namespace VibeSwitcher.App.Tests;

// The friend-test tools: the self-test, the diagnostics zip and its pictures of the windows, and a
// desktop with no tray.
[Collection("Shared UI settings")] // (registers the app's log, sets the theme)
public class DiagnosticsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("vs-diag-").FullName;

    private readonly TimeSpan _savedRecheck = AppHost.TrayRecheck;

    public DiagnosticsTests() => AppHost.TrayRecheck = TimeSpan.FromMilliseconds(10);

    public void Dispose()
    {
        AppHost.TrayRecheck = _savedRecheck;
        AppLog.Register(null);
        AppErrors.Register(null);
        if (Avalonia.Application.Current is { } app && Dispatcher.UIThread.CheckAccess()) app.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Default;
        _ = DeleteSoon(_dir);
    }

    private static FakePlatform Platform()
    {
        var platform = new FakePlatform();
        platform.Audio.Playback.Add(new AudioDeviceInfo("hs", "Arctis Nova 7 Headset", true, HardwareKey: "USB\\VID_1038&PID_2202"));
        platform.Audio.Recording.Add(new AudioDeviceInfo("mic", "Arctis Nova 7 Microphone", false));
        return platform;
    }

    private void WriteConfig(Action<AppConfig>? change = null)
    {
        var config = new ConfigService(new FakeAppLogger(), new FakeSessionErrorTracker(), _dir);
        config.Load();
        config.Current.Profiles.Add(new DeviceProfile { Name = "Gaming", PlaybackDeviceId = "hs" });
        change?.Invoke(config.Current);
        config.SaveImmediate();
    }

    [AvaloniaFact]
    public async Task TheSelfTest_ChecksEachService()
    {
        WriteConfig();
        var platform = Platform();
        platform.Tray = false;
        var report = await SelfTest.RunAsync(platform, new AppPaths(_dir));
        Check Line(string name) => report.Checks.Single(c => c.Name.StartsWith(name));

        Assert.Equal(CheckStatus.Pass, Line("Settings folder").Status);
        Assert.Equal("readable, 1 profile", Line("Settings file").Detail);
        Assert.Equal("1 playback, 1 recording", Line("Audio devices").Detail);
        Assert.Contains(report.Devices, d => d.Contains("Arctis Nova 7 Headset") && d.Contains("[hs]") && d.Contains("USB\\VID_1038&PID_2202"));
        Assert.Equal(CheckStatus.Pass, Line("Mute").Status);
        Assert.Equal(CheckStatus.Pass, Line("Global shortcuts").Status);
        Assert.Empty(platform.Grabber.Keys); // (the test shortcut was let go)
        Assert.Equal(CheckStatus.Pass, Line("Banners").Status);
        Assert.Contains(("VibeSwitcher self-test", "If you can see this, notification banners work."), platform.Notifier.Shown);
        if (!OperatingSystem.IsMacOS()) Assert.Equal(CheckStatus.Warn, Line("Tray").Status); // (the macOS menu bar is always there)
        Assert.Equal(CheckStatus.Info, Line("Start at login").Status);
        Assert.Equal(CheckStatus.Info, Line("Wireless headsets").Status);

        var text = report.Format();
        Assert.StartsWith($"VibeSwitcher {Cli.Version} self-test", text);
        Assert.Contains("  ok    Audio devices", text);
        Assert.Equal(report.Problems > 0 ? 1 : 0, report.ExitCode);
    }

    [AvaloniaFact]
    public async Task ProblemsAreReported_NotThrown()
    {
        var platform = Platform();
        platform.AudioFails = true;
        platform.Grabber.Taken.Add(0x7B); // (the test key is someone else's)
        var report = await SelfTest.RunAsync(platform, new AppPaths(_dir), showBanner: false);
        var audio = report.Checks.Single(c => c.Name == "Audio devices");
        Assert.Equal((CheckStatus.Fail, "no sound system"), (audio.Status, audio.Detail));
        Assert.DoesNotContain(report.Checks, c => c.Name == "Mute"); // (nothing to ask)
        Assert.Contains("taken by another app", report.Checks.Single(c => c.Name == "Global shortcuts").Detail);
        Assert.Equal(CheckStatus.Info, report.Checks.Single(c => c.Name == "Banners").Status);
        Assert.Empty(platform.Notifier.Shown);
        Assert.Equal("none yet (made on the first start)", report.Checks.Single(c => c.Name == "Settings file").Detail);
        Assert.Equal(1, report.ExitCode);
        Assert.Contains("problem", report.Format());
    }

    [AvaloniaFact]
    public async Task GnomeShortcuts_AreReported_WithoutAddingATestOne()
    {
        // Allowed: GNOME's keyboard settings would gain an entry for a test key — none is tried.
        new Hosting.XPlatSettings(GnomeShortcutsAllowed: true).Save(_dir);
        var platform = Platform();
        var gnome = new AppHostTests.FakeGrabber();
        platform.Shortcuts = allowed => allowed
            ? new Hosting.KeyGrabberSetup(gnome, Hosting.ShortcutsWay.GnomeShortcuts, null)
            : new Hosting.KeyGrabberSetup(gnome, Hosting.ShortcutsWay.GnomeNeedsConsent, "GNOME needs your OK.");
        var report = await SelfTest.RunAsync(platform, new AppPaths(_dir), showBanner: false);
        var check = report.Checks.Single(c => c.Name == "Global shortcuts");
        Assert.Equal((CheckStatus.Pass, "GNOME's keyboard settings (allowed in Settings › Shortcuts)"), (check.Status, check.Detail));
        Assert.Equal([true], platform.ShortcutsAsked);
        Assert.Empty(gnome.Keys);

        // Not allowed: a warning that says where to allow them.
        new Hosting.XPlatSettings(GnomeShortcutsAllowed: false).Save(_dir);
        report = await SelfTest.RunAsync(platform, new AppPaths(_dir), showBanner: false);
        check = report.Checks.Single(c => c.Name == "Global shortcuts");
        Assert.Equal(CheckStatus.Warn, check.Status);
        Assert.Contains("Settings › Shortcuts", check.Detail);
    }

    [Theory]
    [InlineData("WARN LinuxNotifier: Could not show a notification: no service", "Could not show a notification: no service")]
    [InlineData("ERROR Audio: pactl failed", "pactl failed")]
    [InlineData("plain text", "plain text")]
    public void LoggedLines_LoseTheirPrefix(string line, string message) => Assert.Equal(message, SelfTest.Message(line));

    [AvaloniaFact]
    public async Task TheZip_HasTheReport_TheSettings_TheLog_AndThePictures()
    {
        WriteConfig();
        File.WriteAllText(Path.Combine(_dir, "error.log"), "[2026-10-15] [WARN] Something: happened\n");
        // (The running app has its log open.)
        using var open = new FileStream(Path.Combine(_dir, "error.log"), FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        var zip = Path.Combine(_dir, "out", "diag.zip");
        await DiagnosticsBundle.CreateAsync(zip, Platform(), new AppPaths(_dir), folder =>
        {
            File.WriteAllText(Path.Combine(folder, "settings-profiles.png"), "png");
            return Task.FromResult<IReadOnlyList<string>>(["Screen 1: 1920×1080 px at 0,0, scaling 1 (main)"]);
        });

        using var archive = ZipFile.OpenRead(zip);
        var names = archive.Entries.Select(e => e.FullName.Replace('\\', '/')).OrderBy(n => n).ToList();
        Assert.Equal(new[] { "config.json", "error.log", "report.txt", "screenshots/settings-profiles.png", "system.txt" }, names);
        string Read(string name) => new StreamReader(archive.GetEntry(name)!.Open()).ReadToEnd();
        Assert.Contains("Audio devices:", Read("report.txt"));
        Assert.Contains("Arctis Nova 7 Headset", Read("report.txt"));
        Assert.Contains("Screen 1: 1920×1080", Read("system.txt"));
        Assert.Contains($"VibeSwitcher {Cli.Version}", Read("system.txt"));
        Assert.Contains("happened", Read("error.log"));
    }

    [AvaloniaFact]
    public async Task ThePictures_ShowEachWindow_OnACopyOfTheSettings()
    {
        // ("Close to tray" off: closing Settings would quit the app — not in the pictures run.)
        WriteConfig(c => c.CloseToTray = false);
        var before = File.ReadAllText(Path.Combine(_dir, "config.json"));
        var folder = Directory.CreateDirectory(Path.Combine(_dir, "shots")).FullName;
        var old = ScreenshotTour.PauseScale;
        ScreenshotTour.PauseScale = 0.05;
        try
        {
            var notes = await ScreenshotTour.RunAsync(folder, Platform(), new AppPaths(_dir), new FakeAppLogger());
            Assert.DoesNotContain(notes, n => n.Contains("failed"));
            Assert.Contains(notes, n => n.StartsWith("Screen 1:"));
        }
        finally { ScreenshotTour.PauseScale = old; }

        var pictures = Directory.GetFiles(folder).Select(Path.GetFileNameWithoutExtension).ToHashSet();
        foreach (var name in new[] { "settings-profiles", "settings-startup", "settings-uninstall", "settings-about", "settings-faq",
                     "settings-minimode", "dialog-alert", "dialog-app-triggers", "dialog-clone", "splash", "banner", "menu" })
            Assert.Contains(name, pictures);
        using (var png = File.OpenRead(Path.Combine(folder, "settings-profiles.png")))
        {
            var head = new byte[4];
            png.ReadExactly(head);
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, head);
        }
        // The user's own settings weren't touched.
        Assert.Equal(before, File.ReadAllText(Path.Combine(_dir, "config.json")));
    }

    [AvaloniaFact]
    public void SettingsCanCloseForGood_WithoutQuitting()
    {
        var setup = SettingsCatalog.WithProfiles();
        setup.Config.Current.CloseToTray = false;
        var window = setup.Window();
        window.Show();
        window.ClosesForGood = true;
        window.Close();
        Assert.False(window.QuitsWhenClosed);
        Assert.False(window.IsVisible);

        var quitting = setup.Window();
        quitting.Show();
        quitting.Close();
        Assert.True(quitting.QuitsWhenClosed); // (the normal close, with "Close to tray" off, quits)
    }

    [Theory]
    [InlineData("bluez_output.AA_BB_CC_DD_EE_FF.1", "bluez_output.xx_xx_xx_xx_EE_FF.1")]
    [InlineData("headset 12:34:56:78:9A:BC here", "headset xx:xx:xx:xx:9A:BC here")]
    [InlineData("0f8fad5b-d9cb-469f-a165-70867728950e", "0f8fad5b-d9cb-469f-a165-70867728950e")]
    [InlineData("at 12:34:56", "at 12:34:56")]
    public void BluetoothAddresses_ArePartlyHidden(string text, string masked) =>
        Assert.Equal(masked, DiagnosticsBundle.MaskAddresses(text));

    [Fact]
    public void TheZipGoesOnTheDesktop_OrHome()
    {
        var path = DiagnosticsBundle.DefaultPath();
        Assert.Matches(@"VibeSwitcher-diagnostics-\d{8}-\d{6}\.zip$", path);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.StartsWith(home, path);
    }

    // ── A desktop with no tray ─────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public async Task ATrayThatTurnsUpSoon_IsUsed()
    {
        WriteConfig(c => c.StartMinimized = true);
        var platform = Platform();
        platform.Tray = false;
        var host = new AppHost(platform, new AppPaths(_dir), AppCommand.Open);
        await host.StartAsync(showSplash: false);
        Assert.False(host.TrayAvailable);
        platform.Tray = true; // (the desktop's panel came up after the app, at login)
        Assert.True(await host.TrayKnown);
        Assert.True(host.TrayAvailable);
        Assert.Null(host.Windows.Window); // (started minimized, as asked: the tray is there)
        Assert.True(host.Windows.CanHide);
        Assert.False(File.Exists(host.NoTrayMarker));
        host.Dispose();
    }

    [AvaloniaFact]
    public async Task WithNoTray_AShortcutLaunch_StillShowsSettings()
    {
        WriteConfig();
        var platform = Platform();
        platform.Tray = false;
        var host = new AppHost(platform, new AppPaths(_dir), new AppCommand(AppCommandKind.Switch, "Gaming"));
        await host.StartAsync(showSplash: false);
        Assert.False(await host.TrayKnown);
        await Until(() => host.Windows.Window?.IsVisible == true);
        Assert.True(host.Windows.Window!.IsVisible);
        Assert.False(host.Windows.CanHide); // (the Settings shortcut minimizes it instead of hiding it)
        host.Windows.Window.Close();
        host.Dispose();
    }

    [AvaloniaFact]
    public async Task WithNoTray_SettingsOpens_SaysWhyOnce_AndClosingDoesntHideIt()
    {
        WriteConfig();
        var config = new ConfigService(new FakeAppLogger(), new FakeSessionErrorTracker(), _dir);
        config.Load();
        config.Current.StartMinimized = true;
        config.SaveImmediate();
        var platform = Platform();
        platform.Tray = false;
        var host = new AppHost(platform, new AppPaths(_dir), AppCommand.Open);
        await host.StartAsync(showSplash: false);
        Assert.False(host.TrayAvailable);
        Assert.False(await host.TrayKnown);
        await Until(() => host.Windows.Window?.IsVisible == true);
        var settings = host.Windows.Window!;
        Assert.True(settings.IsVisible); // (opened although starting minimized: there's nowhere else to be)
        Assert.False(settings.TrayAvailable);
        Assert.True(File.Exists(host.NoTrayMarker)); // (explained once)
        Assert.True(settings.ViewModel.CloseToTray);

        settings.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.False(settings.IsVisible);
        Assert.Null(host.Windows.Window); // closed for real (it quits), not hidden in a tray that isn't there
        host.Dispose();
    }
}
