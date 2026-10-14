using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using VibeSwitcher.App.Controls;

namespace VibeSwitcher.App.Dialogs;

// Port of Views/ConflictRetryDialog.xaml.cs.
public partial class ConflictRetryDialog : VsDialog
{
    public ConflictRetryDialog() : this("", "") { } // (for the XAML loader)

    public ConflictRetryDialog(string title, string message)
    {
        AvaloniaXamlLoader.Load(this);
        Title = "VibeSwitcher";
        this.FindControl<TextBlock>("TitleText")!.Text = title;
        this.FindControl<TextBlock>("MessageText")!.Text = message;
    }

    private void TryAgain_Click(object? sender, RoutedEventArgs e) => CloseWith(true);
    private void Close_Click(object? sender, RoutedEventArgs e) => CloseWith(false);
}
