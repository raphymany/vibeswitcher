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

    // Called once at startup. EvaluateNow() is called separately by App.xaml.cs to
    // catch any schedules that were due while the app wasn't running.
    public void Start() => ScheduleNext(_clock());

    // Recompute and reset the timer — call whenever profiles or schedules change.
    public void Reschedule()
    {
        _timer?.Stop();
        _timer = null;
        ScheduleNext(_clock());
    }

    public void EvaluateNow()
    {
        _timer?.Stop();
        _timer = null;
        Evaluate(_clock());
        ScheduleNext(_clock());
    }

    public void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume) return;
        // SystemEvents fires on a thread-pool thread — marshal to the UI thread so Evaluate()
        // runs on the same thread as the timer-tick path and avoids dictionary races.
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null)
            dispatcher.InvokeAsync(EvaluateNow);
        else
            EvaluateNow(); // headless / test environment
    }

    private void ScheduleNext(DateTime now)
    {
        var next = FindNextFireTime(now);
        if (next == null) return; // no schedules — nothing to wait for

        var delay = next.Value - now;
        if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;

        _timer = new DispatcherTimer { Interval = delay };
        _timer.Tick += (_, _) =>
        {
            _timer?.Stop();
            _timer = null;
            EvaluateNow(); // fire and schedule the one after this
        };
        _timer.Start();
    }

    // Returns the earliest upcoming moment that needs an action (switch or reminder).
    internal DateTime? FindNextFireTime(DateTime now)
    {
        DateTime? earliest = null;
        foreach (var profile in _configService.Current.Profiles)
        {
            foreach (var entry in profile.Schedules)
            {
                if (!entry.Enabled || entry.Days.Count == 0) continue;

                var switchTime = GetNextOccurrence(now, entry.Days, entry.Hour, entry.Minute);
                TakeEarlier(ref earliest, switchTime);

                if (entry.ReminderMinutes > 0 && switchTime != null)
                {
                    var reminderTime = switchTime.Value.AddMinutes(-entry.ReminderMinutes);
                    if (reminderTime > now)
                    {
                        TakeEarlier(ref earliest, reminderTime);
                    }
                    else
                    {
                        // Reminder for the upcoming switch already passed — look one occurrence further.
                        var nextSwitch = GetNextOccurrence(switchTime.Value, entry.Days, entry.Hour, entry.Minute);
                        if (nextSwitch != null)
                            TakeEarlier(ref earliest, nextSwitch.Value.AddMinutes(-entry.ReminderMinutes));
                    }
                }
            }
        }
        return earliest;
    }

    // Finds the next DateTime after 'after' when the given days/hour/minute occurs.
    private static DateTime? GetNextOccurrence(DateTime after, List<DayOfWeek> days, int hour, int minute)
    {
        for (int daysAhead = 0; daysAhead < 8; daysAhead++)
        {
            var candidate = after.Date.AddDays(daysAhead).AddHours(hour).AddMinutes(minute);
            if (candidate > after && days.Contains(candidate.DayOfWeek))
                return candidate;
        }
        return null;
    }

    private static void TakeEarlier(ref DateTime? current, DateTime? candidate)
    {
        if (candidate == null) return;
        if (current == null || candidate < current) current = candidate;
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
        _timer?.Stop();
        _timer = null;
    }
}
