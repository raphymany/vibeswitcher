using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace VibeSwitcher.App.Theming;

// The Windows app's look, read from its own files (embedded at build time, never copied):
//   Themes/LightTheme.xaml, Themes/DarkTheme.xaml — the 100 theme colours (brushes + colours)
//   Controls/Icons.xaml                             — the shared icon shapes (16-unit geometries)
// A colour or icon changed for Windows changes here too. The Fluent theme's own colour keys (text
// boxes, drop-downs, menus, tooltips, scroll bars) are pointed at the same colours, so stock
// controls follow the VibeSwitcher palette.
internal static class WpfResources
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    public static void Install(IResourceDictionary resources)
    {
        resources.ThemeDictionaries[ThemeVariant.Light] = LoadTheme("Light");
        resources.ThemeDictionaries[ThemeVariant.Dark] = LoadTheme("Dark");
        foreach (var (key, geometry) in LoadIcons()) resources[key] = geometry;
    }

    public static ResourceDictionary LoadTheme(string variant)
    {
        var dict = new ResourceDictionary();
        foreach (var el in Load($"Wpf.{variant}Theme.xaml").Root!.Elements())
        {
            // Only plain colours are understood: anything else added to the Windows theme fails here
            // (and in CI) rather than silently going missing.
            var key = (string?)el.Attribute(Xaml + "Key")
                ?? throw new InvalidDataException($"{variant} theme: a <{el.Name.LocalName}> without x:Key.");
            var extra = el.Attributes().Select(a => a.Name.LocalName).Except(["Key", "Color"]).ToList();
            if (extra.Count > 0 || el.HasElements)
                throw new InvalidDataException($"{variant} theme: '{key}' has content this loader doesn't read ({string.Join(", ", extra)}).");
            dict[key] = el.Name.LocalName switch
            {
                "SolidColorBrush" => new SolidColorBrush(Color.Parse((string?)el.Attribute("Color")
                    ?? throw new InvalidDataException($"{variant} theme: '{key}' has no Color."))),
                "Color" => Color.Parse(el.Value.Trim()),
                var other => throw new InvalidDataException($"{variant} theme: '{key}' is a <{other}>, which this loader doesn't read."),
            };
        }
        foreach (var (fluentKey, ourKey) in FluentAliases)
            if (dict.TryGetValue(ourKey, out var value)) dict[fluentKey] = value;
        // The Fluent accent shades all follow VibeSwitcher's accent.
        if (dict.TryGetValue("AccentColor", out var accent))
            foreach (var k in (string[])["SystemAccentColor", "SystemAccentColorDark1", "SystemAccentColorDark2", "SystemAccentColorDark3",
                         "SystemAccentColorLight1", "SystemAccentColorLight2", "SystemAccentColorLight3"])
                dict[k] = accent;
        // Text boxes keep a 1-pixel border when focused, as on Windows; stock controls' text is 12 px
        // (WPF's default) rather than Fluent's 14.
        dict["TextControlBorderThemeThicknessFocused"] = new Thickness(1);
        dict["ControlContentThemeFontSize"] = 12.0;
        return dict;
    }

    public static IEnumerable<(string Key, Geometry Geometry)> LoadIcons()
    {
        foreach (var el in Load("Wpf.Icons.xaml").Root!.Elements())
        {
            if (el.Name.LocalName != "Geometry" || (string?)el.Attribute(Xaml + "Key") is not { } key)
                throw new InvalidDataException($"Icons: <{el.Name.LocalName}> isn't a keyed Geometry, which this loader doesn't read.");
            yield return (key, StreamGeometry.Parse(el.Value.Trim()));
        }
    }

    // How many icons the Windows file defines (for tests).
    internal static int IconCountInSource() => Load("Wpf.Icons.xaml").Root!.Elements().Count();

    private static XDocument Load(string name)
    {
        using var stream = typeof(WpfResources).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded resource {name} is missing.");
        return XDocument.Load(stream);
    }

    // Fluent theme key → the VibeSwitcher theme key it takes its colour from.
    internal static readonly (string Fluent, string Ours)[] FluentAliases =
    [
        ("TextControlBackground", "InputBg"), ("TextControlBackgroundPointerOver", "InputBg"),
        ("TextControlBackgroundFocused", "InputBg"), ("TextControlBackgroundDisabled", "ReadOnlyBg"),
        ("TextControlBorderBrush", "InputBorder"), ("TextControlBorderBrushPointerOver", "InputHoverBorder"),
        ("TextControlBorderBrushFocused", "Accent"), ("TextControlBorderBrushDisabled", "InputBorder"),
        ("TextControlForeground", "PrimaryText"), ("TextControlForegroundPointerOver", "PrimaryText"),
        ("TextControlForegroundFocused", "PrimaryText"), ("TextControlForegroundDisabled", "ReadOnlyText"),
        ("TextControlPlaceholderForeground", "TertiaryText"), ("TextControlPlaceholderForegroundPointerOver", "TertiaryText"),
        ("TextControlPlaceholderForegroundFocused", "TertiaryText"), ("TextControlSelectionHighlightColor", "Accent"),

        ("ComboBoxBackground", "InputBg"), ("ComboBoxBackgroundUnfocused", "InputBg"),
        ("ComboBoxBackgroundPointerOver", "HoverBg"), ("ComboBoxBackgroundPressed", "HoverBg"),
        ("ComboBoxBackgroundBorderBrushFocused", "Accent"), ("ComboBoxBackgroundBorderBrushUnfocused", "InputBorder"),
        ("ComboBoxBorderBrush", "InputBorder"), ("ComboBoxBorderBrushPointerOver", "Accent"), ("ComboBoxBorderBrushPressed", "Accent"),
        ("ComboBoxForeground", "PrimaryText"), ("ComboBoxForegroundFocused", "PrimaryText"),
        ("ComboBoxForegroundFocusedPressed", "PrimaryText"), ("ComboBoxForegroundDisabled", "DisabledText"),
        ("ComboBoxPlaceHolderForeground", "TertiaryText"),
        ("ComboBoxDropDownBackground", "CardBg"), ("ComboBoxDropDownBorderBrush", "InputBorder"),
        ("ComboBoxDropDownGlyphForeground", "SecondaryText"), ("ComboBoxDropDownGlyphForegroundFocused", "Accent"),
        ("ComboBoxDropDownGlyphForegroundFocusedPressed", "Accent"),
        ("ComboBoxItemForeground", "PrimaryText"), ("ComboBoxItemForegroundPointerOver", "PrimaryText"),
        ("ComboBoxItemForegroundPressed", "PrimaryText"), ("ComboBoxItemForegroundSelected", "PrimaryText"),
        ("ComboBoxItemForegroundSelectedPointerOver", "PrimaryText"), ("ComboBoxItemForegroundSelectedPressed", "PrimaryText"),
        ("ComboBoxItemBackgroundPointerOver", "HoverBg"), ("ComboBoxItemBackgroundPressed", "PressedBg"),
        ("ComboBoxItemBackgroundSelected", "SelectedBg"), ("ComboBoxItemBackgroundSelectedPointerOver", "SelectedBg"),
        ("ComboBoxItemBackgroundSelectedPressed", "SelectedBg"),

        ("ToolTipBackground", "TooltipBg"), ("ToolTipBorderBrush", "TooltipBorder"), ("ToolTipForeground", "PrimaryText"),

        ("MenuFlyoutPresenterBackground", "MenuBg"), ("MenuFlyoutPresenterBorderBrush", "MenuBorder"),
        ("MenuFlyoutItemBackgroundPointerOver", "MenuItemHoverBg"), ("MenuFlyoutItemBackgroundPressed", "MenuItemHoverBg"),
        ("MenuFlyoutItemForeground", "PrimaryText"), ("MenuFlyoutItemForegroundPointerOver", "PrimaryText"),
        ("MenuFlyoutItemForegroundPressed", "PrimaryText"), ("MenuFlyoutItemForegroundDisabled", "DisabledText"),

        ("ScrollBarTrackFill", "ScrollBarTrack"), ("ScrollBarTrackFillPointerOver", "ScrollBarTrack"),
        ("ScrollBarPanningThumbBackground", "ScrollBarThumb"), ("ScrollBarThumbFillPointerOver", "ScrollBarThumbHover"),
        ("ScrollBarThumbFillPressed", "ScrollBarThumbPressed"),
    ];
}
