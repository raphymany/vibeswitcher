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

        // The diagnostics zip runs on its own (beside a running VibeSwitcher), drawing the windows if
        // there's a screen.
        var commandLine = CommandLine.Parse(args);
        if (commandLine.Tool == LocalTool.Diagnostics)
        {
            var zip = Path.GetFullPath(commandLine.ToolPath ?? Diagnostics.DiagnosticsBundle.DefaultPath());
            Diagnostics.DiagnosticsRun.Path = zip;
            try
            {
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
                return Diagnostics.DiagnosticsRun.ExitCode;
            }
            catch (Exception ex) when (!Diagnostics.DiagnosticsRun.Started)
            {
                // (No screen to draw on: everything but the pictures.)
                Console.Error.WriteLine($"VibeSwitcher: no screen to draw the windows on ({ex.Message}).");
                return Diagnostics.DiagnosticsRun.RunWithoutScreen(zip);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"VibeSwitcher couldn't make the diagnostics zip: {ex.Message}");
                return 1;
            }
        }

        // One copy at a time: a later launch hands its command to the running one and exits. The lock
        // is taken before anything else starts (a second copy must not even open the log).
        var command = commandLine.Command ?? AppCommand.Open;
        var paths = AppPaths.Current();
        var host = new AppHost(IHostPlatform.Current(), paths, command);
        using var instance = new SingleInstance(paths, warn: message => Console.Error.WriteLine($"VibeSwitcher: {message}"));
        if (Cli.AcquireOrForward(instance, paths, command, host.HandleRemoteAsync, Console.Out, Console.Error) is int code) return code;
        AppHost.Current = host;
        // A tray / menu-bar app: it keeps running with no window open, until Quit.
        try
        {
            var result = BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
            // Uninstalled with "delete my data": the settings folder goes now that nothing can write to
            // it — while this copy still holds the lock (inside it), so no copy started meanwhile can
            // set itself up there.
            if (host.DataToDelete is { } data)
                Hosting.Uninstaller.DeleteData(data, warn: message => Console.Error.WriteLine($"VibeSwitcher: {message}"));
            if (Environment.GetEnvironmentVariable("VS_UNINSTALL_CHECK") is { Length: > 0 } check)
                File.WriteAllText(check, host.DataToDelete != null && !Directory.Exists(host.DataToDelete) ? "uninstalled" : "data kept");
            return result;
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