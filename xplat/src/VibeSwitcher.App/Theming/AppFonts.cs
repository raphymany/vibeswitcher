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
//   Segoe UI Emoji (the icon gallery's emoji) — Noto Emoji, the single-colour outline emoji WPF draws
//              too, cut down to the gallery's 12 symbols at bold weight (NotoEmoji-Gallery.ttf, 7 KB)
internal static class AppFonts
{
    public const string Collection = "fonts:VibeSwitcher";
    public const string Ui = Collection + "#Open Sans";
    public const string Mono = Collection + "#Cascadia Mono";
    public const string Courier = Collection + "#Cousine";
    public const string Emoji = Collection + "#Noto Emoji";

    public static AppBuilder WithVibeSwitcherFonts(this AppBuilder builder) =>
        builder
            .ConfigureFonts(fonts => fonts.AddFontCollection(
                new EmbeddedFontCollection(new Uri(Collection), new Uri("avares://VibeSwitcher/Assets/Fonts"))))
            .With(new FontManagerOptions { DefaultFamilyName = Ui });
}
