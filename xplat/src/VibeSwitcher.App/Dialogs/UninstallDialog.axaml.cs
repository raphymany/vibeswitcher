using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using VibeSwitcher.App.Controls;

namespace VibeSwitcher.App.Dialogs;

// What uninstalling does on this system, in the dialog's words.
public sealed record UninstallText(string Subtitle, string Body, string DataNote)
{
    private const string KeepNote = " Leave this off to keep your profiles for a future reinstall.";

    public static readonly UninstallText Mac = new(
        "The app will close and move to the Trash.",
        "VibeSwitcher will be moved to the Trash, along with its login item. No password is needed.",
        "Deletes the app's data folder (~/Library/Application Support/VibeSwitcher)." + KeepNote);

    public static readonly UninstallText LinuxAppImage = new(
        "The app will close and its file will be deleted.",
        "VibeSwitcher's AppImage file will be deleted, along with its start-at-login entry, any app-menu entry for it, and the headset permission rule if it set one up (that asks for your password).",
        "Deletes the app's data folder (~/.config/VibeSwitcher)." + KeepNote);

    public static readonly UninstallText LinuxPackage = new(
        "The app will close and the system will remove it.",
        "VibeSwitcher will be removed by the system's package manager, along with its start-at-login entry. You'll be asked for your password.",
        "Deletes the app's data folder (~/.config/VibeSwitcher)." + KeepNote);

    // The note naming the folder this copy really uses (XDG_CONFIG_HOME can move it), with the home
    // folder written as "~".
    public UninstallText WithDataFolder(string folder)
    {
        if (string.IsNullOrEmpty(folder)) return this;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('/', '\\');
        var shown = home.Length > 0 && folder.StartsWith(home + "/", StringComparison.Ordinal) ? "~" + folder[home.Length..] : folder;
        return this with { DataNote = $"Deletes the app's data folder ({shown})." + KeepNote };
    }
}

// Port of Views/UninstallDialog.xaml.cs.
public partial class UninstallDialog : VsDialog
{
    public bool DeleteData => this.FindControl<CheckBox>("DeleteDataToggle")!.IsChecked == true;

    public UninstallDialog() : this(UninstallText.Mac) { } // (for the XAML loader)

    public UninstallDialog(UninstallText text)
    {
        AvaloniaXamlLoader.Load(this);
        this.FindControl<TextBlock>("SubtitleText")!.Text = text.Subtitle;
        this.FindControl<TextBlock>("BodyText")!.Text = text.Body;
        this.FindControl<TextBlock>("DataText")!.Text = text.DataNote;
    }

    private void Uninstall_Click(object? sender, RoutedEventArgs e) => CloseWith(true);
    private void Cancel_Click(object? sender, RoutedEventArgs e) => CloseWith(false);
}
