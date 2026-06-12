using VibeSwitcher.Helpers;
using VibeSwitcher.Models;
using VibeSwitcher.Services;

namespace VibeSwitcher.Tests;

internal sealed class FakeDialogService : IDialogService
{
    public HotkeyDefinition? HotkeyCaptureResult { get; set; }
    public bool ConfirmDeleteResult { get; set; } = true;
    public bool ConfirmCloneResult { get; set; } = true;
    public string? BrowseIconFileResult { get; set; }
    public GalleryPickResult? IconGalleryResult { get; set; }
    public ProfileMode? ProfileTypeResult { get; set; } = ProfileMode.Both;

    public List<(string Title, string Message)> AlertsShown { get; } = new();
    public List<(string Title, string Message)> ConflictRetriesShown { get; } = new();
    public bool ConflictRetryResult { get; set; } = false;

    public HotkeyDefinition? ShowHotkeyCapture(HotkeyDefinition current) => HotkeyCaptureResult;
    public bool ShowConfirmDelete(string profileName) => ConfirmDeleteResult;
    public bool ShowConfirmClone(string profileName) => ConfirmCloneResult;
    public string? ShowBrowseIconFile() => BrowseIconFileResult;
    public GalleryPickResult? ShowIconGallery() => IconGalleryResult;
    public void ShowAlert(string title, string message) => AlertsShown.Add((title, message));
    public bool ShowHotkeyConflictRetry(string title, string message)
    {
        ConflictRetriesShown.Add((title, message));
        return ConflictRetryResult;
    }
    public ProfileMode? ShowProfileTypeDialog() => ProfileTypeResult;

    public List<string> MicTestCalledWith { get; } = new();
    public void ShowMicTest(string deviceId, string deviceName) => MicTestCalledWith.Add(deviceId);

    public bool ScheduleConflictResult { get; set; } = false;
    public bool ShowScheduleConflict(string conflictDescription) => ScheduleConflictResult;

    public ScheduleEntry? ScheduleWizardResult { get; set; } = null;
    public bool ScheduleWizardRemoved { get; set; } = false;
    public ScheduleWizardOutcome ShowScheduleWizard(ScheduleEntry source, bool use12Hour, bool isEditing = false) =>
        ScheduleWizardRemoved
            ? ScheduleWizardOutcome.RemovedOutcome
            : ScheduleWizardResult != null
                ? ScheduleWizardOutcome.Saved(ScheduleWizardResult)
                : ScheduleWizardOutcome.Cancelled;

    public SoundOverrideResult? SoundWizardResult { get; set; } = null;
    public SoundOverrideResult? ShowSoundWizard(bool enabled, string? tone, string? customPath, int volume, bool showBanner = false, bool isEditing = false) => SoundWizardResult;

    public bool ConfirmResult { get; set; } = false;
    public bool ShowConfirm(string title, string message, string actionLabel) => ConfirmResult;

    public bool SupportedHeadsetsResult { get; set; } = true;
    public bool ShowSupportedHeadsets() => SupportedHeadsetsResult;

    public List<string>? AppTriggerWizardResult { get; set; } = null;
    public List<string>? ShowAppTriggerWizard(List<string> currentTriggers, IReadOnlyDictionary<string, string> usedByOthers) => AppTriggerWizardResult;

    public int ManageSchedulesShownCount { get; private set; }
    public void ShowManageSchedules(object profileCard) => ManageSchedulesShownCount++;
}
