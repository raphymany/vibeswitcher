using Avalonia;
using Avalonia.Headless;
using VibeSwitcher.App.Tests;
using VibeSwitcher.App.Theming;

[assembly: AvaloniaTestApplication(typeof(TestApp))]

namespace VibeSwitcher.App.Tests;

// The real app (its styles, theme dictionaries and fonts), drawn by Skia without a screen.
public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .WithVibeSwitcherFonts();
}
