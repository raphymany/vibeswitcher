using VibeSwitcher.Models;
using VibeSwitcher.Services;
using VibeSwitcher.ViewModels;
using Xunit;

namespace VibeSwitcher.Tests;

public class DeviceAliasItemTests
{
    private static DeviceAliasItem MakeItem(string rawName, string alias) =>
        new("id1", rawName, alias, isPlayback: true, isConnected: true, isDisabled: false, profileUsage: "");

    [Fact]
    public void Alias_WhitespaceInput_TrimsToEmpty()
    {
        var item = MakeItem("Realtek HD Audio", "");
        item.Alias = "   ";
        Assert.Equal("", item.Alias);
    }

    [Fact]
    public void Alias_PaddedInput_Trims()
    {
        var item = MakeItem("Realtek HD Audio", "");
        item.Alias = "  GoXLR  ";
        Assert.Equal("GoXLR", item.Alias);
    }

    [Fact]
    public void AliasChanged_FiredWithTrimmedValue()
    {
        var item = MakeItem("Realtek HD Audio", "");
        string? received = null;
        item.AliasChanged += (_, alias) => received = alias;

        item.Alias = "  Desk Speakers  ";

        Assert.Equal("Desk Speakers", received);
    }

    [Fact]
    public void AliasChanged_NotFiredWhenValueUnchanged()
    {
        var item = MakeItem("Realtek HD Audio", "GoXLR");
        int firedCount = 0;
        item.AliasChanged += (_, _) => firedCount++;

        item.Alias = "GoXLR";

        Assert.Equal(0, firedCount);
    }
}

public class DeviceAliasTests
{
    private readonly FakeConfigService _fakeConfig = new();
    private readonly FakeAudioService _fakeAudio = new();
    private readonly FakeHotkeyService _fakeHotkey = new();
    private readonly FakeStartupService _fakeStartup = new();
    private readonly FakeDialogService _fakeDialog = new();

    private SettingsViewModel MakeViewModel() =>
        new(_fakeConfig, _fakeAudio, _fakeHotkey, _fakeStartup, _fakeDialog,
            onProfilesChanged: () => { },
            onHotkeyConflict: _ => { },
            applyTheme: _ => { });

    [Fact]
    public void ApplyAliases_NoAliasesConfigured_ReturnsSameList()
    {
        var devices = new List<AudioDeviceInfo>
        {
            new("id1", "Realtek HD Audio", IsPlayback: true),
            new("id2", "USB Microphone",   IsPlayback: false),
        };
        var vm = MakeViewModel();

        var result = vm.ApplyAliases(devices);

        Assert.Equal("Realtek HD Audio", result[0].FriendlyName);
        Assert.Equal("USB Microphone",   result[1].FriendlyName);
    }

    [Fact]
    public void ApplyAliases_AliasSet_SubstitutesFriendlyName()
    {
        _fakeConfig.Current.DeviceAliases["id1"] = "GoXLR";
        var devices = new List<AudioDeviceInfo>
        {
            new("id1", "Microphone (GoXLR Audio)", IsPlayback: false),
            new("id2", "Speakers (Realtek)",       IsPlayback: true),
        };
        var vm = MakeViewModel();

        var result = vm.ApplyAliases(devices);

        Assert.Equal("GoXLR",            result[0].FriendlyName);
        Assert.Equal("Speakers (Realtek)", result[1].FriendlyName);
    }

    [Fact]
    public void ApplyAliases_WhitespaceAlias_KeepsRawName()
    {
        _fakeConfig.Current.DeviceAliases["id1"] = "   ";
        var devices = new List<AudioDeviceInfo>
        {
            new("id1", "Realtek HD Audio", IsPlayback: true),
        };
        var vm = MakeViewModel();

        var result = vm.ApplyAliases(devices);

        Assert.Equal("Realtek HD Audio", result[0].FriendlyName);
    }

    [Fact]
    public void ApplyAliases_PreservesOtherFields()
    {
        _fakeConfig.Current.DeviceAliases["id1"] = "Desk Speakers";
        var device = new AudioDeviceInfo("id1", "Realtek HD Audio", IsPlayback: true,
            IsConnected: false, IsDisabled: true);
        var vm = MakeViewModel();

        var result = vm.ApplyAliases([device]);

        Assert.Equal("Desk Speakers", result[0].FriendlyName);
        Assert.False(result[0].IsConnected);
        Assert.True(result[0].IsDisabled);
        Assert.True(result[0].IsPlayback);
    }
}
