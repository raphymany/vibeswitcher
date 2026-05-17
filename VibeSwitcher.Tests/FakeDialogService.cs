using VibeSwitcher.Models;
using VibeSwitcher.Services;

namespace VibeSwitcher.Tests;

internal sealed class FakeDialogService : IDialogService
{
    public HotkeyDefinition? HotkeyCaptureResult { get; set; }
    public bool ConfirmDeleteResult { get; set; } = true;
    public string? BrowseIconFileResult { get; set; }
    public ProfileMode? ProfileTypeResult { get; set; } = ProfileMode.Both;

    public List<(string Title, string Message)> AlertsShown { get; } = new();

    public HotkeyDefinition? ShowHotkeyCapture(HotkeyDefinition current) => HotkeyCaptureResult;
    public bool ShowConfirmDelete(string profileName) => ConfirmDeleteResult;
    public string? ShowBrowseIconFile() => BrowseIconFileResult;
    public void ShowAlert(string title, string message) => AlertsShown.Add((title, message));
    public ProfileMode? ShowProfileTypeDialog() => ProfileTypeResult;
}
