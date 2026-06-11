using VibeSwitcher.Helpers;
using VibeSwitcher.Models;

namespace VibeSwitcher.ViewModels;

public class ScheduleEntryViewModel : ViewModelBase
{
    private readonly ScheduleEntry _entry;
    private readonly Func<bool> _use12Hour;
    private readonly Action _onChanged;
    private readonly Func<ScheduleEntry, IEnumerable<(string profileName, string conflictDesc)>> _checkConflicts;
    private readonly Action<string> _showConflictAlert;

    private bool _hasConflict;
    private string _conflictMessage = "";

    public ScheduleEntry Entry => _entry;

    public bool HasConflict
    {
        get => _hasConflict;
        private set => SetField(ref _hasConflict, value);
    }

    public string ConflictMessage
    {
        get => _conflictMessage;
        private set => SetField(ref _conflictMessage, value);
    }

    public string Summary
    {
        get
        {
            var timeStr = ScheduleHelpers.FormatTime(_entry.Hour, _entry.Minute, _use12Hour());
            if (_entry.Days.Count == 0)
                return _entry.Enabled ? $"No days selected — {timeStr}" : "Disabled";
            var days = ScheduleHelpers.FormatDays(_entry.Days);
            var reminder = FormatReminder(_entry.ReminderMinutes);
            var silent = _entry.Silent ? " · Silent" : "";
            return _entry.Enabled
                ? $"{days} at {timeStr}{reminder}{silent}"
                : $"Off — {days} at {timeStr}{reminder}{silent}";
        }
    }

    private static string FormatReminder(int minutes)
    {
        if (minutes <= 0) return "";
        var h = minutes / 60;
        var m = minutes % 60;
        if (h == 0) return $" · {m}min reminder";
        if (m == 0) return $" · {h}hr reminder";
        return $" · {h}hr {m}min reminder";
    }

    public bool Enabled
    {
        get => _entry.Enabled;
        set
        {
            if (_entry.Enabled == value) return;
            if (value)
            {
                UpdateConflictState();
                if (_hasConflict)
                {
                    _showConflictAlert("Cannot enable — this schedule conflicts with another at the same time. Click 'Edit' to pick a different time.");
                    return;
                }
            }
            _entry.Enabled = value;
            OnPropertyChanged(nameof(Enabled));
            OnPropertyChanged(nameof(Summary));
            _onChanged();
        }
    }

    public ScheduleEntryViewModel(
        ScheduleEntry entry,
        Func<bool> use12Hour,
        Action onChanged,
        Func<ScheduleEntry, IEnumerable<(string profileName, string conflictDesc)>> checkConflicts,
        Action<string> showConflictAlert)
    {
        _entry = entry;
        _use12Hour = use12Hour;
        _onChanged = onChanged;
        _checkConflicts = checkConflicts;
        _showConflictAlert = showConflictAlert;

        UpdateConflictState();
    }

    public void RefreshFromEntry()
    {
        UpdateConflictState();
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(Enabled));
    }

    public void NotifyTimeFormatChanged()
    {
        OnPropertyChanged(nameof(Summary));
    }

    private void UpdateConflictState()
    {
        if (!_entry.Enabled || _entry.Days.Count == 0)
        {
            HasConflict = false;
            ConflictMessage = "";
            return;
        }
        var conflicts = _checkConflicts(_entry).ToList();
        HasConflict = conflicts.Count > 0;
        ConflictMessage = HasConflict
            ? "⚠ Conflicts with: " + string.Join("; ",
                conflicts.Select(c => $"\"{c.profileName}\" ({c.conflictDesc})"))
            : "";
    }
}
