using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace VibeSwitcher.Views;

public enum AlertKind { Info, Warning }

public partial class AlertDialog : Window
{
    public AlertDialog(string title, string message, AlertKind kind = AlertKind.Warning)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;

        // Use theme brushes so the badge adapts to light/dark instead of staying a fixed light tint.
        if (kind == AlertKind.Warning)
        {
            IconBorder.SetResourceReference(Border.BackgroundProperty, "WarningBg");
            IconPath.Data = (Geometry)FindResource("IcoWarning");
            IconPath.SetResourceReference(Shape.StrokeProperty, "WarningText");
        }
        else
        {
            IconBorder.SetResourceReference(Border.BackgroundProperty, "InfoBadgeBg");
            IconPath.Data = (Geometry)FindResource("IcoInfo");
            IconPath.SetResourceReference(Shape.StrokeProperty, "InfoBadgeText");
        }
    }

    private void OK_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
