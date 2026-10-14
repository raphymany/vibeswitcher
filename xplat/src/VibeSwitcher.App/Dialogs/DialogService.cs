using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using VibeSwitcher.App.Controls;
using VibeSwitcher.Helpers;
using VibeSwitcher.Models;
using VibeSwitcher.Services;

namespace VibeSwitcher.App.Dialogs;

// The Windows DialogService (Services/DialogService.cs): the same dialogs with the same texts, each
// owned by (and centred on) the window the app hands in, normally Settings.
//
// The shared view models call it synchronously, as on Windows, so each IDialogService method waits
// for its dialog the way WPF's ShowDialog does: a nested message loop keeps the app running (the
// dialog works, timers tick) until the dialog closes, while the app's other windows ignore input.
// The tray / menu-bar menu and global hotkeys stay live, as on Windows. The *Async methods are the
// same dialogs for the app's own code. Dialogs ported in a later step are listed below; until then
// they answer "cancelled" (and log it).
// (appWindows: the app's open windows, the desktop lifetime's by default.)
public sealed class DialogService(Func<Window?> owner, Func<IEnumerable<Window>>? appWindows = null) : IDialogService
{
    // What some dialogs need from the app: the system's audio backend (the mic test's level meter)
    // and the log (Windows passes these to its DialogService too).
    public IPlatformAudioService? Audio { get; init; }
    public IAppLogger? Logger { get; init; }

    // ── IDialogService (the shared view models) ────────────────────────────────────────────────────
    public bool ShowConfirmDelete(string profileName) => Wait(ShowConfirmDeleteAsync(profileName));

    public void ShowAlert(string title, string message) => Wait(ShowAlertAsync(title, message));

    public bool ShowHotkeyConflictRetry(string title, string message) => Wait(ShowHotkeyConflictRetryAsync(title, message));

    public bool ShowScheduleConflict(string conflictDescription) => Wait(ShowScheduleConflictAsync(conflictDescription));

    public bool ShowConfirm(string title, string message, string actionLabel) => Wait(ShowConfirmAsync(title, message, actionLabel));

    public string? ShowBrowseIconFile() => Wait(ShowBrowseIconFileAsync());

    // The rest of the Windows DialogService. Still to come (the next steps): the clone, icon gallery,
    // schedule, sound and app-trigger wizards — until then they answer "cancelled" (and log it).
    public HotkeyDefinition? ShowHotkeyCapture(HotkeyDefinition current) => ShowHotkeyCapture(current, subtitle: null);
    public DeviceProfile? ShowCloneWizard(DeviceProfile source, IReadOnlyList<AudioDeviceInfo> playbackDevices,
        IReadOnlyList<AudioDeviceInfo> recordingDevices, bool use12Hour, Func<HotkeyDefinition, HotkeyDefinition?> captureHotkey) =>
        NotYet<DeviceProfile>(nameof(ShowCloneWizard));
    public GalleryPickResult? ShowIconGallery() => NotYet<GalleryPickResult>(nameof(ShowIconGallery));
    public ProfileMode? ShowProfileTypeDialog()
    {
        var dialog = new ProfileTypeDialog();
        return Wait(ShowAsync(dialog)) ? dialog.ChosenMode : null;
    }
    public void ShowMicTest(string deviceId, string deviceName)
    {
        if (Audio == null) Logger?.Warning("DialogService.ShowMicTest", "No audio backend to read the microphone level from.");
        Wait(ShowAsync(new MicTestDialog(deviceId, deviceName, Audio, Logger)));
    }
    public ScheduleWizardOutcome ShowScheduleWizard(ScheduleEntry source, bool use12Hour, bool isEditing = false)
    {
        NotYet<object>(nameof(ShowScheduleWizard));
        return ScheduleWizardOutcome.Cancelled;
    }
    public SoundOverrideResult? ShowSoundWizard(bool enabled, string? tone, string? customPath, int volume, bool showBanner = false, bool isEditing = false) =>
        NotYet<SoundOverrideResult>(nameof(ShowSoundWizard));
    public bool ShowSupportedHeadsets() => Wait(ShowAsync(new SupportedHeadsetsDialog()));
    public List<string>? ShowAppTriggerWizard(List<string> currentTriggers, IReadOnlyDictionary<string, string> usedByOthers) =>
        NotYet<List<string>>(nameof(ShowAppTriggerWizard));
    public void ShowManageSchedules(object profileCard)
    {
        if (profileCard is not ViewModels.ProfileCardViewModel card) return;
        Wait(ShowAsync(new ManageSchedulesDialog(card)));
    }

