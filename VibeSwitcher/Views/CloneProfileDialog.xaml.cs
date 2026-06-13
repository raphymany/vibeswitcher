using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using VibeSwitcher.Helpers;
using VibeSwitcher.Models;
using VibeSwitcher.Services;

namespace VibeSwitcher.Views;

// Multi-step "clone this profile" wizard. Lets the user choose the copy's name, hotkey, devices,
// mode, which optional features carry over, and which schedules to bring along. Builds the new
// DeviceProfile (with a fresh Id) into Result; the caller persists it.
public partial class CloneProfileDialog : Window
{
    private readonly DeviceProfile _source;
    private readonly bool _use12Hour;
    private readonly Func<HotkeyDefinition, HotkeyDefinition?> _captureHotkey;

    private HotkeyDefinition _hotkey = new();                 // clone starts with no hotkey
    private readonly List<(ScheduleEntry entry, CheckBox box)> _scheduleChecks = new();
    private readonly List<int> _steps = new();                // active panel indices, in order
    private int _pos;                                          // cursor into _steps

    public DeviceProfile? Result { get; private set; }

    public CloneProfileDialog(
        DeviceProfile source,
        IReadOnlyList<AudioDeviceInfo> playbackDevices,
        IReadOnlyList<AudioDeviceInfo> recordingDevices,
        bool use12Hour,
        Func<HotkeyDefinition, HotkeyDefinition?> captureHotkey)
    {
        InitializeComponent();
        _source = source;
        _use12Hour = use12Hour;
        _captureHotkey = captureHotkey;

        HeaderSubtitle.Text = $"Make a copy of \"{source.Name}\" and choose what comes with it.";
        NameBox.Text = source.Name + " (copy)";

        // Devices
        PlaybackCombo.ItemsSource = playbackDevices;
        RecordingCombo.ItemsSource = recordingDevices;
        PlaybackCombo.SelectedItem =
            playbackDevices.FirstOrDefault(d => d.Id == source.PlaybackDeviceId) ?? playbackDevices.FirstOrDefault();
        RecordingCombo.SelectedItem =
            recordingDevices.FirstOrDefault(d => d.Id == source.RecordingDeviceId) ?? recordingDevices.FirstOrDefault();

        // Mode (Checked handler updates the device-combo enabled state)
        (source.Mode switch
        {
            ProfileMode.Playback  => ModePlayback,
            ProfileMode.Recording => ModeRecording,
            _                     => ModeBoth,
        }).IsChecked = true;

        BuildCopyOptions();
        BuildScheduleChecks();

        // Steps: name/hotkey + devices + what-to-copy are always shown; schedules only if any exist.
        _steps.Add(0);
        _steps.Add(1);
        _steps.Add(2);
        if (_source.Schedules.Count > 0) _steps.Add(3);
        Dot3.Visibility = _steps.Contains(3) ? Visibility.Visible : Visibility.Collapsed;

        GoTo(0);
    }

    private void BuildCopyOptions()
    {
        // Only show options the source actually has; default each visible one to checked.
        ConfigureOption(chkSound,       _source.SoundOverride);
        ConfigureOption(chkAppTriggers, _source.AppTriggers.Count > 0);
        ConfigureOption(chkAutoSwitch,  _source.TriggerOnConnect);
        ConfigureOption(chkFavorite,    _source.IsPinned);
        ConfigureOption(chkSilent,      _source.Silent);
        ConfigureOption(chkIcon,        !string.IsNullOrEmpty(_source.IconPath));
        ConfigureOption(chkNotes,       !string.IsNullOrWhiteSpace(_source.Notes));

        bool any = chkSound.Visibility == Visibility.Visible || chkAppTriggers.Visibility == Visibility.Visible
                   || chkAutoSwitch.Visibility == Visibility.Visible || chkFavorite.Visibility == Visibility.Visible
                   || chkSilent.Visibility == Visibility.Visible || chkIcon.Visibility == Visibility.Visible
                   || chkNotes.Visibility == Visibility.Visible;
        CopyOptionsPanel.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        NoCopyOptionsHint.Visibility = any ? Visibility.Collapsed : Visibility.Visible;

        UpdateSilentEnabled();
    }

    private static void ConfigureOption(CheckBox box, bool applicable)
    {
        box.Visibility = applicable ? Visibility.Visible : Visibility.Collapsed;
        box.IsChecked = applicable;
    }

    // Mirrors the profile card rule: when a switch sound is copied, the per-profile silent banner
    // toggle doesn't apply (the sound has its own banner setting), so it's disabled.
    private void Sound_Toggled(object sender, RoutedEventArgs e) => UpdateSilentEnabled();

    private void UpdateSilentEnabled()
    {
        if (chkSilent.Visibility != Visibility.Visible) return;
        bool soundOn = chkSound.Visibility == Visibility.Visible && chkSound.IsChecked == true;
        chkSilent.IsEnabled = !soundOn;
        if (soundOn) chkSilent.IsChecked = false;
    }

    private void BuildScheduleChecks()
    {
        var style = (Style?)TryFindResource("CopyCheck");
        foreach (var entry in _source.Schedules)
        {
            var label = $"{ScheduleHelpers.FormatTime(entry.Hour, entry.Minute, _use12Hour)} · {ScheduleHelpers.FormatDays(entry.Days)}";
            var box = new CheckBox { Content = label, IsChecked = true };
            if (style != null) box.Style = style;
            _scheduleChecks.Add((entry, box));
            SchedulesPanel.Children.Add(box);
        }
    }

