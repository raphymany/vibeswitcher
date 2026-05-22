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

    public ScheduleEntry? ScheduleWizardResult { get; set; } = null;
    public ScheduleEntry? ShowScheduleWizard(ScheduleEntry source, bool use12Hour) => ScheduleWizardResult;
}
