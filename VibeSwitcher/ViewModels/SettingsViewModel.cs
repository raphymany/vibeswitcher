using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using VibeSwitcher.Helpers;
using VibeSwitcher.Models;
using VibeSwitcher.Services;
using VibeSwitcher.Views;

namespace VibeSwitcher.ViewModels;

public class SettingsViewModel : ViewModelBase
{
    private readonly ConfigService _configService;
    private readonly AudioService _audioService;
    private readonly HotkeyService _hotkeyService;
    private readonly StartupService _startupService;
    private readonly Action _onProfilesChanged;
    private readonly Action<HotkeyConflictException> _onHotkeyConflict;

    private bool _startWithWindows;
    private bool _startMinimized;
    private bool _closeToTray;
    private bool _showNotifications;

    public ObservableCollection<ProfileCardViewModel> Profiles { get; } = new();

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

    public ICommand AddProfileCommand { get; }

    public SettingsViewModel(
        ConfigService configService,
        AudioService audioService,
        HotkeyService hotkeyService,
        StartupService startupService,
        Action onProfilesChanged,
        Action<HotkeyConflictException> onHotkeyConflict)
    {
        _configService = configService;
        _audioService = audioService;
        _hotkeyService = hotkeyService;
        _startupService = startupService;
        _onProfilesChanged = onProfilesChanged;
        _onHotkeyConflict = onHotkeyConflict;

        _startWithWindows = startupService.IsStartupEnabled();
        _startMinimized = configService.Current.StartMinimized;
        _closeToTray = configService.Current.CloseToTray;
        _showNotifications = configService.Current.ShowNotifications;

        foreach (var profile in configService.Current.Profiles.OrderBy(p => p.SortOrder))
            Profiles.Add(CreateCard(profile));

        AddProfileCommand = new RelayCommand(AddProfile);
    }

    private ProfileCardViewModel CreateCard(DeviceProfile profile)
    {
        return new ProfileCardViewModel(
            profile,
            _configService,
            _audioService,
            _hotkeyService,
            onChanged: card => OnProfileChanged(card),
            onDelete: card => DeleteProfile(card));
    }

    private void AddProfile()
    {
        var dialog = new ProfileTypeDialog
        {
            Owner = Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault()
        };
        if (dialog.ShowDialog() != true || dialog.ChosenMode == null)
            return;

        var profile = new DeviceProfile
        {
            Name = $"Profile {Profiles.Count + 1}",
            Mode = dialog.ChosenMode.Value,
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
        DeleteOrphanedIcon(iconPath);
        ReregisterHotkeys();
        _onProfilesChanged();
    }

    private static void DeleteOrphanedIcon(string? iconPath)
    {
        if (string.IsNullOrEmpty(iconPath)) return;
        if (!iconPath.StartsWith(ConfigService.IconsDir, StringComparison.OrdinalIgnoreCase)) return;
        try { System.IO.File.Delete(iconPath); } catch { }
    }

    private void OnProfileChanged(ProfileCardViewModel card)
    {
        _configService.SaveImmediate();
        ReregisterHotkeys();
        _onProfilesChanged();
    }

    private void ReregisterHotkeys()
    {
        var conflicts = _hotkeyService.RegisterAll(_configService.Current.Profiles);
        foreach (var ex in conflicts)
            _onHotkeyConflict(ex);
    }
}
