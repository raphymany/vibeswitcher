namespace VibeSwitcher.Helpers;

public static class ScheduleHelpers
{
    public static string FormatTime(int hour, int minute, bool use12Hour)
    {
        if (!use12Hour)
            return $"{hour:D2}:{minute:D2}";

        var period = hour < 12 ? "AM" : "PM";
        var displayHour = hour == 0 ? 12 : hour > 12 ? hour - 12 : hour;
        return $"{displayHour}:{minute:D2} {period}";
    }

    public static string FormatDays(IEnumerable<DayOfWeek> days)
    {
        var sorted = days.OrderBy(d => ((int)d + 6) % 7).ToList(); // Mon=0 ordering
        if (sorted.Count == 7) return "Every day";
        if (sorted.SequenceEqual([DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                                   DayOfWeek.Thursday, DayOfWeek.Friday]))
            return "Mon–Fri";
        if (sorted.SequenceEqual([DayOfWeek.Saturday, DayOfWeek.Sunday]))
            return "Weekends";
        return string.Join(", ", sorted.Select(d => d.ToString()[..3]));
    }

    public static IReadOnlyList<string> BuildTimeOptions(bool use12Hour)
    {
        var options = new List<string>(288);
        for (int h = 0; h < 24; h++)
        for (int m = 0; m < 60; m += 5)
            options.Add(FormatTime(h, m, use12Hour));
        return options;
    }

    public static int TimeIndex(int hour, int minute) => hour * 12 + minute / 5;

    public static (int hour, int minute) TimeFromIndex(int index)
        => (index / 12, (index % 12) * 5);
}
