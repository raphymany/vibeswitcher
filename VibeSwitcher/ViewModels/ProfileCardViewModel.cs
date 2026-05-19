using System.Collections.ObjectModel;
using System.Windows.Media;
using VibeSwitcher.Helpers;
using VibeSwitcher.Models;
using VibeSwitcher.Services;
using System.Windows.Input;

namespace VibeSwitcher.ViewModels;

public class ProfileCardViewModel : ViewModelBase, IDisposable
{
    private static readonly AudioDeviceInfo NoneDevice = new AudioDeviceInfo("", "(None)", false);

    private readonly DeviceProfile _model;
    private readonly IConfigService _configService;
    private readonly IHotkeyService _hotkeyService;
    private readonly IDialogService _dialogService;
    private readonly Action<ProfileCardViewModel> _onChanged;
    private readonly Action<ProfileCardViewModel> _onDelete;
    private readonly Action<ProfileCardViewModel> _onClone;
    private readonly Func<string, Task> _onTestSound;

    private string _name;
    private AudioDeviceInfo? _selectedPlaybackDevice;
    private AudioDeviceInfo? _selectedRecordingDevice;
    private string _hotkeyDisplay;
    private string? _iconPath;
    private ImageSource? _iconPreview;
    private bool _loadingDevices;

    public DeviceProfile Model => _model;

    public string Name
    {
        get => _name;
        set
        {
            if (SetField(ref _name, value))
            {
                _model.Name = value;
                _onChanged(this);
            }
        }
    }

    public System.Windows.Visibility PlaybackVisible =>
        _model.Mode is ProfileMode.Playback or ProfileMode.Both
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;

    public System.Windows.Visibility RecordingVisible =>
        _model.Mode is ProfileMode.Recording or ProfileMode.Both
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;

    public string ModeLabel => _model.Mode switch
    {
        ProfileMode.Playback  => "Playback Only",
        ProfileMode.Recording => "Recording Only",
        ProfileMode.Both      => "Both Devices",
        _                     => ""
    };

    public ObservableCollection<AudioDeviceInfo> PlaybackDevices { get; }
    public ObservableCollection<AudioDeviceInfo> RecordingDevices { get; }

    public AudioDeviceInfo? SelectedPlaybackDevice
    {
        get => _selectedPlaybackDevice;
        set
        {
            if (SetField(ref _selectedPlaybackDevice, value) && !_loadingDevices)
            {
                _model.PlaybackDeviceId = string.IsNullOrEmpty(value?.Id) ? null : value.Id;
                _onChanged(this);
                OnPropertyChanged(nameof(IsPlaybackDeviceSet));
            }
        }
    }

    public AudioDeviceInfo? SelectedRecordingDevice
    {
        get => _selectedRecordingDevice;
        set
        {
            if (SetField(ref _selectedRecordingDevice, value) && !_loadingDevices)
            {
                _model.RecordingDeviceId = string.IsNullOrEmpty(value?.Id) ? null : value.Id;
                _onChanged(this);
                OnPropertyChanged(nameof(IsRecordingDeviceSet));
            }
        }
    }

    public bool IsPlaybackDeviceSet =>
        !string.IsNullOrEmpty(_selectedPlaybackDevice?.Id);

    public bool IsRecordingDeviceSet =>
        !string.IsNullOrEmpty(_selectedRecordingDevice?.Id);

    public string HotkeyDisplay
    {
        get => _hotkeyDisplay;
        private set => SetField(ref _hotkeyDisplay, value);
    }

    public string? IconPath
    {
        get => _iconPath;
        set
        {
            if (SetField(ref _iconPath, value))
            {
                _model.IconPath = value;
                UpdateIconPreview();
                _onChanged(this);
            }
        }
    }

    public ImageSource? IconPreview
    {
        get => _iconPreview;
        private set => SetField(ref _iconPreview, value);
    }

    public bool Silent
    {
        get => _model.Silent;
        set
        {
            if (_model.Silent == value) return;
            _model.Silent = value;
            OnPropertyChanged(nameof(Silent));
            _onChanged(this);
        }
    }

    public bool VolumeOverrideEnabled
    {
        get => _model.VolumeOverrideEnabled;
        set
        {
            if (_model.VolumeOverrideEnabled == value) return;
            _model.VolumeOverrideEnabled = value;
            OnPropertyChanged(nameof(VolumeOverrideEnabled));
            _onChanged(this);
        }
    }

    public int Volume
    {
        get => _model.Volume;
        set
        {
            int clamped = Math.Clamp(value, 0, 100);
            if (_model.Volume == clamped) return;
            _model.Volume = clamped;
            OnPropertyChanged(nameof(Volume));
            OnPropertyChanged(nameof(VolumeDisplay));
            _onChanged(this);
        }
    }

    public string VolumeDisplay => $"{_model.Volume}%";

