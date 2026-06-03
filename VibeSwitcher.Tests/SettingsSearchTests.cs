using VibeSwitcher.Models;
using VibeSwitcher.ViewModels;
using Xunit;

namespace VibeSwitcher.Tests;

public class SettingsSearchTests
{
    private readonly FakeConfigService _fakeConfig = new();
    private readonly FakeAudioService  _fakeAudio  = new();
    private readonly FakeHotkeyService _fakeHotkey = new();
    private readonly FakeStartupService _fakeStartup = new();
    private readonly FakeDialogService  _fakeDialog  = new();

    private SettingsViewModel MakeViewModel() =>
        new(_fakeConfig, _fakeAudio, _fakeHotkey, _fakeStartup, _fakeDialog,
            onProfilesChanged: () => { },
            onHotkeyConflict: _ => { },
            applyTheme: _ => { });

    private void AddProfile(string name, ProfileMode mode = ProfileMode.Both,
        bool isPinned = false, List<ScheduleEntry>? schedules = null)
    {
        _fakeConfig.Current.Profiles.Add(new DeviceProfile
        {
            Name      = name,
            Mode      = mode,
            IsPinned  = isPinned,
            Schedules = schedules ?? new(),
        });
    }

    [Fact]
    public void EmptySearch_AllCardsVisible()
    {
        AddProfile("Gaming");
        AddProfile("Work");
        var vm = MakeViewModel();

        vm.SearchText = "";

        Assert.All(vm.Profiles, p => Assert.True(p.IsVisible));
        Assert.False(vm.HasNoFilterResults);
    }

    [Fact]
    public void SearchByName_MatchingCardVisible_NonMatchingHidden()
    {
        AddProfile("Gaming");
        AddProfile("Work");
        var vm = MakeViewModel();

        vm.SearchText = "gam";

        Assert.True(vm.Profiles[0].IsVisible);
        Assert.False(vm.Profiles[1].IsVisible);
    }

    [Fact]
    public void SearchByName_CaseInsensitive()
    {
        AddProfile("Gaming");
        var vm = MakeViewModel();

        vm.SearchText = "GAMING";

        Assert.True(vm.Profiles[0].IsVisible);
    }

    [Fact]
    public void SearchByMode_PlaybackOnly_ShowsCorrectCard()
    {
        AddProfile("Speakers", ProfileMode.Playback);
        AddProfile("Both",     ProfileMode.Both);
        var vm = MakeViewModel();

        vm.SearchText = "playback";

        Assert.True(vm.Profiles[0].IsVisible);
        Assert.False(vm.Profiles[1].IsVisible);
    }

    [Fact]
    public void SearchByMode_Both_ShowsCorrectCard()
    {
        AddProfile("Speakers", ProfileMode.Playback);
        AddProfile("Full",     ProfileMode.Both);
        var vm = MakeViewModel();

        vm.SearchText = "both";

        Assert.False(vm.Profiles[0].IsVisible);
        Assert.True(vm.Profiles[1].IsVisible);
    }

    [Fact]
    public void SearchPinned_ShowsOnlyPinnedCards()
    {
        AddProfile("Gaming", isPinned: true);
        AddProfile("Work",   isPinned: false);
        var vm = MakeViewModel();

        vm.SearchText = "pinned";

        Assert.True(vm.Profiles[0].IsVisible);
        Assert.False(vm.Profiles[1].IsVisible);
    }

    [Fact]
    public void SearchScheduled_ShowsOnlyCardsWithSchedule()
    {
        var schedule = new ScheduleEntry { Days = [DayOfWeek.Monday], Hour = 9 };
        AddProfile("Morning", schedules: [schedule]);
        AddProfile("Evening");
        var vm = MakeViewModel();

        vm.SearchText = "scheduled";

        Assert.True(vm.Profiles[0].IsVisible);
        Assert.False(vm.Profiles[1].IsVisible);
    }

    [Fact]
    public void SearchByDayOfWeek_ShowsCardsScheduledOnThatDay()
    {
        var mondaySchedule = new ScheduleEntry { Days = [DayOfWeek.Monday], Hour = 9 };
        AddProfile("Work",     schedules: [mondaySchedule]);
        AddProfile("Weekend");
        var vm = MakeViewModel();

        vm.SearchText = "monday";

        Assert.True(vm.Profiles[0].IsVisible);
        Assert.False(vm.Profiles[1].IsVisible);
    }

    [Fact]
    public void NoMatch_HasNoFilterResultsIsTrue()
    {
        AddProfile("Gaming");
        AddProfile("Work");
        var vm = MakeViewModel();

        vm.SearchText = "zzz";

        Assert.True(vm.HasNoFilterResults);
        Assert.All(vm.Profiles, p => Assert.False(p.IsVisible));
    }

    [Fact]
    public void ClearSearch_AllCardsRestored()
    {
        AddProfile("Gaming");
        AddProfile("Work");
        var vm = MakeViewModel();
        vm.SearchText = "zzz";

        vm.SearchText = "";

        Assert.All(vm.Profiles, p => Assert.True(p.IsVisible));
        Assert.False(vm.HasNoFilterResults);
    }

    [Fact]
    public void RememberSearch_Disabled_LastSearchNotSaved()
    {
        AddProfile("Gaming");
        var vm = MakeViewModel();
        vm.RememberSearch = false;

        vm.SearchText = "gam";

        Assert.Equal("", _fakeConfig.Current.LastSearch);
    }

    [Fact]
    public void RememberSearch_Enabled_LastSearchSaved()
    {
        AddProfile("Gaming");
        _fakeConfig.Current.RememberSearch = true;
        var vm = MakeViewModel();

        vm.SearchText = "gam";

        Assert.Equal("gam", _fakeConfig.Current.LastSearch);
    }

    [Fact]
    public void RememberSearch_Disabled_ClearsLastSearch()
    {
        _fakeConfig.Current.RememberSearch = true;
        _fakeConfig.Current.LastSearch     = "gam";
        var vm = MakeViewModel();

        vm.RememberSearch = false;

        Assert.Equal("", _fakeConfig.Current.LastSearch);
    }

    [Fact]
    public void PersistedSearch_RestoredOnLoad()
    {
        AddProfile("Gaming");
        AddProfile("Work");
        _fakeConfig.Current.RememberSearch = true;
        _fakeConfig.Current.LastSearch     = "gam";

        var vm = MakeViewModel();

        Assert.Equal("gam", vm.SearchText);
        Assert.True(vm.Profiles[0].IsVisible);
        Assert.False(vm.Profiles[1].IsVisible);
    }
}
