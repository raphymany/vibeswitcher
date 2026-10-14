using VibeSwitcher.App.Controls;
using VibeSwitcher.App.Dialogs;
using VibeSwitcher.Helpers;
using VibeSwitcher.Models;
using VibeSwitcher.Services;
using VibeSwitcher.Tests;
using VibeSwitcher.ViewModels;

namespace VibeSwitcher.App.Tests;

// Every dialog, with the content the Windows app shows in it (for tests and screenshots). Keep the
// shared ones in step with xplat/tools/WpfSnapshots/Program.cs, which draws the Windows originals.
internal static class DialogCatalog
{
    public static readonly string[] Names =
    [
        "alert", "alert-info", "confirm", "confirm-delete", "conflict-retry", "error", "uninstall", "mini-setup",
        "hotkey-capture", "profile-type", "device-aliases", "session-log", "mic-test", "supported-headsets",
        "clone-profile", "icon-gallery", "schedule-wizard", "switch-sound", "controls",
    ];

    public static VsDialog Create(string name) => name switch
    {
        "alert" => new AlertDialog("Device Not Found", "The headset in this profile isn't connected. Plug it in, or pick another device."),
        "alert-info" => new AlertDialog("Mini Mode", "Mini Mode keeps your profiles one click away in a small window.", AlertKind.Info),
        "confirm" => new ConfirmDialog("Replace Configuration?", "This replaces all your profiles and settings with the imported ones.", "Replace", subtitle: ""),
        "confirm-delete" => new ConfirmDeleteDialog("Gaming"),
        "conflict-retry" => new ConflictRetryDialog("Hotkey Already in Use", "Ctrl + Alt + G is used by another app. Try a different shortcut?"),
        "error" => new ErrorDialog(ErrorCode.ProfileSwitchFailed, "Profile Switch Failed",
            "Couldn't switch to \"Gaming\": the playback device isn't available.", "/tmp/vs.log", new FakeAppLogger(), new FakeSessionErrorTracker()),
        "uninstall" => new UninstallDialog(UninstallText.Mac),
        "mini-setup" => new MiniModeSetupDialog("Rows",
        [
            new MiniModeSetupDialog.ProfileChoice { Name = "Gaming", IsSelected = true },
            new MiniModeSetupDialog.ProfileChoice { Name = "Work", IsSelected = true },
            new MiniModeSetupDialog.ProfileChoice { Name = "Calls" },
        ]),
        "hotkey-capture" => new HotkeyCaptureDialog(new HotkeyDefinition { VirtualKeyCode = 0x47, UseCtrl = true, UseAlt = true }),
        "profile-type" => new ProfileTypeDialog(),
        "device-aliases" => new DeviceAliasesDialog(Aliases()),
        "session-log" => new SessionLogWindow(new FakeAppLogger(), new FixedErrors(), "/tmp/vs-no-log/error.log"),
        "mic-test" => new MicTestDialog("mic", "Arctis Nova 7 Microphone", new SteadyMic(0.035f), new FakeAppLogger()),
        "supported-headsets" => new SupportedHeadsetsDialog(),
        "clone-profile" => new CloneProfileDialog(CloneSource(), [SettingsCatalog.Speakers, SettingsCatalog.Headset],
            [SettingsCatalog.HeadsetMic], use12Hour: false, h => h),
        "icon-gallery" => new IconGalleryDialog(""),
        "schedule-wizard" => new ScheduleWizardDialog(
            new ScheduleEntry { Hour = 18, Minute = 0, Days = [DayOfWeek.Friday, DayOfWeek.Saturday], ReminderMinutes = 10 },
            use12Hour: false, isEditing: true),
        "switch-sound" => new SwitchSoundDialog(true, "Chime", null, 60, new FakeAppLogger(), "", showBanner: true, isEditing: true),
        "controls" => new DialogPreviewWindow(),
        _ => throw new ArgumentException(name),
    };

    public static IEnumerable<VsDialog> All() => Names.Select(Create);

    // A profile with something of everything to copy.
    public static DeviceProfile CloneSource() => new()
    {
        Name = "Gaming", Mode = ProfileMode.Both,
        PlaybackDeviceId = SettingsCatalog.Headset.Id, RecordingDeviceId = SettingsCatalog.HeadsetMic.Id,
        IsPinned = true, SoundOverride = true, SoundTone = "Chime", Notes = "Evening sessions",
        AppTriggers = ["/usr/bin/steam"],
        Schedules = [new ScheduleEntry { Hour = 18, Minute = 0, Days = [DayOfWeek.Friday, DayOfWeek.Saturday] }],
    };

    public static List<DeviceAliasItem> Aliases() =>
    [
        new("hs", "Arctis Nova 7 Headset", "Headset", isPlayback: true, isConnected: true, isDisabled: false, profileUsage: "Gaming"),
        new("spk", "Speakers (Built-in Audio)", "", isPlayback: true, isConnected: true, isDisabled: false, profileUsage: ""),
        new("hdmi", "LG HDR 4K (HDMI)", "", isPlayback: true, isConnected: false, isDisabled: false, profileUsage: ""),
        new("mic", "Arctis Nova 7 Microphone", "", isPlayback: false, isConnected: true, isDisabled: false, profileUsage: "Gaming, Calls"),
    ];

    // Two errors at fixed times (so every run draws the same picture).
    public sealed class FixedErrors : ISessionErrorTracker
    {
        public IReadOnlyList<SessionError> Errors { get; } =
        [
            new(new DateTime(2026, 10, 14, 9, 41, 7), ErrorCode.ProfileSwitchFailed, "Profile Switch Failed",
                "Couldn't switch to \"Gaming\": the playback device isn't available."),
            new(new DateTime(2026, 10, 14, 9, 42, 30), ErrorCode.HotkeyConflict, "Hotkey Conflict",
                "Could not register 'Ctrl+Alt+G' — another app is using it."),
        ];
        public bool HasErrors => true;
        public int Count => Errors.Count;
        public event EventHandler? ErrorAdded { add { } remove { } }
        public void Record(ErrorCode code, string title, string message) { }
    }

    // A microphone at a steady level until the test closes the dialog.
    public sealed class SteadyMic(float level) : IPlatformAudioService
    {
        public void RunMicLevelMonitor(string deviceId, CancellationToken ct, Action<float> onLevel)
        {
            onLevel(level);
            ct.WaitHandle.WaitOne();
        }
        public bool? GetDefaultDeviceMute(bool playback) => false;
        public bool SetDefaultDeviceMute(bool playback, bool mute) => true;

        public event Action? DevicesChanged { add { } remove { } }
        public event Action<string>? DevicePropertyChanged { add { } remove { } }
        public IReadOnlyList<AudioDeviceInfo> GetPlaybackDevices() => [];
        public IReadOnlyList<AudioDeviceInfo> GetRecordingDevices() => [];
        public Task<ProfileSwitchResult> ApplyProfileAsync(DeviceProfile profile) => throw new NotSupportedException();
        public Task TestSoundAsync(string deviceId) => Task.CompletedTask;
        public string? GetAudioEndpointPath(string audioDeviceId) => null;
        public void Dispose() { }
    }
}
