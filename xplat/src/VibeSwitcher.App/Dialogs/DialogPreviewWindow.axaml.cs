using Avalonia;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using VibeSwitcher.App.Controls;
using VibeSwitcher.Helpers;

namespace VibeSwitcher.App.Dialogs;

public partial class DialogPreviewWindow : VsDialog
{
    private readonly DialogService _dialogs;

    public DialogPreviewWindow()
    {
        AvaloniaXamlLoader.Load(this);
        Title = "VibeSwitcher";
        _dialogs = new DialogService(() => this);
    }

    private void Alert_Click(object? s, RoutedEventArgs e) =>
        _ = _dialogs.ShowAlertAsync("Device Not Found", "The headset in this profile isn't connected. Plug it in, or pick another device.");

    private void Confirm_Click(object? s, RoutedEventArgs e) =>
        _ = _dialogs.ShowConfirmAsync("Replace Configuration?", "This replaces all your profiles and settings with the imported ones.", "Replace");

    private void Delete_Click(object? s, RoutedEventArgs e) => _ = _dialogs.ShowConfirmDeleteAsync("Gaming");

    private void Retry_Click(object? s, RoutedEventArgs e) =>
        _ = _dialogs.ShowHotkeyConflictRetryAsync("Hotkey Already in Use", "Ctrl + Alt + G is used by another app. Try a different shortcut?");

    private void Error_Click(object? s, RoutedEventArgs e) =>
        _ = _dialogs.ShowAsync(new ErrorDialog(ErrorCode.ProfileSwitchFailed, "Profile Switch Failed",
            "Couldn't switch to \"Gaming\": the playback device isn't available.", "", new AppLogger(Path.GetTempPath()), new SessionErrorTracker()));

    private void Uninstall_Click(object? s, RoutedEventArgs e) =>
        _ = _dialogs.ShowAsync(new UninstallDialog(OperatingSystem.IsMacOS() ? UninstallText.Mac : UninstallText.LinuxAppImage));

    private void Light_Click(object? s, RoutedEventArgs e) => SetTheme(ThemeVariant.Light);
    private void Dark_Click(object? s, RoutedEventArgs e) => SetTheme(ThemeVariant.Dark);
    private void Auto_Click(object? s, RoutedEventArgs e) => SetTheme(ThemeVariant.Default);

    private static void SetTheme(ThemeVariant v)
    {
        if (Application.Current != null) Application.Current.RequestedThemeVariant = v;
    }
}
