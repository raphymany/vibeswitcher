using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using VibeSwitcher.App.Hosting;
using VibeSwitcher.Compat;
using VibeSwitcher.Helpers;
using VibeSwitcher.Models;
using VibeSwitcher.Platform;
using VibeSwitcher.Services;
using VibeSwitcher.Tests;
using static VibeSwitcher.App.Tests.Ui;

namespace VibeSwitcher.App.Tests;

// The running app: start-up in the Windows order, hotkeys, commands from other launches, the tray /
// menu-bar menu, the switch path and quitting — on stand-in system services.
[Collection("Shared UI settings")] // (registers the app's log, sets the theme and logo mode)
public class AppHostTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("vs-host-").FullName;
    private readonly global::VibeSwitcher.Controls.LogoAnimator.AnimMode _savedLogo = global::VibeSwitcher.Controls.LogoAnimator.Mode;

    public void Dispose()
    {
        global::VibeSwitcher.Controls.LogoAnimator.Mode = _savedLogo;
        WavPlayback.Player = null;
        AppLog.Register(null);
        AppErrors.Register(null);
        if (Avalonia.Application.Current is { } app && Dispatcher.UIThread.CheckAccess()) app.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Default;
        _ = DeleteSoon(_dir);
    }

    // ── Stand-ins ──────────────────────────────────────────────────────────────────────────────────

    internal sealed class FakeAudio : IPlatformAudioService
    {
        public List<string> Applied { get; } = [];
        public ProfileSwitchResult Result { get; set; } = new(true, true, null, null);
        public Exception? Fail { get; set; }
        public List<(bool Playback, bool Mute)> MuteCalls { get; } = [];
        public bool Disposed { get; private set; }

        public event Action? DevicesChanged { add { } remove { } }
        public event Action<string>? DevicePropertyChanged { add { } remove { } }
        public List<AudioDeviceInfo> Playback { get; } = [];
        public List<AudioDeviceInfo> Recording { get; } = [];
        public IReadOnlyList<AudioDeviceInfo> GetPlaybackDevices() => Playback;
        public IReadOnlyList<AudioDeviceInfo> GetRecordingDevices() => Recording;
        public Task<ProfileSwitchResult> ApplyProfileAsync(DeviceProfile profile)
        {
            lock (Applied) Applied.Add(profile.Name);
            return Fail != null ? Task.FromException<ProfileSwitchResult>(Fail) : Task.FromResult(Result);
        }
        public Task TestSoundAsync(string deviceId) => Task.CompletedTask;
        public string? GetAudioEndpointPath(string audioDeviceId) => null;
        public void RunMicLevelMonitor(string deviceId, CancellationToken ct, Action<float> onLevel) { }
        public bool? GetDefaultDeviceMute(bool playback) => false;
        public bool SetDefaultDeviceMute(bool playback, bool mute) { MuteCalls.Add((playback, mute)); return true; }
        public void Dispose() => Disposed = true;
    }

    internal sealed class FakeGrabber : IGlobalKeyGrabber
    {
        public Dictionary<ushort, int> Keys { get; } = []; // id → virtual key
        public bool Disposed { get; private set; }
        public event Action<ushort>? Pressed;
        public HashSet<int> Taken { get; } = []; // keys another app has
        public GrabResult Register(ushort id, int modifiers, int virtualKey, out string? error)
        {
            error = null;
            if (Taken.Contains(virtualKey)) return GrabResult.InUse;
            Keys[id] = virtualKey;
            return GrabResult.Ok;
        }
        public void Unregister(ushort id) => Keys.Remove(id);
        public void Press(int virtualKey) => Pressed?.Invoke(Keys.First(k => k.Value == virtualKey).Key);
        public void Dispose() => Disposed = true;
    }

    internal sealed class FakeNotifier : IDesktopNotifier
    {
        public List<(string Title, string Message)> Shown { get; } = [];
        public Task<bool> ShowAsync(string title, string message)
        {
            lock (Shown) Shown.Add((title, message));
            return Task.FromResult(true);
        }
    }

    internal sealed class FakePlatform : IHostPlatform
    {
        public FakeAudio Audio { get; } = new();
        public FakeGrabber Grabber { get; } = new();
        public FakeNotifier Notifier { get; } = new();
        public bool AudioFails { get; set; }
        public Dialogs.HeadsetAccessHooks? Headsets { get; set; }
        public IPlatformAudioService CreateAudio(IAppLogger logger, ISessionErrorTracker errorTracker) =>
            AudioFails ? throw new InvalidOperationException("no sound system") : Audio;
        public IStartupService CreateStartup(IAppLogger logger, ISessionErrorTracker errorTracker) => new FakeStartupService();
        // How shortcuts work for a GNOME-allowed choice (default: the system's own, with Grabber).
        public Func<bool, KeyGrabberSetup>? Shortcuts { get; set; }
        public List<bool> ShortcutsAsked { get; } = [];
        public Task? ShortcutsDelay { get; set; } // (setting the way up takes this long)
        public async Task<KeyGrabberSetup> CreateKeyGrabberAsync(IAppLogger logger, bool gnomeShortcutsAllowed = false, bool lookOnly = false)
        {
            ShortcutsAsked.Add(gnomeShortcutsAllowed);
            if (ShortcutsDelay != null) await ShortcutsDelay;
            return Shortcuts?.Invoke(gnomeShortcutsAllowed) ?? new KeyGrabberSetup(Grabber, ShortcutsWay.Native, null);
        }
        public bool NotifierFails { get; set; }
        public IDesktopNotifier? CreateNotifier(IAppLogger logger) => NotifierFails ? throw new DllNotFoundException("libX11") : Notifier;
        public IRunningProcessSource CreateProcesses() => new NoProcesses();
        public IWavPlayer? CreateWavPlayer() => null;
        public Dialogs.HeadsetAccessHooks? HeadsetAccess() => Headsets;
        public bool WatchesHeadsets => false;
        public bool? Tray { get; set; } = true;
        public Task<bool?> IsTrayAvailableAsync() => Task.FromResult(Tray);
    }

    private sealed class NoProcesses : IRunningProcessSource
    {
        public IReadOnlyList<RunningProcess>? Snapshot() => [];
    }

    private static readonly HotkeyDefinition GamingKey = new() { VirtualKeyCode = 0x47, UseCtrl = true, UseAlt = true };
    private static readonly HotkeyDefinition SettingsKey = new() { VirtualKeyCode = 0x56, UseCtrl = true, UseAlt = true };
    private static readonly HotkeyDefinition MuteKey = new() { VirtualKeyCode = 0x4D, UseCtrl = true, UseShift = true };

    // A config with Gaming (pinned, with a shortcut) and Work, Gaming active.
    private void WriteConfig(Action<AppConfig>? change = null)
    {
        var config = new ConfigService(new FakeAppLogger(), new FakeSessionErrorTracker(), _dir);
        config.Load();
        var gaming = new DeviceProfile { Name = "Gaming", IsPinned = true, SortOrder = 0, Hotkey = GamingKey };
        var work = new DeviceProfile { Name = "Work", SortOrder = 1 };
        config.Current.Profiles.AddRange([gaming, work]);
        config.Current.ActiveProfileId = gaming.Id;
        config.Current.StartMinimized = false;
        config.Current.SettingsHotkeyEnabled = true;
        config.Current.SettingsHotkey = SettingsKey;
        config.Current.MuteMicHotkeyEnabled = true;
        config.Current.MuteMicHotkey = MuteKey;
        change?.Invoke(config.Current);
        config.SaveImmediate();
    }

    private async Task<(AppHost Host, FakePlatform Platform)> Start(AppCommand? launch = null, Action<AppConfig>? change = null,
        FakePlatform? platform = null)
    {
        WriteConfig(change);
        platform ??= new FakePlatform();
        var host = new AppHost(platform, new AppPaths(_dir), launch ?? AppCommand.Open);
        await host.StartAsync(showSplash: false);
        return (host, platform);
    }

    private static void Close(AppHost host)
    {
        host.Windows.Window?.Close();
        host.Dispose();
    }

    private static List<string?> MenuHeaders(TrayService tray) =>
        tray.Menu.Items.Select(i => i is NativeMenuItemSeparator ? "—" : (i as NativeMenuItem)?.Header).ToList();

    // ── Start-up ───────────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public async Task StartUp_RestoresTheProfile_ShowsTheIcon_AndOpensSettings()
    {
        var (host, platform) = await Start();
        await Until(() => platform.Audio.Applied.Count == 1);
        Assert.Equal(new[] { "Gaming" }, platform.Audio.Applied);
        Assert.True(host.Tray.IsShown);
        Assert.True(host.Windows.Window!.IsVisible);
        Assert.True(host.Ready.IsCompleted);
        Assert.True(File.Exists(host.LogPath));

        // The menu, as on Windows (Quit for Exit), with the active profile checked.
        Assert.Equal(new[] { "VibeSwitcher", "—", "★ Gaming", "—", "Work", "—", "About", "Help & FAQ", "Settings", "Mini Mode", "Open Sound Settings", "—", "Quit" },
            MenuHeaders(host.Tray));
        var items = host.Tray.Menu.Items.OfType<NativeMenuItem>().Where(i => i is not NativeMenuItemSeparator).ToList();
        Assert.True(items.Single(i => i.Header == "★ Gaming").IsChecked);
        Assert.False(items.Single(i => i.Header == "Work").IsChecked);
        Assert.Equal($"VibeSwitcher — Gaming\nGaming: {GamingKey.ToDisplayString()}", host.Tray.Icon.ToolTipText);
        Assert.NotNull(host.Tray.Icon.Icon);
        Close(host);
    }

    [AvaloniaFact]
    public async Task StartingMinimized_OrForACommand_LeavesSettingsClosed()
    {
        var (host, platform) = await Start(change: c => c.StartMinimized = true);
        Assert.Null(host.Windows.Window);
        Assert.True(host.Tray.IsShown);
        Close(host);

        // Started by a desktop shortcut: does just that.
        (host, platform) = await Start(new AppCommand(AppCommandKind.Switch, "work"));
        await Until(() => platform.Audio.Applied.Contains("Work"));
        Assert.Equal(new[] { "Work" }, platform.Audio.Applied); // (instead of restoring Gaming)
        Assert.Null(host.Windows.Window);
        Close(host);
    }

    [AvaloniaFact]
    public async Task NoBanners_DontStopTheApp()
    {
        // (The D-Bus library once threw while looking for the session bus: start-up stopped.)
        var platform = new FakePlatform { NotifierFails = true };
        var (host, _) = await Start(change: c => c.StartMinimized = true, platform: platform);
        Assert.True(host.Ready.IsCompletedSuccessfully);
        Assert.True(host.Tray.IsShown);
        Close(host);
    }

    [AvaloniaFact]
    public async Task AMissingActiveProfile_IsForgotten()
    {
        var (host, platform) = await Start(change: c => c.ActiveProfileId = Guid.NewGuid());
        Assert.Null(host.Config.Current.ActiveProfileId);
        Assert.Empty(platform.Audio.Applied);
        Close(host);
    }

    // ── GNOME keyboard shortcuts: the user's choice (Linux) ────────────────────────────────────────

    private static FakePlatform GnomePlatform(FakeGrabber gnome) => new()
    {
        Shortcuts = allowed => allowed
            ? new KeyGrabberSetup(gnome, ShortcutsWay.GnomeShortcuts, null)
            : new KeyGrabberSetup(new Platform.Linux.Hotkeys.NoKeyGrabber("not allowed"), ShortcutsWay.GnomeNeedsConsent, "GNOME needs your OK."),
    };

    [AvaloniaFact]
    public async Task GnomeShortcuts_AreOffUntilAllowed_ThenMoveOverAtOnce_AndStopTakesThemAway()
    {
        var gnome = new FakeGrabber();
        var (host, platform) = await Start(change: c => c.ActiveProfileId = c.Profiles[1].Id, platform: GnomePlatform(gnome));
        await Until(() => platform.Audio.Applied.Count == 1); // (Work restored)
        Assert.Equal([false], platform.ShortcutsAsked);
        var window = host.Windows.Window!;
        Assert.True(Named<Border>(window, "ShortcutsWayCard").IsVisible);
        Assert.StartsWith("GNOME needs your OK.", Named<TextBlock>(window, "ShortcutsWayNote").Text);
        var button = Named<Button>(window, "ShortcutsWayBtn");
        Assert.Equal("Add my shortcuts to GNOME", button.Content);

        // Allowed: remembered, and every shortcut is registered the new way — no restart.
        Ui.Click(button);
        await Until(() => Equals(button.Content, "Remove my shortcuts from GNOME"));
        Assert.Equal([false, true], platform.ShortcutsAsked);
        Assert.True(XPlatSettings.Load(_dir).GnomeShortcutsAllowed);
        Assert.Same(gnome, host.Grabber.Inner);
        Assert.Contains(GamingKey.VirtualKeyCode, gnome.Keys.Values);
        Assert.Contains(SettingsKey.VirtualKeyCode, gnome.Keys.Values);
        var before = platform.Audio.Applied.Count;
        gnome.Press(GamingKey.VirtualKeyCode); // (presses arrive through the new way)
        await Until(() => platform.Audio.Applied.Count > before);
        Assert.Equal("Gaming", platform.Audio.Applied[^1]);

        // Stopped: the shortcuts leave GNOME's way, and that way is closed.
        Ui.Click(button);
        await Until(() => Equals(button.Content, "Add my shortcuts to GNOME"));
        Assert.False(XPlatSettings.Load(_dir).GnomeShortcutsAllowed);
        Assert.Empty(gnome.Keys);
        await Until(() => gnome.Disposed);
        Assert.True(gnome.Disposed);
        Close(host);
    }

    [AvaloniaFact]
    public async Task TheGnomeChoice_IsRemembered_AndNothingIsExplained_WhereShortcutsJustWork()
    {
        new XPlatSettings(GnomeShortcutsAllowed: true).Save(_dir);
        var gnome = new FakeGrabber();
        var (host, platform) = await Start(platform: GnomePlatform(gnome));
        Assert.Equal([true], platform.ShortcutsAsked);
        Assert.Contains(GamingKey.VirtualKeyCode, gnome.Keys.Values);
        Close(host);

        (host, _) = await Start(); // (the system's own shortcuts: macOS, X11)
        Assert.Null(host.ShortcutsInfo());
        Assert.False(Named<Border>(host.Windows.Window!, "ShortcutsWayCard").IsVisible);
        Close(host);
    }

    [AvaloniaFact]
    public async Task WhenTheNewWayCantBeSetUp_TheShortcutsKeepWorking()
    {
        var (host, platform) = await Start();
        platform.Shortcuts = _ => throw new InvalidOperationException("no session bus");
        await host.SetGnomeShortcutsAsync(true);
        Assert.Same(platform.Grabber, host.Grabber.Inner);
        Assert.Contains(GamingKey.VirtualKeyCode, platform.Grabber.Keys.Values);
        Assert.False(platform.Grabber.Disposed);
        Assert.False(XPlatSettings.Load(_dir).GnomeShortcutsAllowed);
        Close(host);
    }

    [AvaloniaFact]
    public async Task AnOkGnomeCouldntUse_IsntKept_AndTheReasonIsShown()
    {
        var platform = new FakePlatform
        {
            Shortcuts = allowed => allowed
                ? new KeyGrabberSetup(new Platform.Linux.Hotkeys.NoKeyGrabber("can't start"), ShortcutsWay.Off, "GNOME couldn't start this copy from where it's running.")
                : new KeyGrabberSetup(new Platform.Linux.Hotkeys.NoKeyGrabber("not allowed"), ShortcutsWay.GnomeNeedsConsent, "GNOME needs your OK."),
        };
        var (host, _) = await Start(platform: platform);
        await host.SetGnomeShortcutsAsync(true);
        Assert.Equal("GNOME couldn't start this copy from where it's running.", Named<TextBlock>(host.Windows.Window!, "ShortcutsWayNote").Text);
        Assert.False(Named<Button>(host.Windows.Window!, "ShortcutsWayBtn").IsVisible);
        Assert.False(XPlatSettings.Load(_dir).GnomeShortcutsAllowed); // (asked again next time)
        Close(host);
    }

    [AvaloniaFact]
    public async Task AChoiceThatCantBeSaved_IsSaidSo_AndAStoppedOneStaysStopped()
    {
        var gnome = new FakeGrabber();
        var (host, platform) = await Start(platform: GnomePlatform(gnome));
        Directory.CreateDirectory(Path.Combine(_dir, XPlatSettings.FileName + ".tmp")); // (the save's new file can't be written)
        var window = host.Windows.Window!;
        await host.SetGnomeShortcutsAsync(true);
        Assert.Same(gnome, host.Grabber.Inner); // (it took effect…)
        Assert.False(XPlatSettings.Load(_dir).GnomeShortcutsAllowed); // (…but isn't remembered)
        await Until(() => window.OwnedWindows.Count == 1);
        Assert.Contains(Texts(window.OwnedWindows[0]), t => t?.Contains("ask again next time") == true);
        window.OwnedWindows[0].Close();

        // Stopped while an earlier OK is on file: the file goes (no file means "not allowed").
        Directory.Delete(Path.Combine(_dir, XPlatSettings.FileName + ".tmp"));
        new XPlatSettings(GnomeShortcutsAllowed: true).Save(_dir);
        Directory.CreateDirectory(Path.Combine(_dir, XPlatSettings.FileName + ".tmp"));
        await host.SetGnomeShortcutsAsync(false);
        Assert.False(File.Exists(Path.Combine(_dir, XPlatSettings.FileName)));
        Assert.Empty(window.OwnedWindows);
        Close(host);
    }

    [AvaloniaFact]
    public async Task OneChangeAtATime_AndNotDuringAnUninstall()
    {
        var gnome = new FakeGrabber();
        var (host, platform) = await Start(platform: GnomePlatform(gnome));
        var slow = new TaskCompletionSource();
        platform.ShortcutsDelay = slow.Task;
        var first = host.SetGnomeShortcutsAsync(true);
        await host.SetGnomeShortcutsAsync(false); // (ignored while the first runs)
        await host.UninstallAsync(new InstallInfo(InstallKind.AppImage, "/x.AppImage", "2.0.1"), deleteData: false); // (likewise)
        Assert.False(host.Uninstalling);
        slow.SetResult();
        await first;
        Assert.Equal([false, true], platform.ShortcutsAsked);
        Assert.Same(gnome, host.Grabber.Inner);
        Close(host);
    }

    // ── Hotkeys ────────────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public async Task Hotkeys_SwitchOpenSettingsAndMute_OncePerPress()
    {
        var (host, platform) = await Start(change: c => { c.ActiveProfileId = c.Profiles[1].Id; c.StartMinimized = true; });
        await Until(() => platform.Audio.Applied.Count == 1); // (Work restored)
        // Raised on another thread, as the systems do.
        await Task.Run(() => platform.Grabber.Press(GamingKey.VirtualKeyCode));
        await Until(() => platform.Audio.Applied.Count == 2);
        Assert.Equal("Gaming", platform.Audio.Applied[^1]);
        Assert.Equal(host.Config.Current.Profiles[0].Id, host.Config.Current.ActiveProfileId);
        // A held key repeats: within 500 ms it doesn't count again.
        platform.Grabber.Press(GamingKey.VirtualKeyCode);
        await Task.Delay(100);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, platform.Audio.Applied.Count);

        platform.Grabber.Press(SettingsKey.VirtualKeyCode);
        await Until(() => host.Windows.Window?.IsVisible == true);
        Assert.True(host.Windows.Window!.IsVisible);

        platform.Grabber.Press(MuteKey.VirtualKeyCode);
        await Until(() => platform.Audio.MuteCalls.Count == 1);
        Assert.Equal((false, true), platform.Audio.MuteCalls[0]); // the default microphone, muted
        await Until(() => platform.Notifier.Shown.Contains(("VibeSwitcher", "Microphone muted")));
        Assert.Contains(("VibeSwitcher", "Microphone muted"), platform.Notifier.Shown);
        Close(host);
    }

    // ── Other launches ─────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public async Task CommandsFromOtherLaunches_AreRun_OnceStartedUp()
    {
        WriteConfig(c => { c.StartMinimized = true; c.CompactIntroShown = true; });
        var platform = new FakePlatform();
        var host = new AppHost(platform, new AppPaths(_dir), AppCommand.Open);
        // Sent while starting: answered once the app is ready.
        var early = Task.Run(() => host.HandleRemoteAsync(new AppCommand(AppCommandKind.Switch, "WORK")));
        await host.StartAsync(showSplash: false);
        await Until(() => early.IsCompleted);
        Assert.Equal("Switching to Work.", await early);
        await Until(() => platform.Audio.Applied.Contains("Work"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => host.HandleRemoteAsync(new AppCommand(AppCommandKind.Switch, "Nope")));
        Assert.Equal("ok", await host.HandleRemoteAsync(new AppCommand(AppCommandKind.Mute, MuteScope.Speakers.ToString())));
        Assert.Equal((true, true), platform.Audio.MuteCalls.Single());
        Assert.Equal("ok", await host.HandleRemoteAsync(AppCommand.Open));
        Assert.True(host.Windows.Window!.IsVisible);
        Assert.Equal("ok", await host.HandleRemoteAsync(new AppCommand(AppCommandKind.MiniMode)));
        Assert.True(host.Windows.IsMiniModeActive());
        host.Windows.Window.ExitCompact();
        Close(host);
    }

    [Fact]
    public async Task ASecondLaunch_HandsItsCommandToTheFirst()
    {
        // (macOS's temp folder is too deep for a command socket's path: a short folder, as the app's own is.)
        var dir = new AppPaths(_dir).SocketPathFits ? _dir : Directory.CreateDirectory($"/tmp/vs-{Guid.NewGuid():N}"[..16]).FullName;
        var paths = new AppPaths(dir);
        using var first = new SingleInstance(paths);
        string reply = "ok";
        var output = new StringWriter();
        var error = new StringWriter();
        Assert.Null(Cli.AcquireOrForward(first, paths, AppCommand.Open, _ => Task.FromResult(reply), output, error));

        using var second = new SingleInstance(paths);
        Assert.Equal(0, Cli.AcquireOrForward(second, paths, AppCommand.Open, _ => Task.FromResult(""), output, error));
        Assert.Equal("", output.ToString()); // ("ok" isn't printed)
        reply = "Switching to Gaming.";
        Assert.Equal(0, Cli.AcquireOrForward(second, paths, new AppCommand(AppCommandKind.Switch, "Gaming"), _ => Task.FromResult(""), output, error));
        Assert.Contains("Switching to Gaming.", output.ToString());
        reply = "error: No profile named 'X'.";
        Assert.Equal(1, Cli.AcquireOrForward(second, paths, new AppCommand(AppCommandKind.Switch, "X"), _ => Task.FromResult(""), output, error));
        Assert.Contains("No profile named 'X'.", error.ToString());
        if (dir != _dir) await DeleteSoon(dir);
    }

    // ── Tray / menu bar ────────────────────────────────────────────────────────────────────────────

    private static TrayService Tray(FakeConfigService config, FakeNotifier notifier, List<string> done, Func<bool>? mini = null) =>
        new(config, new FakeAppLogger(), new FakeSessionErrorTracker(), notifier, new TrayActions(
            OpenSettings: () => done.Add("settings"), OpenSettingsExpanded: () => done.Add("settings+"),
            OpenAbout: () => done.Add("about"), OpenFaq: () => done.Add("faq"), ToggleMiniMode: () => done.Add("mini"),
            IsMiniModeActive: mini ?? (() => false), Quit: () => done.Add("quit")));

    [AvaloniaFact]
    public void TheMenu_FollowsSettingsTray_AndItsItemsDoWhatTheySay()
    {
        var config = new FakeConfigService();
        var a = new DeviceProfile { Name = "A", SortOrder = 0 };
        var b = new DeviceProfile { Name = "B", SortOrder = 1, IsPinned = true };
        config.Current.Profiles.AddRange([a, b]);
        config.Current.TrayShowAbout = false;
        config.Current.TrayShowSoundSettings = false;
        var done = new List<string>();
        var miniOn = false;
        using var tray = Tray(config, new FakeNotifier(), done, () => miniOn);
        var switched = new List<string>();
        tray.SwitchRequested = p => switched.Add(p.Name);
        tray.RebuildMenu();

        Assert.Equal(new[] { "VibeSwitcher", "—", "★ B", "—", "A", "—", "Help & FAQ", "Settings", "Mini Mode", "—", "Quit" }, MenuHeaders(tray));
        NativeMenuItem Item(string header) => tray.Menu.Items.OfType<NativeMenuItem>().Where(i => i is not NativeMenuItemSeparator).Single(i => i.Header == header);
        foreach (var header in new[] { "VibeSwitcher", "Help & FAQ", "Settings", "Mini Mode", "Quit", "A" }) Click(Item(header));
        Assert.Equal(new[] { "settings", "faq", "settings+", "mini", "quit" }, done);
        Assert.Equal(new[] { "A" }, switched);

        // The Mini Mode item says what it will do.
        miniOn = true;
        tray.RefreshMiniMenuItem();
        Assert.NotNull(tray.Menu.Items.OfType<NativeMenuItem>().Where(i => i is not NativeMenuItemSeparator).SingleOrDefault(i => i.Header == "Exit Mini Mode"));

        // A switch only moves the check.
        tray.SetActiveProfile(a.Id);
        Assert.True(Item("A").IsChecked);
        Assert.False(Item("★ B").IsChecked);
    }

    private static void Click(NativeMenuItem item) =>
        typeof(NativeMenuItem).GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
            .First(m => m.GetParameters().Length == 0 && (m.Name.EndsWith("RaiseClicked") || m.Name.EndsWith("RaiseClick")))
            .Invoke(item, null);

    [AvaloniaFact]
    public void LeftClick_CyclesProfilesInMenuOrder()
    {
        var config = new FakeConfigService();
        var a = new DeviceProfile { Name = "A", SortOrder = 0 };
        var b = new DeviceProfile { Name = "B", SortOrder = 1, IsPinned = true };
        var c = new DeviceProfile { Name = "C", SortOrder = 2 };
        config.Current.Profiles.AddRange([a, b, c]);
        config.Current.ActiveProfileId = b.Id;
        using var tray = Tray(config, new FakeNotifier(), []);
        var switched = new List<string>();
        tray.SwitchRequested = p => switched.Add(p.Name);
        tray.CycleNextProfile();
        config.Current.ActiveProfileId = c.Id;
        tray.CycleNextProfile();
        config.Current.ActiveProfileId = Guid.NewGuid(); // (unknown: from the first)
        tray.CycleNextProfile();
        Assert.Equal(new[] { "A", "B", "B" }, switched); // pinned B first: B → A → C → B
    }

    [AvaloniaFact]
    public void Banners_WaitForTheIcon_ThenGoToTheSystem()
    {
        var notifier = new FakeNotifier();
        using var tray = Tray(new FakeConfigService(), notifier, []);
        tray.ShowBalloon("Hotkey Conflict", "In use");
        Assert.Empty(notifier.Shown);
        tray.ShowIcon();
        Assert.Equal(new[] { ("Hotkey Conflict", "In use") }, notifier.Shown);
        tray.ShowBalloon("VibeSwitcher", "Switched to A");
        Assert.Equal(2, notifier.Shown.Count);
        Assert.True(tray.Icon.IsVisible);
    }

    // ── The switch path ────────────────────────────────────────────────────────────────────────────

    private sealed class RecordingTray : ITrayService
    {
        public List<string> Log { get; } = [];
        public void UpdateIcon(DeviceProfile? activeProfile) => Log.Add($"icon {activeProfile?.Name}");
        public void SetActiveProfile(Guid activeProfileId) => Log.Add("active");
        public void SetSwitchingTooltip(string profileName) => Log.Add($"switching {profileName}");
        public void ShowBalloon(string title, string message, bool sound = true) => Log.Add($"banner {title}: {message}");
        public void RebuildMenu() { }
        public void ClearIconCache() { }
    }

    private sealed class NoSound : ISwitchSoundService
    {
        public Task PlayAsync(DeviceProfile profile) => Task.CompletedTask;
        public Task TestAsync(string tone, string? customPath, int volume) => Task.CompletedTask;
    }

    [AvaloniaFact]
    public async Task TheSwitchPath_SavesShowsAndExplains()
    {
        var config = new FakeConfigService();
        var gaming = new DeviceProfile { Name = "Gaming" };
        config.Current.Profiles.Add(gaming);
        var audio = new FakeAudio();
        var tray = new RecordingTray();
        var errors = new FakeSessionErrorTracker();
        var shownErrors = new List<string>();
        using var orchestrator = new ProfileSwitchOrchestrator(config, audio, tray, new NoSound(), new FakeAppLogger(), errors, shownErrors.Add);

        await orchestrator.SwitchAsync(gaming);
        Assert.Equal(gaming.Id, config.Current.ActiveProfileId);
        Assert.Equal(new[] { "switching Gaming", "icon Gaming", "active", "banner VibeSwitcher: Switched to Gaming" }, tray.Log);

        // Silent: no banner. A missing device: always said.
        tray.Log.Clear();
        gaming.Silent = true;
        await orchestrator.SwitchAsync(gaming);
        Assert.DoesNotContain(tray.Log, l => l.StartsWith("banner"));
        audio.Result = new ProfileSwitchResult(false, true, "hs", null);
        await orchestrator.SwitchAsync(gaming);
        Assert.Contains("banner Device Unavailable: Playback device for 'Gaming' is disconnected.", tray.Log);

        // A failure: the dialog for a switch the user asked for; a banner for a scheduled one.
        audio.Fail = new InvalidOperationException("the device vanished");
        await orchestrator.SwitchAsync(gaming);
        Assert.Equal(new[] { "Could not switch to 'Gaming': the device vanished" }, shownErrors);
        Assert.Contains(errors.Errors, e => e.Code == ErrorCode.ProfileSwitchFailed);
        await orchestrator.SwitchAsync(gaming, scheduleSilent: true);
        Assert.Single(shownErrors);
        Assert.Contains("banner Profile Switch Failed: Could not switch to 'Gaming': the device vanished", tray.Log);
    }

    // ── Wake and quit ──────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public async Task WakingUp_ReappliesTheProfile_AndQuittingSavesAndLetsGo()
    {
        var (host, platform) = await Start(change: c => c.StartMinimized = true);
        await Until(() => platform.Audio.Applied.Count == 1);
        host.OnResumed();
        await Until(() => platform.Audio.Applied.Count == 2);
        Assert.Equal(new[] { "Gaming", "Gaming" }, platform.Audio.Applied);

        host.Config.Current.ShowNotifications = false; // (changed, not yet written)
        host.Dispose();
        Assert.True(platform.Grabber.Disposed);
        Assert.Empty(platform.Grabber.Keys); // every shortcut let go
        Assert.True(platform.Audio.Disposed);
        Assert.False(host.Tray.Icon.IsVisible);
        var saved = new ConfigService(new FakeAppLogger(), new FakeSessionErrorTracker(), _dir);
        saved.Load();
        Assert.False(saved.Current.ShowNotifications);
        // A command arriving now is refused, not run on what's been let go.
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.HandleRemoteAsync(AppCommand.Open));
    }

    // ── Start-up edge cases ────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public async Task ACommandSentDuringTheSplash_RunsAfterIt_AndStays()
    {
        WriteConfig(c => c.StartMinimized = true);
        var platform = new FakePlatform();
        var host = new AppHost(platform, new AppPaths(_dir), AppCommand.Open);
        await host.StartAsync(showSplash: true);
        Assert.NotNull(host.Splash);
        Assert.False(host.Tray.IsShown);
        var open = Task.Run(() => host.HandleRemoteAsync(AppCommand.Open));
        await Task.Delay(200);
        Dispatcher.UIThread.RunJobs();
        Assert.False(open.IsCompleted); // (waits for the splash)

        host.Splash!.Close(); // closed early: what comes after the splash still happens
        await Until(() => open.IsCompleted);
        Assert.Equal("ok", await open);
        Assert.True(host.Tray.IsShown);
        Assert.True(host.Windows.Window!.IsVisible);
        Close(host);
    }

    [AvaloniaFact]
    public async Task ALaunchFor_AProfileThatDoesntExist_SaysSo()
    {
        var (host, platform) = await Start(new AppCommand(AppCommandKind.Switch, "Nope"), c => c.StartMinimized = true);
        await Until(() => platform.Notifier.Shown.Count > 0);
        Assert.Contains(("VibeSwitcher", "No profile named 'Nope'."), platform.Notifier.Shown);
        await Until(() => platform.Audio.Applied.Count == 1);
        Assert.Equal(new[] { "Gaming" }, platform.Audio.Applied); // (the restore still happens)
        Close(host);
    }

    [AvaloniaFact]
    public async Task ShortcutsAnotherAppHas_AreReported_OnceTheIconIsUp()
    {
        var platform = new FakePlatform();
        platform.Grabber.Taken.Add(GamingKey.VirtualKeyCode);
        var (host, _) = await Start(change: c => c.StartMinimized = true, platform: platform);
        Assert.Contains(platform.Notifier.Shown, b => b.Title == "Hotkey Conflict");
        Assert.Contains(host.Errors.Errors, e => e.Code == ErrorCode.HotkeyConflict);
        Close(host);
    }

    [AvaloniaFact]
    public async Task MuteCommands_TakeTheirScopeInAnySpelling()
    {
        var (host, platform) = await Start(change: c => c.StartMinimized = true);
        host.Run(new AppCommand(AppCommandKind.Mute, "speakers"));
        host.Run(new AppCommand(AppCommandKind.Mute, "ALL"));
        Assert.Equal(new[] { (true, true), (false, true), (true, true) }, platform.Audio.MuteCalls); // speakers; then mic + speakers
        Close(host);
    }

    [AvaloniaFact]
    public async Task AStartUpThatFails_FailsTheWaitingCommandsToo()
    {
        WriteConfig();
        var host = new AppHost(new FakePlatform { AudioFails = true }, new AppPaths(_dir), AppCommand.Open);
        var waiting = Task.Run(() => host.HandleRemoteAsync(AppCommand.Open));
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync(showSplash: false));
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => waiting);
        Assert.Equal("VibeSwitcher couldn't start.", refused.Message);
        host.Dispose(); // (after a partial start)
    }

    [AvaloniaFact]
    public async Task HeadsetAccess_IsOfferedOnce_WhenAnAutoSwitchHeadsetCantBeRead()
    {
        var checks = 0;
        var platform = new FakePlatform
        {
            Headsets = new Dialogs.HeadsetAccessHooks(() => { Interlocked.Increment(ref checks); return VibeSwitcher.Platform.Linux.Hid.HeadsetAccess.NeedsRule; },
                () => VibeSwitcher.Platform.Linux.Hid.RuleInstallResult.Cancelled),
        };
        var (host, _) = await Start(change: c => { c.StartMinimized = true; c.Profiles[0].TriggerOnConnect = true; }, platform: platform);
        await Until(() => File.Exists(host.HeadsetOfferMarker));
        Assert.True(File.Exists(host.HeadsetOfferMarker));
        Assert.Equal(1, checks);
        Close(host);

        (host, _) = await Start(change: c => { c.StartMinimized = true; c.Profiles[0].TriggerOnConnect = true; }, platform: platform);
        await Task.Delay(100);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, checks); // offered already: not again
        Close(host);
    }

    [AvaloniaFact]
    public async Task OpeningTheAppAgain_OnMacOS_ShowsSettings_AndKeepsItShown()
    {
        var (host, _) = await Start(change: c => c.StartMinimized = true);
        Assert.Null(host.Windows.Window);
        host.Reopen();
        Assert.True(host.Windows.Window!.IsVisible);
        host.Reopen(); // (never hides it, unlike the shortcut)
        Assert.True(host.Windows.Window.IsVisible);
        Close(host);
    }
}