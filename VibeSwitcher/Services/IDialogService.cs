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
    bool ShowConfirmScheduleDelete(string scheduleSummary);
    bool ShowScheduleConflict(string conflictDescription);
    ScheduleEntry? ShowScheduleWizard(ScheduleEntry source, bool use12Hour);
    SoundOverrideResult? ShowSoundWizard(bool enabled, string? tone, string? customPath, int volume, bool showBanner = false);
}
