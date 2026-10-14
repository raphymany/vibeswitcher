using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using VibeSwitcher.App.Controls;

namespace VibeSwitcher.App.Dialogs;

// Port of Views/MiniModeSetupDialog.xaml.cs.
public partial class MiniModeSetupDialog : VsDialog
{
    public sealed class ProfileChoice
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = "";
        public System.Windows.Media.ImageSource? Icon { get; init; }
        public bool HasIcon => Icon != null;
        public bool IsSelected { get; set; }
    }

    private readonly IReadOnlyList<ProfileChoice> _profiles;

    public string SelectedLayout { get; private set; }
    public List<Guid> SelectedProfileIds { get; private set; } = new(); // empty = show all

    public MiniModeSetupDialog() : this("Rows", []) { } // (for the XAML loader)

    public MiniModeSetupDialog(string currentLayout, IReadOnlyList<ProfileChoice> profiles)
    {
        AvaloniaXamlLoader.Load(this);
        _profiles = profiles;

        SelectedLayout = currentLayout == "Grid" ? "Grid" : "Rows";
        Toggle("LayoutRows").IsChecked = SelectedLayout == "Rows";
        Toggle("LayoutGrid").IsChecked = SelectedLayout == "Grid";

        this.FindControl<ItemsControl>("ProfileChecklist")!.ItemsSource = profiles;
        // The checklist starts active; "Show all" flips on automatically when every profile is checked.
        this.FindControl<CheckBox>("AllToggle")!.IsChecked = false;
        UpdateChecklistEnabled();
    }

    private ToggleButton Toggle(string name) => this.FindControl<ToggleButton>(name)!;

    private void ProfileCheck_Changed(object? sender, RoutedEventArgs e)
    {
        // Deferred so the IsSelected binding has flushed before we count.
        Dispatcher.UIThread.Post(() =>
        {
            var all = this.FindControl<CheckBox>("AllToggle")!;
            if (_profiles.Count > 0 && all.IsChecked != true && _profiles.All(p => p.IsSelected))
                all.IsChecked = true;
        }, DispatcherPriority.Background);
    }

    private void LayoutRows_Click(object? sender, RoutedEventArgs e)
    {
        Toggle("LayoutRows").IsChecked = true;
        Toggle("LayoutGrid").IsChecked = false;
        SelectedLayout = "Rows";
    }

    private void LayoutGrid_Click(object? sender, RoutedEventArgs e)
    {
        Toggle("LayoutGrid").IsChecked = true;
        Toggle("LayoutRows").IsChecked = false;
        SelectedLayout = "Grid";
    }

    private void AllToggle_Changed(object? sender, RoutedEventArgs e) => UpdateChecklistEnabled();

    private void UpdateChecklistEnabled()
    {
        // (Before the XAML has loaded, the toggle isn't there yet.)
        if (this.FindControl<CheckBox>("AllToggle") is not { } all) return;
        bool listActive = all.IsChecked != true;
        var host = this.FindControl<Border>("ChecklistHost")!;
        host.IsEnabled = listActive;
        host.Opacity = listActive ? 1.0 : 0.45;
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        SelectedProfileIds = this.FindControl<CheckBox>("AllToggle")!.IsChecked == true
            ? new List<Guid>()
            : _profiles.Where(p => p.IsSelected).Select(p => p.Id).ToList();
        CloseWith(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => CloseWith(false);
}
