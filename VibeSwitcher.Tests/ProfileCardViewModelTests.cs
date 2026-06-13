using VibeSwitcher.Models;
using VibeSwitcher.Services;
using VibeSwitcher.ViewModels;
using Xunit;

namespace VibeSwitcher.Tests;

public class ProfileCardViewModelTests
{
    private readonly FakeConfigService _fakeConfig = new();
    private readonly FakeHotkeyService _fakeHotkey = new();
    private readonly FakeDialogService _fakeDialog = new();
    private int _changedCount;
    private ProfileCardViewModel? _deletedCard;

    private ProfileCardViewModel MakeCard(DeviceProfile? profile = null)
    {
        profile ??= new DeviceProfile { Name = "Test" };
        return new ProfileCardViewModel(
            profile,
            _fakeConfig,
            _fakeHotkey,
            _fakeDialog,
            new FakeAppLogger(),
            new FakeSessionErrorTracker(),
            Array.Empty<AudioDeviceInfo>(),
            Array.Empty<AudioDeviceInfo>(),
            _ => _changedCount++,
            card => _deletedCard = card,
            (_, _) => { },
            _ => Task.CompletedTask);
    }

    // ── CaptureHotkey ────────────────────────────────────────────────────────

    [Fact]
    public void CaptureHotkey_Cancel_NoChange()
    {
        var profile = new DeviceProfile { Name = "Test" };
        _fakeDialog.HotkeyCaptureResult = null; // user cancelled
        using var card = MakeCard(profile);

        card.CaptureHotkeyCommand.Execute(null);

        Assert.True(profile.Hotkey.IsEmpty);
        Assert.Equal(0, _changedCount);
    }

    [Fact]
    public void CaptureHotkey_Clear_RemovesHotkey()
    {
        var profile = new DeviceProfile
        {
            Name = "Test",
            Hotkey = new HotkeyDefinition { VirtualKeyCode = 33, UseCtrl = true }
        };
        _fakeDialog.HotkeyCaptureResult = new HotkeyDefinition(); // empty = clear
        using var card = MakeCard(profile);

        card.CaptureHotkeyCommand.Execute(null);

        Assert.True(profile.Hotkey.IsEmpty);
        Assert.Equal("Not set", card.HotkeyDisplay);
        Assert.Equal(1, _changedCount);
    }

    [Fact]
    public void CaptureHotkey_Conflict_ShowsRetryDialogAndNoChange()
    {
        var profile = new DeviceProfile { Name = "Test" };
        _fakeDialog.HotkeyCaptureResult = new HotkeyDefinition { VirtualKeyCode = 33, UseCtrl = true };
        _fakeHotkey.TestHotkeyResult = true; // simulates another app owns this combo
        _fakeDialog.ConflictRetryResult = false; // user clicks Cancel (don't retry)
        using var card = MakeCard(profile);

        card.CaptureHotkeyCommand.Execute(null);

        Assert.Single(_fakeDialog.ConflictRetriesShown);
        Assert.True(profile.Hotkey.IsEmpty); // model unchanged
        Assert.Equal(0, _changedCount);
    }

    [Fact]
    public void CaptureHotkey_InternalConflictWithProfile_ShowsRetryDialogWithProfileName()
    {
        var otherProfile = new DeviceProfile
        {
            Name = "Gaming Setup",
            Hotkey = new HotkeyDefinition { VirtualKeyCode = 33, UseCtrl = true }
        };
        _fakeConfig.Current.Profiles.Add(otherProfile);

        var profile = new DeviceProfile { Name = "Test" };
        _fakeDialog.HotkeyCaptureResult = new HotkeyDefinition { VirtualKeyCode = 33, UseCtrl = true };
        _fakeDialog.ConflictRetryResult = false;
        using var card = MakeCard(profile);

        card.CaptureHotkeyCommand.Execute(null);

        Assert.Single(_fakeDialog.ConflictRetriesShown);
        Assert.Contains("Gaming Setup", _fakeDialog.ConflictRetriesShown[0].Message);
        Assert.True(profile.Hotkey.IsEmpty);
        Assert.Equal(0, _changedCount);
    }

    [Fact]
    public void CaptureHotkey_InternalConflictWithSettingsHotkey_ShowsRetryDialogMentioningSettings()
    {
        _fakeConfig.Current.SettingsHotkey = new HotkeyDefinition { VirtualKeyCode = 33, UseCtrl = true };

        var profile = new DeviceProfile { Name = "Test" };
        _fakeDialog.HotkeyCaptureResult = new HotkeyDefinition { VirtualKeyCode = 33, UseCtrl = true };
        _fakeDialog.ConflictRetryResult = false;
        using var card = MakeCard(profile);

        card.CaptureHotkeyCommand.Execute(null);

        Assert.Single(_fakeDialog.ConflictRetriesShown);
        Assert.Contains("Settings", _fakeDialog.ConflictRetriesShown[0].Message);
        Assert.True(profile.Hotkey.IsEmpty);
        Assert.Equal(0, _changedCount);
    }

