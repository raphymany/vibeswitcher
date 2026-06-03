using VibeSwitcher.Models;
using VibeSwitcher.ViewModels;
using Xunit;

namespace VibeSwitcher.Tests;

public class SettingsSearchTests
{
    private readonly FakeConfigService  _fakeConfig  = new();
    private readonly FakeAudioService   _fakeAudio   = new();
    private readonly FakeHotkeyService  _fakeHotkey  = new();
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

    // ── Name filter ─────────────────────────────────────────────────────────

    [Fact]
    public void NoFilters_AllCardsVisible()
    {
        AddProfile("Gaming");
        AddProfile("Work");
        var vm = MakeViewModel();

        Assert.All(vm.Profiles, p => Assert.True(p.IsVisible));
        Assert.False(vm.HasNoFilterResults);
        Assert.False(vm.IsAnyFilterActive);
    }

    [Fact]
    public void NameFilter_MatchingCardVisible_OtherHidden()
    {
        AddProfile("Gaming");
        AddProfile("Work");
        var vm = MakeViewModel();

        vm.NameFilter = "gam";

        Assert.True(vm.Profiles[0].IsVisible);
        Assert.False(vm.Profiles[1].IsVisible);
    }

    [Fact]
    public void NameFilter_CaseInsensitive()
    {
        AddProfile("Gaming");
        var vm = MakeViewModel();

        vm.NameFilter = "GAMING";

        Assert.True(vm.Profiles[0].IsVisible);
    }

    [Fact]
    public void NameFilter_Clear_AllCardsRestored()
    {
        AddProfile("Gaming");
        AddProfile("Work");
        var vm = MakeViewModel();
        vm.NameFilter = "zzz";

        vm.NameFilter = "";

        Assert.All(vm.Profiles, p => Assert.True(p.IsVisible));
        Assert.False(vm.HasNoFilterResults);
    }

    // ── Mode filter ──────────────────────────────────────────────────────────

    [Fact]
    public void ModeFilter_PlaybackOnly_ShowsCorrectCards()
    {
        AddProfile("Speakers", ProfileMode.Playback);
        AddProfile("Full",     ProfileMode.Both);
        var vm = MakeViewModel();

        vm.ModeFilter = "Playback only";

        Assert.True(vm.Profiles[0].IsVisible);
        Assert.False(vm.Profiles[1].IsVisible);
    }

    [Fact]
    public void ModeFilter_AnyMode_ShowsAll()
    {
        AddProfile("Speakers", ProfileMode.Playback);
        AddProfile("Full",     ProfileMode.Both);
        var vm = MakeViewModel();

        vm.ModeFilter = "Any mode";

        Assert.All(vm.Profiles, p => Assert.True(p.IsVisible));
    }

    // ── Pinned filter ────────────────────────────────────────────────────────

    [Fact]
    public void PinnedFilter_ShowsOnlyPinnedCards()
    {
        AddProfile("Gaming", isPinned: true);
        AddProfile("Work",   isPinned: false);
        var vm = MakeViewModel();

        vm.PinnedFilter = true;

        Assert.True(vm.Profiles[0].IsVisible);
        Assert.False(vm.Profiles[1].IsVisible);
    }

    // ── Scheduled filter ─────────────────────────────────────────────────────

    [Fact]
    public void ScheduledFilter_ShowsOnlyCardsWithSchedule()
    {
        var schedule = new ScheduleEntry { Days = [DayOfWeek.Monday], Hour = 9 };
        AddProfile("Morning", schedules: [schedule]);
        AddProfile("Evening");
        var vm = MakeViewModel();

        vm.ScheduledFilter = true;

        Assert.True(vm.Profiles[0].IsVisible);
        Assert.False(vm.Profiles[1].IsVisible);
    }

    [Fact]
    public void ScheduledFilter_Off_ClearsDayChips()
    {
        var schedule = new ScheduleEntry { Days = [DayOfWeek.Monday] };
        AddProfile("Morning", schedules: [schedule]);
        var vm = MakeViewModel();
        vm.ScheduledFilter = true;
        vm.DayChips.First(d => d.Day == DayOfWeek.Monday).IsSelected = true;

        vm.ScheduledFilter = false;

        Assert.All(vm.DayChips, d => Assert.False(d.IsSelected));
    }

    // ── Day chip filter ──────────────────────────────────────────────────────

