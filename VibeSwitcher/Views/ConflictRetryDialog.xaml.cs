using System.Windows;

namespace VibeSwitcher.Views;

public partial class ConflictRetryDialog : Window
{
    public ConflictRetryDialog(string title, string message)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
    }

    private void TryAgain_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
