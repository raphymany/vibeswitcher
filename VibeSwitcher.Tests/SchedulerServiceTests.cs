using VibeSwitcher.Models;
using VibeSwitcher.Services;

namespace VibeSwitcher.Tests;

public class SchedulerServiceTests
{
    private static FakeConfigService MakeConfig(params DeviceProfile[] profiles)
    {
        var svc = new FakeConfigService();
        foreach (var p in profiles)
            svc.Current.Profiles.Add(p);
        return svc;
    }

    private static DeviceProfile ProfileWithSchedule(ScheduleEntry entry)
    {
        var p = new DeviceProfile { Name = "Test" };
        p.Schedules.Add(entry);
        return p;
    }

    [Fact]
    public void NoProfiles_NoSwitches()
    {
        var switched = new List<DeviceProfile>();
        var svc = new SchedulerService(MakeConfig(), (p, _) => switched.Add(p), (_, _) => { },
            clock: () => new DateTime(2026, 1, 5, 9, 0, 0)); // Monday 09:00
        svc.EvaluateNow();
        Assert.Empty(switched);
    }

    [Fact]
    public void DisabledEntry_NoSwitch()
    {
        var entry = new ScheduleEntry { Enabled = false, Hour = 9, Minute = 0, Days = [DayOfWeek.Monday] };
        var switched = new List<DeviceProfile>();
        var svc = new SchedulerService(MakeConfig(ProfileWithSchedule(entry)), (p, _) => switched.Add(p), (_, _) => { },
            clock: () => new DateTime(2026, 1, 5, 9, 0, 0));
        svc.EvaluateNow();
        Assert.Empty(switched);
    }

    [Fact]
    public void EntryWithNoDays_NoSwitch()
    {
        var entry = new ScheduleEntry { Enabled = true, Hour = 9, Minute = 0, Days = [] };
        var switched = new List<DeviceProfile>();
        var svc = new SchedulerService(MakeConfig(ProfileWithSchedule(entry)), (p, _) => switched.Add(p), (_, _) => { },
            clock: () => new DateTime(2026, 1, 5, 9, 0, 0));
        svc.EvaluateNow();
        Assert.Empty(switched);
    }

    [Fact]
    public void MatchingTimeAndDay_FiresSwitch()
    {
        var entry = new ScheduleEntry { Enabled = true, Hour = 9, Minute = 0, Days = [DayOfWeek.Monday] };
        var profile = ProfileWithSchedule(entry);
        var switched = new List<DeviceProfile>();
        var svc = new SchedulerService(MakeConfig(profile), (p, _) => switched.Add(p), (_, _) => { },
            clock: () => new DateTime(2026, 1, 5, 9, 0, 0)); // Monday 09:00
        svc.EvaluateNow();
        Assert.Single(switched);
        Assert.Same(profile, switched[0]);
    }

    [Fact]
    public void WrongDay_NoSwitch()
    {
        var entry = new ScheduleEntry { Enabled = true, Hour = 9, Minute = 0, Days = [DayOfWeek.Tuesday] };
        var switched = new List<DeviceProfile>();
        var svc = new SchedulerService(MakeConfig(ProfileWithSchedule(entry)), (p, _) => switched.Add(p), (_, _) => { },
            clock: () => new DateTime(2026, 1, 5, 9, 0, 0)); // Monday
        svc.EvaluateNow();
        Assert.Empty(switched);
    }

    [Fact]
    public void WrongTime_NoSwitch()
    {
        var entry = new ScheduleEntry { Enabled = true, Hour = 9, Minute = 0, Days = [DayOfWeek.Monday] };
        var switched = new List<DeviceProfile>();
        // 12:00 is well outside the catch-up window of the 09:00 schedule, so nothing fires.
        var svc = new SchedulerService(MakeConfig(ProfileWithSchedule(entry)), (p, _) => switched.Add(p), (_, _) => { },
            clock: () => new DateTime(2026, 1, 5, 12, 0, 0));
        svc.EvaluateNow();
        Assert.Empty(switched);
    }