    // Settings' own dialogs (the Windows window opens these itself): an app shortcut with the row's
    // wording, and the device aliases.
    public HotkeyDefinition? ShowHotkeyCapture(HotkeyDefinition current, string? subtitle)
    {
        var dialog = new HotkeyCaptureDialog(current, subtitle);
        return Wait(ShowAsync(dialog)) ? dialog.CapturedHotkey : null;
    }
    public void ShowDeviceAliases(System.Collections.ObjectModel.ObservableCollection<ViewModels.DeviceAliasItem> aliases) =>
        Wait(ShowAsync(new DeviceAliasesDialog(aliases)));

    private static T? NotYet<T>(string dialog) where T : class
    {
        AppLog.Warning("DialogService", $"{dialog}: this dialog isn't available on macOS/Linux yet.");
        return null;
    }

    // ── The same dialogs, awaited ──────────────────────────────────────────────────────────────────
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

    // The system's file picker, for .ico files (as on Windows). Null when cancelled — or when no
    // window is showing, since the picker has to belong to one.
    public async Task<string?> ShowBrowseIconFileAsync()
    {
        if (Owner() is not { } parent) return null;
        // (Linux pickers don't block the window they belong to; Windows' and macOS's do.)
        using var _ = DisableWindows(except: null);
        var files = await parent.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Icon File",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Icon Files")
                {
                    Patterns = ["*.ico", "*.ICO"],
                    MimeTypes = ["image/x-icon", "image/vnd.microsoft.icon"],
                    AppleUniformTypeIdentifiers = ["com.microsoft.ico"],
                },
            ],
        });
        return files.Count == 1 ? files[0].TryGetLocalPath() : null;
    }

    // Backup & Restore: the system's save and open pickers for a .json config file (Windows:
    // SaveFileDialog / OpenFileDialog). Null when cancelled or when no window is showing.
    private static readonly FilePickerFileType JsonFiles = new("JSON Files")
    {
        Patterns = ["*.json", "*.JSON"],
        MimeTypes = ["application/json"],
        AppleUniformTypeIdentifiers = ["public.json"],
    };

    public async Task<string?> PickSaveJsonAsync(string title, string suggestedName)
    {
        if (Owner() is not { } parent) return null;
        using var _ = DisableWindows(except: null);
        var file = await parent.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = "json",
            FileTypeChoices = [JsonFiles],
            ShowOverwritePrompt = true,
        });
        if (file == null) return null;
        // (Some Linux pickers keep a name typed without ".json" as it is; Windows adds it.)
        var path = LocalPath(file);
        return path != null && !System.IO.Path.HasExtension(path) ? path + ".json" : path;
    }

    // The picked file's path — null (and logged) for one that isn't on this computer's disk (e.g. a
    // network location the picker offers), which the config can't be written to or read from.
    private static string? LocalPath(IStorageItem item)
    {
        var path = item.TryGetLocalPath();
        if (path == null) AppLog.Warning("DialogService", $"'{item.Name}' isn't a file on this computer, so it can't be used.");
        return path;
    }

    public async Task<string?> PickOpenJsonAsync(string title)
    {
        if (Owner() is not { } parent) return null;
        using var _ = DisableWindows(except: null);
        var files = await parent.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [JsonFiles],
        });
        return files.Count == 1 ? LocalPath(files[0]) : null;
    }

    // The dialogs open right now, innermost last: a dialog opened from a dialog (the clone wizard's
    // hotkey capture) belongs to that dialog, as on Windows.
    private readonly List<Window> _open = [];

    private Window? Owner() =>
        _open.LastOrDefault(w => w.IsVisible) ?? (owner() is { IsVisible: true } window ? window : null);

    // True when the dialog closed with its "yes" button (DialogResult == true on Windows).
    public async Task<bool> ShowAsync(VsDialog dialog)
    {
        var parent = Owner();
        // Like WPF's ShowDialog, every other window of the app (Mini Mode too) waits for the dialog.
        using var _ = DisableWindows(except: dialog);
        _open.Add(dialog);
        try
        {
            // Without a visible window to sit on (at start-up, or Settings hidden in the tray — Avalonia
            // can't own a dialog by a hidden window), a dialog stands on its own, centred on screen
            // and in front.
            if (parent == null)
            {
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                dialog.Topmost = true;
                var closed = new TaskCompletionSource();
                dialog.Closed += (_, _) => closed.TrySetResult();
                dialog.Show();
                dialog.Activate();
                await closed.Task;
                return dialog.Result;
            }
            await dialog.ShowDialog(parent);
            return dialog.Result;
        }
        finally
        {
            _open.Remove(dialog);
        }
    }

    // Makes the app's other open windows ignore clicks, keys and scrolling until disposed — what
    // WPF's ShowDialog does (Win32 EnableWindow), without IsEnabled's greyed-out look. A click on a
    // blocked window brings the newest dialog to the front instead.
    private IDisposable DisableWindows(Window? except)
    {
        var windows = appWindows?.Invoke()
            ?? (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows
            ?? [];
        var blocked = windows.Where(w => w != except).ToList();
        EventHandler<RoutedEventArgs> block = (_, e) =>
        {
            if (e is PointerPressedEventArgs) _open.LastOrDefault(w => w.IsVisible)?.Activate();
            e.Handled = true;
        };
        foreach (var w in blocked)
            foreach (var routed in BlockedInput)
                w.AddHandler(routed, block, RoutingStrategies.Tunnel, handledEventsToo: true);
        return new Restore(blocked, block);
    }

    private static readonly RoutedEvent[] BlockedInput =
    [
        InputElement.PointerPressedEvent, InputElement.PointerReleasedEvent, InputElement.PointerWheelChangedEvent,
        InputElement.KeyDownEvent, InputElement.KeyUpEvent, InputElement.TextInputEvent,
    ];

    private sealed class Restore(List<Window> windows, EventHandler<RoutedEventArgs> block) : IDisposable
    {
        public void Dispose()
        {
            foreach (var w in windows)
                foreach (var routed in BlockedInput)
                    w.RemoveHandler(routed, block);
            windows.Clear();
        }
    }

    // Runs the message loop until the task finishes, then returns its result (UI thread only, like
    // WPF's ShowDialog). When the app is quitting the loop can't finish a dialog any more, so the
    // answer is the default — "cancelled" — instead of waiting forever.
    private static volatile bool _quitting;

    static DialogService() => Dispatcher.UIThread.ShutdownStarted += (_, _) => _quitting = true;

    internal static T? Wait<T>(Task<T> task) => Wait((Task)task) ? task.GetAwaiter().GetResult() : default;

    internal static bool Wait(Task task)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!task.IsCompleted && !_quitting)
        {
            var frame = new DispatcherFrame();
            task.ContinueWith(_ => Dispatcher.UIThread.Post(() => frame.Continue = false), TaskScheduler.Default);
            Dispatcher.UIThread.PushFrame(frame);
        }
        if (!task.IsCompleted)
        {
            AppLog.Warning("DialogService", "The app is quitting: a dialog was left unanswered.");
            return false;
        }
        task.GetAwaiter().GetResult(); // (a dialog's own failure surfaces here, as on Windows)
        return true;
    }
}
