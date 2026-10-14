using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using VibeSwitcher.App.Controls;
using VibeSwitcher.Models;

namespace VibeSwitcher.App.Dialogs;

// Port of Views/ScheduleWizardDialog.xaml.cs: time, days, reminder and silent, one step at a time.
// ScheduleResult is the edited entry (Windows calls it Result; here that's the yes/no answer);
// RemoveRequested when an existing schedule's Remove was clicked.
public partial class ScheduleWizardDialog : VsDialog
{
    private readonly bool _use12Hour;
    private readonly Guid _sourceId;
    private readonly bool _sourceEnabled;
    private readonly bool _isEditing;
    private int _currentStep;
    private bool _isPm;
    private bool _initializingReminderStep;

    private static readonly string[] ReminderLabels =
        ["None", "5 min before", "10 min before", "15 min before", "30 min before", "Custom…"];
    private static readonly int[] ReminderPresetMinutes = [0, 5, 10, 15, 30];

    private static readonly List<string> HourOptions24 = Enumerable.Range(0, 24).Select(h => h.ToString()).ToList();
    private static readonly List<string> HourOptions12 = Enumerable.Range(1, 12).Select(h => h.ToString()).ToList();
    private static readonly List<string> MinuteOptions = Enumerable.Range(0, 60).Select(m => m.ToString("D2")).ToList();
    private static readonly List<string> ReminderHrOptions = Enumerable.Range(0, 24).Select(h => h.ToString()).ToList();
    private static readonly List<string> ReminderMinOptions = Enumerable.Range(0, 60).Select(m => m.ToString("D2")).ToList();

    public ScheduleEntry? ScheduleResult { get; private set; }

    // Set when the user clicks "Remove" while editing an existing schedule.
    public bool RemoveRequested { get; private set; }

    private int _hour;
    private int _minute;
    private readonly List<DayOfWeek> _days;
    private int _reminderMinutes;
    private bool _silent;

    public ScheduleWizardDialog() : this(new ScheduleEntry(), false) { } // (for the XAML loader)

