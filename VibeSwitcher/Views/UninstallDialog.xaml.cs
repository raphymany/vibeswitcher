using System.Windows;

namespace VibeSwitcher.Views;

public partial class UninstallDialog : Window
{
    public bool DeleteData => DeleteDataToggle.IsChecked == true;

    public UninstallDialog()
    {
        InitializeComponent();
    }

    private void Uninstall_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
