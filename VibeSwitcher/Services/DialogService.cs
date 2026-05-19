using Microsoft.Win32;
using System.Windows;
using VibeSwitcher.Models;
using VibeSwitcher.Views;

namespace VibeSwitcher.Services;

public class DialogService : IDialogService
{
    private static Window? OwnerWindow =>
        Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault();

    public HotkeyDefinition? ShowHotkeyCapture(HotkeyDefinition current)
    {
        var dialog = new HotkeyCaptureDialog(current) { Owner = OwnerWindow };
        return dialog.ShowDialog() == true ? dialog.CapturedHotkey : null;
    }

    public bool ShowConfirmDelete(string profileName)
    {
        var dialog = new ConfirmDeleteDialog(profileName) { Owner = OwnerWindow };
        return dialog.ShowDialog() == true;
    }

    public string? ShowBrowseIconFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Icon File",
            Filter = "Icon Files (*.ico)|*.ico",
            CheckFileExists = true
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public void ShowAlert(string title, string message)
    {
        new AlertDialog(title, message) { Owner = OwnerWindow }.ShowDialog();
    }

    public bool ShowHotkeyConflictRetry(string title, string message)
    {
        return MessageBox.Show(OwnerWindow, message + "\n\nWould you like to try a different hotkey?",
            title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    public ProfileMode? ShowProfileTypeDialog()
    {
        var dialog = new ProfileTypeDialog { Owner = OwnerWindow };
        return dialog.ShowDialog() == true ? dialog.ChosenMode : null;
    }
}