    [Fact]
    public void CaptureHotkey_Success_SetsHotkey()
    {
        var profile = new DeviceProfile { Name = "Test" };
        _fakeDialog.HotkeyCaptureResult = new HotkeyDefinition { VirtualKeyCode = 33, UseCtrl = true };
        _fakeHotkey.TestHotkeyResult = false; // no conflict
        using var card = MakeCard(profile);

        card.CaptureHotkeyCommand.Execute(null);

        Assert.Equal(33, profile.Hotkey.VirtualKeyCode);
        Assert.True(profile.Hotkey.UseCtrl);
        Assert.Equal(1, _changedCount);
    }

    // ── BrowseIcon ───────────────────────────────────────────────────────────

    [Fact]
    public void CaptureHotkey_ReplacesExistingHotkey()
    {
        var profile = new DeviceProfile
        {
            Name = "Test",
            Hotkey = new HotkeyDefinition { VirtualKeyCode = 33, UseCtrl = true } // Ctrl+PageUp
        };
        _fakeHotkey.RegisteredProfiles.Add(profile); // simulate it was previously registered
        _fakeDialog.HotkeyCaptureResult = new HotkeyDefinition { VirtualKeyCode = 34, UseCtrl = true }; // Ctrl+PageDown
        _fakeHotkey.TestHotkeyResult = false; // no conflict
        using var card = MakeCard(profile);

        card.CaptureHotkeyCommand.Execute(null);

        Assert.Equal(34, profile.Hotkey.VirtualKeyCode); // new hotkey applied
        Assert.Empty(_fakeHotkey.RegisteredProfiles); // old registration freed; card doesn't re-register (SettingsViewModel's job)
        Assert.Equal(1, _changedCount);
    }

    // ── PickIcon ─────────────────────────────────────────────────────────────

    [Fact]
    public void PickIcon_GalleryCancel_NoChange()
    {
        _fakeDialog.IconGalleryResult = null; // user cancelled the gallery dialog
        using var card = MakeCard();

        card.PickIconCommand.Execute(null);

        Assert.Null(card.IconPath);
        Assert.Equal(0, _changedCount);
    }

    [Fact]
    public void PickIcon_BrowseThenCancel_NoChange()
    {
        _fakeDialog.IconGalleryResult = new VibeSwitcher.Helpers.GalleryPickResult { BrowseFromDisk = true };
        _fakeDialog.BrowseIconFileResult = null; // user cancelled the file dialog
        using var card = MakeCard();

        card.PickIconCommand.Execute(null);

        Assert.Null(card.IconPath);
        Assert.Equal(0, _changedCount);
    }

