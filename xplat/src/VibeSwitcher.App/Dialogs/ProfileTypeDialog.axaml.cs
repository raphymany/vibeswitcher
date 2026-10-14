using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using VibeSwitcher.App.Controls;
using VibeSwitcher.Models;

namespace VibeSwitcher.App.Dialogs;

// Port of Views/ProfileTypeDialog.xaml.cs.
public partial class ProfileTypeDialog : VsDialog
{
    public ProfileMode? ChosenMode { get; private set; }

    public ProfileTypeDialog()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void Playback_Click(object? sender, RoutedEventArgs e) => Choose(ProfileMode.Playback);
    private void Recording_Click(object? sender, RoutedEventArgs e) => Choose(ProfileMode.Recording);
    private void Both_Click(object? sender, RoutedEventArgs e) => Choose(ProfileMode.Both);

    private void Choose(ProfileMode mode)
    {
        ChosenMode = mode;
        CloseWith(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => CloseWith(false);
}