    [Fact]
    public void DayChip_Monday_ShowsCardsScheduledOnMonday()
    {
        var mondaySchedule = new ScheduleEntry { Days = [DayOfWeek.Monday] };
        AddProfile("Work",    schedules: [mondaySchedule]);
        AddProfile("Weekend");
        var vm = MakeViewModel();
        vm.ScheduledFilter = true;

        vm.DayChips.First(d => d.Day == DayOfWeek.Monday).IsSelected = true;

        Assert.True(vm.Profiles[0].IsVisible);
        Assert.False(vm.Profiles[1].IsVisible);
    }

    // ── Combined filters ─────────────────────────────────────────────────────

    [Fact]
    public void CombinedFilter_NameAndMode_BothApplied()
    {
        AddProfile("Gaming", ProfileMode.Playback);
        AddProfile("Work",   ProfileMode.Both);
        AddProfile("Gaming Mic", ProfileMode.Both);
        var vm = MakeViewModel();

        vm.NameFilter = "gaming";
        vm.ModeFilter = "Both devices";

        Assert.False(vm.Profiles[0].IsVisible); // name matches, mode doesn't
        Assert.False(vm.Profiles[1].IsVisible); // mode matches, name doesn't
        Assert.True(vm.Profiles[2].IsVisible);  // both match
    }

    // ── No results / IsAnyFilterActive ───────────────────────────────────────

    [Fact]
    public void NoMatch_HasNoFilterResultsIsTrue()
    {
        AddProfile("Gaming");
        var vm = MakeViewModel();

        vm.NameFilter = "zzz";

        Assert.True(vm.HasNoFilterResults);
    }

    [Fact]
    public void IsAnyFilterActive_TrueWhenNameSet()
    {
        var vm = MakeViewModel();
        vm.NameFilter = "gam";
        Assert.True(vm.IsAnyFilterActive);
    }

    [Fact]
    public void IsAnyFilterActive_TrueWhenModeChanged()
    {
        var vm = MakeViewModel();
        vm.ModeFilter = "Playback only";
        Assert.True(vm.IsAnyFilterActive);
    }

    [Fact]
    public void IsAnyFilterActive_FalseWhenAllDefault()
    {
        var vm = MakeViewModel();
        Assert.False(vm.IsAnyFilterActive);
    }

    // ── Clear all ────────────────────────────────────────────────────────────

    [Fact]
    public void ClearFilters_ResetsAllFilters()
    {
        AddProfile("Gaming", ProfileMode.Playback, isPinned: true);
        AddProfile("Work",   ProfileMode.Both);
        var vm = MakeViewModel();
        vm.NameFilter      = "gam";
        vm.ModeFilter      = "Playback only";
        vm.PinnedFilter    = true;
        vm.ScheduledFilter = true;

        vm.ClearFiltersCommand.Execute(null);

        Assert.Equal("", vm.NameFilter);
        Assert.Equal("Any mode", vm.ModeFilter);
        Assert.False(vm.PinnedFilter);
        Assert.False(vm.ScheduledFilter);
        Assert.False(vm.IsAnyFilterActive);
        Assert.All(vm.Profiles, p => Assert.True(p.IsVisible));
    }

    // ── Remember last search ─────────────────────────────────────────────────

    [Fact]
    public void RememberSearch_Enabled_NameFilterPersisted()
    {
        AddProfile("Gaming");
        _fakeConfig.Current.RememberSearch = true;
        var vm = MakeViewModel();

        vm.NameFilter = "gam";

        Assert.Equal("gam", _fakeConfig.Current.LastSearch);
    }

    [Fact]
    public void RememberSearch_Disabled_NameFilterNotPersisted()
    {
        AddProfile("Gaming");
        var vm = MakeViewModel();

        vm.NameFilter = "gam";

        Assert.Equal("", _fakeConfig.Current.LastSearch);
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
    public void PersistedNameFilter_RestoredOnLoad()
    {
        AddProfile("Gaming");
        AddProfile("Work");
        _fakeConfig.Current.RememberSearch = true;
        _fakeConfig.Current.LastSearch     = "gam";

        var vm = MakeViewModel();

        Assert.Equal("gam", vm.NameFilter);
        Assert.True(vm.Profiles[0].IsVisible);
        Assert.False(vm.Profiles[1].IsVisible);
    }
}
