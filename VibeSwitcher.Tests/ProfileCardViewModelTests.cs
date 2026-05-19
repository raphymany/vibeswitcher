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
            Array.Empty<AudioDeviceInfo>(),
            Array.Empty<AudioDeviceInfo>(),
            _ => _changedCount++,
            card => _deletedCard = card,
            _ => { });
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
        Assert.Equal("(none)", card.HotkeyDisplay);
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

    // ── BrowseIcon ───────────────────────────────────────────────────────────

    [Fact]
    public void BrowseIcon_Cancel_NoChange()
    {
        _fakeDialog.BrowseIconFileResult = null; // user cancelled the file dialog
        using var card = MakeCard();

        card.BrowseIconCommand.Execute(null);

        Assert.Null(card.IconPath);
        Assert.Equal(0, _changedCount);
    }

    [Fact]
    public void BrowseIcon_CopySuccess_UpdatesIconPath()
    {
        var sourceFile = Path.Combine(Path.GetTempPath(), $"vs-test-{Guid.NewGuid():N}.ico");
        File.WriteAllBytes(sourceFile, [0x00]);
        try
        {
            _fakeDialog.BrowseIconFileResult = sourceFile;
            using var card = MakeCard();

            card.BrowseIconCommand.Execute(null);

            Assert.NotNull(card.IconPath);
            Assert.StartsWith(_fakeConfig.IconsDir, card.IconPath, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { File.Delete(sourceFile); } catch { }
        }
    }

    [Fact]
    public void BrowseIcon_CopyFailure_ShowsAlert()
    {
        _fakeDialog.BrowseIconFileResult = @"C:\DoesNotExist\missing.ico";
        using var card = MakeCard();

        card.BrowseIconCommand.Execute(null);

        Assert.Single(_fakeDialog.AlertsShown);
        Assert.Null(card.IconPath);
    }

    // ── DeleteProfile ────────────────────────────────────────────────────────

    [Fact]
    public void BrowseIcon_SamePath_SkipsCopyAndUpdatesPath()
    {
        // If the user selects the file that is already in the icons dir, no File.Copy runs.
        var profile = new DeviceProfile { Name = "Test" };
        var dest = Path.Combine(_fakeConfig.IconsDir, $"Test-{profile.Id.ToString("N")[..8]}.ico");
        Directory.CreateDirectory(_fakeConfig.IconsDir);
        File.WriteAllBytes(dest, [0x00]); // file already exists at the destination
        try
        {
            _fakeDialog.BrowseIconFileResult = dest; // source == dest → copy skipped
            using var card = MakeCard(profile);

            card.BrowseIconCommand.Execute(null);

            Assert.Equal(dest, card.IconPath, StringComparer.OrdinalIgnoreCase);
            Assert.Empty(_fakeDialog.AlertsShown); // no error shown
        }
        finally
        {
            try { File.Delete(dest); } catch { }
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
            [], [],
            _ => changedCount++, _ => { }, _ => { });

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
            pb, [],
            _ => changedCount++, _ => { }, _ => { });

        card.SelectedPlaybackDevice = card.PlaybackDevices[1]; // index 0 is (None)

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
}
