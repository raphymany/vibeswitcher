using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace VibeSwitcher.Views;

public partial class MiniModeSetupDialog : Window
{
    public sealed class ProfileChoice
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = "";
        public ImageSource? Icon { get; init; }
        public bool HasIcon => Icon != null;
        public bool IsSelected { get; set; }
    }

    private readonly IReadOnlyList<ProfileChoice> _profiles;

    public string SelectedLayout { get; private set; }
    public List<Guid> SelectedProfileIds { get; private set; } = new(); // empty = show all

    public MiniModeSetupDialog(string currentLayout, IReadOnlyList<ProfileChoice> profiles)
    {
        InitializeComponent();
        _profiles = profiles;

        SelectedLayout = currentLayout == "Grid" ? "Grid" : "Rows";
        LayoutRows.IsChecked = SelectedLayout == "Rows";
        LayoutGrid.IsChecked = SelectedLayout == "Grid";

        ProfileChecklist.ItemsSource = profiles;
        // The checklist starts active; "Show all" flips on automatically when every profile is checked.
        AllToggle.IsChecked = false;
        UpdateChecklistEnabled();

        KeyDown += (_, e) => { if (e.Key == Key.Escape) DialogResult = false; };
    }

    private void ProfileCheck_Changed(object sender, RoutedEventArgs e)
    {
        // Deferred so the IsSelected binding has flushed before we count.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_profiles.Count > 0 && AllToggle.IsChecked != true && _profiles.All(p => p.IsSelected))
                AllToggle.IsChecked = true;
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void LayoutRows_Click(object sender, RoutedEventArgs e)
    {
        LayoutRows.IsChecked = true;
        LayoutGrid.IsChecked = false;
        SelectedLayout = "Rows";
    }

    private void LayoutGrid_Click(object sender, RoutedEventArgs e)
    {
        LayoutGrid.IsChecked = true;
        LayoutRows.IsChecked = false;
        SelectedLayout = "Grid";
    }

    private void AllToggle_Changed(object sender, RoutedEventArgs e) => UpdateChecklistEnabled();

    private void UpdateChecklistEnabled()
    {
        bool listActive = AllToggle.IsChecked != true;
        ChecklistHost.IsEnabled = listActive;
        ChecklistHost.Opacity = listActive ? 1.0 : 0.45;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        SelectedProfileIds = AllToggle.IsChecked == true
            ? new List<Guid>()
            : _profiles.Where(p => p.IsSelected).Select(p => p.Id).ToList();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
