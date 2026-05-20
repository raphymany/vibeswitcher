using System.Windows;

namespace VibeSwitcher.Views;

public partial class HelpDialog : Window
{
    public HelpDialog()
    {
        InitializeComponent();
    }

    private void GotIt_Click(object sender, RoutedEventArgs e) => Close();
}
