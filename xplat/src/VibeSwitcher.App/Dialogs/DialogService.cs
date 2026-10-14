using Avalonia.Controls;
using VibeSwitcher.App.Controls;

namespace VibeSwitcher.App.Dialogs;

// The dialogs of the Windows DialogService (Services/DialogService.cs) ported so far, the same
// dialogs with the same texts — awaited instead of blocking, which is how Avalonia shows a modal
// dialog. Every dialog is owned by (and centred on) the window the app hands in, normally Settings.
// The rest of the Windows IDialogService follows with the Settings window and its view models.
public sealed class DialogService(Func<Window?> owner)
{
    public Task ShowAlertAsync(string title, string message) =>
        ShowAsync(new AlertDialog(title, message));

    public Task<bool> ShowConfirmDeleteAsync(string profileName) =>
        ShowAsync(new ConfirmDeleteDialog(profileName));

    public Task<bool> ShowHotkeyConflictRetryAsync(string title, string message) =>
        ShowAsync(new ConflictRetryDialog(title, message));

    public Task<bool> ShowScheduleConflictAsync(string conflictDescription) =>
        ShowAsync(new ConflictRetryDialog(
            "Schedule Conflict",
            $"This schedule conflicts with an existing schedule:\n{conflictDescription}\n\nTry again with a different time or days?"));

    public Task<bool> ShowConfirmAsync(string title, string message, string actionLabel) =>
        ShowAsync(new ConfirmDialog(title, message, actionLabel, subtitle: ""));

    // True when the dialog closed with its "yes" button (DialogResult == true on Windows).
    public async Task<bool> ShowAsync(VsDialog dialog)
    {
        // Without a visible window to sit on (at start-up, or Settings hidden in the tray — Avalonia
        // can't own a dialog by a hidden window), a dialog stands on its own, centred on screen.
        if (owner() is not { IsVisible: true } parent)
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            var closed = new TaskCompletionSource();
            dialog.Closed += (_, _) => closed.TrySetResult();
            dialog.Show();
            await closed.Task;
            return dialog.Result;
        }
        await dialog.ShowDialog(parent);
        return dialog.Result;
    }
}
