namespace VibeSwitcher.Models;

// Outcome of the schedule wizard. Removed=true means the user clicked "Remove" while
// editing an existing schedule; Entry holds the saved schedule otherwise (null when cancelled).
public record ScheduleWizardOutcome(ScheduleEntry? Entry, bool Removed)
{
    public static readonly ScheduleWizardOutcome Cancelled = new(null, false);
    public static ScheduleWizardOutcome Saved(ScheduleEntry entry) => new(entry, false);
    public static readonly ScheduleWizardOutcome RemovedOutcome = new(null, true);
}
