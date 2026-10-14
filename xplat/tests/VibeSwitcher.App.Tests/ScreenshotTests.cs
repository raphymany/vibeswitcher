using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using VibeSwitcher.App.Controls;

namespace VibeSwitcher.App.Tests;

// Screenshots of every dialog in light and dark, with the Linux frame and the macOS one. They're
// saved to screenshots/ next to the test binaries on every OS (CI keeps them for review). On Linux —
// where CI renders them the same way every time — they're also compared with Baselines/, so the
// look can't change unnoticed. After an intended change: VS_UPDATE_SCREENSHOTS=1 on Linux rewrites
// the baselines (review them before committing; scripts/wsl-test.sh works on a copy of the repo, so
// copy ~/vs-src/xplat/tests/VibeSwitcher.App.Tests/Baselines back from there).
[Collection("Shared UI settings")] // (changes Wording.Mac, the logo mode and hotkey labels; so do the Settings tests)
public class ScreenshotTests
{
    public static TheoryData<string, string, string> Cases()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var name in DialogCatalog.Names)
            foreach (var theme in new[] { "light", "dark" })
                foreach (var chrome in new[] { "linux", "mac" })
                    data.Add(name, theme, chrome);
        return data;
    }

    public static TheoryData<string, string, string> SettingsCases()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var name in SettingsCatalog.Names)
            foreach (var theme in new[] { "light", "dark" })
                foreach (var chrome in new[] { "linux", "mac" })
                    data.Add(name, theme, chrome);
        return data;
    }

    [AvaloniaTheory]
    [MemberData(nameof(Cases))]
    public void Dialog(string name, string theme, string chrome)
    {
        // (In each system's words: the dialogs created inside read them.)
        var oldWording = VibeSwitcher.App.Settings.Wording.Mac;
        VibeSwitcher.App.Settings.Wording.Mac = chrome == "mac";
        // (The mic level is set directly: a busy test run could draw it before the meter's first reading.
        // The app picker's lists load in the background: they're waited for.)
        try
        {
            Capture(name, theme, chrome, () => DialogCatalog.Create(name), w =>
            {
                (w as Dialogs.MicTestDialog)?.ShowLevel(0.035f);
                var giveUp = DateTime.UtcNow.AddSeconds(5);
                while (w is Dialogs.AppTriggerDialog { AppsLoaded: false } && DateTime.UtcNow < giveUp) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
            });
        }
        finally { VibeSwitcher.App.Settings.Wording.Mac = oldWording; }
    }

    // The Settings window: the Profiles page, with the filter bar open, with a profile's popup open,
    // and with no profiles; each Settings page, About and the FAQ — in the system's own words (the
    // macOS ones with the Mac frame). (The logo is shown at rest, so every run draws the same picture.)
    [AvaloniaTheory]
    [MemberData(nameof(SettingsCases))]
    public void Settings(string name, string theme, string chrome)
    {
        var oldMode = VibeSwitcher.Controls.LogoAnimator.Mode;
        var oldLabels = VibeSwitcher.Models.HotkeyDefinition.LabelStyle;
        var oldWording = VibeSwitcher.App.Settings.Wording.Mac;
        VibeSwitcher.Controls.LogoAnimator.Mode = VibeSwitcher.Controls.LogoAnimator.AnimMode.Static;
        VibeSwitcher.Models.HotkeyDefinition.LabelStyle = chrome == "mac"
            ? VibeSwitcher.Models.HotkeyLabelStyle.MacOS : VibeSwitcher.Models.HotkeyLabelStyle.Linux;
        VibeSwitcher.App.Settings.Wording.Mac = chrome == "mac";
        try
        {
            var setup = name == "settings-empty" ? new SettingsCatalog.Setup() : SettingsCatalog.WithProfiles();
            setup.Config.Current.CloseToTray = false;
            if (name.StartsWith("settings-minimode-"))
            {
                // Mini Mode as the app restores it at start-up (rows / icon grid).
                setup.Config.Current.CompactMode = true;
                setup.Config.Current.CompactLayout = name.EndsWith("grid") ? "Grid" : "Rows";
            }
            if (name == "settings-shortcuts")
            {
                // One shortcut of each kind set (one with its banner silenced), the others not.
                setup.Config.Current.SettingsHotkey = new VibeSwitcher.Models.HotkeyDefinition { VirtualKeyCode = 0x56, UseCtrl = true, UseAlt = true };
                setup.Config.Current.MuteMicHotkey = new VibeSwitcher.Models.HotkeyDefinition { VirtualKeyCode = 0x4D, UseCtrl = true, UseShift = true };
                setup.Config.Current.MuteMicSilent = true;
            }
            Capture(name, theme, chrome, setup.Window, window =>
            {
                var settings = (VibeSwitcher.App.Settings.SettingsWindow)window;
                SettingsCatalog.WaitForDevices(settings);
                SettingsCatalog.FixInstall(settings, chrome == "mac");
                if (SettingsCatalog.Pages.TryGetValue(name, out var page))
                {
                    settings.ExpandSettings();
                    settings.ViewModel.SelectedCategory = page;
                }
                if (name == "settings-about") settings.OpenAboutPanel();
                if (name == "settings-faq") settings.OpenFaqPanel();
                if (name == "settings-filters")
                {
                    settings.FindControl<Avalonia.Controls.Button>("FiltersToggleBtn")!
                        .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                    var vm = settings.ViewModel;
                    vm.PinnedFilter = true;
                    vm.ScheduledFilter = true;
                    // (Let the slide finish.)
                    var until = DateTime.UtcNow.AddMilliseconds(400);
                    while (DateTime.UtcNow < until) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                }
                if (name == "settings-popup") settings.OpenProfileDetail(settings.ViewModel.Profiles[0]);
            });
        }
        finally
        {
            VibeSwitcher.Controls.LogoAnimator.Mode = oldMode;
            VibeSwitcher.Models.HotkeyDefinition.LabelStyle = oldLabels;
            VibeSwitcher.App.Settings.Wording.Mac = oldWording;
        }
    }

    private static void Capture(string name, string theme, string chrome, Func<Avalonia.Controls.Window> create, Action<Avalonia.Controls.Window> prepare)
    {
        var app = Application.Current!;
        var oldChrome = WindowChrome.Style;
        WindowChrome.Style = chrome == "mac" ? ChromeStyle.Mac : ChromeStyle.Custom;
        app.RequestedThemeVariant = theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        try
        {
            var window = create();
            window.Show();
            Dispatcher.UIThread.RunJobs();
            prepare(window);
            // Let every transition finish (e.g. Fluent's colour fades after the theme switch): without
            // a screen, animations only advance on forced frames, so a picture could land mid-fade.
            var settled = DateTime.UtcNow.AddMilliseconds(250);
            while (DateTime.UtcNow < settled)
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Thread.Sleep(16);
            }
            Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("nothing rendered");
            window.Close();

            var file = $"{name}-{theme}-{chrome}.png";
            var shot = Path.Combine(AppContext.BaseDirectory, "screenshots", file);
            Directory.CreateDirectory(Path.GetDirectoryName(shot)!);
            frame.Save(shot);

            if (!OperatingSystem.IsLinux()) return;
            var baseline = Path.Combine(Baselines, file);
            if (Environment.GetEnvironmentVariable("VS_UPDATE_SCREENSHOTS") == "1")
            {
                Directory.CreateDirectory(Baselines);
                File.Copy(shot, baseline, overwrite: true);
                return;
            }
            Assert.True(File.Exists(baseline), $"No baseline for {file}: run the tests on Linux with VS_UPDATE_SCREENSHOTS=1.");
            var diff = Difference(shot, baseline);
            Assert.True(diff.SameSize, $"{file}: size changed ({diff.Detail}).");
            Assert.True(diff.Fraction <= 0.002, $"{file}: {diff.Fraction:P2} of pixels differ from the baseline (see {shot}).");
        }
        finally
        {
            WindowChrome.Style = oldChrome;
            app.RequestedThemeVariant = ThemeVariant.Default;
        }
    }

    private static string Baselines =>
        Path.Combine(FindUp("VibeSwitcher.XPlat.sln"), "tests", "VibeSwitcher.App.Tests", "Baselines");

    private static string FindUp(string marker)
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, marker))) dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("xplat folder not found");
    }

    // The share of pixels where any channel differs by more than a little (anti-aliasing noise).
    private static (bool SameSize, double Fraction, string Detail) Difference(string a, string b)
    {
        using var x = new Bitmap(a);
        using var y = new Bitmap(b);
        if (x.PixelSize != y.PixelSize) return (false, 1, $"{x.PixelSize} vs {y.PixelSize}");
        var pa = Pixels(x);
        var pb = Pixels(y);
        int differing = 0;
        for (int i = 0; i < pa.Length; i += 4)
        {
            for (int c = 0; c < 4; c++)
            {
                if (Math.Abs(pa[i + c] - pb[i + c]) > 24) { differing++; break; }
            }
        }
        return (true, differing / (pa.Length / 4.0), "");
    }

    private static byte[] Pixels(Bitmap bitmap)
    {
        int stride = bitmap.PixelSize.Width * 4;
        var bytes = new byte[stride * bitmap.PixelSize.Height];
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(bitmap.PixelSize), handle.AddrOfPinnedObject(), bytes.Length, stride); }
        finally { handle.Free(); }
        return bytes;
    }
}
