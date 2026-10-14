using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using VibeSwitcher.App.Theming;
using VibeSwitcher.Tests;

namespace VibeSwitcher.App.Tests;

public class ThemeTests
{
    // The Windows theme files define exactly these 100 keys (96 brushes + 4 colours), in both themes.
    [AvaloniaFact]
    public void BothWindowsThemes_AreLoaded_WithAllTheirColours()
    {
        foreach (var variant in new[] { "Light", "Dark" })
        {
            var dict = WpfResources.LoadTheme(variant);
            int brushes = dict.Values.OfType<ISolidColorBrush>().Count();
            Assert.True(brushes >= 96, $"{variant}: only {brushes} brushes");
            Assert.IsType<Color>(dict["AccentColor"]);
            Assert.IsType<Color>(dict["ToggleOffColor"]);
        }
        Assert.Equal(Color.Parse("#FF8000"), WpfResources.LoadTheme("Light")["AccentColor"]);
        Assert.Equal(Color.Parse("#F5820A"), WpfResources.LoadTheme("Dark")["AccentColor"]);
        Assert.Equal(Color.Parse("#0D0D12"), ((ISolidColorBrush)WpfResources.LoadTheme("Dark")["AppBg"]!).Color);
    }

    [AvaloniaFact]
    public void FluentControls_UseTheVibeSwitcherColours()
    {
        var dark = WpfResources.LoadTheme("Dark");
        foreach (var (fluent, ours) in WpfResources.FluentAliases)
            Assert.Same(dark[ours], dark[fluent]);
        Assert.Equal(dark["AccentColor"], dark["SystemAccentColor"]);
    }

    [AvaloniaFact]
    public void TheWindowsIcons_AreLoaded()
    {
        var icons = WpfResources.LoadIcons().ToDictionary(i => i.Key, i => i.Geometry);
        Assert.Equal(WpfResources.IconCountInSource(), icons.Count);
        Assert.True(icons.Count >= 18);
        Assert.All(icons.Values, g => Assert.True(g.Bounds.Width > 0));
        Assert.True(Application.Current!.TryFindResource("IcoTrash", out var trash) && trash is Geometry);
    }

    [AvaloniaFact]
    public void ThemeService_SetsTheVariant_AndReportsSystemChangesOnlyWhenFollowingIt()
    {
        var config = new FakeConfigService();
        var service = new ThemeService(config);
        int applied = 0;
        service.ThemeApplied += () => applied++;
        var app = Application.Current!;
        try
        {
            service.Apply("Dark");
            Assert.Equal(ThemeVariant.Dark, app.RequestedThemeVariant);
            service.Apply("Light");
            Assert.Equal(ThemeVariant.Light, app.RequestedThemeVariant);
            config.Current.Theme = "Auto";
            service.Apply();
            Assert.Equal(ThemeVariant.Default, app.RequestedThemeVariant);
            Assert.Equal(3, applied);

            // The look changing by itself (the system's setting) counts only when following it.
            service.StartListening();
            app.RequestedThemeVariant = ThemeVariant.Dark;
            Assert.Equal(4, applied);
            config.Current.Theme = "Light";
            app.RequestedThemeVariant = ThemeVariant.Light;
            Assert.Equal(4, applied);
            service.StopListening();
            config.Current.Theme = "Auto";
            app.RequestedThemeVariant = ThemeVariant.Dark;
            Assert.Equal(4, applied);
        }
        finally { app.RequestedThemeVariant = ThemeVariant.Default; }
    }
}
