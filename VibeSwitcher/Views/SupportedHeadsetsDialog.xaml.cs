using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

            Color badgeBgColor = group.Tested
                ? Color.FromRgb(0xE8, 0xF5, 0xE9)
                : Color.FromRgb(0xFF, 0xF8, 0xE1);
            Color badgeFgColor = group.Tested
                ? Color.FromRgb(0x2E, 0x7D, 0x32)
                : Color.FromRgb(0xE6, 0x5C, 0x00);

            var badge = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                Background = new SolidColorBrush(badgeBgColor),
                Child = new TextBlock
                {
                    Text = group.Tested ? "Tested ✅" : "Untested ⚠️",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(badgeFgColor),
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
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
        DialogResult = false;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) DialogResult = false;
    }
}
