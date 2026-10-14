using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using VibeSwitcher.App.Controls;

using Path = Avalonia.Controls.Shapes.Path;

namespace VibeSwitcher.App.Dialogs;

// Port of Views/ConfirmDialog.xaml.cs.
public partial class ConfirmDialog : VsDialog
{
    public ConfirmDialog() : this("", "", "") { } // (for the XAML loader)

    public ConfirmDialog(string title, string message, string actionLabel, string subtitle = "This action cannot be undone.",
        string iconGeometry = "IcoWarning", string iconBgResource = "WarningBg", Control? iconElement = null)
    {
        AvaloniaXamlLoader.Load(this);
        Title = "VibeSwitcher";
        this.FindControl<TextBlock>("TitleText")!.Text = title;
        // (An empty subtitle still takes its line, as on Windows.)
        this.FindControl<TextBlock>("SubtitleText")!.Text = subtitle;
        this.FindControl<TextBlock>("MessageText")!.Text = message;
        this.FindControl<Button>("ActionBtn")!.Content = actionLabel;

        this.FindControl<ContentControl>("IconContent")!.Content = iconElement ?? BuildIcon(iconGeometry, iconBgResource);
        Res.Bind(this.FindControl<Border>("IconBadge")!, Border.BackgroundProperty, iconBgResource);
    }

    // A geometric badge icon: white on an accent badge, the warning colour on a tinted one.
    private Path BuildIcon(string geometryKey, string bgResource)
    {
        var path = new Path
        {
            Data = Res.Icon(this, geometryKey),
            Width = 18,
            Height = 18,
            Stretch = Stretch.Uniform,
            Fill = Brushes.Transparent,
            StrokeThickness = 1.5,
            StrokeJoin = PenLineJoin.Round,
            StrokeLineCap = PenLineCap.Round,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (bgResource == "Accent") path.Stroke = Brushes.White;
        else Res.Bind(path, Shape.StrokeProperty, "WarningText");
        return path;
    }

    private void Confirm_Click(object? sender, RoutedEventArgs e) => CloseWith(true);
    private void Cancel_Click(object? sender, RoutedEventArgs e) => CloseWith(false);
}
