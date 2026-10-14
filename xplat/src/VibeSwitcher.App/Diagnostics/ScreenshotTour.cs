using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using VibeSwitcher.App.Dialogs;
using VibeSwitcher.App.Hosting;
using VibeSwitcher.App.Imaging;
using VibeSwitcher.App.Settings;
using VibeSwitcher.Helpers;
using VibeSwitcher.Models;
using VibeSwitcher.Platform;
using VibeSwitcher.Services;

namespace VibeSwitcher.App.Diagnostics;

// The app's windows as this computer draws them (its fonts, scaling, theme), for the diagnostics
// zip: Settings on each page, About, the FAQ, Mini Mode, the dialogs, the splash, the tray / menu-bar
// menu and a banner. Each window is shown for a moment and drawn by the app itself (no screen
// recording permission needed). Settings works on a copy of the user's settings, so nothing changes.
internal static class ScreenshotTour
{
    private static readonly string[] Pages = ["startup", "appear", "notif", "devices", "tray", "shortcuts", "compact", "logs", "uninstall"];

    // Returns notes for system.txt (the screens, and any picture that failed).
    public static async Task<IReadOnlyList<string>> RunAsync(string folder, IHostPlatform platform, AppPaths paths, IAppLogger logger)
    {
        var notes = new List<string>();
        var copy = Directory.CreateTempSubdirectory("vs-tour-").FullName;
        try
        {
            CopySettings(paths.ConfigDir, copy);
            var errors = new SessionErrorTracker();
            var config = new ConfigService(logger, errors, copy);
            config.Load();
            config.Current.CompactIntroShown = true; // (no first-time welcome in the way)
            using var audio = platform.CreateAudio(logger, errors);
            // (Shortcuts shown as set, without taking any keys.)
            var hotkeys = new HotkeyService(new PictureGrabber(), logger, errors);
            var startup = platform.CreateStartup(logger, errors);

            async Task Shot(string name, Func<Window> make, Func<Window, Task>? prepare = null)
            {
                Window? window = null;
                try
                {
                    window = make();
                    window.Show();
                    await Pause(300); // (laid out, fonts loaded, devices listed)
                    if (prepare != null) await prepare(window);
                    await Pause(150);
                    Save(window, Path.Combine(folder, name + ".png"));
                }
                catch (Exception ex) { notes.Add($"Picture '{name}' failed: {ex.Message}"); }
                finally { window?.Close(); }
            }

            // Settings: the profiles, each page, About, the FAQ, Mini Mode.
            // In the user's own theme.
            var theme = new Theming.ThemeService(config);
            theme.Apply();
            var settings = new SettingsWindow(config, audio, hotkeys, startup, logger, errors, theme.Apply) { ClosesForGood = true };
            try
            {
                settings.Show();
                if (settings.IsCompact) settings.ExitCompact();
                await Pause(1200);
                notes.AddRange(settings.Screens.All.Select((s, i) =>
                    $"Screen {i + 1}: {s.Bounds.Width}×{s.Bounds.Height} px at {s.Bounds.X},{s.Bounds.Y}, scaling {s.Scaling}{(s.IsPrimary ? " (main)" : "")}"));
                Save(settings, Path.Combine(folder, "settings-profiles.png"));
                settings.ExpandSettings();
                foreach (var page in Pages)
                {
                    settings.ViewModel.SelectedCategory = page;
                    await Pause(250);
                    Save(settings, Path.Combine(folder, $"settings-{page}.png"));
                }
                settings.OpenAboutPanel();
                await Pause(250);
                Save(settings, Path.Combine(folder, "settings-about.png"));
                settings.OpenFaqPanel();
                await Pause(250);
                Save(settings, Path.Combine(folder, "settings-faq.png"));
                if (config.Current.Profiles.Count > 0)
                {
                    settings.EnterCompact();
                    await Pause(600);
                    Save(settings, Path.Combine(folder, "settings-minimode.png"));
                    settings.ExitCompact();
                }
            }
            catch (Exception ex) { notes.Add($"Pictures of Settings failed: {ex.Message}"); }
            finally { settings.Close(); }

            // The dialogs, with sample content (the app-trigger list is this computer's).
            await Shot("dialog-alert", () => new AlertDialog("Device Not Found", "The headset in this profile isn't connected. Plug it in, or pick another device."));
            await Shot("dialog-confirm", () => new ConfirmDialog("Replace Configuration?", "This replaces all your profiles and settings with the imported ones.", "Replace", subtitle: ""));
            await Shot("dialog-hotkey", () => new HotkeyCaptureDialog(new HotkeyDefinition { VirtualKeyCode = 0x47, UseCtrl = true, UseAlt = true }));
            await Shot("dialog-profile-type", () => new ProfileTypeDialog());
            await Shot("dialog-headsets", () => new SupportedHeadsetsDialog());
            await Shot("dialog-schedule", () => new ScheduleWizardDialog(new ScheduleEntry { Hour = 18, Minute = 0, Days = [DayOfWeek.Friday] }, use12Hour: false));
            await Shot("dialog-sound", () => new SwitchSoundDialog(true, "Chime", null, 60, logger, config.SoundsLibraryDir));
            await Shot("dialog-icons", () => new IconGalleryDialog(config.IconsLibraryDir));
            await Shot("dialog-app-triggers", () => new AppTriggerDialog([], new Dictionary<string, string>(), PlatformApps.Catalog(), AppIcons.Load),
                async w =>
                {
                    var until = DateTime.UtcNow.AddSeconds(15);
                    while (w is AppTriggerDialog { AppsLoaded: false } && DateTime.UtcNow < until) await Pause(100);
                });
            if (config.Current.Profiles.FirstOrDefault() is { } first)
                await Shot("dialog-clone", () => new CloneProfileDialog(first, audio.GetPlaybackDevices(), audio.GetRecordingDevices(), use12Hour: false, h => h));
            await Shot("splash", () => new SplashWindow(play: false));
            await Shot("banner", () => new BannerWindow("VibeSwitcher", "Switched to a profile", TimeSpan.Zero));

            // The tray / menu-bar menu, as the Windows-styled menu draws it (Linux shows the
            // desktop's own menu, which can't be pictured from here).
            // (The menu's lines only: no tray icon is made.)
            using var tray = new TrayService(config, logger, errors, null,
                new TrayActions(() => { }, () => { }, () => { }, () => { }, () => { }, () => false, () => { }), new NoMenuBarItem());
            await Shot("menu", () => new Window
            {
                SystemDecorations = SystemDecorations.None, SizeToContent = SizeToContent.WidthAndHeight,
                Content = new StyledTrayMenu(_ => null).Build(tray.BuildEntries()),
            });
        }
        finally
        {
            // (Settings may still be writing its copy of the settings.)
            await Task.Delay(500);
            try { Directory.Delete(copy, recursive: true); } catch (Exception) { }
        }
        return notes;
    }

