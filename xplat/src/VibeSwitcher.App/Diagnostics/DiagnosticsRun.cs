using Avalonia.Controls.ApplicationLifetimes;
using VibeSwitcher.App.Hosting;
using VibeSwitcher.Helpers;
using VibeSwitcher.Platform;

namespace VibeSwitcher.App.Diagnostics;

// `vibeswitcher --diagnostics [file]`, run on its own (beside a running VibeSwitcher, without taking
// its place): the app starts only to draw its windows, writes the zip, prints where, and quits. With
// no screen to draw on (started over SSH, say), the zip is made without the pictures.
internal static class DiagnosticsRun
{
    // Set by Program for App to pick up once Avalonia is running.
    public static string? Path { get; set; }
    // (Failed until the zip is saved: a run that ends early never reports success.)
    public static int ExitCode { get; private set; } = 1;
    // Whether the app got as far as making the zip (Program falls back to no pictures only if not).
    public static bool Started { get; private set; }

    public static async Task RunAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        Started = true;
        try
        {
            var platform = IHostPlatform.Current();
            var paths = AppPaths.Current();
            var logger = new QuietLogger();
            var saved = await DiagnosticsBundle.CreateAsync(Path!, platform, paths, folder => ScreenshotTour.RunAsync(folder, platform, paths, logger));
            Console.WriteLine($"Saved {saved}");
            ExitCode = 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"VibeSwitcher couldn't make the diagnostics zip: {ex.Message}");
            ExitCode = 1;
        }
        desktop.Shutdown(ExitCode);
    }

    // Without a screen: everything but the pictures.
    public static int RunWithoutScreen(string path)
    {
        try
        {
            var saved = DiagnosticsBundle.CreateAsync(path, IHostPlatform.Current(), AppPaths.Current(), screenshots: null).GetAwaiter().GetResult();
            Console.WriteLine($"Saved {saved} (without pictures of the windows: there's no screen to draw them on)");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"VibeSwitcher couldn't make the diagnostics zip: {ex.Message}");
            return 1;
        }
    }

    private sealed class QuietLogger : IAppLogger
    {
        public void Info(string context, string message) { }
        public void Warning(string context, string message) { }
        public void Error(string context, string message) { }
        public void Error(string context, Exception ex) { }
        public void Debug(string context, string message) { }
    }
}
