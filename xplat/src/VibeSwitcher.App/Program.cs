using Avalonia;
using VibeSwitcher.App.Theming;

namespace VibeSwitcher.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // (Also used by the designer.)
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithVibeSwitcherFonts()
            .LogToTrace();
}
