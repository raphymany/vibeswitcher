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
    private string _timeText = "";
    private bool _isPm;
    private string _timeError = "";
    private bool _hasTimeError;
    private bool _reminderEnabled;
    private string _reminderText = "";
    private string _reminderError = "";
    private bool _hasReminderError;

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

    // ── Time ─────────────────────────────────────────────────────────

    public string TimeText
    {
        get => _timeText;
        set
        {
            if (_timeText == value) return;
            _timeText = value;
            OnPropertyChanged(nameof(TimeText));
            ParseAndSetTime();
        }
    }

    public bool IsPm
    {
        get => _isPm;
        set
        {
            if (_isPm == value) return;
            _isPm = value;
            OnPropertyChanged(nameof(IsPm));
            OnPropertyChanged(nameof(AmPmLabel));
            ParseAndSetTime();
        }
    }

    public string AmPmLabel => _isPm ? "PM" : "AM";
    public bool ShowAmPmToggle => _use12Hour();

    public string TimeError
    {
        get => _timeError;
        private set => SetField(ref _timeError, value);
    }

    public bool HasTimeError
    {
        get => _hasTimeError;
        private set => SetField(ref _hasTimeError, value);
    }

    // ── Reminder ─────────────────────────────────────────────────────

    public bool ReminderEnabled
    {
        get => _reminderEnabled;
        set
        {
            if (_reminderEnabled == value) return;
            _reminderEnabled = value;
            OnPropertyChanged(nameof(ReminderEnabled));
            if (!value)
            {
                _entry.ReminderMinutes = 0;
                ReminderText = "";
                ReminderError = "";
                HasReminderError = false;
                OnPropertyChanged(nameof(Summary));
                _onChanged();
            }
        }
    }

    public string ReminderText
    {
        get => _reminderText;
        set
        {
            if (_reminderText == value) return;
            _reminderText = value;
            OnPropertyChanged(nameof(ReminderText));
            if (_reminderEnabled)
                ParseAndSetReminder();
        }
    }

    public string ReminderError
    {
        get => _reminderError;
        private set => SetField(ref _reminderError, value);
    }

    public bool HasReminderError
    {
        get => _hasReminderError;
        private set => SetField(ref _hasReminderError, value);
    }

    // ── Enabled ──────────────────────────────────────────────────────

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

    // ── Days ─────────────────────────────────────────────────────────

    public bool Mon { get => _entry.Days.Contains(DayOfWeek.Monday);    set => SetDay(DayOfWeek.Monday,     value, nameof(Mon)); }
    public bool Tue { get => _entry.Days.Contains(DayOfWeek.Tuesday);   set => SetDay(DayOfWeek.Tuesday,    value, nameof(Tue)); }
    public bool Wed { get => _entry.Days.Contains(DayOfWeek.Wednesday); set => SetDay(DayOfWeek.Wednesday,  value, nameof(Wed)); }
    public bool Thu { get => _entry.Days.Contains(DayOfWeek.Thursday);  set => SetDay(DayOfWeek.Thursday,   value, nameof(Thu)); }
    public bool Fri { get => _entry.Days.Contains(DayOfWeek.Friday);    set => SetDay(DayOfWeek.Friday,     value, nameof(Fri)); }
    public bool Sat { get => _entry.Days.Contains(DayOfWeek.Saturday);  set => SetDay(DayOfWeek.Saturday,   value, nameof(Sat)); }
    public bool Sun { get => _entry.Days.Contains(DayOfWeek.Sunday);    set => SetDay(DayOfWeek.Sunday,     value, nameof(Sun)); }

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

        InitTimeText();

        _reminderEnabled = _entry.ReminderMinutes > 0;
        _reminderText = _reminderEnabled ? _entry.ReminderMinutes.ToString() : "";

        ToggleExpandCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
        DeleteCommand = new RelayCommand(() => _onDelete(this));
        ToggleAmPmCommand = new RelayCommand(() => IsPm = !IsPm);
    }

    public void NotifyTimeFormatChanged()
    {
        InitTimeText();
        OnPropertyChanged(nameof(TimeText));
        OnPropertyChanged(nameof(IsPm));
        OnPropertyChanged(nameof(AmPmLabel));
        OnPropertyChanged(nameof(ShowAmPmToggle));
        OnPropertyChanged(nameof(Summary));
    }

    private void InitTimeText()
    {
        if (_use12Hour())
        {
            _isPm = _entry.Hour >= 12;
            var displayHour = _entry.Hour == 0 ? 12 : _entry.Hour > 12 ? _entry.Hour - 12 : _entry.Hour;
            _timeText = $"{displayHour}:{_entry.Minute:D2}";
        }
        else
        {
            _timeText = $"{_entry.Hour}:{_entry.Minute:D2}";
        }
    }

    private void ParseAndSetTime()
    {
        var parts = _timeText.Trim().Split(':');
        if (parts.Length != 2
            || !int.TryParse(parts[0].Trim(), out var h)
            || !int.TryParse(parts[1].Trim(), out var m))
        {
            TimeError = "Enter a time like 9:30";
            HasTimeError = true;
            return;
        }
        if (m < 0 || m > 59)
        {
            TimeError = "Minutes must be 0–59";
            HasTimeError = true;
            return;
        }
        if (_use12Hour())
        {
            if (h < 1 || h > 12)
            {
                TimeError = "Hour must be 1–12";
                HasTimeError = true;
                return;
            }
            _entry.Hour = _isPm ? (h == 12 ? 12 : h + 12) : (h == 12 ? 0 : h);
        }
        else
        {
            if (h < 0 || h > 23)
            {
                TimeError = "Hour must be 0–23";
                HasTimeError = true;
                return;
            }
            _entry.Hour = h;
        }
        _entry.Minute = m;
        TimeError = "";
        HasTimeError = false;
        OnPropertyChanged(nameof(Summary));
        UpdateConflictState();
        _onChanged();
    }

    private void ParseAndSetReminder()
    {
        if (!int.TryParse(_reminderText.Trim(), out var minutes) || minutes < 1 || minutes > 1440)
        {
            ReminderError = "Enter 1–1440 minutes";
            HasReminderError = true;
            return;
        }
        _entry.ReminderMinutes = minutes;
        ReminderError = "";
        HasReminderError = false;
        OnPropertyChanged(nameof(Summary));
        _onChanged();
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