    // ── Navigation ─────────────────────────────────────────────────────────────

    private void GoTo(int pos)
    {
        _pos = pos;
        int step = _steps[pos];

        Step0Panel.Visibility = step == 0 ? Visibility.Visible : Visibility.Collapsed;
        Step1Panel.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        Step2Panel.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        Step3Panel.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;

        (StepTitle.Text, StepSubtitle.Text) = step switch
        {
            0 => ("Name & Hotkey", "Name the copy and, if you want, give it its own hotkey."),
            1 => ("Devices & Mode", "Choose what this profile switches to — change the devices or mode if you like."),
            2 => ("What to Copy", "Pick which of the original's features carry over to the copy."),
            _ => ("Schedules", "Choose which of the original's schedules to copy over."),
        };

        BackBtn.Visibility = pos > 0 ? Visibility.Visible : Visibility.Collapsed;
        NextBtn.Content = pos == _steps.Count - 1 ? "Create ✓" : "Next →";
        UpdateDots();
    }

    private void UpdateDots()
    {
        var on  = (Brush?)TryFindResource("Accent")     ?? Brushes.Orange;
        var off = (Brush?)TryFindResource("ChipBorder") ?? Brushes.LightGray;
        Ellipse[] dots = { Dot0, Dot1, Dot2, Dot3 };
        for (int i = 0; i < dots.Length; i++)
        {
            if (i >= _steps.Count) { dots[i].Visibility = Visibility.Collapsed; continue; }
            dots[i].Visibility = Visibility.Visible;
            dots[i].Fill = i <= _pos ? on : off;
        }
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_pos < _steps.Count - 1) GoTo(_pos + 1);
        else Finish();
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_pos > 0) GoTo(_pos - 1);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) DialogResult = false;
    }

    // ── Step 0 — hotkey ────────────────────────────────────────────────────────

    private void SetHotkey_Click(object sender, RoutedEventArgs e)
    {
        var captured = _captureHotkey(_hotkey);
        if (captured == null) return; // cancelled / conflict-abandoned
        _hotkey = captured;
        HotkeyLabel.Text = _hotkey.IsEmpty ? "None" : _hotkey.ToDisplayString();
    }

    private void ClearHotkey_Click(object sender, RoutedEventArgs e)
    {
        _hotkey = new HotkeyDefinition();
        HotkeyLabel.Text = "None";
    }

    // ── Step 1 — mode/device enabling ──────────────────────────────────────────

    private void Mode_Changed(object sender, RoutedEventArgs e)
    {
        // Step1 controls aren't created yet during the initial Mode IsChecked set, so guard.
        if (PlaybackCombo == null) return;
        bool playback = ModePlayback.IsChecked == true || ModeBoth.IsChecked == true;
        bool recording = ModeRecording.IsChecked == true || ModeBoth.IsChecked == true;
        PlaybackCombo.IsEnabled = playback;
        RecordingCombo.IsEnabled = recording;
        PlaybackLabel.Opacity = playback ? 1.0 : 0.5;
        RecordingLabel.Opacity = recording ? 1.0 : 0.5;
    }

    private ProfileMode SelectedMode() =>
        ModePlayback.IsChecked == true ? ProfileMode.Playback :
        ModeRecording.IsChecked == true ? ProfileMode.Recording : ProfileMode.Both;

    // ── Build the result ───────────────────────────────────────────────────────

    private void Finish()
    {
        var mode = SelectedMode();
        var clone = new DeviceProfile
        {
            Name = string.IsNullOrWhiteSpace(NameBox.Text) ? _source.Name + " (copy)" : NameBox.Text.Trim(),
            Mode = mode,
            PlaybackDeviceId  = mode == ProfileMode.Recording ? null : DeviceId(PlaybackCombo),
            RecordingDeviceId = mode == ProfileMode.Playback  ? null : DeviceId(RecordingCombo),
            Hotkey = _hotkey,
        };

        if (IsOn(chkSound))
        {
            clone.SoundOverride   = true;
            clone.SoundShowBanner = _source.SoundShowBanner;
            clone.SoundTone       = _source.SoundTone;
            clone.SoundCustomPath = _source.SoundCustomPath;
            clone.SoundVolume     = _source.SoundVolume;
        }
        if (IsOn(chkAppTriggers)) clone.AppTriggers = new List<string>(_source.AppTriggers);
        if (IsOn(chkAutoSwitch))  clone.TriggerOnConnect = true;
        if (IsOn(chkFavorite))    clone.IsPinned = true;
        if (IsOn(chkSilent))      clone.Silent = true;
        if (IsOn(chkIcon))        clone.IconPath = _source.IconPath; // caller copies the file to a fresh path
        if (IsOn(chkNotes))       clone.Notes = _source.Notes;

        foreach (var (entry, box) in _scheduleChecks)
        {
            if (box.IsChecked != true) continue;
            clone.Schedules.Add(new ScheduleEntry
            {
                Enabled = entry.Enabled,
                Hour = entry.Hour,
                Minute = entry.Minute,
                Days = new List<DayOfWeek>(entry.Days),
                ReminderMinutes = entry.ReminderMinutes,
                Silent = entry.Silent,
            });
        }

        Result = clone;
        DialogResult = true;
    }

    private static bool IsOn(CheckBox box) => box.Visibility == Visibility.Visible && box.IsChecked == true;

    private static string? DeviceId(ComboBox combo)
    {
        var id = (combo.SelectedItem as AudioDeviceInfo)?.Id;
        return string.IsNullOrEmpty(id) ? null : id;
    }
}
