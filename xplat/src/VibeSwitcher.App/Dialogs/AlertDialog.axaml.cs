using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using VibeSwitcher.App.Controls;

using Path = Avalonia.Controls.Shapes.Path;

namespace VibeSwitcher.App.Dialogs;

public enum AlertKind { Info, Warning }

// Port of Views/AlertDialog.xaml.cs.
public partial class AlertDialog : VsDialog
{
    public AlertDialog() : this("", "") { } // (for the XAML loader)

    public AlertDialog(string title, string message, AlertKind kind = AlertKind.Warning)
    {
        AvaloniaXamlLoader.Load(this);
        Title = title;
        this.FindControl<TextBlock>("TitleText")!.Text = title;
        this.FindControl<TextBlock>("MessageText")!.Text = message;

        // Theme brushes, so the badge adapts to light/dark.
        var border = this.FindControl<Border>("IconBorder")!;
        var path = this.FindControl<Path>("IconPath")!;
        bool warning = kind == AlertKind.Warning;
        Res.Bind(border, Border.BackgroundProperty, warning ? "WarningBg" : "InfoBadgeBg");
        path.Data = Res.Icon(this, warning ? "IcoWarning" : "IcoInfo");
        Res.Bind(path, Shape.StrokeProperty, warning ? "WarningText" : "InfoBadgeText");
    }

    private void OK_Click(object? sender, RoutedEventArgs e) => CloseWith(true);
}