    public ScheduleWizardDialog(ScheduleEntry source, bool use12Hour, bool isEditing = false)
    {
        AvaloniaXamlLoader.Load(this);
        _use12Hour = use12Hour;
        _sourceId = source.Id;
        _sourceEnabled = source.Enabled;

        _hour = source.Hour;
        _minute = source.Minute;
        _days = new List<DayOfWeek>(source.Days);
        _reminderMinutes = source.ReminderMinutes;
        _silent = source.Silent;

        _isEditing = isEditing;
        if (isEditing)
        {
            Named<TextBlock>("HeaderTitle").Text = "Edit Schedule";
            Named<Button>("RemoveBtn").IsVisible = true;
            Named<Button>("CancelBtn").Margin = new Thickness(8, 0, 0, 0);
        }

        InitTimeStep();
        InitReminderStep();
        Named<CheckBox>("SilentCheck").IsChecked = _silent;
        GoToStep(0);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) CloseWith(false); };
    }

    private T Named<T>(string name) where T : Control => this.FindControl<T>(name)!;

    private void InitTimeStep()
    {
        var minute = Named<ComboBox>("MinuteCombo");
        var hour = Named<ComboBox>("HourCombo");
        var amPm = Named<Button>("AmPmButton");
        minute.ItemsSource = MinuteOptions;
        minute.SelectedIndex = _minute;

        if (_use12Hour)
        {
            hour.ItemsSource = HourOptions12;
            _isPm = _hour >= 12;
            var display = _hour == 0 ? 12 : _hour > 12 ? _hour - 12 : _hour;
            hour.SelectedIndex = display - 1;
            amPm.Content = _isPm ? "PM" : "AM";
            amPm.IsVisible = true;
        }
        else
        {
            hour.ItemsSource = HourOptions24;
            hour.SelectedIndex = _hour;
            amPm.IsVisible = false;
        }
    }

    private void InitReminderStep()
    {
        _initializingReminderStep = true;

        var hr = Named<ComboBox>("ReminderHrCombo");
        var min = Named<ComboBox>("ReminderMinCombo");
        var reminder = Named<ComboBox>("ReminderCombo");
        hr.ItemsSource = ReminderHrOptions;
        min.ItemsSource = ReminderMinOptions;
        reminder.ItemsSource = ReminderLabels;

        var presetIdx = Array.IndexOf(ReminderPresetMinutes, _reminderMinutes);
        if (presetIdx >= 0)
        {
            reminder.SelectedIndex = presetIdx;
            hr.SelectedIndex = 0;
            min.SelectedIndex = 0;
            Named<StackPanel>("CustomReminderPanel").IsVisible = false;
        }
        else
        {
            reminder.SelectedIndex = ReminderLabels.Length - 1;
            hr.SelectedIndex = Math.Min(_reminderMinutes / 60, 23);
            min.SelectedIndex = Math.Min(_reminderMinutes % 60, 59);
            Named<StackPanel>("CustomReminderPanel").IsVisible = true;
        }

        _initializingReminderStep = false;
        UpdateReminderTotal();
    }

    private static readonly (string Name, DayOfWeek Day)[] DayButtons =
    [
        ("MonBtn", DayOfWeek.Monday), ("TueBtn", DayOfWeek.Tuesday), ("WedBtn", DayOfWeek.Wednesday),
        ("ThuBtn", DayOfWeek.Thursday), ("FriBtn", DayOfWeek.Friday), ("SatBtn", DayOfWeek.Saturday),
        ("SunBtn", DayOfWeek.Sunday),
    ];

    private void InitDaysStep()
    {
        foreach (var (name, day) in DayButtons) Named<ToggleButton>(name).IsChecked = _days.Contains(day);
    }

    internal int CurrentStep => _currentStep;

    private void GoToStep(int step)
    {
        if (step == 1) InitDaysStep();

        _currentStep = step;
        for (int i = 0; i < 4; i++) Named<StackPanel>($"Step{i}Panel").IsVisible = step == i;

        (Named<TextBlock>("StepTitle").Text, Named<TextBlock>("StepSubtitle").Text) = step switch
        {
            0 => ("Set Schedule Time", "Choose when this schedule should activate."),
            1 => ("Choose Days", "Pick which days of the week this schedule repeats."),
            2 => ("Set a Reminder", "Optionally get notified before the switch happens."),
            _ => ("Notification", "Choose whether to show a banner when this schedule fires."),
        };

        Named<Button>("BackBtn").IsVisible = step > 0;
        // The save-early shortcut only makes sense when EDITING (tweak one step, save). A brand-new
        // schedule must be walked through every step so the user has seen all its choices before it
        // exists; on the last step "Next" is itself the save action.
        Named<Button>("SaveBtn").IsVisible = _isEditing && step < 3;
        Named<Button>("NextBtn").Content = step == 3 ? "Save ✓" : "Next →";

        for (int i = 0; i < 4; i++) Named<Ellipse>($"Dot{i}").Classes.Set("on", i <= step);

        if (step == 1) UpdateNoDaysHint();
    }

    private void CommitStep(int step)
    {
        switch (step)
        {
            case 0:
                var hourIndex = Named<ComboBox>("HourCombo").SelectedIndex;
                if (_use12Hour)
                {
                    var display = hourIndex + 1;
                    _hour = _isPm
                        ? (display == 12 ? 12 : display + 12)
                        : (display == 12 ? 0 : display);
                }
                else
                {
                    _hour = Math.Max(0, hourIndex);
                }
                _minute = Math.Max(0, Named<ComboBox>("MinuteCombo").SelectedIndex);
                break;

            case 1:
                _days.Clear();
                foreach (var (name, day) in DayButtons)
                    if (Named<ToggleButton>(name).IsChecked == true) _days.Add(day);
                break;

            case 2:
                var idx = Named<ComboBox>("ReminderCombo").SelectedIndex;
                if (idx >= 0 && idx < ReminderPresetMinutes.Length)
                {
                    _reminderMinutes = ReminderPresetMinutes[idx];
                }
                else
                {
                    var total = Named<ComboBox>("ReminderHrCombo").SelectedIndex * 60 + Named<ComboBox>("ReminderMinCombo").SelectedIndex;
                    _reminderMinutes = total >= 1 ? total : 0;
                }
                break;

            case 3:
                _silent = Named<CheckBox>("SilentCheck").IsChecked == true;
                break;
        }
    }

    private void Advance()
    {
        CommitStep(_currentStep);
        if (_currentStep < 3) GoToStep(_currentStep + 1);
        else Finish();
    }

    private void Finish()
    {
        ScheduleResult = new ScheduleEntry
        {
            Id = _sourceId,
            Enabled = _sourceEnabled,
            Hour = _hour,
            Minute = _minute,
            Days = _days,
            ReminderMinutes = _reminderMinutes,
            Silent = _silent,
        };
        CloseWith(true);
    }

    private void Next_Click(object? sender, RoutedEventArgs e) => Advance();

    // Commit the step the user is looking at and finish right away — the remaining steps keep their
    // current values (the existing ones when editing, defaults when adding).
    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        CommitStep(_currentStep);
        Finish();
    }

    private void Back_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentStep > 0) GoToStep(_currentStep - 1);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => CloseWith(false);

    private void Remove_Click(object? sender, RoutedEventArgs e)
    {
        RemoveRequested = true;
        CloseWith(true);
    }

    private void AmPm_Click(object? sender, RoutedEventArgs e)
    {
        _isPm = !_isPm;
        Named<Button>("AmPmButton").Content = _isPm ? "PM" : "AM";
    }

    private void ReminderCombo_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_initializingReminderStep) return;
        var isCustom = Named<ComboBox>("ReminderCombo").SelectedIndex == ReminderLabels.Length - 1;
        Named<StackPanel>("CustomReminderPanel").IsVisible = isCustom;
        if (isCustom) UpdateReminderTotal();
    }

    private void ReminderDropdowns_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_initializingReminderStep) return;
        UpdateReminderTotal();
    }

    private void UpdateReminderTotal()
    {
        var hr = this.FindControl<ComboBox>("ReminderHrCombo");
        var min = this.FindControl<ComboBox>("ReminderMinCombo");
        if (hr == null || min == null || hr.SelectedIndex < 0 || min.SelectedIndex < 0) return;
        var h = hr.SelectedIndex;
        var m = min.SelectedIndex;
        var total = h * 60 + m;
        Named<TextBlock>("ReminderTotalHint").Text = total == 0
            ? "Must be at least 1 minute — will be saved as No reminder"
            : h > 0
                ? $"{h} h {m:D2} min before the switch"
                : $"{m} min before the switch";
    }

    private void DayBtn_Changed(object? sender, RoutedEventArgs e) => UpdateNoDaysHint();

    private void UpdateNoDaysHint() =>
        Named<TextBlock>("NoDaysHint").IsVisible = !DayButtons.Any(d => Named<ToggleButton>(d.Name).IsChecked == true);
}
