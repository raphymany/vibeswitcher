using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using VibeSwitcher.Helpers;
using VibeSwitcher.Models;
using VibeSwitcher.Services;
using VibeSwitcher.Views;
using Microsoft.Win32;
using Visibility = System.Windows.Visibility;

namespace VibeSwitcher.ViewModels;

public class ProfileCardViewModel : ViewModelBase
{
    private readonly DeviceProfile _model;
    private readonly ConfigService _configService;
    private readonly HotkeyService _hotkeyService;
    private readonly Action<ProfileCardViewModel> _onChanged;
    private readonly Action<ProfileCardViewModel> _onDelete;

    private string _name;
    private AudioDeviceInfo? _selectedPlaybackDevice;
    private AudioDeviceInfo? _selectedRecordingDevice;
    private string _hotkeyDisplay;
    private string? _iconPath;
    private ImageSource? _iconPreview;

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

    public Visibility PlaybackVisible =>
        _model.Mode is ProfileMode.Playback or ProfileMode.Both ? Visibility.Visible : Visibility.Collapsed;

    public Visibility RecordingVisible =>
        _model.Mode is ProfileMode.Recording or ProfileMode.Both ? Visibility.Visible : Visibility.Collapsed;

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
            if (SetField(ref _selectedPlaybackDevice, value))
            {
                _model.PlaybackDeviceId = value?.Id;
                _onChanged(this);
            }
        }
    }

    public AudioDeviceInfo? SelectedRecordingDevice
    {
        get => _selectedRecordingDevice;
        set
        {
            if (SetField(ref _selectedRecordingDevice, value))
            {
                _model.RecordingDeviceId = value?.Id;
                _onChanged(this);
            }
        }
    }

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

    public ICommand CaptureHotkeyCommand { get; }
    public ICommand BrowseIconCommand { get; }
    public ICommand DeleteCommand { get; }

    public ProfileCardViewModel(
        DeviceProfile model,
        ConfigService configService,
        AudioService audioService,
        HotkeyService hotkeyService,
        Action<ProfileCardViewModel> onChanged,
        Action<ProfileCardViewModel> onDelete)
    {
        _model = model;
        _configService = configService;
        _hotkeyService = hotkeyService;
        _onChanged = onChanged;
        _onDelete = onDelete;

        _name = model.Name;
        _hotkeyDisplay = model.Hotkey.ToDisplayString();
        _iconPath = model.IconPath;

        PlaybackDevices = new ObservableCollection<AudioDeviceInfo>(audioService.GetPlaybackDevices());
        RecordingDevices = new ObservableCollection<AudioDeviceInfo>(audioService.GetRecordingDevices());

        _selectedPlaybackDevice = PlaybackDevices.FirstOrDefault(d => d.Id == model.PlaybackDeviceId);
        _selectedRecordingDevice = RecordingDevices.FirstOrDefault(d => d.Id == model.RecordingDeviceId);

        UpdateIconPreview();

        CaptureHotkeyCommand = new RelayCommand(CaptureHotkey);
        BrowseIconCommand = new RelayCommand(BrowseIcon);
        DeleteCommand = new RelayCommand(DeleteProfile);
    }

    private void CaptureHotkey()
    {
        // Unregister this profile's hotkey so pressing it inside the dialog is detectable.
        // Without this, Windows intercepts the registered key before WPF sees it.
        _hotkeyService.UnregisterProfile(_model.Id);

        var dialog = new HotkeyCaptureDialog(_model.Hotkey)
        {
            Owner = Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault()
        };
        bool applied = false;

        if (dialog.ShowDialog() == true && dialog.CapturedHotkey != null)
        {
            if (!dialog.CapturedHotkey.IsEmpty && _hotkeyService.TestHotkey(dialog.CapturedHotkey))
            {
                MessageBox.Show(
                    $"The hotkey '{dialog.CapturedHotkey.ToDisplayString()}' is already in use by another application.",
                    "Hotkey Conflict",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            else
            {
                _model.Hotkey = dialog.CapturedHotkey;
                HotkeyDisplay = _model.Hotkey.ToDisplayString();
                _onChanged(this); // triggers Refresh → re-registers all hotkeys with new value
                applied = true;
            }
        }

        if (!applied)
            _hotkeyService.RegisterProfile(_model); // restore original on cancel / conflict
    }

    private void BrowseIcon()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Icon File",
            Filter = "Icon Files (*.ico)|*.ico",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() != true) return;

        var source = dialog.FileName;
        var dest = System.IO.Path.Combine(ConfigService.IconsDir, $"{_model.Id}.ico");

        if (!string.Equals(source, dest, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                System.IO.Directory.CreateDirectory(ConfigService.IconsDir);
                System.IO.File.Copy(source, dest, overwrite: true);
            }
            catch
            {
                // Fall back to original path if copy fails
                dest = source;
            }
        }

        IconPath = dest;
    }

    private void DeleteProfile()
    {
        var dialog = new ConfirmDeleteDialog(_model.Name)
        {
            Owner = Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault()
        };
        if (dialog.ShowDialog() == true)
            _onDelete(this);
    }

    private void UpdateIconPreview()
    {
        try
        {
            var icon = IconHelper.LoadIcon(_iconPath);
            IconPreview = IconHelper.ToImageSource(icon);
        }
        catch
        {
            IconPreview = null;
        }
    }
}