    public ICommand CaptureHotkeyCommand { get; }
    public ICommand BrowseIconCommand { get; }
    public ICommand CloneCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand TestSoundCommand { get; }
    public ICommand TestMicCommand { get; }

    public ProfileCardViewModel(
        DeviceProfile model,
        IConfigService configService,
        IHotkeyService hotkeyService,
        IDialogService dialogService,
        IReadOnlyList<AudioDeviceInfo> playbackDevices,
        IReadOnlyList<AudioDeviceInfo> recordingDevices,
        Action<ProfileCardViewModel> onChanged,
        Action<ProfileCardViewModel> onDelete,
        Action<ProfileCardViewModel> onClone,
        Func<string, Task> onTestSound)
    {
        _model = model;
        _configService = configService;
        _hotkeyService = hotkeyService;
        _dialogService = dialogService;
        _onChanged = onChanged;
        _onDelete = onDelete;
        _onClone = onClone;
        _onTestSound = onTestSound;

        _name = model.Name;
        _hotkeyDisplay = model.Hotkey.ToDisplayString();
        _iconPath = model.IconPath;

        PlaybackDevices = new ObservableCollection<AudioDeviceInfo> { NoneDevice };
        foreach (var d in playbackDevices) PlaybackDevices.Add(d);
        RecordingDevices = new ObservableCollection<AudioDeviceInfo> { NoneDevice };
        foreach (var d in recordingDevices) RecordingDevices.Add(d);

        _selectedPlaybackDevice = string.IsNullOrEmpty(model.PlaybackDeviceId)
            ? NoneDevice
            : PlaybackDevices.FirstOrDefault(d => d.Id == model.PlaybackDeviceId) ?? NoneDevice;
        _selectedRecordingDevice = string.IsNullOrEmpty(model.RecordingDeviceId)
            ? NoneDevice
            : RecordingDevices.FirstOrDefault(d => d.Id == model.RecordingDeviceId) ?? NoneDevice;

        UpdateIconPreview();

        CaptureHotkeyCommand = new RelayCommand(CaptureHotkey);
        BrowseIconCommand = new RelayCommand(BrowseIcon);
        CloneCommand = new RelayCommand(() => _onClone(this));
        DeleteCommand = new RelayCommand(DeleteProfile);
        TestSoundCommand = new RelayCommand(() => _ = TestSound());
        TestMicCommand = new RelayCommand(TestMic);
    }

    private async Task TestSound()
    {
        var deviceId = _selectedPlaybackDevice?.Id;
        if (string.IsNullOrEmpty(deviceId)) return;
        try { await _onTestSound(deviceId); }
        catch (Exception ex) { AppLogger.Warning("ProfileCardViewModel.TestSound", ex.Message); }
    }

    private void TestMic()
    {
        var deviceId = _selectedRecordingDevice?.Id;
        if (string.IsNullOrEmpty(deviceId)) return;
        var deviceName = _selectedRecordingDevice?.FriendlyName ?? deviceId;
        _dialogService.ShowMicTest(deviceId, deviceName);
    }

    // Called by SettingsViewModel once async device enumeration completes.
    // _loadingDevices prevents the TwoWay ComboBox binding from writing null back into the model
    // when Clear() removes the previously selected item — without the guard, the saved device ID
    // gets wiped and a config save fires before the real devices are even added back.
    public void LoadDevices(IReadOnlyList<AudioDeviceInfo> playback, IReadOnlyList<AudioDeviceInfo> recording)
    {
        _loadingDevices = true;
        try
        {
            PlaybackDevices.Clear();
            PlaybackDevices.Add(NoneDevice);
            foreach (var d in playback) PlaybackDevices.Add(d);
            RecordingDevices.Clear();
            RecordingDevices.Add(NoneDevice);
            foreach (var d in recording) RecordingDevices.Add(d);

            _selectedPlaybackDevice = string.IsNullOrEmpty(_model.PlaybackDeviceId)
                ? NoneDevice
                : PlaybackDevices.FirstOrDefault(d => d.Id == _model.PlaybackDeviceId) ?? NoneDevice;
            _selectedRecordingDevice = string.IsNullOrEmpty(_model.RecordingDeviceId)
                ? NoneDevice
                : RecordingDevices.FirstOrDefault(d => d.Id == _model.RecordingDeviceId) ?? NoneDevice;
        }
        finally
        {
            _loadingDevices = false;
        }
        OnPropertyChanged(nameof(SelectedPlaybackDevice));
        OnPropertyChanged(nameof(SelectedRecordingDevice));
        OnPropertyChanged(nameof(IsPlaybackDeviceSet));
        OnPropertyChanged(nameof(IsRecordingDeviceSet));
    }

