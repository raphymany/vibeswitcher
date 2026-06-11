using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VibeSwitcher.Models;

namespace VibeSwitcher.Views;

public partial class ScheduleWizardDialog : Window
{
    private readonly bool _use12Hour;
    private readonly Guid _sourceId;
    private readonly bool _sourceEnabled;
    private int _currentStep;
    private bool _isPm;
    private bool _initializingReminderStep;

    private static readonly string[] ReminderLabels =
        ["None", "5 min before", "10 min before", "15 min before", "30 min before", "Custom…"];
    private static readonly int[] ReminderPresetMinutes = [0, 5, 10, 15, 30];

    private static readonly List<string> HourOptions24 =
        Enumerable.Range(0, 24).Select(h => h.ToString()).ToList();
    private static readonly List<string> HourOptions12 =
        Enumerable.Range(1, 12).Select(h => h.ToString()).ToList();
    private static readonly List<string> MinuteOptions =
        Enumerable.Range(0, 60).Select(m => m.ToString("D2")).ToList();
    private static readonly List<string> ReminderHrOptions =
        Enumerable.Range(0, 24).Select(h => h.ToString()).ToList();
    private static readonly List<string> ReminderMinOptions =
        Enumerable.Range(0, 60).Select(m => m.ToString("D2")).ToList();

    public ScheduleEntry? Result { get; private set; }

    // Set when the user clicks "Remove" while editing an existing schedule.
    public bool RemoveRequested { get; private set; }

    private int _hour;
    private int _minute;
    private readonly List<DayOfWeek> _days;
    private int _reminderMinutes;
    private bool _silent;

    public ScheduleWizardDialog(ScheduleEntry source, bool use12Hour, bool isEditing = false)
    {
        InitializeComponent();
        _use12Hour = use12Hour;
        _sourceId = source.Id;
        _sourceEnabled = source.Enabled;

        _hour = source.Hour;
        _minute = source.Minute;
        _days = new List<DayOfWeek>(source.Days);
        _reminderMinutes = source.ReminderMinutes;
        _silent = source.Silent;

        if (isEditing)
        {
            HeaderTitle.Text = "Edit Schedule";
            RemoveBtn.Visibility = Visibility.Visible;
        }

        InitTimeStep();
        InitReminderStep();
        InitSilentStep();
        GoToStep(0);
    }

    private void InitTimeStep()
    {
        MinuteCombo.ItemsSource = MinuteOptions;
        MinuteCombo.SelectedIndex = _minute;

        if (_use12Hour)
        {
            HourCombo.ItemsSource = HourOptions12;
            _isPm = _hour >= 12;
            var display = _hour == 0 ? 12 : _hour > 12 ? _hour - 12 : _hour;
            HourCombo.SelectedIndex = display - 1;
            AmPmButton.Content = _isPm ? "PM" : "AM";
            AmPmButton.Visibility = Visibility.Visible;
        }
        else
        {
            HourCombo.ItemsSource = HourOptions24;
            HourCombo.SelectedIndex = _hour;
            AmPmButton.Visibility = Visibility.Collapsed;
        }
    }

    private void InitReminderStep()
    {
        _initializingReminderStep = true;

        ReminderHrCombo.ItemsSource = ReminderHrOptions;
        ReminderMinCombo.ItemsSource = ReminderMinOptions;

        var presetIdx = Array.IndexOf(ReminderPresetMinutes, _reminderMinutes);
        if (presetIdx >= 0)
        {
            ReminderCombo.ItemsSource = ReminderLabels;
            ReminderCombo.SelectedIndex = presetIdx;
            ReminderHrCombo.SelectedIndex = 0;
            ReminderMinCombo.SelectedIndex = 0;
            CustomReminderPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            ReminderCombo.ItemsSource = ReminderLabels;
            ReminderCombo.SelectedIndex = ReminderLabels.Length - 1;
            ReminderHrCombo.SelectedIndex = Math.Min(_reminderMinutes / 60, 23);
            ReminderMinCombo.SelectedIndex = Math.Min(_reminderMinutes % 60, 59);
            CustomReminderPanel.Visibility = Visibility.Visible;
        }

        _initializingReminderStep = false;
        UpdateReminderTotal();
    }

    private void InitSilentStep()
    {
        SilentCheck.IsChecked = _silent;
    }

    private void InitDaysStep()
    {
        MonBtn.IsChecked = _days.Contains(DayOfWeek.Monday);
        TueBtn.IsChecked = _days.Contains(DayOfWeek.Tuesday);
        WedBtn.IsChecked = _days.Contains(DayOfWeek.Wednesday);
        ThuBtn.IsChecked = _days.Contains(DayOfWeek.Thursday);
        FriBtn.IsChecked = _days.Contains(DayOfWeek.Friday);
        SatBtn.IsChecked = _days.Contains(DayOfWeek.Saturday);
        SunBtn.IsChecked = _days.Contains(DayOfWeek.Sunday);
    }

