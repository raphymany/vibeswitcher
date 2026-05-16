using System.Windows;

namespace VibeSwitcher.Views;

public partial class ConfirmDeleteDialog : Window
{
    public ConfirmDeleteDialog(string profileName)
    {
        InitializeComponent();
        MessageText.Text = $"Are you sure you want to delete \"{profileName}\"?";
    }

    private void Delete_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
