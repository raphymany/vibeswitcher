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
    private readonly Func<int, int?> _showCustomReminderDialog;
    private readonly Func<bool> _use12Hour;

    private bool _isExpanded;
    private bool _hasConflict;
    private string _conflictMessage = "";
    private bool _isPm;

    private static readonly string[] PresetReminderLabels =
        ["None", "5 min before", "10 min before", "15 min before", "30 min before"];
    private static readonly int[] PresetReminderValues = [0, 5, 10, 15, 30];

    public static IReadOnlyList<string> MinuteOptions { get; } =
        Enumerable.Range(0, 60).Select(m => m.ToString("D2")).ToList();

    public ScheduleEntry Entry => _entry;

    // ── Expand / collapse ────────────────────────────────────────────

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetField(ref _isExpanded, value);
    }

    // ── Conflict ─────────────────────────────────────────────────────

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

    // ── Hour / Minute dropdowns ───────────────────────────────────────

    private IReadOnlyList<string> _hourOptions;
    public IReadOnlyList<string> HourOptions
    {
        get => _hourOptions;
        private set => SetField(ref _hourOptions, value);
    }

    public int SelectedHourIndex
    {
        get => _use12Hour()
            ? DisplayHour12(_entry.Hour) - 1          // 0-based (1→0, …, 12→11)
            : _entry.Hour;                             // 0-based == hour in 24h
        set
        {
            int newHour;
            if (_use12Hour())
            {
                var display = value + 1;               // 1–12
                newHour = _isPm
                    ? (display == 12 ? 12 : display + 12)
                    : (display == 12 ? 0 : display);
            }
            else
            {
                newHour = value;                       // 0–23
            }
            if (_entry.Hour == newHour) return;
            _entry.Hour = newHour;
            OnPropertyChanged(nameof(SelectedHourIndex));
            OnPropertyChanged(nameof(Summary));
            UpdateConflictState();
            _onChanged();
        }
    }

    public int SelectedMinuteIndex
    {
        get => _entry.Minute;
        set
        {
            if (_entry.Minute == value) return;
            _entry.Minute = value;
            OnPropertyChanged(nameof(SelectedMinuteIndex));
            OnPropertyChanged(nameof(Summary));
            UpdateConflictState();
            _onChanged();
        }
    }

    // ── AM / PM ───────────────────────────────────────────────────────

    public bool IsPm
    {
        get => _isPm;
        set
        {
            if (_isPm == value) return;
            _isPm = value;
            OnPropertyChanged(nameof(IsPm));
            OnPropertyChanged(nameof(AmPmLabel));
            if (_use12Hour())
            {
                var display = SelectedHourIndex + 1;
                _entry.Hour = value
                    ? (display == 12 ? 12 : display + 12)
                    : (display == 12 ? 0 : display);
                OnPropertyChanged(nameof(Summary));
                UpdateConflictState();
                _onChanged();
            }
        }
    }

    public string AmPmLabel => _isPm ? "PM" : "AM";
    public bool ShowAmPmToggle => _use12Hour();

    // ── Enabled ───────────────────────────────────────────────────────

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

    // ── Silent (per-schedule) ─────────────────────────────────────────

    public bool Silent
    {
        get => _entry.Silent;
        set
        {
            if (_entry.Silent == value) return;
            _entry.Silent = value;
            OnPropertyChanged(nameof(Silent));
            _onChanged();
        }
    }

    // ── Days ──────────────────────────────────────────────────────────

    public bool Mon { get => _entry.Days.Contains(DayOfWeek.Monday);    set => SetDay(DayOfWeek.Monday,     value, nameof(Mon)); }
    public bool Tue { get => _entry.Days.Contains(DayOfWeek.Tuesday);   set => SetDay(DayOfWeek.Tuesday,    value, nameof(Tue)); }
    public bool Wed { get => _entry.Days.Contains(DayOfWeek.Wednesday); set => SetDay(DayOfWeek.Wednesday,  value, nameof(Wed)); }
    public bool Thu { get => _entry.Days.Contains(DayOfWeek.Thursday);  set => SetDay(DayOfWeek.Thursday,   value, nameof(Thu)); }
    public bool Fri { get => _entry.Days.Contains(DayOfWeek.Friday);    set => SetDay(DayOfWeek.Friday,     value, nameof(Fri)); }
    public bool Sat { get => _entry.Days.Contains(DayOfWeek.Saturday);  set => SetDay(DayOfWeek.Saturday,   value, nameof(Sat)); }
    public bool Sun { get => _entry.Days.Contains(DayOfWeek.Sunday);    set => SetDay(DayOfWeek.Sunday,     value, nameof(Sun)); }

    // ── Reminder ──────────────────────────────────────────────────────

    public IReadOnlyList<string> ReminderOptions
    {
        get
        {
            if (_entry.ReminderMinutes > 0 && !PresetReminderValues.Contains(_entry.ReminderMinutes))
            {
                var list = new List<string>(PresetReminderLabels) { $"Custom: {_entry.ReminderMinutes} min" };
                return list;
            }
            return PresetReminderLabels;
        }
    }

    public int SelectedReminderIndex
    {
        get
        {
            var idx = Array.IndexOf(PresetReminderValues, _entry.ReminderMinutes);
            if (idx >= 0) return idx;
            return PresetReminderLabels.Length; // custom item is last
        }
        set
        {
            if (value < 0 || value >= PresetReminderValues.Length) return;
            var minutes = PresetReminderValues[value];
            if (_entry.ReminderMinutes == minutes) return;
            _entry.ReminderMinutes = minutes;
            OnPropertyChanged(nameof(SelectedReminderIndex));
            OnPropertyChanged(nameof(ReminderOptions));
            OnPropertyChanged(nameof(Summary));
            _onChanged();
        }
    }

    // ── Summary ───────────────────────────────────────────────────────

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
    public ICommand ToggleAmPmCommand { get; }
    public ICommand SetCustomReminderCommand { get; }

    public ScheduleEntryViewModel(
        ScheduleEntry entry,
        Func<bool> use12Hour,
        Action onChanged,
        Action<ScheduleEntryViewModel> onDelete,
        Func<ScheduleEntry, IEnumerable<(string profileName, string conflictDesc)>> checkConflicts,
        Func<IEnumerable<(string profileName, string conflictDesc)>, bool> showConflictDialog,
        Func<int, int?> showCustomReminderDialog)
    {
        _entry = entry;
        _use12Hour = use12Hour;
        _onChanged = onChanged;
        _onDelete = onDelete;
        _checkConflicts = checkConflicts;
        _showConflictDialog = showConflictDialog;
        _showCustomReminderDialog = showCustomReminderDialog;

        _hourOptions = BuildHourOptions(use12Hour());
        _isPm = use12Hour() && entry.Hour >= 12;

        ToggleExpandCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
        DeleteCommand = new RelayCommand(() => _onDelete(this));
        ToggleAmPmCommand = new RelayCommand(() => IsPm = !IsPm);
        SetCustomReminderCommand = new RelayCommand(OpenCustomReminderDialog);
    }

    public void NotifyTimeFormatChanged()
    {
        HourOptions = BuildHourOptions(_use12Hour());
        _isPm = _use12Hour() && _entry.Hour >= 12;
        OnPropertyChanged(nameof(SelectedHourIndex));
        OnPropertyChanged(nameof(IsPm));
        OnPropertyChanged(nameof(AmPmLabel));
        OnPropertyChanged(nameof(ShowAmPmToggle));
        OnPropertyChanged(nameof(Summary));
    }

    private void OpenCustomReminderDialog()
    {
        var result = _showCustomReminderDialog(_entry.ReminderMinutes);
        if (result is null) return;
        _entry.ReminderMinutes = result.Value;
        OnPropertyChanged(nameof(SelectedReminderIndex));
        OnPropertyChanged(nameof(ReminderOptions));
        OnPropertyChanged(nameof(Summary));
        _onChanged();
    }

    private static IReadOnlyList<string> BuildHourOptions(bool use12Hour) =>
        use12Hour
            ? Enumerable.Range(1, 12).Select(h => h.ToString()).ToList()
            : Enumerable.Range(0, 24).Select(h => h.ToString()).ToList();

    private static int DisplayHour12(int hour24) =>
        hour24 == 0 ? 12 : hour24 > 12 ? hour24 - 12 : hour24;

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
