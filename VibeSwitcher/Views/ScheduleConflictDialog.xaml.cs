using System.Windows;

namespace VibeSwitcher.Views;

public partial class ScheduleConflictDialog : Window
{
    public ScheduleConflictDialog(IEnumerable<(string profileName, string conflictDesc)> conflicts)
    {
        InitializeComponent();
        ConflictList.ItemsSource = conflicts
            .Select(c => new ConflictItem(c.profileName, c.conflictDesc))
            .ToList();
    }

    private void KeepBoth_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void GoBack_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    public sealed record ConflictItem(string ProfileName, string ConflictDesc);
}
