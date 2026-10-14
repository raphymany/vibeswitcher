using VibeSwitcher.App.Dialogs;
using VibeSwitcher.Compat;
using VibeSwitcher.Helpers;
using VibeSwitcher.Platform;
using VibeSwitcher.Platform.Linux.Apps;
using VibeSwitcher.Platform.Linux.Audio;
using VibeSwitcher.Platform.Linux.Hotkeys;
using VibeSwitcher.Platform.Linux.OS;
using VibeSwitcher.Platform.MacOS.Apps;
using VibeSwitcher.Platform.MacOS.Audio;
using VibeSwitcher.Platform.MacOS.Hotkeys;
using VibeSwitcher.Platform.MacOS.OS;
using VibeSwitcher.Services;

namespace VibeSwitcher.App.Hosting;

// The system-specific services the app runs on (tests stand in for them).
internal interface IHostPlatform
{
    IPlatformAudioService CreateAudio(IAppLogger logger, ISessionErrorTracker errorTracker);
    IStartupService CreateStartup(IAppLogger logger, ISessionErrorTracker errorTracker);
    // Global shortcuts; never null (a grabber that registers nothing when the system offers none).
    // lookOnly: the self-test — nothing the running app set up is changed.
    Task<IGlobalKeyGrabber> CreateKeyGrabberAsync(IAppLogger logger, bool lookOnly = false);
    // Notification banners (null: none — banners are only logged).
    IDesktopNotifier? CreateNotifier(IAppLogger logger);
    IRunningProcessSource CreateProcesses();
    IWavPlayer? CreateWavPlayer();
    // Linux: whether a connected headset's receiver can be read, and the rule that allows it.
    HeadsetAccessHooks? HeadsetAccess();
    // Whether to watch for supported wireless headsets (HidSharp); tests leave real devices alone.
    bool WatchesHeadsets => true;
    // macOS: the app's own menu-bar item, which opens the Windows-styled menu. Null: Avalonia's tray
    // icon with the system's own menu (Linux — and macOS if the item can't be made). UI thread.
    IMenuBarItem? CreateMenuBarItem() => null;
    // Whether the desktop shows tray icons (null: can't tell). macOS: always (the menu bar).
    Task<bool?> IsTrayAvailableAsync() => Task.FromResult<bool?>(true);

    static IHostPlatform Current() =>
        OperatingSystem.IsMacOS() ? new MacHostPlatform()
        : OperatingSystem.IsLinux() ? new LinuxHostPlatform()
        : throw new PlatformNotSupportedException("VibeSwitcher for macOS and Linux runs on macOS and Linux (the Windows app is separate).");
}

internal sealed class LinuxHostPlatform : IHostPlatform
{
    public IPlatformAudioService CreateAudio(IAppLogger logger, ISessionErrorTracker errorTracker) => new LinuxAudioService(logger, errorTracker);
    public IStartupService CreateStartup(IAppLogger logger, ISessionErrorTracker errorTracker) => new LinuxStartupService(logger, errorTracker);

    // X11, the desktop's Global Shortcuts portal, or (GNOME before 48) nothing until the user agrees
    // to GNOME custom shortcuts — that choice is offered in Settings (a later step).
    public async Task<IGlobalKeyGrabber> CreateKeyGrabberAsync(IAppLogger logger, bool lookOnly = false)
    {
        var setup = await Task.Run(() => LinuxHotkeys.CreateAsync(logger, gnomeShortcutsAllowed: false, cleanUp: !lookOnly));
        logger.Info("Hotkeys", $"Global shortcuts: {setup.Backend}{(setup.Explanation is { } why ? $" ({why})" : "")}");
        return setup.Grabber;
    }

    public IDesktopNotifier? CreateNotifier(IAppLogger logger) => new LinuxNotifier(logger);
    public IRunningProcessSource CreateProcesses() => new LinuxProcesses();
    public IWavPlayer? CreateWavPlayer() => new PulseWavPlayer();
    public HeadsetAccessHooks? HeadsetAccess() => PlatformApps.HeadsetAccess();
    public Task<bool?> IsTrayAvailableAsync() => LinuxTrayCheck.IsTrayAvailableAsync();
}

internal sealed class MacHostPlatform : IHostPlatform
{
    public IPlatformAudioService CreateAudio(IAppLogger logger, ISessionErrorTracker errorTracker) => new MacAudioService(logger, errorTracker);
    public IStartupService CreateStartup(IAppLogger logger, ISessionErrorTracker errorTracker) => new MacStartupService(logger, errorTracker);

    public Task<IGlobalKeyGrabber> CreateKeyGrabberAsync(IAppLogger logger, bool lookOnly = false)
    {
        try { return Task.FromResult<IGlobalKeyGrabber>(new CarbonKeyGrabber()); }
        catch (Exception ex)
        {
            logger.Error("Hotkeys", $"Global shortcuts are unavailable: {ex.Message}");
            return Task.FromResult<IGlobalKeyGrabber>(new NoKeyGrabber("Global shortcuts couldn't be set up on this Mac."));
        }
    }

    // (The system's banners need a signed app: the app draws its own.)
    public IDesktopNotifier? CreateNotifier(IAppLogger logger) => new BannerNotifier();
    public IRunningProcessSource CreateProcesses() => new MacProcesses();
    public IWavPlayer? CreateWavPlayer() => new MacWavPlayer();
    public HeadsetAccessHooks? HeadsetAccess() => null;
    public IMenuBarItem? CreateMenuBarItem() => MacStatusItem.TryCreate() is { } item ? new MacMenuBarItem(item) : null;
}
