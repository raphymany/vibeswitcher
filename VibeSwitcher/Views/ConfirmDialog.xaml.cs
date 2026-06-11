using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace VibeSwitcher.Views;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog(string title, string message, string actionLabel, string subtitle = "This action cannot be undone.", string iconGeometry = "IcoWarning", string iconBgResource = "WarningBg", UIElement? iconElement = null)
    {
        InitializeComponent();
        Title = "VibeSwitcher";
        TitleText.Text = title;
        SubtitleText.Text = subtitle;
        MessageText.Text = message;
        ActionBtn.Content = actionLabel;

        IconContent.Content = iconElement ?? BuildIcon(iconGeometry, iconBgResource);

        IconBadge.SetResourceReference(Border.BackgroundProperty, iconBgResource);
    }

    // Builds a geometric badge icon. White stroke on an accent badge, contextual on a tinted one.
    private Path BuildIcon(string geometryKey, string bgResource)
    {
        var path = new Path
        {
            Data = (Geometry)FindResource(geometryKey),
            Width = 18,
            Height = 18,
            Stretch = Stretch.Uniform,
            Fill = Brushes.Transparent,
            StrokeThickness = 1.5,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (bgResource == "Accent")
            path.Stroke = Brushes.White;
        else
            path.SetResourceReference(Shape.StrokeProperty, "WarningText");
        return path;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Cancel_Click(object sender, RoutedEventArgs e)  => DialogResult = false;
}
