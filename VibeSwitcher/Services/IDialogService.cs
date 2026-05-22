using VibeSwitcher.Helpers;
using VibeSwitcher.Models;

namespace VibeSwitcher.Services;

public interface IDialogService
{
    HotkeyDefinition? ShowHotkeyCapture(HotkeyDefinition current);
    bool ShowConfirmDelete(string profileName);
    bool ShowConfirmClone(string profileName);
    string? ShowBrowseIconFile();
    GalleryPickResult? ShowIconGallery();
    void ShowAlert(string title, string message);
    bool ShowHotkeyConflictRetry(string title, string message);
    ProfileMode? ShowProfileTypeDialog();
    void ShowMicTest(string deviceId, string deviceName);
    bool ShowScheduleConflict(IEnumerable<(string profileName, string conflictDesc)> conflicts);
}
