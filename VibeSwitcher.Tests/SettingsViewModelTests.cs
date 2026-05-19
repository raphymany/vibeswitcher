using VibeSwitcher.Models;
using VibeSwitcher.Services;
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
    public async Task LoadDevicesAsync_ConcurrentCalls_DoNotThrow()
    {
        // Simulates rapid plug/unplug events firing DevicesChanged concurrently.
        // The Interlocked.Exchange CancellationTokenSource swap must not race or throw.
        _fakeAudio.PlaybackResult = [new AudioDeviceInfo("id1", "Speakers", true)];
        var vm = MakeViewModel();

        var tasks = Enumerable.Range(0, 10)
            .Select(_ => Task.Run(() => _fakeAudio.RaiseDevicesChanged()))
            .ToArray();
        await Task.WhenAll(tasks);
    }

    [Fact]
    public async Task OnDevicesChanged_CalledFromBackgroundThread_DoesNotThrow()
    {
        // DeviceNotificationClient fires DevicesChanged from a thread-pool thread;
        // SettingsViewModel.OnDevicesChanged must be safe to call off the UI thread.
        var vm = MakeViewModel();
        Exception? caught = null;

        await Task.Run(() =>
        {
            try { _fakeAudio.RaiseDevicesChanged(); }
            catch (Exception ex) { caught = ex; }
        });

        Assert.Null(caught);
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

    [Fact]
    public void CloneProfile_CreatesNewProfileWithCopySuffix()
    {
        var original = new DeviceProfile
        {
            Name = "Gaming",
            Mode = ProfileMode.Playback,
            PlaybackDeviceId = "dev-123",
            Silent = true,
            SortOrder = 0
        };
        _fakeConfig.Current.Profiles.Add(original);
        _fakeDialog.ProfileTypeResult = ProfileMode.Playback;
        var vm = MakeViewModel();

        vm.Profiles[0].CloneCommand.Execute(null);

        Assert.Equal(2, vm.Profiles.Count);
        var clone = vm.Profiles[1];
        Assert.Equal("Gaming (copy)", clone.Model.Name);
        Assert.Equal(ProfileMode.Playback, clone.Model.Mode);
        Assert.Equal("dev-123", clone.Model.PlaybackDeviceId);
        Assert.True(clone.Model.Silent);
        Assert.NotEqual(original.Id, clone.Model.Id);
    }

    [Fact]
    public void CloneProfile_DoesNotCopyHotkeyOrIconPath()
    {
        var original = new DeviceProfile
        {
            Name = "Work",
            Hotkey = new HotkeyDefinition { VirtualKeyCode = 33, UseCtrl = true },
            IconPath = @"C:\icons\work.ico",
            SortOrder = 0
        };
        _fakeConfig.Current.Profiles.Add(original);
        var vm = MakeViewModel();

        vm.Profiles[0].CloneCommand.Execute(null);

        var clone = vm.Profiles[1].Model;
        Assert.True(clone.Hotkey.IsEmpty);
        Assert.Null(clone.IconPath);
    }

    [Fact]
    public void MoveProfile_ReordersCollectionAndUpdatesSortOrder()
    {
        var p1 = new DeviceProfile { Name = "First", SortOrder = 0 };
        var p2 = new DeviceProfile { Name = "Second", SortOrder = 1 };
        var p3 = new DeviceProfile { Name = "Third", SortOrder = 2 };
        _fakeConfig.Current.Profiles.AddRange([p1, p2, p3]);
        var vm = MakeViewModel();

        vm.MoveProfile(vm.Profiles[0], vm.Profiles[2]); // move First to position of Third

        Assert.Equal("Second", vm.Profiles[0].Model.Name);
        Assert.Equal("Third", vm.Profiles[1].Model.Name);
        Assert.Equal("First", vm.Profiles[2].Model.Name);
        Assert.Equal(0, vm.Profiles[0].Model.SortOrder);
        Assert.Equal(1, vm.Profiles[1].Model.SortOrder);
        Assert.Equal(2, vm.Profiles[2].Model.SortOrder);
    }

    [Fact]
    public void DeleteProfile_RecompactsSortOrder()
    {
        var p1 = new DeviceProfile { Name = "First", SortOrder = 0 };
        var p2 = new DeviceProfile { Name = "Second", SortOrder = 1 };
        var p3 = new DeviceProfile { Name = "Third", SortOrder = 2 };
        _fakeConfig.Current.Profiles.AddRange([p1, p2, p3]);
        _fakeDialog.ConfirmDeleteResult = true;
        var vm = MakeViewModel();

        vm.Profiles[1].DeleteCommand.Execute(null); // delete Second

        Assert.Equal(2, vm.Profiles.Count);
        Assert.Equal(0, vm.Profiles[0].Model.SortOrder);
        Assert.Equal(1, vm.Profiles[1].Model.SortOrder);
    }

}