    [Fact]
    public void CatchUp_FiresMissedSwitch_WithinWindow()
    {
        // App launches at 09:30 having missed the 09:00 switch (PC was off/asleep) — it fires.
        var entry = new ScheduleEntry { Enabled = true, Hour = 9, Minute = 0, Days = [DayOfWeek.Monday] };
        var profile = ProfileWithSchedule(entry);
        var switched = new List<DeviceProfile>();
        var svc = new SchedulerService(MakeConfig(profile), (p, _) => switched.Add(p), (_, _) => { },
            clock: () => new DateTime(2026, 1, 5, 9, 30, 0)); // Monday 09:30 — 30 min late
        var fired = svc.EvaluateNow();
        Assert.Single(switched);
        Assert.True(fired);
    }

    [Fact]
    public void CatchUp_DoesNotFire_OutsideWindow()
    {
        // Three hours after the schedule is too late to catch up — nothing fires.
        var entry = new ScheduleEntry { Enabled = true, Hour = 9, Minute = 0, Days = [DayOfWeek.Monday] };
        var switched = new List<DeviceProfile>();
        var svc = new SchedulerService(MakeConfig(ProfileWithSchedule(entry)), (p, _) => switched.Add(p), (_, _) => { },
            clock: () => new DateTime(2026, 1, 5, 12, 1, 0)); // Monday 12:01
        Assert.False(svc.EvaluateNow());
        Assert.Empty(switched);
    }

    [Fact]
    public void SwitchDoesNotFireTwiceInSameMinute()
    {
        var entry = new ScheduleEntry { Enabled = true, Hour = 9, Minute = 0, Days = [DayOfWeek.Monday] };
        var switched = new List<DeviceProfile>();
        var now = new DateTime(2026, 1, 5, 9, 0, 30); // Monday 09:00:30
        var svc = new SchedulerService(MakeConfig(ProfileWithSchedule(entry)), (p, _) => switched.Add(p), (_, _) => { },
            clock: () => now);
        svc.EvaluateNow();
        svc.EvaluateNow(); // same minute, should not fire again
        Assert.Single(switched);
    }

    [Fact]
    public void SwitchFiresAgainAfterTwoMinutes()
    {
        var entry = new ScheduleEntry { Enabled = true, Hour = 9, Minute = 0, Days = [DayOfWeek.Monday, DayOfWeek.Wednesday] };
        var switched = new List<DeviceProfile>();
        var now = new DateTime(2026, 1, 5, 9, 0, 0); // Monday 09:00
        var svc = new SchedulerService(MakeConfig(ProfileWithSchedule(entry)), (p, _) => switched.Add(p), (_, _) => { },
            clock: () => now);
        svc.EvaluateNow();
        now = new DateTime(2026, 1, 7, 9, 0, 0); // Wednesday 09:00 (same time, different day — entry.Id still matches but >2 min ago)
        svc.EvaluateNow();
        Assert.Equal(2, switched.Count);
    }

    [Fact]
    public void ReminderFires_WhenNowPlusReminderEqualsScheduledTime()
    {
        var entry = new ScheduleEntry
        {
            Enabled = true, Hour = 9, Minute = 0,
            Days = [DayOfWeek.Monday],
            ReminderMinutes = 10
        };
        var reminders = new List<string>();
        var svc = new SchedulerService(MakeConfig(ProfileWithSchedule(entry)),
            (_, _) => { },
            (_, msg) => reminders.Add(msg),
            clock: () => new DateTime(2026, 1, 5, 8, 50, 0)); // 8:50 AM — 10 min before 9:00
        svc.EvaluateNow();
        Assert.Single(reminders);
        Assert.Contains("Test", reminders[0]);
    }

    [Fact]
    public void ReminderDoesNotFireTwiceInSameMinute()
    {
        var entry = new ScheduleEntry
        {
            Enabled = true, Hour = 9, Minute = 0,
            Days = [DayOfWeek.Monday],
            ReminderMinutes = 5
        };
        var reminders = new List<string>();
        var now = new DateTime(2026, 1, 5, 8, 55, 0);
        var svc = new SchedulerService(MakeConfig(ProfileWithSchedule(entry)),
            (_, _) => { },
            (_, msg) => reminders.Add(msg),
            clock: () => now);
        svc.EvaluateNow();
        svc.EvaluateNow();
        Assert.Single(reminders);
    }