    private void CaptureHotkey()
    {
        // Unregister ALL hotkeys so no profile or settings hotkey can fire while the dialog is open.
        // Without this, Windows intercepts registered keys before WPF sees them.
        _hotkeyService.UnregisterAll();

        bool applied = false;
        var dialogSeed = _model.Hotkey; // what the capture dialog opens with

        while (true)
        {
            var captured = _dialogService.ShowHotkeyCapture(dialogSeed);
            if (captured == null) break; // cancelled

            if (!captured.IsEmpty)
            {
                var internalOwner = FindInternalConflictOwner(captured);
                if (internalOwner != null)
                {
                    bool retry = _dialogService.ShowHotkeyConflictRetry("Hotkey Already in Use",
                        $"'{captured.ToDisplayString()}' is already assigned to {internalOwner}.");
                    if (retry) { dialogSeed = captured; continue; }
                    break;
                }

                if (_hotkeyService.TestHotkey(captured))
                {
                    bool retry = _dialogService.ShowHotkeyConflictRetry("Hotkey Conflict",
                        $"'{captured.ToDisplayString()}' is already in use by another application.");
                    if (retry) { dialogSeed = captured; continue; }
                    break;
                }
            }

            _model.Hotkey = captured;
            HotkeyDisplay = _model.Hotkey.ToDisplayString();
            _onChanged(this); // triggers ReregisterHotkeys → re-registers all hotkeys
            applied = true;
            break;
        }

        if (!applied)
        {
            // Restore all hotkeys (profiles + Settings) that were unregistered above.
            _hotkeyService.RegisterAll(_configService.Current.Profiles);
            var settingsHk = _configService.Current.SettingsHotkey;
            if (settingsHk is { IsEmpty: false })
                _hotkeyService.RegisterSettingsHotkey(settingsHk);
        }
    }

    private string? FindInternalConflictOwner(HotkeyDefinition hotkey)
    {
        foreach (var p in _configService.Current.Profiles)
        {
            if (p.Id == _model.Id) continue;
            if (!p.Hotkey.IsEmpty && hotkey.Matches(p.Hotkey))
                return $"\"{p.Name}\"";
        }
        var settingsHk = _configService.Current.SettingsHotkey;
        if (settingsHk is { IsEmpty: false } && hotkey.Matches(settingsHk))
            return "the Settings shortcut";
        return null;
    }

    private void BrowseIcon()
    {
        var source = _dialogService.ShowBrowseIconFile();
        if (source == null) return;

        var namePrefix = SanitizeName(_model.Name);
        var guidPrefix = _model.Id.ToString("N")[..8];
        var dest = System.IO.Path.Combine(_configService.IconsDir, $"{namePrefix}-{guidPrefix}.ico");

        if (!string.Equals(source, dest, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                System.IO.Directory.CreateDirectory(_configService.IconsDir);
                System.IO.File.Copy(source, dest, overwrite: true);
            }
            catch (Exception ex)
            {
                AppLogger.Error("ProfileCardViewModel.BrowseIcon", ex);
                SessionErrorTracker.Record(ErrorCode.IconCopyFailed, "Icon Copy Failed",
                    $"Could not copy icon file to app storage: {ex.Message}");
                _dialogService.ShowAlert("Icon Error", $"Could not copy the icon file:\n{ex.Message}");
                return;
            }
        }

        // Delete the old icon from IconsDir if we're replacing it with a new copy
        var previous = _iconPath;
        var iconsPrefix = _configService.IconsDir + System.IO.Path.DirectorySeparatorChar;
        if (!string.IsNullOrEmpty(previous) &&
            previous.StartsWith(iconsPrefix, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(previous, dest, StringComparison.OrdinalIgnoreCase))
        {
            try { System.IO.File.Delete(previous); }
            catch (Exception ex)
            {
                AppLogger.Warning("ProfileCardViewModel.BrowseIcon", ex.Message);
                SessionErrorTracker.Record(ErrorCode.IconDeleteFailed, "Icon Delete Failed",
                    $"Could not delete old icon file (it may still be on disk): {ex.Message}");
            }
        }

        IconPath = dest;
    }

    private void DeleteProfile()
    {
        if (_dialogService.ShowConfirmDelete(_model.Name))
            _onDelete(this);
    }

    public void Dispose()
    {
        _iconPreview = null;
    }

    private static string SanitizeName(string name)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var sanitized = string.Concat(name.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c));
        return sanitized.Length > 20 ? sanitized[..20] : sanitized;
    }

    private void UpdateIconPreview()
    {
        try
        {
            using var icon = IconHelper.LoadIcon(_iconPath, _configService.IconsDir);
            IconPreview = IconHelper.ToImageSource(icon);
        }
        catch (Exception ex)
        {
            AppLogger.Warning("ProfileCardViewModel.UpdateIconPreview", ex.Message);
            SessionErrorTracker.Record(ErrorCode.IconPreviewFailed, "Icon Preview Failed",
                $"Could not load icon preview: {ex.Message}");
            IconPreview = null;
        }
    }
}
