using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using VibeSwitcher.App.Controls;

namespace VibeSwitcher.App.Dialogs;

// Port of Views/ConfirmDeleteDialog.xaml.cs.
public partial class ConfirmDeleteDialog : VsDialog
{
    public ConfirmDeleteDialog() : this("") { } // (for the XAML loader)

    public ConfirmDeleteDialog(string profileName)
    {
        AvaloniaXamlLoader.Load(this);
        this.FindControl<TextBlock>("MessageText")!.Text = $"Are you sure you want to delete \"{profileName}\"?";
    }

    private void Delete_Click(object? sender, RoutedEventArgs e) => CloseWith(true);
    private void Cancel_Click(object? sender, RoutedEventArgs e) => CloseWith(false);
}