    [Fact]
    public void ReminderDoesNotFire_WhenNoReminder()
    {
        var entry = new ScheduleEntry { Enabled = true, Hour = 9, Minute = 0, Days = [DayOfWeek.Monday], ReminderMinutes = 0 };
        var reminders = new List<string>();
        var svc = new SchedulerService(MakeConfig(ProfileWithSchedule(entry)),
            (_, _) => { },
            (_, msg) => reminders.Add(msg),
            clock: () => new DateTime(2026, 1, 5, 8, 50, 0));
        svc.EvaluateNow();
        Assert.Empty(reminders);
    }

    [Fact]
    public void ReminderAcrossMidnight_FiresCorrectly()
    {
        // Schedule: Sunday at 00:05, reminder 30 min before = Saturday at 23:35
        var entry = new ScheduleEntry
        {
            Enabled = true, Hour = 0, Minute = 5,
            Days = [DayOfWeek.Sunday],
            ReminderMinutes = 30
        };
        var reminders = new List<string>();
        var svc = new SchedulerService(MakeConfig(ProfileWithSchedule(entry)),
            (_, _) => { },
            (_, msg) => reminders.Add(msg),
            clock: () => new DateTime(2026, 1, 3, 23, 35, 0)); // Saturday 23:35
        svc.EvaluateNow();
        Assert.Single(reminders);
    }

    [Fact]
    public void MultipleSchedulesOnProfile_AllCanFire()
    {
        var profile = new DeviceProfile { Name = "Multi" };
        profile.Schedules.Add(new ScheduleEntry { Enabled = true, Hour = 9, Minute = 0, Days = [DayOfWeek.Monday] });
        profile.Schedules.Add(new ScheduleEntry { Enabled = true, Hour = 9, Minute = 0, Days = [DayOfWeek.Wednesday] });

        var switched = new List<DeviceProfile>();
        var svc = new SchedulerService(MakeConfig(profile), (p, _) => switched.Add(p), (_, _) => { },
            clock: () => new DateTime(2026, 1, 5, 9, 0, 0)); // Monday
        svc.EvaluateNow();
        Assert.Single(switched); // only Monday entry fires
    }

    [Fact]
    public void SwitchAndReminderFire_AreIndependent()
    {
        // Schedule: 9:00 with 5-min reminder → at 8:55, only reminder should fire
        var entry = new ScheduleEntry { Enabled = true, Hour = 9, Minute = 0, Days = [DayOfWeek.Monday], ReminderMinutes = 5 };
        var switched = new List<DeviceProfile>();
        var reminders = new List<string>();
        var svc = new SchedulerService(MakeConfig(ProfileWithSchedule(entry)), (p, _) => switched.Add(p), (_, msg) => reminders.Add(msg),
            clock: () => new DateTime(2026, 1, 5, 8, 55, 0));
        svc.EvaluateNow();
        Assert.Empty(switched);
        Assert.Single(reminders);
    }

    [Fact]
    public void SwitchAndReminderBothFire_WhenBothDue()
    {
        // Schedule: 9:00 with 0-min "reminder" does not fire reminder; both entries' switches fire correctly
        var entryA = new ScheduleEntry { Enabled = true, Hour = 9, Minute = 0, Days = [DayOfWeek.Monday], ReminderMinutes = 0 };
        var entryB = new ScheduleEntry { Enabled = true, Hour = 9, Minute = 0, Days = [DayOfWeek.Monday], ReminderMinutes = 5 };
        var profile = new DeviceProfile { Name = "P" };
        profile.Schedules.Add(entryA);
        profile.Schedules.Add(entryB);

        var switched = new List<DeviceProfile>();
        var reminders = new List<string>();
        // At 8:55: entryB's reminder fires; entryA switch does not fire
        var svc = new SchedulerService(MakeConfig(profile), (p, _) => switched.Add(p), (_, msg) => reminders.Add(msg),
            clock: () => new DateTime(2026, 1, 5, 8, 55, 0));
        svc.EvaluateNow();
        Assert.Empty(switched);
        Assert.Single(reminders);
    }

