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

    // Returns true if a profile switch was fired — used at startup so the last-active-profile
    // restore doesn't clobber a schedule that was due while the app wasn't running.
    public bool EvaluateNow()
    {
        _timer?.Stop();
        _timer = null;
        bool fired = Evaluate(_clock());
        ScheduleNext(_clock());
        return fired;
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
        // Floor the interval at 1s so a just-passed target can't arm a 0ms timer that
        // re-evaluates and re-schedules in a tight loop.
        if (delay < TimeSpan.FromSeconds(1)) delay = TimeSpan.FromSeconds(1);

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
                        {
                            var fallbackReminder = nextSwitch.Value.AddMinutes(-entry.ReminderMinutes);
                            // Guard against a past time (e.g. very large ReminderMinutes) which would
                            // arm a zero-delay timer.
                            if (fallbackReminder > now)
                                TakeEarlier(ref earliest, fallbackReminder);
                        }
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

    // Catch-up window: fire a schedule whose most-recent occurrence is within this window of
    // "now". This makes a switch missed while the PC was asleep/off fire shortly after wake or
    // launch, and lets a late timer tick (heavy load, DST spring-forward) still fire instead of
    // being rejected by an exact-minute match. The per-slot dedup below prevents repeats.
    private static readonly TimeSpan CatchUpWindow = TimeSpan.FromHours(2);

    private bool Evaluate(DateTime now)
    {
        bool firedSwitch = false;
        foreach (var profile in _configService.Current.Profiles)
        {
            foreach (var entry in profile.Schedules)
            {
                if (!entry.Enabled || entry.Days.Count == 0) continue;

                // Switch: the most recent scheduled occurrence at or before now. Fire it if it's
                // recent (within the catch-up window) and that exact slot hasn't been fired yet.
                var occurrence = GetMostRecentOccurrence(now, entry.Days, entry.Hour, entry.Minute);
                if (occurrence != null && now - occurrence.Value <= CatchUpWindow)
                {
                    if (!_lastSwitchFired.TryGetValue(entry.Id, out var last) || last != occurrence.Value)
                    {
                        _lastSwitchFired[entry.Id] = occurrence.Value;
                        _switchCallback(profile, entry.Silent);
                        firedSwitch = true;
                    }
                }

                // Reminder: fire once we reach the moment ReminderMinutes before the upcoming
                // switch, but before the switch itself. Deduped by the upcoming switch slot.
                if (entry.ReminderMinutes > 0)
                {
                    var nextSwitch = GetNextOccurrence(now, entry.Days, entry.Hour, entry.Minute);
                    if (nextSwitch != null)
                    {
                        var reminderMoment = nextSwitch.Value.AddMinutes(-entry.ReminderMinutes);
                        if (now >= reminderMoment && now < nextSwitch.Value &&
                            now - reminderMoment <= CatchUpWindow)
                        {
                            if (!_lastReminderFired.TryGetValue(entry.Id, out var lastReminder) ||
                                lastReminder != nextSwitch.Value)
                            {
                                _lastReminderFired[entry.Id] = nextSwitch.Value;
                                _notifyCallback("VibeSwitcher",
                                    $"{profile.Name} activates in {entry.ReminderMinutes} minutes");
                            }
                        }
                    }
                }
            }
        }
        return firedSwitch;
    }

    // Latest occurrence at or before 'now' matching the given days/hour/minute (looks back a week).
    private static DateTime? GetMostRecentOccurrence(DateTime now, List<DayOfWeek> days, int hour, int minute)
    {
        for (int back = 0; back < 8; back++)
        {
            var candidate = now.Date.AddDays(-back).AddHours(hour).AddMinutes(minute);
            if (candidate <= now && days.Contains(candidate.DayOfWeek))
                return candidate;
        }
        return null;
    }

    public void Dispose()
    {
        _timer?.Stop();
        _timer = null;
    }
}
