namespace VibeSwitcher.ViewModels;

public sealed record ProfileFilter
{
    public string             ModeFilter    { get; init; } = "Any mode";
    public bool               PinnedOnly    { get; init; }
    public bool               ActiveOnly    { get; init; }
    public bool               SilentOnly    { get; init; }
    public bool               HotkeyOnly    { get; init; }
    public bool               NotesOnly     { get; init; }
    public bool               IconOnly      { get; init; }
    public bool               WarningOnly   { get; init; }
    public bool               ScheduledOnly { get; init; }
    public bool               ReminderOnly  { get; init; }
    public bool               SoundOnly     { get; init; }
    public HashSet<DayOfWeek> ActiveDays    { get; init; } = [];

    public bool IsActive =>
        ModeFilter != "Any mode"               ||
        PinnedOnly  || ActiveOnly  || SilentOnly  ||
        HotkeyOnly  || NotesOnly   || IconOnly     ||
        WarningOnly || ScheduledOnly || ReminderOnly ||
        SoundOnly   || ActiveDays.Count > 0;
}