    private void GoToStep(int step)
    {
        if (step == 1) InitDaysStep();

        _currentStep = step;

        Step0Panel.Visibility = step == 0 ? Visibility.Visible : Visibility.Collapsed;
        Step1Panel.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        Step2Panel.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        Step3Panel.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;

        (StepTitle.Text, StepSubtitle.Text) = step switch
        {
            0 => ("Set Schedule Time", "Choose when this schedule should activate."),
            1 => ("Choose Days", "Pick which days of the week this schedule repeats."),
            2 => ("Set a Reminder", "Optionally get notified before the switch happens."),
            _ => ("Notification", "Choose whether to show a banner when this schedule fires.")
        };

        BackBtn.Visibility = step > 0 ? Visibility.Visible : Visibility.Collapsed;
        SkipBtn.Visibility = step < 3 ? Visibility.Visible : Visibility.Collapsed;
        NextBtn.Content = step == 3 ? "Finish ✓" : "Next →";

        UpdateDots(step);

        if (step == 1) UpdateNoDaysHint();
    }

    private void UpdateDots(int active)
    {
        var on  = (Brush?)TryFindResource("Accent")      ?? Brushes.Orange;
        var off = (Brush?)TryFindResource("ChipBorder")  ?? Brushes.LightGray;
        Dot0.Fill = on;
        Dot1.Fill = active >= 1 ? on : off;
        Dot2.Fill = active >= 2 ? on : off;
        Dot3.Fill = active >= 3 ? on : off;
    }

    private void CommitStep(int step)
    {
        switch (step)
        {
            case 0:
                if (_use12Hour)
                {
                    var display = HourCombo.SelectedIndex + 1;
                    _hour = _isPm
                        ? (display == 12 ? 12 : display + 12)
                        : (display == 12 ? 0 : display);
                }
                else
                {
                    _hour = Math.Max(0, HourCombo.SelectedIndex);
                }
                _minute = Math.Max(0, MinuteCombo.SelectedIndex);
                break;

            case 1:
                _days.Clear();
                if (MonBtn.IsChecked == true) _days.Add(DayOfWeek.Monday);
                if (TueBtn.IsChecked == true) _days.Add(DayOfWeek.Tuesday);
                if (WedBtn.IsChecked == true) _days.Add(DayOfWeek.Wednesday);
                if (ThuBtn.IsChecked == true) _days.Add(DayOfWeek.Thursday);
                if (FriBtn.IsChecked == true) _days.Add(DayOfWeek.Friday);
                if (SatBtn.IsChecked == true) _days.Add(DayOfWeek.Saturday);
                if (SunBtn.IsChecked == true) _days.Add(DayOfWeek.Sunday);
                break;

            case 2:
                var idx = ReminderCombo.SelectedIndex;
                if (idx >= 0 && idx < ReminderPresetMinutes.Length)
                {
                    _reminderMinutes = ReminderPresetMinutes[idx];
                }
                else
                {
                    var total = ReminderHrCombo.SelectedIndex * 60 + ReminderMinCombo.SelectedIndex;
                    _reminderMinutes = total >= 1 ? total : 0;
                }
                break;

            case 3:
                _silent = SilentCheck.IsChecked == true;
                break;
        }
    }

    private void Advance(bool commit)
    {
        if (commit) CommitStep(_currentStep);
        if (_currentStep < 3)
            GoToStep(_currentStep + 1);
        else
            Finish();
    }

    private void Finish()
    {
        Result = new ScheduleEntry
        {
            Id = _sourceId,
            Enabled = _sourceEnabled,
            Hour = _hour,
            Minute = _minute,
            Days = _days,
            ReminderMinutes = _reminderMinutes,
            Silent = _silent
        };
        DialogResult = true;
    }

    private void Next_Click(object sender, RoutedEventArgs e) => Advance(commit: true);
    private void Skip_Click(object sender, RoutedEventArgs e) => Advance(commit: false);

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStep > 0) GoToStep(_currentStep - 1);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        RemoveRequested = true;
        DialogResult = true;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) DialogResult = false;
    }

    private void AmPm_Click(object sender, RoutedEventArgs e)
    {
        _isPm = !_isPm;
        AmPmButton.Content = _isPm ? "PM" : "AM";
    }

    private void ReminderCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_initializingReminderStep) return;
        var isCustom = ReminderCombo.SelectedIndex == ReminderLabels.Length - 1;
        CustomReminderPanel.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
        if (isCustom) UpdateReminderTotal();
    }

    private void ReminderDropdowns_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_initializingReminderStep) return;
        UpdateReminderTotal();
    }

    private void UpdateReminderTotal()
    {
        if (ReminderHrCombo.SelectedIndex < 0 || ReminderMinCombo.SelectedIndex < 0) return;
        var h = ReminderHrCombo.SelectedIndex;
        var m = ReminderMinCombo.SelectedIndex;
        var total = h * 60 + m;
        ReminderTotalHint.Text = total == 0
            ? "Must be at least 1 minute — will be saved as No reminder"
            : h > 0
                ? $"{h} h {m:D2} min before the switch"
                : $"{m} min before the switch";
    }

    private void DayBtn_Changed(object sender, RoutedEventArgs e) => UpdateNoDaysHint();

    private void UpdateNoDaysHint()
    {
        var any = MonBtn.IsChecked == true || TueBtn.IsChecked == true ||
                  WedBtn.IsChecked == true || ThuBtn.IsChecked == true ||
                  FriBtn.IsChecked == true || SatBtn.IsChecked == true ||
                  SunBtn.IsChecked == true;
        NoDaysHint.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
    }
}
