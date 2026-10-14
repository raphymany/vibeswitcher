using Avalonia;
using Avalonia.Controls;
using VibeSwitcher.App.Hosting;
using VibeSwitcher.App.Theming;
using VibeSwitcher.Platform;

namespace VibeSwitcher.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (Cli.Handle(args, Console.Out, Console.Error) is int exitCode) return exitCode;
        // (On Windows — development only — the app shows its dialog preview.)
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

        // One copy at a time: a later launch hands its command to the running one and exits. The lock
        // is taken before anything else starts (a second copy must not even open the log).
        var command = CommandLine.Parse(args).Command ?? AppCommand.Open;
        var paths = AppPaths.Current();
        var host = new AppHost(IHostPlatform.Current(), paths, command);
        using var instance = new SingleInstance(paths, warn: message => Console.Error.WriteLine($"VibeSwitcher: {message}"));
        if (Cli.AcquireOrForward(instance, paths, command, host.HandleRemoteAsync, Console.Out, Console.Error) is int code) return code;
        AppHost.Current = host;
        // A tray / menu-bar app: it keeps running with no window open, until Quit.
        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
        }
        catch (Exception ex) when (host.Logger == null)
        {
            // (Before the app got going — typically no screen to show it on: started over SSH, say.)
            Console.Error.WriteLine($"VibeSwitcher couldn't start: {ex.Message}");
            Console.Error.WriteLine("It needs a desktop session (an X11 or Wayland display).");
            return 1;
        }
    }

    // (Also used by the designer.)
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // (macOS: a menu-bar app has no Dock icon — also when not started from its .app.)
            .With(new MacOSPlatformOptions { ShowInDock = false })
            .WithVibeSwitcherFonts()
            .LogToTrace();
}