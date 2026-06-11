using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using VibeSwitcher.Helpers;
using VibeSwitcher.Models;
using VibeSwitcher.Services;
using VibeSwitcher.Tray;
using VibeSwitcher.Views;

namespace VibeSwitcher;

public class ProfileSwitchOrchestrator : IDisposable
{
    private readonly IConfigService _configService;
    private readonly IAudioService _audioService;
    private readonly TrayService _trayService;
    private readonly ISwitchSoundService _soundService;
    private readonly Dispatcher _dispatcher;
    private readonly IAppLogger _logger;
    private readonly ISessionErrorTracker _errorTracker;
    private readonly SemaphoreSlim _switchLock = new(1, 1);

    public event Action? ProfileSwitched;

    public ProfileSwitchOrchestrator(
        IConfigService configService,
        IAudioService audioService,
        TrayService trayService,
        ISwitchSoundService soundService,
        Dispatcher dispatcher,
        IAppLogger logger,
        ISessionErrorTracker errorTracker)
    {
        _configService = configService;
        _audioService = audioService;
        _trayService = trayService;
        _soundService = soundService;
        _dispatcher = dispatcher;
        _logger = logger;
        _errorTracker = errorTracker;
    }

    public void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume) return;
        var activeProfile = _configService.Current.Profiles
            .FirstOrDefault(p => p.Id == _configService.Current.ActiveProfileId);
        if (activeProfile != null)
            SwitchToProfile(activeProfile);
        // If a switch is in progress when the PC resumes, the resume request is dropped, but the
        // lock is now held only for the audio apply itself (UI feedback/error dialog run outside
        // it), so the window is tiny.
    }

    // async void is intentional: called as fire-and-forget from WndProc, PowerModeChanged, and tray click.
    // The try/catch ensures exceptions are always handled, so the async void is safe.
    public async void SwitchToProfile(DeviceProfile profile, bool? scheduleSilent = null)
    {
        // Drop concurrent switch requests — spamming the hotkey or tray menu cannot queue overlapping
        // ApplyProfileAsync calls, which would leave audio devices in an undefined state.
        if (!_switchLock.Wait(0))
        {
            _logger.Warning("SwitchToProfile", $"Switch to '{profile.Name}' dropped — another switch is already in progress.");
            return;
        }

        ProfileSwitchResult? result = null;
        Exception? failure = null;
        try
        {
            // Dispatch to UI thread — SwitchToProfile can be called from the PowerModeChanged
            // background thread (SystemEvents callbacks run off the UI thread).
            await _dispatcher.InvokeAsync(() => _trayService.SetSwitchingTooltip(profile.Name));
            try
            {
                result = await _audioService.ApplyProfileAsync(profile);
                await _dispatcher.InvokeAsync(() =>
                {
                    _configService.Current.ActiveProfileId = profile.Id;
                    ProfileSwitched?.Invoke();
                    _configService.SaveDeferred();
                    _trayService.UpdateIcon(profile);
                    _trayService.SetActiveProfile(profile.Id);
                });
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        }
        finally
        {
            // Release BEFORE any user feedback (banners, switch sound, or the modal error dialog)
            // so a slow dialog or sound can't hold the lock and drop every other switch.
            _switchLock.Release();
        }

        if (failure != null)
        {
            _logger.Error("SwitchToProfile", failure);
            var detail = failure.InnerException?.Message ?? failure.Message;
            _errorTracker.Record(ErrorCode.ProfileSwitchFailed, "Profile Switch Failed",
                $"Could not switch to '{profile.Name}': {detail}");
            await _dispatcher.InvokeAsync(() =>
            {
                var still = _configService.Current.Profiles
                    .FirstOrDefault(p => p.Id == _configService.Current.ActiveProfileId);
                _trayService.UpdateIcon(still);
                // Background switches (scheduled — they pass scheduleSilent) must never pop a modal
                // dialog that would block, e.g. a failed 3am schedule. They still log + record.
                if (scheduleSilent == null)
                {
                    var dialog = new ErrorDialog(ErrorCode.ProfileSwitchFailed, "Profile Switch Failed",
                        $"Could not switch to '{profile.Name}': {detail}", _logger, _errorTracker);
                    var owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsVisible);
                    if (owner != null)
                    {
                        dialog.Owner = owner;
                        dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                    }
                    dialog.ShowDialog();
                }
                else
                {
                    _trayService.ShowBalloon("Profile Switch Failed",
                        $"Could not switch to '{profile.Name}': {detail}");
                }
            });
            return;
        }

        // Success feedback runs with the switch lock already released.
        var r = result!;
        await _dispatcher.InvokeAsync(() =>
        {
            _ = _soundService.PlayAsync(profile);

            if (r.MissingPlaybackId != null)
            {
                var msg = $"Playback device for '{profile.Name}' is disconnected.";
                _logger.Warning("SwitchToProfile", msg);
                _errorTracker.Record(ErrorCode.PlaybackDeviceUnavailable, "Device Unavailable", msg);
            }
            if (r.MissingRecordingId != null)
            {
                var msg = $"Recording device for '{profile.Name}' is disconnected.";
                _logger.Warning("SwitchToProfile", msg);
                _errorTracker.Record(ErrorCode.RecordingDeviceUnavailable, "Device Unavailable", msg);
            }

            bool anyMissing   = r.MissingPlaybackId != null || r.MissingRecordingId != null;
            bool anySetFailed = r.PlaybackSetFailed || r.RecordingSetFailed; // active device, but apply failed

            if (_configService.Current.ShowNotifications)
            {
                if (!anyMissing && !anySetFailed)
                {
                    if (profile.SoundOverride)
                    {
                        // Switch sound handles audio; show a silent banner only if the profile opts in,
                        // and only when the schedule hasn't requested silence.
                        bool scheduleSuppressed = scheduleSilent.HasValue && scheduleSilent.Value;
                        if (!scheduleSuppressed && profile.SoundShowBanner)
                            _trayService.ShowBalloon("VibeSwitcher", $"Switched to {profile.Name}", sound: false);
                    }
                    else
                    {
                        // No switch sound — standard banner + Windows ding, gated by Silent flag.
                        bool effectiveSilent = scheduleSilent.HasValue ? scheduleSilent.Value : profile.Silent;
                        if (!effectiveSilent)
                            _trayService.ShowBalloon("VibeSwitcher", $"Switched to {profile.Name}");
                    }
                }
                else
                {
                    // Warnings always show regardless of Silent flag.
                    if (r.MissingPlaybackId != null)
                        _trayService.ShowBalloon("Device Unavailable",
                            $"Playback device for '{profile.Name}' is disconnected.");
                    if (r.MissingRecordingId != null)
                        _trayService.ShowBalloon("Device Unavailable",
                            $"Recording device for '{profile.Name}' is disconnected.");
                    // Present-but-failed apply (not a disconnect) — surface it instead of a false "Switched".
                    if (anySetFailed && !anyMissing)
                        _trayService.ShowBalloon("Switch Incomplete",
                            $"'{profile.Name}' could not be fully applied — see the session log.");
                }
            }
        });
    }

    public void Dispose() => _switchLock.Dispose();
}