    [Fact]
    public void TwoConflictingProfiles_BothSwitch()
    {
        var p1 = new DeviceProfile { Name = "P1" };
        p1.Schedules.Add(new ScheduleEntry { Enabled = true, Hour = 9, Minute = 0, Days = [DayOfWeek.Monday] });
        var p2 = new DeviceProfile { Name = "P2" };
        p2.Schedules.Add(new ScheduleEntry { Enabled = true, Hour = 9, Minute = 0, Days = [DayOfWeek.Monday] });

        var switched = new List<DeviceProfile>();
        var svc = new SchedulerService(MakeConfig(p1, p2), (p, _) => switched.Add(p), (_, _) => { },
            clock: () => new DateTime(2026, 1, 5, 9, 0, 0));
        svc.EvaluateNow();
        Assert.Equal(2, switched.Count);
    }

    [Fact]
    public void DisabledEntry_AfterBeingEnabled_DoesNotFire()
    {
        var entry = new ScheduleEntry { Enabled = true, Hour = 9, Minute = 0, Days = [DayOfWeek.Monday] };
        var profile = ProfileWithSchedule(entry);
        var switched = new List<DeviceProfile>();
        var now = new DateTime(2026, 1, 5, 9, 0, 0);
        var svc = new SchedulerService(MakeConfig(profile), (p, _) => switched.Add(p), (_, _) => { }, clock: () => now);
        entry.Enabled = false;
        svc.EvaluateNow();
        Assert.Empty(switched);
    }

    // ── FindNextFireTime ──────────────────────────────────────────────────────

    [Fact]
    public void FindNextFireTime_ReturnsNull_WhenNoProfiles()
    {
        var svc = new SchedulerService(MakeConfig(), (_, _) => { }, (_, _) => { });
        Assert.Null(svc.FindNextFireTime(new DateTime(2026, 1, 5, 9, 0, 0)));
    }

    [Fact]
    public void FindNextFireTime_ReturnsNull_WhenNoEnabledSchedules()
    {
        var entry = new ScheduleEntry { Enabled = false, Hour = 9, Minute = 0, Days = [DayOfWeek.Monday] };
        var svc = new SchedulerService(MakeConfig(ProfileWithSchedule(entry)), (_, _) => { }, (_, _) => { });
        Assert.Null(svc.FindNextFireTime(new DateTime(2026, 1, 5, 8, 0, 0)));
    }

    [Fact]
    public void FindNextFireTime_ReturnsTodayOccurrence_WhenScheduledLaterToday()
    {
        // Monday 08:00, schedule is Monday 09:00 → next is today at 09:00
        var entry = new ScheduleEntry { Enabled = true, Hour = 9, Minute = 0, Days = [DayOfWeek.Monday] };
        var svc = new SchedulerService(MakeConfig(ProfileWithSchedule(entry)), (_, _) => { }, (_, _) => { });
        var now = new DateTime(2026, 1, 5, 8, 0, 0); // Monday 08:00
        var next = svc.FindNextFireTime(now);
        Assert.Equal(new DateTime(2026, 1, 5, 9, 0, 0), next);
    }

    [Fact]
    public void FindNextFireTime_WrapsToNextWeek_WhenTimePassedToday()
    {
        // Monday 10:00, schedule is Monday 09:00 → next occurrence is next Monday
        var entry = new ScheduleEntry { Enabled = true, Hour = 9, Minute = 0, Days = [DayOfWeek.Monday] };
        var svc = new SchedulerService(MakeConfig(ProfileWithSchedule(entry)), (_, _) => { }, (_, _) => { });
        var now = new DateTime(2026, 1, 5, 10, 0, 0); // Monday 10:00
        var next = svc.FindNextFireTime(now);
        Assert.Equal(new DateTime(2026, 1, 12, 9, 0, 0), next); // next Monday
    }

