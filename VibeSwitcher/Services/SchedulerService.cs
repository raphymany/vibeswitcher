using System.Windows.Threading;
using Microsoft.Win32;
using VibeSwitcher.Helpers;
using VibeSwitcher.Models;

namespace VibeSwitcher.Services;

public class SchedulerService : IDisposable
{
    private readonly IConfigService _configService;
    private readonly Action<DeviceProfile, bool> _switchCallback;
    private readonly Action<string, string> _notifyCallback;
    private readonly Func<DateTime> _clock;

    private DispatcherTimer? _timer;
    private readonly Dictionary<Guid, DateTime> _lastSwitchFired = new();
    private readonly Dictionary<Guid, DateTime> _lastReminderFired = new();

    public SchedulerService(
        IConfigService configService,
        Action<DeviceProfile, bool> switchCallback,
        Action<string, string> notifyCallback,
        Func<DateTime>? clock = null)
    {
        _configService = configService;
        _switchCallback = switchCallback;
        _notifyCallback = notifyCallback;
        _clock = clock ?? (() => DateTime.Now);
    }

    public void Start()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _timer.Tick += (_, _) => EvaluateNow();
        _timer.Start();
    }

    public void EvaluateNow() => Evaluate(_clock());

    public void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume) return;
        // SystemEvents fires on a thread-pool thread — marshal to the UI thread so Evaluate()
        // runs on the same thread as the DispatcherTimer.Tick path and avoids dictionary races.
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null)
            dispatcher.InvokeAsync(EvaluateNow);
        else
            EvaluateNow(); // headless / test environment
    }

    private void Evaluate(DateTime now)
    {
        foreach (var profile in _configService.Current.Profiles)
        {
            foreach (var entry in profile.Schedules)
            {
                if (!entry.Enabled || entry.Days.Count == 0) continue;

                if (entry.Days.Contains(now.DayOfWeek) &&
                    entry.Hour == now.Hour && entry.Minute == now.Minute)
                {
                    var slot = new DateTime(now.Year, now.Month, now.Day, entry.Hour, entry.Minute, 0);
                    if (!_lastSwitchFired.TryGetValue(entry.Id, out var last) || last != slot)
                    {
                        _lastSwitchFired[entry.Id] = slot;
                        _switchCallback(profile, entry.Silent);
                    }
                }

                if (entry.ReminderMinutes > 0)
                {
                    var reminderTarget = now.AddMinutes(entry.ReminderMinutes);
                    if (entry.Days.Contains(reminderTarget.DayOfWeek) &&
                        reminderTarget.Hour == entry.Hour &&
                        reminderTarget.Minute == entry.Minute)
                    {
                        var reminderSlot = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);
                        if (!_lastReminderFired.TryGetValue(entry.Id, out var lastReminder) || lastReminder != reminderSlot)
                        {
                            _lastReminderFired[entry.Id] = reminderSlot;
                            _notifyCallback("VibeSwitcher",
                                $"{profile.Name} activates in {entry.ReminderMinutes} minutes");
                        }
                    }
                }
            }
        }
    }

    public void Dispose()
    {
        if (_timer != null)
        {
            _timer.Stop();
            _timer = null;
        }
    }
}
