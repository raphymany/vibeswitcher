using VibeSwitcher.Models;
using VibeSwitcher.ViewModels;
using Xunit;

namespace VibeSwitcher.Tests;

public class SettingsViewModelTests
{
    private readonly FakeConfigService _fakeConfig = new();
    private readonly FakeAudioService _fakeAudio = new();
    private readonly FakeHotkeyService _fakeHotkey = new();
    private readonly FakeStartupService _fakeStartup = new();
    private readonly FakeDialogService _fakeDialog = new();
    private int _profilesChangedCount;

    private SettingsViewModel MakeViewModel() =>
        new(_fakeConfig, _fakeAudio, _fakeHotkey, _fakeStartup, _fakeDialog,
            onProfilesChanged: () => _profilesChangedCount++,
            onHotkeyConflict: _ => { });

    [Fact]
    public void AddProfile_DialogConfirmed_AddsToProfiles()
    {
        _fakeDialog.ProfileTypeResult = ProfileMode.Both;
        var vm = MakeViewModel();

        vm.AddProfileCommand.Execute(null);

        Assert.Single(vm.Profiles);
        Assert.False(vm.HasNoProfiles);
        Assert.Equal(1, _profilesChangedCount);
    }

    [Fact]
    public void AddProfile_DialogCancelled_DoesNotAdd()
    {
        _fakeDialog.ProfileTypeResult = null;
        var vm = MakeViewModel();

        vm.AddProfileCommand.Execute(null);

        Assert.Empty(vm.Profiles);
        Assert.True(vm.HasNoProfiles);
    }

    [Fact]
    public void DeleteProfile_ConfirmTrue_RemovesFromCollection()
    {
        _fakeConfig.Current.Profiles.Add(new DeviceProfile { Name = "P1" });
        _fakeDialog.ConfirmDeleteResult = true;
        var vm = MakeViewModel();

        vm.Profiles[0].DeleteCommand.Execute(null);

        Assert.Empty(vm.Profiles);
        Assert.True(vm.HasNoProfiles);
        Assert.Equal(1, _profilesChangedCount);
    }

    [Fact]
    public void DeleteProfile_ConfirmFalse_ProfileRemains()
    {
        _fakeConfig.Current.Profiles.Add(new DeviceProfile { Name = "P1" });
        _fakeDialog.ConfirmDeleteResult = false;
        var vm = MakeViewModel();

        vm.Profiles[0].DeleteCommand.Execute(null);

        Assert.Single(vm.Profiles);
    }

    [Fact]
    public void StartWithWindows_SetTrue_CallsEnable()
    {
        var vm = MakeViewModel(); // _fakeStartup starts disabled → _startWithWindows = false

        vm.StartWithWindows = true;

        Assert.True(_fakeStartup.StartupEnabled);
    }

    [Fact]
    public void StartWithWindows_SetFalse_CallsDisable()
    {
        _fakeStartup.Enable(); // start enabled so setting false triggers a change
        var vm = MakeViewModel(); // reads IsStartupEnabled() → true → _startWithWindows = true

        vm.StartWithWindows = false;

        Assert.False(_fakeStartup.StartupEnabled);
    }

    [Fact]
    public void ProfileChange_TriggersHotkeyReregistration()
    {
        var profile = new DeviceProfile
        {
            Name = "P1",
            Hotkey = new HotkeyDefinition { VirtualKeyCode = 33, UseCtrl = true }
        };
        _fakeConfig.Current.Profiles.Add(profile);
        var vm = MakeViewModel();

        vm.Profiles[0].Name = "Updated";

        Assert.Single(_fakeHotkey.RegisteredProfiles);
        Assert.Equal(profile.Id, _fakeHotkey.RegisteredProfiles[0].Id);
        Assert.Equal(1, _profilesChangedCount);
    }
}
