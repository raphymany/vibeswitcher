using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using VibeSwitcher.App.Controls;
using VibeSwitcher.Helpers;
using VibeSwitcher.Models;
using VibeSwitcher.Services;

namespace VibeSwitcher.App.Dialogs;

// Port of Views/CloneProfileDialog.xaml.cs: the multi-step "clone this profile" wizard. The user
// chooses the copy's name, hotkey, devices, mode, which optional features carry over and which
// schedules come along; ClonedProfile is the new DeviceProfile (fresh Id) for the caller to save.
// (Windows calls it Result; here that's the dialog's yes/no answer.)
public partial class CloneProfileDialog : VsDialog
{
    private readonly DeviceProfile _source;
    private readonly bool _use12Hour;
    private readonly Func<HotkeyDefinition, HotkeyDefinition?> _captureHotkey;

    private HotkeyDefinition _hotkey = new();               // clone starts with no hotkey
    private readonly List<(ScheduleEntry entry, CheckBox box)> _scheduleChecks = new();
    private readonly List<int> _steps = new();              // active panel indices, in order
    private int _pos;                                        // cursor into _steps
    private readonly bool _built;

    public DeviceProfile? ClonedProfile { get; private set; }

    public CloneProfileDialog() : this(new DeviceProfile(), [], [], false, h => h) { } // (for the XAML loader)

