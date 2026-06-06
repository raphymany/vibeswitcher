using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VibeSwitcher.Services;

namespace VibeSwitcher.Views;

public partial class SupportedHeadsetsDialog : Window
{
    public SupportedHeadsetsDialog()
    {
        InitializeComponent();
        BuildBrandSections();
    }

    private record BrandGroup(string Name, bool Tested, IReadOnlyList<string> ModelNames);

    private static IReadOnlyList<BrandGroup> BuildGroups() =>
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
        var groups = BuildGroups();
        for (var i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            var isLast = i == groups.Count - 1;

            var headerGrid = new Grid { Margin = new Thickness(12, 8, 12, 4) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var brandLabel = new TextBlock
            {
                Text = group.Name,
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
            };
            brandLabel.SetResourceReference(ForegroundProperty, "PrimaryText");
            Grid.SetColumn(brandLabel, 0);

            string bgKey = group.Tested ? "SuccessBadgeBg" : "WarningBadgeBg";
            string fgKey = group.Tested ? "SuccessBadgeText" : "WarningBadgeText";

            var badgeLabel = new TextBlock
            {
                Text = group.Tested ? "Tested ✅" : "Untested ⚠️",
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
            };
            badgeLabel.SetResourceReference(ForegroundProperty, fgKey);

            var badge = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                Child = badgeLabel,
            };
            badge.SetResourceReference(BackgroundProperty, bgKey);
            Grid.SetColumn(badge, 1);

            headerGrid.Children.Add(brandLabel);
            headerGrid.Children.Add(badge);
            BrandsPanel.Children.Add(headerGrid);

            var modelsBlock = new TextBlock
            {
                Text = string.Join(", ", group.ModelNames),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(12, 0, 12, 8),
                LineHeight = 16,
            };
            modelsBlock.SetResourceReference(ForegroundProperty, "SecondaryText");
            BrandsPanel.Children.Add(modelsBlock);

            if (!isLast)
            {
                var sep = new Border { Height = 1, Margin = new Thickness(12, 0, 12, 0) };
                sep.SetResourceReference(BackgroundProperty, "CardSeparator");
                BrandsPanel.Children.Add(sep);
            }
        }
    }

    private void Good_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Request_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://github.com/raphymany/vibeswitcher/issues/new?template=add-headset.yml")
                { UseShellExecute = true });
        }
        catch { }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) DialogResult = false;
    }
}
