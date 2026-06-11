using System.Windows;
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

        if (kind == AlertKind.Warning)
        {
            IconBorder.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xF8, 0xE0));
            IconPath.Data = (Geometry)FindResource("IcoWarning");
            IconPath.Stroke = new SolidColorBrush(Color.FromRgb(0x7A, 0x58, 0x00)); // dark amber
        }
        else
        {
            IconBorder.Background = new SolidColorBrush(Color.FromRgb(0xE8, 0xF4, 0xFF));
            IconPath.Data = (Geometry)FindResource("IcoInfo");
            IconPath.Stroke = new SolidColorBrush(Color.FromRgb(0x15, 0x65, 0xC0)); // info blue
        }
    }

    private void OK_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
