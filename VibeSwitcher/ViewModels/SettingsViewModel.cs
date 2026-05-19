using System.Collections.ObjectModel;
using System.Windows.Input;
using VibeSwitcher.Helpers;
using VibeSwitcher.Models;
using VibeSwitcher.Services;

namespace VibeSwitcher.ViewModels;

public class SettingsViewModel : ViewModelBase
{
    private readonly IConfigService _configService;
    private readonly IAudioService _audioService;
    private readonly IHotkeyService _hotkeyService;
    private readonly IStartupService _startupService;
    private readonly IDialogService _dialogService;
    private readonly Action _onProfilesChanged;
    private readonly Action<HotkeyConflictException> _onHotkeyConflict;

    private bool _startWithWindows;
    private bool _startMinimized;
    private bool _closeToTray;
    private bool _showNotifications;
    private bool _useLegacySoundPanel;

    // Device lists loaded once async and shared across all profile cards.
    private volatile IReadOnlyList<AudioDeviceInfo> _playbackDevices = [];
    private volatile IReadOnlyList<AudioDeviceInfo> _recordingDevices = [];
    private CancellationTokenSource? _loadCts;

    public ObservableCollection<ProfileCardViewModel> Profiles { get; }

    public bool HasNoProfiles => Profiles.Count == 0;

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (SetField(ref _startWithWindows, value))
            {
                if (value) _startupService.Enable();
                else _startupService.Disable();
            }
        }
    }

    public bool StartMinimized
    {
        get => _startMinimized;
        set
        {
            if (SetField(ref _startMinimized, value))
            {
                _configService.Current.StartMinimized = value;
                _configService.SaveImmediate();
            }
        }
    }

    public bool CloseToTray
    {
        get => _closeToTray;
        set
        {
            if (SetField(ref _closeToTray, value))
            {
                _configService.Current.CloseToTray = value;
                _configService.SaveImmediate();
            }
        }
    }

    public bool ShowNotifications
    {
        get => _showNotifications;
        set
        {
            if (SetField(ref _showNotifications, value))
            {
                _configService.Current.ShowNotifications = value;
                _configService.SaveImmediate();
            }
        }
    }

    public bool UseLegacySoundPanel
    {
        get => _useLegacySoundPanel;
        set
        {
            if (SetField(ref _useLegacySoundPanel, value))
            {
                _configService.Current.UseLegacySoundPanel = value;
                _configService.SaveImmediate();
            }
        }
    }

    public HotkeyDefinition SettingsHotkey
    {
        get => _configService.Current.SettingsHotkey ?? new HotkeyDefinition();
        set
        {
            _configService.Current.SettingsHotkey = value;
            _configService.SaveImmediate();
            _hotkeyService.UnregisterSettingsHotkey();
            if (!value.IsEmpty && _configService.Current.SettingsHotkeyEnabled)
            {
                var conflict = _hotkeyService.RegisterSettingsHotkey(value);
                if (conflict != null)
                    _onHotkeyConflict(conflict);
            }
            OnPropertyChanged(nameof(SettingsHotkey));
            OnPropertyChanged(nameof(SettingsHotkeyDisplay));
            OnPropertyChanged(nameof(SettingsHotkeyIsSet));
        }
    }

    public string SettingsHotkeyDisplay =>
        _configService.Current.SettingsHotkey is { IsEmpty: false } hk
            ? hk.ToDisplayString()
            : "None";

    public bool SettingsHotkeyIsSet =>
        _configService.Current.SettingsHotkey is { IsEmpty: false };

    public bool SettingsHotkeyEnabled
    {
        get => _configService.Current.SettingsHotkeyEnabled;
        set
        {
            if (_configService.Current.SettingsHotkeyEnabled == value) return;
            _configService.Current.SettingsHotkeyEnabled = value;
            _configService.SaveImmediate();
            if (value)
            {
                var hk = _configService.Current.SettingsHotkey;
                if (hk is { IsEmpty: false })
                {
                    var conflict = _hotkeyService.RegisterSettingsHotkey(hk);
                    if (conflict != null) _onHotkeyConflict(conflict);
                }
            }
            else
            {
                _hotkeyService.UnregisterSettingsHotkey();
            }
            OnPropertyChanged(nameof(SettingsHotkeyEnabled));
        }
    }

    public ICommand AddProfileCommand { get; }

    public SettingsViewModel(
        IConfigService configService,
        IAudioService audioService,
        IHotkeyService hotkeyService,
        IStartupService startupService,
        IDialogService dialogService,
        Action onProfilesChanged,
        Action<HotkeyConflictException> onHotkeyConflict)
    {
        _configService = configService;
        _audioService = audioService;
        _hotkeyService = hotkeyService;
        _startupService = startupService;
        _dialogService = dialogService;
        _onProfilesChanged = onProfilesChanged;
        _onHotkeyConflict = onHotkeyConflict;

        _startWithWindows = startupService.IsStartupEnabled();
        _startMinimized = configService.Current.StartMinimized;
        _closeToTray = configService.Current.CloseToTray;
        _showNotifications = configService.Current.ShowNotifications;
        _useLegacySoundPanel = configService.Current.UseLegacySoundPanel;

        // Batch-initialize from the ordered profile list — no per-item CollectionChanged during load.
        Profiles = new ObservableCollection<ProfileCardViewModel>(
            configService.Current.Profiles
                .OrderBy(p => p.SortOrder)
                .Select(p => CreateCard(p)));

        AddProfileCommand = new RelayCommand(AddProfile);

        Profiles.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoProfiles));

        // Refresh device dropdowns whenever a device is plugged in or removed.
        _audioService.DevicesChanged += OnDevicesChanged;

        // Enumerate audio devices once on a background STA thread, then populate all cards.
        // Cards start with empty device dropdowns and populate within a fraction of a second.
        _ = LoadDevicesAsync();
    }

    private void OnDevicesChanged()
    {
        // Called from a thread-pool thread via DeviceNotificationClient's debounce.
        // LoadDevicesAsync handles its own UI-thread dispatch.
        _ = LoadDevicesAsync();
    }

    private async Task LoadDevicesAsync()
    {
        // Cancel any in-progress enumeration and start a fresh one.
        // Interlocked.Exchange atomically replaces the field so concurrent OnDevicesChanged
        // calls (from the thread-pool debounce) can't race on the same CTS instance.
        var cts = new CancellationTokenSource();
        var old = Interlocked.Exchange(ref _loadCts, cts);
        old?.Cancel();
        old?.Dispose();
        var token = cts.Token;

        var audioService = _audioService;
        try
        {
            var (pb, rec) = await Task.Run(() =>
                (audioService.GetPlaybackDevices(), audioService.GetRecordingDevices()));

            if (token.IsCancellationRequested) return;

            _playbackDevices = pb;
            _recordingDevices = rec;

            // ObservableCollection is not thread-safe; must populate on the UI thread.
            // Application.Current is null in headless test environments; skip dispatch there.
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null) return;
            await dispatcher.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested) return;
                foreach (var card in Profiles)
                    card.LoadDevices(pb, rec);
            });
        }
        catch (Exception ex) when (!token.IsCancellationRequested)
        {
            AppLogger.Error("SettingsViewModel.LoadDevicesAsync", ex);
            SessionErrorTracker.Record(ErrorCode.AudioEnumerationFailed, "Audio Device Error",
                $"Could not load audio devices: {ex.Message}");
        }
    }

    private ProfileCardViewModel CreateCard(DeviceProfile profile)
    {
        return new ProfileCardViewModel(
            profile,
            _configService,
            _hotkeyService,
            _dialogService,
            _playbackDevices,
            _recordingDevices,
            onChanged: card => OnProfileChanged(card),
            onDelete: card => DeleteProfile(card));
    }

    private void AddProfile()
    {
        var mode = _dialogService.ShowProfileTypeDialog();
        if (mode == null) return;

        var profile = new DeviceProfile
        {
            Name = $"Profile {Profiles.Count + 1}",
            Mode = mode.Value,
            SortOrder = Profiles.Count
        };

        _configService.Current.Profiles.Add(profile);
        _configService.SaveImmediate();

        var card = CreateCard(profile);
        Profiles.Add(card);
        _onProfilesChanged();
    }

    private void DeleteProfile(ProfileCardViewModel card)
    {
        var iconPath = card.Model.IconPath;
        _configService.Current.Profiles.Remove(card.Model);
        _configService.SaveImmediate();
        Profiles.Remove(card);
        card.Dispose();
        DeleteOrphanedIcon(iconPath, _configService.IconsDir);
        ReregisterHotkeys();
        _onProfilesChanged();
    }

    private static void DeleteOrphanedIcon(string? iconPath, string iconsDir)
    {
        if (string.IsNullOrEmpty(iconPath)) return;
        var prefix = iconsDir + System.IO.Path.DirectorySeparatorChar;
        if (!iconPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return;
        try { System.IO.File.Delete(iconPath); }
        catch (Exception ex)
        {
            AppLogger.Warning("SettingsViewModel.DeleteOrphanedIcon", ex.Message);
            SessionErrorTracker.Record(ErrorCode.IconDeleteFailed, "Icon Delete Failed",
                $"Could not delete orphaned icon file (it may remain on disk): {ex.Message}");
        }
    }

    private void OnProfileChanged(ProfileCardViewModel card)
    {
        _configService.SaveImmediate();
        ReregisterHotkeys();
        _onProfilesChanged();
    }

    internal void ReregisterHotkeys()
    {
        var conflicts = _hotkeyService.RegisterAll(_configService.Current.Profiles);
        foreach (var ex in conflicts)
            _onHotkeyConflict(ex);

        // RegisterAll wipes all hotkeys including the settings hotkey — restore it if enabled.
        var settingsHotkey = _configService.Current.SettingsHotkey;
        if (settingsHotkey is { IsEmpty: false } && _configService.Current.SettingsHotkeyEnabled)
        {
            var conflict = _hotkeyService.RegisterSettingsHotkey(settingsHotkey);
            if (conflict != null)
                _onHotkeyConflict(conflict);
        }
    }
}
