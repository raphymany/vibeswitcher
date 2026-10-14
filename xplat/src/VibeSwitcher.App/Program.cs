using Avalonia;
using VibeSwitcher.App.Theming;

namespace VibeSwitcher.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (Cli.Handle(args, Console.Out, Console.Error) is int exitCode) return exitCode;
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // (Also used by the designer.)
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithVibeSwitcherFonts()
            .LogToTrace();
}
