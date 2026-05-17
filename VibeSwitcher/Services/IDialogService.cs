using VibeSwitcher.Models;

namespace VibeSwitcher.Services;

public interface IDialogService
{
    HotkeyDefinition? ShowHotkeyCapture(HotkeyDefinition current);
    bool ShowConfirmDelete(string profileName);
    string? ShowBrowseIconFile();
    void ShowAlert(string title, string message);
    ProfileMode? ShowProfileTypeDialog();
}
