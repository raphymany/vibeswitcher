namespace VibeSwitcher.Models;

public class ScheduleEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public bool Enabled { get; set; } = true;
    public int Hour { get; set; } = 9;
    public int Minute { get; set; } = 0;
    public List<DayOfWeek> Days { get; set; } = new();
    public int ReminderMinutes { get; set; } = 0;
    public bool Silent { get; set; } = false;
}
