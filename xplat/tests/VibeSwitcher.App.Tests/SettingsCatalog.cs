using VibeSwitcher.App.Settings;
using VibeSwitcher.Models;
using VibeSwitcher.Platform;
using VibeSwitcher.Services;
using VibeSwitcher.Tests;

namespace VibeSwitcher.App.Tests;

// A Settings window over fake services with a typical set of profiles (for tests and screenshots).
internal static class SettingsCatalog
{
    public static readonly string[] Names =
    [
        "settings-profiles", "settings-filters", "settings-popup", "settings-empty",
        .. Pages.Keys, "settings-about", "settings-faq",
    ];

    // The Settings pages, by screenshot name → the sidebar category that shows them.
    public static Dictionary<string, string> Pages => new()
    {
        ["settings-startup"] = "startup",
        ["settings-appearance"] = "appear",
        ["settings-notifications"] = "notif",
        ["settings-devices"] = "devices",
        ["settings-tray"] = "tray",
        ["settings-shortcuts"] = "shortcuts",
        ["settings-mini"] = "compact",
        ["settings-logs"] = "logs",
        ["settings-install"] = "uninstall",
    };

    // An installed copy and data folder that look the same on every machine (none of it exists, so
    // the sizes read "—").
    public static void FixInstall(SettingsWindow window, bool mac)
    {
        window.Install = mac
            ? new InstallInfo(InstallKind.MacApp, "/Applications/VibeSwitcher.app", "2.0.1")
            : new InstallInfo(InstallKind.AppImage, "/home/alex/Applications/VibeSwitcher-x86_64.AppImage", "2.0.1");
        window.DataFolder = mac ? "/Users/alex/Library/Application Support/VibeSwitcher" : "/home/alex/.config/VibeSwitcher";
        window.Version = "2.0.1-preview";
        window.RefreshStorageInfo();
    }

    public static readonly AudioDeviceInfo Speakers = new("spk", "Speakers (Built-in Audio)", true);
    public static readonly AudioDeviceInfo Headset = new("hs", "Arctis Nova 7 Headset", true);
    public static readonly AudioDeviceInfo HeadsetMic = new("hs-mic", "Arctis Nova 7 Microphone", false);

    internal sealed class Setup
    {
        public FakeConfigService Config { get; } = new();
        public FakeAudioService Audio { get; } = new()
        {
            PlaybackResult = [Speakers, Headset],
            RecordingResult = [HeadsetMic],
        };
        public FakeSessionErrorTracker Errors { get; } = new();
        public FakeHotkeyService Hotkeys { get; } = new();
        public FakeStartupService Startup { get; } = new();
        public FakeAppLogger Logger { get; } = new();
        public List<string> AppliedThemes { get; } = [];

        public SettingsWindow Window() =>
            new(Config, Audio, Hotkeys, Startup, Logger, Errors, AppliedThemes.Add);
    }

    // The view model lists the devices in the background (as on Windows); waits until the cards
    // show them, so tests and screenshots see the same thing every run.
    public static void WaitForDevices(SettingsWindow window)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (window.ViewModel.Profiles.Any(p => p.PlaybackDevices.Count <= 1 && p.RecordingDevices.Count <= 1)
               && DateTime.UtcNow < until)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    public static Setup WithProfiles()
    {
        var setup = new Setup();
        var gaming = new DeviceProfile
        {
            Name = "Gaming", Mode = ProfileMode.Both, PlaybackDeviceId = Headset.Id, RecordingDeviceId = HeadsetMic.Id,
            IsPinned = true, SortOrder = 0,
            Hotkey = new HotkeyDefinition { VirtualKeyCode = 0x47, UseCtrl = true, UseAlt = true },
            Schedules = [new ScheduleEntry { Hour = 18, Minute = 0, Days = [DayOfWeek.Friday, DayOfWeek.Saturday] }],
            AppTriggers = ["/usr/bin/steam"],
        };
        var work = new DeviceProfile
        {
            Name = "Work", Mode = ProfileMode.Playback, PlaybackDeviceId = Speakers.Id, SortOrder = 1,
            Silent = true, Notes = "Desk speakers for meetings",
        };
        var calls = new DeviceProfile
        {
            Name = "Calls", Mode = ProfileMode.Recording, RecordingDeviceId = HeadsetMic.Id, SortOrder = 2,
            SoundOverride = true, SoundTone = "Chime", SoundVolume = 60,
        };
        setup.Config.Current.Profiles.AddRange([gaming, work, calls]);
        setup.Config.Current.ActiveProfileId = gaming.Id;
        return setup;
    }
}
