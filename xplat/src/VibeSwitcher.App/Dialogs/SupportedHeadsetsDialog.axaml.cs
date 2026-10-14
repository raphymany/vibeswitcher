using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using VibeSwitcher.App.Controls;
using VibeSwitcher.App.Settings;
using VibeSwitcher.Helpers;
using VibeSwitcher.Services;

namespace VibeSwitcher.App.Dialogs;

// Port of Views/SupportedHeadsetsDialog.xaml.cs: the wireless headsets whose power on/off switches
// profiles (from the shared KnownHidHeadsets list), by brand.
public partial class SupportedHeadsetsDialog : VsDialog
{
    internal const string RequestUrl = "https://github.com/raphymany/vibeswitcher/issues/new?template=add-headset.yml";

    public SupportedHeadsetsDialog()
    {
        AvaloniaXamlLoader.Load(this);
        BuildBrandSections();
    }

    internal sealed record BrandGroup(string Name, bool Tested, IReadOnlyList<string> ModelNames);

    internal static IReadOnlyList<BrandGroup> BuildGroups() =>
    [
        new("Logitech", true, KnownHidHeadsets.All
            .Where(h => h.Protocol == HidProtocolType.LogitechHidPP)
            .Select(h => h.ModelName).Distinct().OrderBy(n => n).ToList()),
        new("Corsair", false, KnownHidHeadsets.All
            .Where(h => h.Protocol == HidProtocolType.CorsairVoid)
            .Select(h => h.ModelName).Distinct().OrderBy(n => n).ToList()),
        new("SteelSeries", false, KnownHidHeadsets.All
            .Where(h => h.Protocol is HidProtocolType.SteelSeriesLegacy or HidProtocolType.SteelSeriesNova)
            .Select(h => h.ModelName).Distinct().OrderBy(n => n).ToList()),
        new("HyperX", false, KnownHidHeadsets.All
            .Where(h => h.Protocol is HidProtocolType.HyperXAlpha or HidProtocolType.HyperXCloudII)
            .Select(h => h.ModelName).Distinct().OrderBy(n => n).ToList()),
    ];

    private void BuildBrandSections()
    {
        var panel = this.FindControl<StackPanel>("BrandsPanel")!;
        var groups = BuildGroups();
        for (var i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            var isLast = i == groups.Count - 1;

            var headerGrid = new Grid { Margin = new Thickness(12, 8, 12, 4), ColumnDefinitions = new ColumnDefinitions("*,Auto") };

            var brandLabel = new TextBlock
            {
                Text = group.Name,
                FontWeight = FontWeight.SemiBold,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Res.Bind(brandLabel, TextBlock.ForegroundProperty, "PrimaryText");

            var badgeLabel = new TextBlock
            {
                Text = group.Tested ? "Tested" : "Untested",
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Res.Bind(badgeLabel, TextBlock.ForegroundProperty, group.Tested ? "SuccessBadgeText" : "WarningBadgeText");

            var badge = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children = { badgeLabel, group.Tested ? CheckMark("SuccessBadgeText") : WarningSign("WarningBadgeText") },
                },
            };
            Res.Bind(badge, Border.BackgroundProperty, group.Tested ? "SuccessBadgeBg" : "WarningBadgeBg");
            Grid.SetColumn(badge, 1);

            headerGrid.Children.Add(brandLabel);
            headerGrid.Children.Add(badge);
            panel.Children.Add(headerGrid);

            var modelsBlock = new TextBlock
            {
                Text = string.Join(", ", group.ModelNames),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(12, 0, 12, 8),
                LineHeight = 16,
            };
            Res.Bind(modelsBlock, TextBlock.ForegroundProperty, "SecondaryText");
            panel.Children.Add(modelsBlock);

            if (!isLast)
            {
                var sep = new Border { Height = 1, Margin = new Thickness(12, 0, 12, 0) };
                Res.Bind(sep, Border.BackgroundProperty, "CardSeparator");
                panel.Children.Add(sep);
            }
        }
    }

    // Windows writes "Tested ✅" / "Untested ⚠️", which WPF draws as single-colour outlines in the
    // badge's text colour. Emoji fonts differ between Macs and Linux systems (and some Linux
    // installs have none, showing empty boxes), so the two symbols are drawn the same way here.
    private static Control CheckMark(string inkKey) => Symbol(inkKey,
        "M2.5,1 H9.5 A1.5,1.5 0 0 1 11,2.5 V9.5 A1.5,1.5 0 0 1 9.5,11 H2.5 A1.5,1.5 0 0 1 1,9.5 V2.5 A1.5,1.5 0 0 1 2.5,1 Z M3.4,6.2 L5.3,8.1 L8.8,4.1");

    private static Control WarningSign(string inkKey) => Symbol(inkKey,
        "M6,1.2 L11.2,10.6 H0.8 Z M6,4.6 V7.2 M6,8.9 V9");

    private static Control Symbol(string inkKey, string data)
    {
        var path = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse(data),
            StrokeThickness = 1,
            StrokeJoin = PenLineJoin.Round,
            StrokeLineCap = PenLineCap.Round,
            Width = 12,
            Height = 12,
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Res.Bind(path, Avalonia.Controls.Shapes.Shape.StrokeProperty, inkKey);
        return path;
    }

    private void Good_Click(object? sender, RoutedEventArgs e) => CloseWith(true);

    private void Request_Click(object? sender, RoutedEventArgs e)
    {
        if (!SystemOpener.OpenUrl(RequestUrl, out var error)) AppLog.Warning("SupportedHeadsetsDialog.Request", error);
    }

    // (Escape: the Close button is the cancel button.)
    private void Close_Click(object? sender, RoutedEventArgs e) => CloseWith(false);
}
