using System.Windows;
using System.Windows.Controls;

namespace VibeSwitcher.Views;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog(string title, string message, string actionLabel, string subtitle = "This action cannot be undone.", string icon = "⚠", string iconBgResource = "WarningBg", UIElement? iconElement = null)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        SubtitleText.Text = subtitle;
        MessageText.Text = message;
        ActionBtn.Content = actionLabel;

        IconContent.Content = iconElement ?? new TextBlock { Text = icon, FontSize = 17 };

        IconBadge.SetResourceReference(Border.BackgroundProperty, iconBgResource);
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Cancel_Click(object sender, RoutedEventArgs e)  => DialogResult = false;
}
