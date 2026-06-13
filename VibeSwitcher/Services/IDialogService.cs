using VibeSwitcher.Helpers;
using VibeSwitcher.Models;

namespace VibeSwitcher.Services;

public interface IDialogService
{
    HotkeyDefinition? ShowHotkeyCapture(HotkeyDefinition current);
    bool ShowConfirmDelete(string profileName);
    // Opens the clone wizard. Returns a fully-built copy (new Id) to persist, or null if cancelled.
    // captureHotkey runs the caller's capture+conflict flow and returns the chosen hotkey (or null).
    DeviceProfile? ShowCloneWizard(
        DeviceProfile source,
        IReadOnlyList<AudioDeviceInfo> playbackDevices,
        IReadOnlyList<AudioDeviceInfo> recordingDevices,
        bool use12Hour,
        Func<HotkeyDefinition, HotkeyDefinition?> captureHotkey);
    string? ShowBrowseIconFile();
    GalleryPickResult? ShowIconGallery();
    void ShowAlert(string title, string message);
    bool ShowHotkeyConflictRetry(string title, string message);
    ProfileMode? ShowProfileTypeDialog();
    void ShowMicTest(string deviceId, string deviceName);
    bool ShowScheduleConflict(string conflictDescription);
    ScheduleWizardOutcome ShowScheduleWizard(ScheduleEntry source, bool use12Hour, bool isEditing = false);
    SoundOverrideResult? ShowSoundWizard(bool enabled, string? tone, string? customPath, int volume, bool showBanner = false, bool isEditing = false);
    bool ShowConfirm(string title, string message, string actionLabel);
    bool ShowSupportedHeadsets();
    List<string>? ShowAppTriggerWizard(List<string> currentTriggers, IReadOnlyDictionary<string, string> usedByOthers);
    // Takes the ProfileCardViewModel as object so the dialog contract stays free of view-model types.
    void ShowManageSchedules(object profileCard);
}
