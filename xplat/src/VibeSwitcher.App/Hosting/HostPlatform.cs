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
    Task<IGlobalKeyGrabber> CreateKeyGrabberAsync(IAppLogger logger);
    // Notification banners; null where the app shows its own (macOS, next step) — banners are logged.
    IDesktopNotifier? CreateNotifier(IAppLogger logger);
    IRunningProcessSource CreateProcesses();
    IWavPlayer? CreateWavPlayer();
    // Linux: whether a connected headset's receiver can be read, and the rule that allows it.
    HeadsetAccessHooks? HeadsetAccess();
    // Whether to watch for supported wireless headsets (HidSharp); tests leave real devices alone.
    bool WatchesHeadsets => true;

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
    public async Task<IGlobalKeyGrabber> CreateKeyGrabberAsync(IAppLogger logger)
    {
        var setup = await Task.Run(() => LinuxHotkeys.CreateAsync(logger, gnomeShortcutsAllowed: false));
        logger.Info("Hotkeys", $"Global shortcuts: {setup.Backend}{(setup.Explanation is { } why ? $" ({why})" : "")}");
        return setup.Grabber;
    }

    public IDesktopNotifier? CreateNotifier(IAppLogger logger) => new LinuxNotifier(logger);
    public IRunningProcessSource CreateProcesses() => new LinuxProcesses();
    public IWavPlayer? CreateWavPlayer() => new PulseWavPlayer();
    public HeadsetAccessHooks? HeadsetAccess() => PlatformApps.HeadsetAccess();
}

internal sealed class MacHostPlatform : IHostPlatform
{
    public IPlatformAudioService CreateAudio(IAppLogger logger, ISessionErrorTracker errorTracker) => new MacAudioService(logger, errorTracker);
    public IStartupService CreateStartup(IAppLogger logger, ISessionErrorTracker errorTracker) => new MacStartupService(logger, errorTracker);

    public Task<IGlobalKeyGrabber> CreateKeyGrabberAsync(IAppLogger logger)
    {
        try { return Task.FromResult<IGlobalKeyGrabber>(new CarbonKeyGrabber()); }
        catch (Exception ex)
        {
            logger.Error("Hotkeys", $"Global shortcuts are unavailable: {ex.Message}");
            return Task.FromResult<IGlobalKeyGrabber>(new NoKeyGrabber("Global shortcuts couldn't be set up on this Mac."));
        }
    }

    public IDesktopNotifier? CreateNotifier(IAppLogger logger) => null;
    public IRunningProcessSource CreateProcesses() => new MacProcesses();
    public IWavPlayer? CreateWavPlayer() => new MacWavPlayer();
    public HeadsetAccessHooks? HeadsetAccess() => null;
}