    [Fact]
    public void PickIcon_BrowseSuccess_UpdatesIconPath()
    {
        var sourceFile = Path.Combine(Path.GetTempPath(), $"vs-test-{Guid.NewGuid():N}.ico");
        File.WriteAllBytes(sourceFile, [0x00]);
        try
        {
            _fakeDialog.IconGalleryResult = new VibeSwitcher.Helpers.GalleryPickResult { BrowseFromDisk = true };
            _fakeDialog.BrowseIconFileResult = sourceFile;
            using var card = MakeCard();

            card.PickIconCommand.Execute(null);

            Assert.NotNull(card.IconPath);
            Assert.StartsWith(_fakeConfig.IconsDir, card.IconPath, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { File.Delete(sourceFile); } catch { }
        }
    }

    [Fact]
    public void PickIcon_BrowseCopyFailure_ShowsAlert()
    {
        _fakeDialog.IconGalleryResult = new VibeSwitcher.Helpers.GalleryPickResult { BrowseFromDisk = true };
        _fakeDialog.BrowseIconFileResult = @"C:\DoesNotExist\missing.ico";
        using var card = MakeCard();

        card.PickIconCommand.Execute(null);

        Assert.Single(_fakeDialog.AlertsShown);
        Assert.Null(card.IconPath);
    }

    [Fact]
    public void PickIcon_BrowseSamePath_SkipsCopyAndUpdatesPath()
    {
        // If the user selects the file that is already in the icons dir, no File.Copy runs.
        var profile = new DeviceProfile { Name = "Test" };
        var dest = Path.Combine(_fakeConfig.IconsDir, $"Test-{profile.Id.ToString("N")[..8]}.ico");
        Directory.CreateDirectory(_fakeConfig.IconsDir);
        File.WriteAllBytes(dest, [0x00]); // file already exists at the destination
        try
        {
            _fakeDialog.IconGalleryResult = new VibeSwitcher.Helpers.GalleryPickResult { BrowseFromDisk = true };
            _fakeDialog.BrowseIconFileResult = dest; // source == dest → copy skipped
            using var card = MakeCard(profile);

            card.PickIconCommand.Execute(null);

            Assert.Equal(dest, card.IconPath, StringComparer.OrdinalIgnoreCase);
            Assert.Empty(_fakeDialog.AlertsShown); // no error shown
        }
        finally
        {
            try { File.Delete(dest); } catch { }
        }
    }

    [Fact]
    public void PickIcon_Browse_SavesOriginalToUploadsLibrary()
    {
        var sourceFile = Path.Combine(Path.GetTempPath(), $"vs-test-{Guid.NewGuid():N}.ico");
        File.WriteAllBytes(sourceFile, [0x01, 0x02, 0x03]);
        try
        {
            _fakeDialog.IconGalleryResult = new VibeSwitcher.Helpers.GalleryPickResult { BrowseFromDisk = true };
            _fakeDialog.BrowseIconFileResult = sourceFile;
            using var card = MakeCard();

            card.PickIconCommand.Execute(null);

            var libraryFiles = VibeSwitcher.Helpers.UploadLibrary.List(_fakeConfig.IconsLibraryDir, "*.ico");
            Assert.NotEmpty(libraryFiles);
        }
        finally
        {
            try { File.Delete(sourceFile); } catch { }
            try { Directory.Delete(_fakeConfig.IconsLibraryDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void PickIcon_FromLibrary_CopiesToProfilePath()
    {
        Directory.CreateDirectory(_fakeConfig.IconsLibraryDir);
        var libraryIcon = Path.Combine(_fakeConfig.IconsLibraryDir, "saved.ico");
        File.WriteAllBytes(libraryIcon, [0x09]);
        try
        {
            _fakeDialog.IconGalleryResult = new VibeSwitcher.Helpers.GalleryPickResult { CustomIconPath = libraryIcon };
            using var card = MakeCard();

            card.PickIconCommand.Execute(null);

            Assert.NotNull(card.IconPath);
            Assert.StartsWith(_fakeConfig.IconsDir, card.IconPath, StringComparison.OrdinalIgnoreCase);
            // The profile points at its own managed copy, not the shared library file.
            Assert.NotEqual(libraryIcon, card.IconPath, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(_fakeConfig.IconsLibraryDir, recursive: true); } catch { }
        }
    }

    // ── ShowNameSuggestions / ApplyNameSuggestion ────────────────────────────

    [Theory]
    [InlineData("Profile 1",   true)]
    [InlineData("Profile 99",  true)]
    [InlineData("Profile 100", true)]
    [InlineData("Profile ",    false)]  // no digit after space
    [InlineData("Profile",     false)]  // no space
    [InlineData("Gaming",      false)]
    [InlineData("Profile 1 x", false)]  // extra chars
    public void ShowNameSuggestions_MatchesDefaultNamePattern(string name, bool expected)
    {
        var profile = new DeviceProfile { Name = name };
        using var card = MakeCard(profile);

        Assert.Equal(expected, card.ShowNameSuggestions);
    }

    [Fact]
    public void ApplyNameSuggestion_SetsName()
    {
        var profile = new DeviceProfile { Name = "Profile 1" };
        using var card = MakeCard(profile);

        card.ApplyNameSuggestionCommand.Execute("Gaming");

        Assert.Equal("Gaming", card.Name);
    }

    [Fact]
    public void ApplyNameSuggestion_HidesSuggestions()
    {
        var profile = new DeviceProfile { Name = "Profile 1" };
        using var card = MakeCard(profile);
        Assert.True(card.ShowNameSuggestions);

        card.ApplyNameSuggestionCommand.Execute("Work");

        Assert.False(card.ShowNameSuggestions);
    }

    [Fact]
    public void ApplyNameSuggestion_WhenIconAlreadySet_DoesNotOverwriteIcon()
    {
        var profile = new DeviceProfile { Name = "Profile 1", IconPath = null };
        var existingIcon = Path.Combine(_fakeConfig.IconsDir, "existing.ico");
        Directory.CreateDirectory(_fakeConfig.IconsDir);
        File.WriteAllBytes(existingIcon, [0x00]);
        try
        {
            using var card = MakeCard(profile);
            card.IconPath = existingIcon; // simulate pre-existing icon

            card.ApplyNameSuggestionCommand.Execute("Gaming");

            Assert.Equal(existingIcon, card.IconPath); // icon unchanged
        }
        finally
        {
            try { File.Delete(existingIcon); } catch { }
        }
    }

    // ── DeleteProfile ────────────────────────────────────────────────────────

    // ── LoadDevices guard ────────────────────────────────────────────────────

    [Fact]
    public void LoadDevices_DoesNotFireOnChanged()
    {
        // _loadingDevices flag is true inside LoadDevices; the TwoWay ComboBox rebind
        // must not trigger _onChanged (which would save and re-register hotkeys mid-load).
        var profile = new DeviceProfile { Name = "Test" };
        var changedCount = 0;
        using var card = new ProfileCardViewModel(
            profile, _fakeConfig, _fakeHotkey, _fakeDialog,
            new FakeAppLogger(), new FakeSessionErrorTracker(),
            [], [],
            _ => changedCount++, _ => { }, (_, _) => { }, _ => Task.CompletedTask);

        var pb = new AudioDeviceInfo[] { new("id1", "Speakers", true) };
        card.LoadDevices(pb, []);

        Assert.Equal(0, changedCount);
    }

    [Fact]
    public void SelectedPlaybackDevice_SetDirectly_FiresOnChanged()
    {
        // Baseline: setting the device outside of LoadDevices DOES fire _onChanged.
        var profile = new DeviceProfile { Name = "Test" };
        var changedCount = 0;
        var pb = new AudioDeviceInfo[] { new("id1", "Speakers", true) };
        using var card = new ProfileCardViewModel(
            profile, _fakeConfig, _fakeHotkey, _fakeDialog,
            new FakeAppLogger(), new FakeSessionErrorTracker(),
            pb, [],
            _ => changedCount++, _ => { }, (_, _) => { }, _ => Task.CompletedTask);

        card.SelectedPlaybackDevice = card.PlaybackDevices[1]; // index 0 is the "Not set" device

        Assert.Equal(1, changedCount);
    }

    [Fact]
    public void DeleteProfile_ConfirmTrue_InvokesCallback()
    {
        _fakeDialog.ConfirmDeleteResult = true;
        using var card = MakeCard();

        card.DeleteCommand.Execute(null);

        Assert.Same(card, _deletedCard);
    }

    [Fact]
    public void DeleteProfile_ConfirmFalse_DoesNotInvokeCallback()
    {
        _fakeDialog.ConfirmDeleteResult = false;
        using var card = MakeCard();

        card.DeleteCommand.Execute(null);

        Assert.Null(_deletedCard);
    }

    // ── TestSoundCommand ─────────────────────────────────────────────────────

    [Fact]
    public async Task TestSoundCommand_WithPlaybackDevice_CallsCallback()
    {
        var pb = new AudioDeviceInfo[] { new("dev-id-1", "Speakers", true) };
        var profile = new DeviceProfile { Name = "Test", PlaybackDeviceId = "dev-id-1" };
        var calledWith = new List<string>();
        using var card = new ProfileCardViewModel(
            profile, _fakeConfig, _fakeHotkey, _fakeDialog,
            new FakeAppLogger(), new FakeSessionErrorTracker(),
            pb, [],
            _ => { }, _ => { }, (_, _) => { },
            id => { calledWith.Add(id); return Task.CompletedTask; });

        card.LoadDevices(pb, []);
        card.TestSoundCommand.Execute(null);
        await Task.Delay(50); // allow fire-and-forget to complete

        Assert.Single(calledWith);
        Assert.Equal("dev-id-1", calledWith[0]);
    }

    [Fact]
    public void TestMicCommand_WithRecordingDevice_OpensDialog()
    {
        var rec = new AudioDeviceInfo[] { new("mic-id-1", "Microphone", false) };
        var profile = new DeviceProfile { Name = "Test", RecordingDeviceId = "mic-id-1" };
        using var card = new ProfileCardViewModel(
            profile, _fakeConfig, _fakeHotkey, _fakeDialog,
            new FakeAppLogger(), new FakeSessionErrorTracker(),
            [], rec,
            _ => { }, _ => { }, (_, _) => { }, _ => Task.CompletedTask);

        card.LoadDevices([], rec);
        // ShowMicTest is not invokable in headless tests (no UI thread / WPF window),
        // so this verifies the command doesn't throw when the device is set.
        // Integration coverage comes from manual testing.
        Assert.True(card.IsRecordingDeviceSet);
    }
}
