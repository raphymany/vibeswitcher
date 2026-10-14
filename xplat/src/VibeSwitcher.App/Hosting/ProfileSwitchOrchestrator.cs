using Avalonia.Threading;
using VibeSwitcher.Helpers;
using VibeSwitcher.Models;
using VibeSwitcher.Services;

namespace VibeSwitcher.App.Hosting;

// Fork of ProfileSwitchOrchestrator.cs: the single path every switch takes (hotkeys, the tray menu,
// schedules, auto-switch, app triggers, the command line). Unchanged apart from the UI thread being
// Avalonia's and the error dialog being shown by the host (showError) instead of here.
internal sealed class ProfileSwitchOrchestrator : IDisposable
{
    private readonly IConfigService _configService;
    private readonly IAudioService _audioService;
    private readonly ITrayService _trayService;
    private readonly ISwitchSoundService _soundService;
    private readonly IAppLogger _logger;
    private readonly ISessionErrorTracker _errorTracker;
    private readonly SemaphoreSlim _switchLock = new(1, 1);
    // Re-resolves profiles whose device came back under a new ID; true = something rebound.
    private readonly Func<bool>? _rebindDevices;
    // Shows the "Profile Switch Failed" dialog (its message); on the UI thread.
    private readonly Action<string> _showError;

    public event Action? ProfileSwitched;

    public ProfileSwitchOrchestrator(
        IConfigService configService,
        IAudioService audioService,
        ITrayService trayService,
        ISwitchSoundService soundService,
        IAppLogger logger,
        ISessionErrorTracker errorTracker,
        Action<string> showError,
        Func<bool>? rebindDevices = null)
    {
        _rebindDevices = rebindDevices;
        _configService = configService;
        _audioService = audioService;
        _trayService = trayService;
        _soundService = soundService;
        _logger = logger;
        _errorTracker = errorTracker;
        _showError = showError;
    }

    // Finishes when the switch and its feedback are done (callers fire and forget it; tests await it).
    // Nothing escapes it: failures are logged, recorded and shown.
    public void SwitchToProfile(DeviceProfile profile, bool? scheduleSilent = null) => _ = SwitchAsync(profile, scheduleSilent);

    // waitTurn: a switch asked for from the command line waits (up to 10 s) for one in progress
    // instead of being dropped — it was asked for once, on purpose, and answered "switching".
    internal async Task SwitchAsync(DeviceProfile profile, bool? scheduleSilent = null, bool waitTurn = false)
    {
        // Drop concurrent switch requests — spamming the hotkey or tray menu cannot queue overlapping
        // ApplyProfileAsync calls, which would leave audio devices in an undefined state.
        if (waitTurn ? !await _switchLock.WaitAsync(TimeSpan.FromSeconds(10)) : !_switchLock.Wait(0))
        {
            _logger.Warning("SwitchToProfile", $"Switch to '{profile.Name}' dropped — another switch is already in progress.");
            return;
        }

        ProfileSwitchResult? result = null;
        Exception? failure = null;
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => _trayService.SetSwitchingTooltip(profile.Name));
            result = await _audioService.ApplyProfileAsync(profile);
            // A device reported missing may just have a new ID. Rebind on the UI thread (it mutates
            // shared profiles) and retry once. Only runs on a miss, so normal switches pay nothing.
            if (_rebindDevices != null && (result.MissingPlaybackId != null || result.MissingRecordingId != null)
                && await Dispatcher.UIThread.InvokeAsync(_rebindDevices))
            {
                result = await _audioService.ApplyProfileAsync(profile);
            }
            await Dispatcher.UIThread.InvokeAsync(() =>
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
        finally
        {
            // Release BEFORE any user feedback so a slow dialog or sound can't hold the lock.
            _switchLock.Release();
        }

        try
        {
            if (failure != null)
            {
                _logger.Error("SwitchToProfile", failure);
                var detail = failure.InnerException?.Message ?? failure.Message;
                _errorTracker.Record(ErrorCode.ProfileSwitchFailed, "Profile Switch Failed",
                    $"Could not switch to '{profile.Name}': {detail}");
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var still = _configService.Current.Profiles
                        .FirstOrDefault(p => p.Id == _configService.Current.ActiveProfileId);
                    _trayService.UpdateIcon(still);
                    // Background switches (scheduled — they pass scheduleSilent) must never pop a
                    // dialog, e.g. a failed 3am schedule. They still log + record.
                    if (scheduleSilent == null)
                        _showError($"Could not switch to '{profile.Name}': {detail}");
                    else
                        _trayService.ShowBalloon("Profile Switch Failed", $"Could not switch to '{profile.Name}': {detail}");
                });
                return;
            }

            var r = result!;
            await Dispatcher.UIThread.InvokeAsync(() =>
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

                bool anyMissing = r.MissingPlaybackId != null || r.MissingRecordingId != null;
                bool anySetFailed = r.PlaybackSetFailed || r.RecordingSetFailed; // active device, but apply failed

                if (_configService.Current.ShowNotifications)
                {
                    if (!anyMissing && !anySetFailed)
                    {
                        if (profile.SoundOverride)
                        {
                            // The switch sound handles audio; a silent banner only if the profile opts
                            // in, and only when the schedule hasn't asked for silence.
                            bool scheduleSuppressed = scheduleSilent.HasValue && scheduleSilent.Value;
                            if (!scheduleSuppressed && profile.SoundShowBanner)
                                _trayService.ShowBalloon("VibeSwitcher", $"Switched to {profile.Name}", sound: false);
                        }
                        else
                        {
                            bool effectiveSilent = scheduleSilent.HasValue ? scheduleSilent.Value : profile.Silent;
                            if (!effectiveSilent)
                                _trayService.ShowBalloon("VibeSwitcher", $"Switched to {profile.Name}");
                        }
                    }
                    else
                    {
                        // Warnings always show regardless of Silent.
                        if (r.MissingPlaybackId != null)
                            _trayService.ShowBalloon("Device Unavailable", $"Playback device for '{profile.Name}' is disconnected.");
                        if (r.MissingRecordingId != null)
                            _trayService.ShowBalloon("Device Unavailable", $"Recording device for '{profile.Name}' is disconnected.");
                        if (anySetFailed && !anyMissing)
                            _trayService.ShowBalloon("Switch Incomplete", $"'{profile.Name}' could not be fully applied — see the session log.");
                    }
                }
            });
        }
        catch (Exception ex)
        {
            _logger.Error("SwitchToProfile.Feedback", ex);
        }
    }

    // (The lock isn't disposed: a switch still running when the app quits releases it afterwards.)
    public void Dispose() { }
}
