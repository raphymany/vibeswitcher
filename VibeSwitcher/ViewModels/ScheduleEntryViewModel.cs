using System.Collections.ObjectModel;
using System.Windows.Input;
using VibeSwitcher.Helpers;
using VibeSwitcher.Models;

namespace VibeSwitcher.ViewModels;

public class ScheduleEntryViewModel : ViewModelBase
{
    private readonly ScheduleEntry _entry;
    private readonly Action _onChanged;
    private readonly Action<ScheduleEntryViewModel> _onDelete;
    private readonly Func<ScheduleEntry, IEnumerable<(string profileName, string conflictDesc)>> _checkConflicts;
    private readonly Func<IEnumerable<(string profileName, string conflictDesc)>, bool> _showConflictDialog;
    private readonly Func<bool> _use12Hour;

    private bool _isExpanded;
    private bool _hasConflict;
    private string _conflictMessage = "";
    private IReadOnlyList<string> _timeOptions;

    public static IReadOnlyList<string> ReminderOptions { get; } =
        ["None", "5 min before", "10 min before", "15 min before", "30 min before"];
    private static readonly int[] ReminderMinuteValues = [0, 5, 10, 15, 30];

    public ScheduleEntry Entry => _entry;

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetField(ref _isExpanded, value);
    }

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

    public IReadOnlyList<string> TimeOptions
    {
        get => _timeOptions;
        private set => SetField(ref _timeOptions, value);
    }

    public bool Enabled
    {
        get => _entry.Enabled;
        set
        {
            if (_entry.Enabled == value) return;

            if (value)
            {
                var conflicts = _checkConflicts(_entry).ToList();
                if (conflicts.Count > 0)
                {
                    var proceed = _showConflictDialog(conflicts);
                    if (!proceed) return;
                }
            }

            _entry.Enabled = value;
            OnPropertyChanged(nameof(Enabled));
            OnPropertyChanged(nameof(Summary));
            UpdateConflictState();
            _onChanged();
        }
    }

    public int SelectedTimeIndex
    {
        get => ScheduleHelpers.TimeIndex(_entry.Hour, _entry.Minute);
        set
        {
            var (h, m) = ScheduleHelpers.TimeFromIndex(value);
            if (_entry.Hour == h && _entry.Minute == m) return;
            _entry.Hour = h;
            _entry.Minute = m;
            OnPropertyChanged(nameof(SelectedTimeIndex));
            OnPropertyChanged(nameof(Summary));
            UpdateConflictState();
            _onChanged();
        }
    }

    public bool Mon
    {
        get => _entry.Days.Contains(DayOfWeek.Monday);
        set => SetDay(DayOfWeek.Monday, value, nameof(Mon));
    }
    public bool Tue
    {
        get => _entry.Days.Contains(DayOfWeek.Tuesday);
        set => SetDay(DayOfWeek.Tuesday, value, nameof(Tue));
    }
    public bool Wed
    {
        get => _entry.Days.Contains(DayOfWeek.Wednesday);
        set => SetDay(DayOfWeek.Wednesday, value, nameof(Wed));
    }
    public bool Thu
    {
        get => _entry.Days.Contains(DayOfWeek.Thursday);
        set => SetDay(DayOfWeek.Thursday, value, nameof(Thu));
    }
    public bool Fri
    {
        get => _entry.Days.Contains(DayOfWeek.Friday);
        set => SetDay(DayOfWeek.Friday, value, nameof(Fri));
    }
    public bool Sat
    {
        get => _entry.Days.Contains(DayOfWeek.Saturday);
        set => SetDay(DayOfWeek.Saturday, value, nameof(Sat));
    }
    public bool Sun
    {
        get => _entry.Days.Contains(DayOfWeek.Sunday);
        set => SetDay(DayOfWeek.Sunday, value, nameof(Sun));
    }

    public int SelectedReminderIndex
    {
        get
        {
            var idx = Array.IndexOf(ReminderMinuteValues, _entry.ReminderMinutes);
            return idx < 0 ? 0 : idx;
        }
        set
        {
            if (value < 0 || value >= ReminderMinuteValues.Length) return;
            var minutes = ReminderMinuteValues[value];
            if (_entry.ReminderMinutes == minutes) return;
            _entry.ReminderMinutes = minutes;
            OnPropertyChanged(nameof(SelectedReminderIndex));
            OnPropertyChanged(nameof(Summary));
            _onChanged();
        }
    }

    public string Summary
    {
        get
        {
            var timeStr = ScheduleHelpers.FormatTime(_entry.Hour, _entry.Minute, _use12Hour());
            if (_entry.Days.Count == 0)
                return _entry.Enabled ? $"No days selected — {timeStr}" : "Disabled";
            var days = ScheduleHelpers.FormatDays(_entry.Days);
            var reminder = _entry.ReminderMinutes > 0
                ? $" · {_entry.ReminderMinutes} min reminder"
                : "";
            return _entry.Enabled
                ? $"{days} at {timeStr}{reminder}"
                : $"Off — {days} at {timeStr}{reminder}";
        }
    }

    public ICommand ToggleExpandCommand { get; }
    public ICommand DeleteCommand { get; }

    public ScheduleEntryViewModel(
        ScheduleEntry entry,
        Func<bool> use12Hour,
        Action onChanged,
        Action<ScheduleEntryViewModel> onDelete,
        Func<ScheduleEntry, IEnumerable<(string profileName, string conflictDesc)>> checkConflicts,
        Func<IEnumerable<(string profileName, string conflictDesc)>, bool> showConflictDialog)
    {
        _entry = entry;
        _use12Hour = use12Hour;
        _onChanged = onChanged;
        _onDelete = onDelete;
        _checkConflicts = checkConflicts;
        _showConflictDialog = showConflictDialog;
        _timeOptions = ScheduleHelpers.BuildTimeOptions(use12Hour());

        ToggleExpandCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
        DeleteCommand = new RelayCommand(() => _onDelete(this));
    }

    public void NotifyTimeFormatChanged()
    {
        TimeOptions = ScheduleHelpers.BuildTimeOptions(_use12Hour());
        OnPropertyChanged(nameof(SelectedTimeIndex));
        OnPropertyChanged(nameof(Summary));
    }

    private void SetDay(DayOfWeek day, bool value, string propertyName)
    {
        var had = _entry.Days.Contains(day);
        if (had == value) return;

        if (value) _entry.Days.Add(day);
        else _entry.Days.Remove(day);

        OnPropertyChanged(propertyName);
        OnPropertyChanged(nameof(Summary));
        UpdateConflictState();
        _onChanged();
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