    private sealed class PictureGrabber : IGlobalKeyGrabber
    {
        public event Action<ushort>? Pressed { add { } remove { } }
        public GrabResult Register(ushort id, int modifiers, int virtualKey, out string? error) { error = null; return GrabResult.Ok; }
        public void Unregister(ushort id) { }
        public void Dispose() { }
    }

    private sealed class NoMenuBarItem : IMenuBarItem
    {
        public event Action? Clicked { add { } remove { } }
        public void SetImage(byte[] png) { }
        public void SetToolTip(string text) { }
        public void SetVisible(bool visible) { }
        public (double X, double Y)? MenuAnchor() => null;
        public void PerformClick() { }
        public void Dispose() { }
    }

    // (Tests make the pauses shorter.)
    internal static double PauseScale { get; set; } = 1;
    private static Task Pause(int ms) => Task.Delay((int)(ms * PauseScale));

    private static void CopySettings(string from, string to)
    {
        if (File.Exists(Path.Combine(from, "config.json"))) File.Copy(Path.Combine(from, "config.json"), Path.Combine(to, "config.json"));
        foreach (var dir in new[] { "Icons", "Sounds" })
        {
            var source = Path.Combine(from, dir);
            if (!Directory.Exists(source)) continue;
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(to, dir, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
        }
    }

    // The window's content drawn at the screen's scaling.
    internal static void Save(Window window, string file)
    {
        var scale = window.RenderScaling;
        var size = window.ClientSize;
        var pixels = new PixelSize(Math.Max(1, (int)Math.Ceiling(size.Width * scale)), Math.Max(1, (int)Math.Ceiling(size.Height * scale)));
        using var bitmap = new RenderTargetBitmap(pixels, new Vector(96 * scale, 96 * scale));
        bitmap.Render(window);
        bitmap.Save(file);
    }
}