    public CloneProfileDialog(
        DeviceProfile source,
        IReadOnlyList<AudioDeviceInfo> playbackDevices,
        IReadOnlyList<AudioDeviceInfo> recordingDevices,
        bool use12Hour,
        Func<HotkeyDefinition, HotkeyDefinition?> captureHotkey)
    {
        AvaloniaXamlLoader.Load(this);
        _source = source;
        _use12Hour = use12Hour;
        _captureHotkey = captureHotkey;

        Named<TextBlock>("HeaderSubtitle").Text = $"Make a copy of \"{source.Name}\" and choose what comes with it.";
        Named<TextBox>("NameBox").Text = source.Name + " (copy)";

        // Devices
        var playback = Named<ComboBox>("PlaybackCombo");
        var recording = Named<ComboBox>("RecordingCombo");
        playback.ItemsSource = playbackDevices;
        recording.ItemsSource = recordingDevices;
        playback.SelectedItem = playbackDevices.FirstOrDefault(d => d.Id == source.PlaybackDeviceId) ?? playbackDevices.FirstOrDefault();
        recording.SelectedItem = recordingDevices.FirstOrDefault(d => d.Id == source.RecordingDeviceId) ?? recordingDevices.FirstOrDefault();

        // Mode (its change handler sets which device lists can be used)
        _built = true;
        Named<RadioButton>(source.Mode switch
        {
            ProfileMode.Playback => "ModePlayback",
            ProfileMode.Recording => "ModeRecording",
            _ => "ModeBoth",
        }).IsChecked = true;

        BuildCopyOptions();
        BuildScheduleChecks();

        // Steps: name/hotkey + devices + what-to-copy are always shown; schedules only if any exist.
        _steps.Add(0);
        _steps.Add(1);
        _steps.Add(2);
        if (_source.Schedules.Count > 0) _steps.Add(3);

        GoTo(0);

        // Focus on the name field (its "(copy)" text selected), so a keyboard user can rename the
        // clone right away.
        Opened += (_, _) =>
        {
            var name = Named<TextBox>("NameBox");
            name.Focus();
            name.SelectAll();
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) CloseWith(false); };
    }

    private T Named<T>(string name) where T : Control => this.FindControl<T>(name)!;

    private static readonly string[] Options = ["chkSound", "chkAppTriggers", "chkAutoSwitch", "chkFavorite", "chkSilent", "chkIcon", "chkNotes"];

    private void BuildCopyOptions()
    {
        // Only show options the source actually has; default each visible one to checked.
        ConfigureOption("chkSound", _source.SoundOverride);
        ConfigureOption("chkAppTriggers", _source.AppTriggers.Count > 0);
        ConfigureOption("chkAutoSwitch", _source.TriggerOnConnect);
        ConfigureOption("chkFavorite", _source.IsPinned);
        ConfigureOption("chkSilent", _source.Silent);
        ConfigureOption("chkIcon", !string.IsNullOrEmpty(_source.IconPath));
        ConfigureOption("chkNotes", !string.IsNullOrWhiteSpace(_source.Notes));

        bool any = Options.Any(o => Named<CheckBox>(o).IsVisible);
        Named<StackPanel>("CopyOptionsPanel").IsVisible = any;
        Named<TextBlock>("NoCopyOptionsHint").IsVisible = !any;

        UpdateSilentEnabled();
    }

    private void ConfigureOption(string name, bool applicable)
    {
        var box = Named<CheckBox>(name);
        box.IsVisible = applicable;
        box.IsChecked = applicable;
    }

    // Mirrors the profile card rule: when a switch sound is copied, the per-profile silent banner
    // toggle doesn't apply (the sound has its own banner setting), so it's disabled.
    private void Sound_Toggled(object? sender, RoutedEventArgs e) => UpdateSilentEnabled();

    private void UpdateSilentEnabled()
    {
        var silent = this.FindControl<CheckBox>("chkSilent");
        if (silent is not { IsVisible: true }) return;
        var sound = Named<CheckBox>("chkSound");
        bool soundOn = sound.IsVisible && sound.IsChecked == true;
        silent.IsEnabled = !soundOn;
        if (soundOn) silent.IsChecked = false;
    }

    private void BuildScheduleChecks()
    {
        var panel = Named<StackPanel>("SchedulesPanel");
        var theme = this.FindResource("ThemedCheckBox") as Avalonia.Styling.ControlTheme;
        foreach (var entry in _source.Schedules)
        {
            var label = $"{ScheduleHelpers.FormatTime(entry.Hour, entry.Minute, _use12Hour)} · {ScheduleHelpers.FormatDays(entry.Days)}";
            var box = new CheckBox { Content = label, IsChecked = true, Theme = theme };
            _scheduleChecks.Add((entry, box));
            panel.Children.Add(box);
        }
    }

    // ── Navigation ─────────────────────────────────────────────────────────────────────────────────

    internal int Step => _steps[_pos];

    private void GoTo(int pos)
    {
        _pos = pos;
        int step = _steps[pos];

        for (int i = 0; i < 4; i++) Named<StackPanel>($"Step{i}Panel").IsVisible = step == i;

        (Named<TextBlock>("StepTitle").Text, Named<TextBlock>("StepSubtitle").Text) = step switch
        {
            0 => ("Name & Hotkey", "Name the copy and, if you want, give it its own hotkey."),
            1 => ("Devices & Mode", "Choose what this profile switches to — change the devices or mode if you like."),
            2 => ("What to Copy", "Pick which of the original's features carry over to the copy."),
            _ => ("Schedules", "Choose which of the original's schedules to copy over."),
        };

        Named<Button>("BackBtn").IsVisible = pos > 0;
        Named<Button>("NextBtn").Content = pos == _steps.Count - 1 ? "Create ✓" : "Next →";
        UpdateDots();
    }

    private void UpdateDots()
    {
        for (int i = 0; i < 4; i++)
        {
            var dot = Named<Ellipse>($"Dot{i}");
            dot.IsVisible = i < _steps.Count;
            dot.Classes.Set("on", i <= _pos);
        }
    }

    private void Next_Click(object? sender, RoutedEventArgs e)
    {
        if (_pos < _steps.Count - 1) GoTo(_pos + 1);
        else Finish();
    }

    private void Back_Click(object? sender, RoutedEventArgs e)
    {
        if (_pos > 0) GoTo(_pos - 1);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => CloseWith(false);

    // ── Step 0 — hotkey ────────────────────────────────────────────────────────────────────────────

    private void SetHotkey_Click(object? sender, RoutedEventArgs e)
    {
        var captured = _captureHotkey(_hotkey);
        if (captured == null) return; // cancelled / conflict-abandoned
        _hotkey = captured;
        Named<TextBlock>("HotkeyLabel").Text = _hotkey.IsEmpty ? "Not set" : _hotkey.ToDisplayString();
    }

    private void ClearHotkey_Click(object? sender, RoutedEventArgs e)
    {
        _hotkey = new HotkeyDefinition();
        Named<TextBlock>("HotkeyLabel").Text = "Not set";
    }

    // ── Step 1 — mode/device enabling ──────────────────────────────────────────────────────────────

    private void Mode_Changed(object? sender, RoutedEventArgs e)
    {
        // (Not while the XAML is still being built. Also on a pill being unchecked: Avalonia unchecks the
        // previous one after the new one reports, so the state is only final then.)
        if (!_built) return;
        bool playback = Named<RadioButton>("ModePlayback").IsChecked == true || Named<RadioButton>("ModeBoth").IsChecked == true;
        bool recording = Named<RadioButton>("ModeRecording").IsChecked == true || Named<RadioButton>("ModeBoth").IsChecked == true;
        Named<ComboBox>("PlaybackCombo").IsEnabled = playback;
        Named<ComboBox>("RecordingCombo").IsEnabled = recording;
        Named<TextBlock>("PlaybackLabel").Opacity = playback ? 1.0 : 0.5;
        Named<TextBlock>("RecordingLabel").Opacity = recording ? 1.0 : 0.5;
    }

    private ProfileMode SelectedMode() =>
        Named<RadioButton>("ModePlayback").IsChecked == true ? ProfileMode.Playback :
        Named<RadioButton>("ModeRecording").IsChecked == true ? ProfileMode.Recording : ProfileMode.Both;

    // ── Build the result ───────────────────────────────────────────────────────────────────────────

    private void Finish()
    {
        var mode = SelectedMode();
        var name = Named<TextBox>("NameBox").Text;
        var clone = new DeviceProfile
        {
            Name = string.IsNullOrWhiteSpace(name) ? _source.Name + " (copy)" : name.Trim(),
            Mode = mode,
            PlaybackDeviceId = mode == ProfileMode.Recording ? null : DeviceId("PlaybackCombo"),
            RecordingDeviceId = mode == ProfileMode.Playback ? null : DeviceId("RecordingCombo"),
            Hotkey = _hotkey,
        };

        if (IsOn("chkSound"))
        {
            clone.SoundOverride = true;
            clone.SoundShowBanner = _source.SoundShowBanner;
            clone.SoundTone = _source.SoundTone;
            clone.SoundCustomPath = _source.SoundCustomPath;
            clone.SoundVolume = _source.SoundVolume;
        }
        if (IsOn("chkAppTriggers")) clone.AppTriggers = new List<string>(_source.AppTriggers);
        if (IsOn("chkAutoSwitch")) clone.TriggerOnConnect = true;
        if (IsOn("chkFavorite")) clone.IsPinned = true;
        if (IsOn("chkSilent")) clone.Silent = true;
        if (IsOn("chkIcon")) clone.IconPath = _source.IconPath; // caller copies the file to a fresh path
        if (IsOn("chkNotes")) clone.Notes = _source.Notes;

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

        ClonedProfile = clone;
        CloseWith(true);
    }

    private bool IsOn(string name) => Named<CheckBox>(name) is { IsVisible: true, IsChecked: true };

    private string? DeviceId(string combo)
    {
        var id = (Named<ComboBox>(combo).SelectedItem as AudioDeviceInfo)?.Id;
        return string.IsNullOrEmpty(id) ? null : id;
    }
}
