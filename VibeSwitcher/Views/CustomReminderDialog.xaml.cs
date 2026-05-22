using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace VibeSwitcher.Views;

public partial class CustomReminderDialog : Window
{
    public int ResultMinutes { get; private set; }

    public CustomReminderDialog(int currentMinutes)
    {
        InitializeComponent();
        MinutesBox.Text = currentMinutes > 0 ? currentMinutes.ToString() : "";
        MinutesBox.SelectAll();
        Loaded += (_, _) => MinutesBox.Focus();
    }

    private void MinutesBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var valid = int.TryParse(MinutesBox.Text.Trim(), out var v) && v >= 1 && v <= 1440;
        OkButton.IsEnabled = valid;
        ErrorText.Visibility = MinutesBox.Text.Length > 0 && !valid
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void MinutesBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && OkButton.IsEnabled)
            Confirm();
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => Confirm();

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            DialogResult = false;
    }

    private void Confirm()
    {
        ResultMinutes = int.Parse(MinutesBox.Text.Trim());
        DialogResult = true;
    }
}
