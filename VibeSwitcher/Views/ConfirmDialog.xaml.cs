using System.Windows;

namespace VibeSwitcher.Views;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog(string title, string message, string actionLabel, string subtitle = "This action cannot be undone.")
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        SubtitleText.Text = subtitle;
        MessageText.Text = message;
        ActionBtn.Content = actionLabel;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Cancel_Click(object sender, RoutedEventArgs e)  => DialogResult = false;
}
