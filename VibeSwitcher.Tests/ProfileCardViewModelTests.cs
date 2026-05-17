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
            card => _deletedCard = card);
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
    public void CaptureHotkey_Conflict_ShowsAlertAndNoChange()
    {
        var profile = new DeviceProfile { Name = "Test" };
        _fakeDialog.HotkeyCaptureResult = new HotkeyDefinition { VirtualKeyCode = 33, UseCtrl = true };
        _fakeHotkey.TestHotkeyResult = true; // simulates another app owns this combo
        using var card = MakeCard(profile);

        card.CaptureHotkeyCommand.Execute(null);

        Assert.Single(_fakeDialog.AlertsShown);
        Assert.True(profile.Hotkey.IsEmpty); // model unchanged
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
