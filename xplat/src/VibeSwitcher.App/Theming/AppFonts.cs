using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Fonts;

namespace VibeSwitcher.App.Theming;

// Fonts bundled with the app, so text looks the same on every system (and in screenshots):
//   UI text  — Open Sans, by Segoe UI's designer; the closest freely licensed match to the
//              Windows app's Segoe UI (Selawik, which copies Segoe's measurements exactly, can
//              replace it here if its files are added)
//   Consolas — Cascadia Mono (Microsoft's open monospaced font)
//   Courier New — Cousine (made to Courier New's measurements)
internal static class AppFonts
{
    public const string Collection = "fonts:VibeSwitcher";
    public const string Ui = Collection + "#Open Sans";
    public const string Mono = Collection + "#Cascadia Mono";
    public const string Courier = Collection + "#Cousine";

    public static AppBuilder WithVibeSwitcherFonts(this AppBuilder builder) =>
        builder
            .ConfigureFonts(fonts => fonts.AddFontCollection(
                new EmbeddedFontCollection(new Uri(Collection), new Uri("avares://VibeSwitcher/Assets/Fonts"))))
            .With(new FontManagerOptions { DefaultFamilyName = Ui });
}
