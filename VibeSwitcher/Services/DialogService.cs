using Microsoft.Win32;
using System.Windows;
using VibeSwitcher.Helpers;
using VibeSwitcher.Models;
using VibeSwitcher.Views;

namespace VibeSwitcher.Services;

public class DialogService : IDialogService
{
    private readonly IAppLogger _logger;
    private readonly IConfigService _configService;

    public DialogService(IAppLogger logger, IConfigService configService)
    {
        _logger = logger;
        _configService = configService;
    }

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

    public DeviceProfile? ShowCloneWizard(
        DeviceProfile source,
        IReadOnlyList<AudioDeviceInfo> playbackDevices,
        IReadOnlyList<AudioDeviceInfo> recordingDevices,
        bool use12Hour,
        Func<HotkeyDefinition, HotkeyDefinition?> captureHotkey)
    {
        var dialog = new CloneProfileDialog(source, playbackDevices, recordingDevices, use12Hour, captureHotkey)
        { Owner = OwnerWindow };
        return dialog.ShowDialog() == true ? dialog.Result : null;
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

    public GalleryPickResult? ShowIconGallery()
    {
        var dialog = new IconGalleryDialog(_configService.IconsLibraryDir) { Owner = OwnerWindow };
        if (dialog.ShowDialog() != true) return null;
        return new GalleryPickResult
        {
            Item = dialog.SelectedItem,
            BrowseFromDisk = dialog.BrowseFromDisk,
            IconColor = dialog.SelectedColor,
            CustomIconPath = dialog.CustomIconPath,
        };
    }

    public void ShowAlert(string title, string message)
    {
        new AlertDialog(title, message) { Owner = OwnerWindow }.ShowDialog();
    }

    public bool ShowHotkeyConflictRetry(string title, string message)
    {
        return new ConflictRetryDialog(title, message) { Owner = OwnerWindow }.ShowDialog() == true;
    }

    public ProfileMode? ShowProfileTypeDialog()
    {
        var dialog = new ProfileTypeDialog { Owner = OwnerWindow };
        return dialog.ShowDialog() == true ? dialog.ChosenMode : null;
    }

    public void ShowMicTest(string deviceId, string deviceName)
    {
        new MicTestDialog(deviceId, deviceName, _logger) { Owner = OwnerWindow }.ShowDialog();
    }

    public bool ShowScheduleConflict(string conflictDescription)
    {
        return new ConflictRetryDialog(
            "Schedule Conflict",
            $"This schedule conflicts with an existing schedule:\n{conflictDescription}\n\nTry again with a different time or days?")
        { Owner = OwnerWindow }.ShowDialog() == true;
    }

    public ScheduleWizardOutcome ShowScheduleWizard(ScheduleEntry source, bool use12Hour, bool isEditing = false)
    {
        var dialog = new ScheduleWizardDialog(source, use12Hour, isEditing) { Owner = OwnerWindow };
        if (dialog.ShowDialog() != true) return ScheduleWizardOutcome.Cancelled;
        if (dialog.RemoveRequested) return ScheduleWizardOutcome.RemovedOutcome;
        return dialog.Result != null
            ? ScheduleWizardOutcome.Saved(dialog.Result)
            : ScheduleWizardOutcome.Cancelled;
    }

    public SoundOverrideResult? ShowSoundWizard(bool enabled, string? tone, string? customPath, int volume, bool showBanner = false, bool isEditing = false)
    {
        var dialog = new SwitchSoundDialog(enabled, tone, customPath, volume, _logger, _configService.SoundsLibraryDir, showBanner, isEditing) { Owner = OwnerWindow };
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    public bool ShowConfirm(string title, string message, string actionLabel)
    {
        var dialog = new ConfirmDialog(title, message, actionLabel, subtitle: "") { Owner = OwnerWindow };
        return dialog.ShowDialog() == true;
    }

    public bool ShowSupportedHeadsets()
    {
        return new SupportedHeadsetsDialog { Owner = OwnerWindow }.ShowDialog() == true;
    }

    public List<string>? ShowAppTriggerWizard(List<string> currentTriggers, IReadOnlyDictionary<string, string> usedByOthers)
    {
        var dialog = new AppTriggerDialog(currentTriggers, usedByOthers) { Owner = OwnerWindow };
        return dialog.ShowDialog() == true ? dialog.ResultTriggers : null;
    }

    public void ShowManageSchedules(object profileCard)
    {
        if (profileCard is not ViewModels.ProfileCardViewModel card) return;
        new ManageSchedulesDialog(card) { Owner = OwnerWindow }.ShowDialog();
    }

}
