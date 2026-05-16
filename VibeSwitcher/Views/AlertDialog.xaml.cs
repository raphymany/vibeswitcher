using System.Windows;
using System.Windows.Media;

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
            IconText.Text = "⚠";
        }
        else
        {
            IconBorder.Background = new SolidColorBrush(Color.FromRgb(0xE8, 0xF4, 0xFF));
            IconText.Text = "ℹ";
        }
    }

    private void OK_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