    [Fact]
    public void FindNextFireTime_ReturnsReminderTime_WhenEarlierThanSwitch()
    {
        // Monday 08:00, schedule 09:00 with 30-min reminder → reminder at 08:30 is earliest
        var entry = new ScheduleEntry
        {
            Enabled = true, Hour = 9, Minute = 0,
            Days = [DayOfWeek.Monday],
            ReminderMinutes = 30
        };
        var svc = new SchedulerService(MakeConfig(ProfileWithSchedule(entry)), (_, _) => { }, (_, _) => { });
        var now = new DateTime(2026, 1, 5, 8, 0, 0);
        var next = svc.FindNextFireTime(now);
        Assert.Equal(new DateTime(2026, 1, 5, 8, 30, 0), next); // reminder at 08:30
    }

    [Fact]
    public void FindNextFireTime_ReturnsSwitchTime_WhenReminderAlreadyPassed()
    {
        // Monday 08:45, schedule 09:00 with 30-min reminder → reminder was 08:30 (past),
        // switch at 09:00 is earliest. Next reminder is next Monday at 08:30.
        var entry = new ScheduleEntry
        {
            Enabled = true, Hour = 9, Minute = 0,
            Days = [DayOfWeek.Monday],
            ReminderMinutes = 30
        };
        var svc = new SchedulerService(MakeConfig(ProfileWithSchedule(entry)), (_, _) => { }, (_, _) => { });
        var now = new DateTime(2026, 1, 5, 8, 45, 0); // past the 08:30 reminder
        var next = svc.FindNextFireTime(now);
        // Switch at 09:00 is sooner than next Monday's reminder at 08:30
        Assert.Equal(new DateTime(2026, 1, 5, 9, 0, 0), next);
    }

    [Fact]
    public void FindNextFireTime_LooksAheadForReminder_WhenSwitchAndReminderBothPassed()
    {
        // Monday 09:05, schedule 09:00 with 30-min reminder — both switch and reminder have passed.
        // Next switch is next Monday 09:00; next reminder is next Monday 08:30.
        // Earliest should be next Monday 08:30.
        var entry = new ScheduleEntry
        {
            Enabled = true, Hour = 9, Minute = 0,
            Days = [DayOfWeek.Monday],
            ReminderMinutes = 30
        };
        var svc = new SchedulerService(MakeConfig(ProfileWithSchedule(entry)), (_, _) => { }, (_, _) => { });
        var now = new DateTime(2026, 1, 5, 9, 5, 0); // past both
        var next = svc.FindNextFireTime(now);
        Assert.Equal(new DateTime(2026, 1, 12, 8, 30, 0), next); // next Monday reminder
    }

    [Fact]
    public void FindNextFireTime_ReturnsEarliest_AcrossMultipleProfiles()
    {
        var p1 = new DeviceProfile { Name = "P1" };
        p1.Schedules.Add(new ScheduleEntry { Enabled = true, Hour = 10, Minute = 0, Days = [DayOfWeek.Monday] });
        var p2 = new DeviceProfile { Name = "P2" };
        p2.Schedules.Add(new ScheduleEntry { Enabled = true, Hour = 9, Minute = 0, Days = [DayOfWeek.Monday] });

        var svc = new SchedulerService(MakeConfig(p1, p2), (_, _) => { }, (_, _) => { });
        var now = new DateTime(2026, 1, 5, 8, 0, 0); // Monday 08:00
        var next = svc.FindNextFireTime(now);
        Assert.Equal(new DateTime(2026, 1, 5, 9, 0, 0), next); // P2 at 09:00 wins
    }

    [Fact]
    public void DedupClearsAfterSufficientTime_OnSameDay()
    {
        var entry = new ScheduleEntry { Enabled = true, Hour = 9, Minute = 0, Days = [DayOfWeek.Monday] };
        var profile = ProfileWithSchedule(entry);
        var switched = new List<DeviceProfile>();
        var now = new DateTime(2026, 1, 5, 9, 0, 0);
        var svc = new SchedulerService(MakeConfig(profile), (p, _) => switched.Add(p), (_, _) => { }, clock: () => now);
        svc.EvaluateNow(); // fires
        now = new DateTime(2026, 1, 5, 9, 2, 1); // 2m01s later — same day, past dedup window
        svc.EvaluateNow(); // should not fire — 09:02 != 09:00
        Assert.Single(switched); // still only one — time does not match at 09:02
    }
}
