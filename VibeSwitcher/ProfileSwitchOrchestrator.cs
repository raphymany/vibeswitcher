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
    private readonly SemaphoreSlim _switchLock = new(1, 1);

    public ProfileSwitchOrchestrator(
        IConfigService configService,
        IAudioService audioService,
        TrayService trayService,
        ISwitchSoundService soundService,
        Dispatcher dispatcher)
    {
        _configService = configService;
        _audioService = audioService;
        _trayService = trayService;
        _soundService = soundService;
        _dispatcher = dispatcher;
    }

    public void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume) return;
        var activeProfile = _configService.Current.Profiles
            .FirstOrDefault(p => p.Id == _configService.Current.ActiveProfileId);
        if (activeProfile != null)
            SwitchToProfile(activeProfile);
        // Note: if a switch is already in progress when the PC resumes, SwitchToProfile will drop
        // the resume request (switchLock.Wait(0) returns false). This is extremely unlikely in
        // practice — the lock is held for the duration of one audio API call only.
    }

    // async void is intentional: called as fire-and-forget from WndProc, PowerModeChanged, and tray click.
    // The try/catch ensures exceptions are always handled, so the async void is safe.
    public async void SwitchToProfile(DeviceProfile profile, bool? scheduleSilent = null)
    {
        // Drop concurrent switch requests — spamming the hotkey or tray menu cannot queue overlapping
        // ApplyProfileAsync calls, which would leave audio devices in an undefined state.
        if (!_switchLock.Wait(0))
        {
            AppLogger.Warning("SwitchToProfile", $"Switch to '{profile.Name}' dropped — another switch is already in progress.");
            return;
        }
        try
        {
        // Dispatch to UI thread — SwitchToProfile can be called from the PowerModeChanged
        // background thread (SystemEvents callbacks run off the UI thread).
        await _dispatcher.InvokeAsync(() => _trayService.SetSwitchingTooltip(profile.Name));
        try
        {
            var result = await _audioService.ApplyProfileAsync(profile);
            await _dispatcher.InvokeAsync(() =>
            {
                _configService.Current.ActiveProfileId = profile.Id;
                _ = Task.Run(_configService.SaveImmediate);
                _trayService.UpdateIcon(profile);
                _trayService.SetActiveProfile(profile.Id);
                _trayService.FlashSwitch(profile);
                _ = _soundService.PlayAsync(profile, _configService.Current);

                if (result.MissingPlaybackId != null)
                {
                    var msg = $"Playback device for '{profile.Name}' is disconnected.";
                    AppLogger.Warning("SwitchToProfile", msg);
                    SessionErrorTracker.Record(ErrorCode.PlaybackDeviceUnavailable, "Device Unavailable", msg);
                }
                if (result.MissingRecordingId != null)
                {
                    var msg = $"Recording device for '{profile.Name}' is disconnected.";
                    AppLogger.Warning("SwitchToProfile", msg);
                    SessionErrorTracker.Record(ErrorCode.RecordingDeviceUnavailable, "Device Unavailable", msg);
                }

                if (_configService.Current.ShowNotifications)
                {
                    if (result.MissingPlaybackId == null && result.MissingRecordingId == null)
                    {
                        bool effectiveSilent = scheduleSilent.HasValue ? scheduleSilent.Value : profile.Silent;
                        if (!effectiveSilent)
                            _trayService.ShowBalloon("VibeSwitcher", $"Switched to {profile.Name}");
                    }
                    else
                    {
                        // Device-unavailable warnings always show regardless of Silent flag
                        if (result.MissingPlaybackId != null)
                            _trayService.ShowBalloon("Device Unavailable",
                                $"Playback device for '{profile.Name}' is disconnected.");
                        if (result.MissingRecordingId != null)
                            _trayService.ShowBalloon("Device Unavailable",
                                $"Recording device for '{profile.Name}' is disconnected.");
                    }
                }
            });
        }
        catch (Exception ex)
        {
            AppLogger.Error("SwitchToProfile", ex);
            var detail = ex.InnerException?.Message ?? ex.Message;
            SessionErrorTracker.Record(ErrorCode.ProfileSwitchFailed, "Profile Switch Failed",
                $"Could not switch to '{profile.Name}': {detail}");
            await _dispatcher.InvokeAsync(() =>
            {
                var still = _configService.Current.Profiles
                    .FirstOrDefault(p => p.Id == _configService.Current.ActiveProfileId);
                _trayService.UpdateIcon(still);
                var dialog = new ErrorDialog(ErrorCode.ProfileSwitchFailed, "Profile Switch Failed",
                    $"Could not switch to '{profile.Name}': {detail}");
                var owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsVisible);
                if (owner != null)
                {
                    dialog.Owner = owner;
                    dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                }
                dialog.ShowDialog();
            });
        }
        }
        finally
        {
            _switchLock.Release();
        }
    }

    public void Dispose() => _switchLock.Dispose();
}
