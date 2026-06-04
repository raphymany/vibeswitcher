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

    // ── Mode filter ──────────────────────────────────────────────────────────

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
    public void ModeFilter_PlaybackOnly_ShowsCorrectCards()
    {
        AddProfile("Speakers", ProfileMode.Playback);
        AddProfile("Full",     ProfileMode.Both);
        var vm = MakeViewModel();

        vm.ModePlayback = true;

        Assert.True(vm.Profiles[0].IsVisible);
        Assert.False(vm.Profiles[1].IsVisible);
    }

    [Fact]
    public void ModeFilter_AnyMode_ShowsAll()
    {
        AddProfile("Speakers", ProfileMode.Playback);
        AddProfile("Full",     ProfileMode.Both);
        var vm = MakeViewModel();

        // default state — no mode chip selected means "Any mode"
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

    // ── Active / Silent / Hotkey / Notes / Icon / Warning / Sound filters ───

    [Fact]
    public void ActiveFilter_ShowsOnlyActiveProfile()
    {
        _fakeConfig.Current.ActiveProfileId = null; // none active
        AddProfile("Gaming");
        AddProfile("Work");
        var vm = MakeViewModel();

        vm.ActiveFilter = true;

        Assert.All(vm.Profiles, p => Assert.False(p.IsVisible));
    }

    [Fact]
    public void SilentFilter_ShowsOnlySilentProfiles()
    {
        _fakeConfig.Current.Profiles.Add(new DeviceProfile { Name = "Silent", Silent = true });
        _fakeConfig.Current.Profiles.Add(new DeviceProfile { Name = "Loud",   Silent = false });
        var vm = MakeViewModel();

        vm.SilentFilter = true;

        Assert.True(vm.Profiles[0].IsVisible);
        Assert.False(vm.Profiles[1].IsVisible);
    }

    [Fact]
    public void HotkeyFilter_ShowsOnlyProfilesWithHotkey()
    {
        _fakeConfig.Current.Profiles.Add(new DeviceProfile
            { Name = "WithKey", Hotkey = new HotkeyDefinition { VirtualKeyCode = 71, UseCtrl = true } });
        _fakeConfig.Current.Profiles.Add(new DeviceProfile { Name = "NoKey" });
        var vm = MakeViewModel();

        vm.HotkeyFilter = true;

        Assert.True(vm.Profiles[0].IsVisible);
        Assert.False(vm.Profiles[1].IsVisible);
    }

    [Fact]
    public void NotesFilter_ShowsOnlyProfilesWithNotes()
    {
        _fakeConfig.Current.Profiles.Add(new DeviceProfile { Name = "WithNotes", Notes = "My notes" });
        _fakeConfig.Current.Profiles.Add(new DeviceProfile { Name = "NoNotes" });
        var vm = MakeViewModel();

        vm.NotesFilter = true;

        Assert.True(vm.Profiles[0].IsVisible);
        Assert.False(vm.Profiles[1].IsVisible);
    }

    [Fact]
    public void IconFilter_ShowsOnlyProfilesWithCustomIcon()
    {
        _fakeConfig.Current.Profiles.Add(new DeviceProfile { Name = "WithIcon", IconPath = "C:\\icon.ico" });
        _fakeConfig.Current.Profiles.Add(new DeviceProfile { Name = "NoIcon" });
        var vm = MakeViewModel();

        vm.IconFilter = true;

        Assert.True(vm.Profiles[0].IsVisible);
        Assert.False(vm.Profiles[1].IsVisible);
    }

    [Fact]
    public void ReminderFilter_ShowsOnlyProfilesWithReminderSchedule()
    {
        var withReminder = new ScheduleEntry { Days = [DayOfWeek.Monday], ReminderMinutes = 10 };
        var noReminder   = new ScheduleEntry { Days = [DayOfWeek.Monday], ReminderMinutes = 0 };
        AddProfile("Reminded",   schedules: [withReminder]);
        AddProfile("NoReminder", schedules: [noReminder]);
        var vm = MakeViewModel();

        vm.ReminderFilter = true;

        Assert.True(vm.Profiles[0].IsVisible);
        Assert.False(vm.Profiles[1].IsVisible);
    }

    [Fact]
    public void SoundFilter_ShowsOnlyProfilesWithSoundOverride()
    {
        _fakeConfig.Current.Profiles.Add(new DeviceProfile { Name = "WithSound", SoundOverride = true, SoundTone = "Click", SoundVolume = 50 });
        _fakeConfig.Current.Profiles.Add(new DeviceProfile { Name = "NoSound" });
        var vm = MakeViewModel();

        vm.SoundFilter = true;

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

    [Fact]
    public void ModeFilter_DeselectChip_RevetsToAnyMode()
    {
        AddProfile("Speakers", ProfileMode.Playback);
        AddProfile("Full",     ProfileMode.Both);
        var vm = MakeViewModel();
        vm.ModePlayback = true;

        vm.ModePlayback = false; // re-click to deselect

        Assert.False(vm.ModePlayback);
        Assert.False(vm.IsAnyFilterActive);
        Assert.All(vm.Profiles, p => Assert.True(p.IsVisible));
    }

    // ── Combined filters ─────────────────────────────────────────────────────

    [Fact]
    public void CombinedFilter_PinnedAndMode_BothApplied()
    {
        AddProfile("Gaming", ProfileMode.Playback, isPinned: true);
        AddProfile("Work",   ProfileMode.Both,     isPinned: false);
        AddProfile("Pinned Both", ProfileMode.Both, isPinned: true);
        var vm = MakeViewModel();

        vm.PinnedFilter = true;
        vm.ModeBoth     = true;

        Assert.False(vm.Profiles[0].IsVisible); // pinned but wrong mode
        Assert.False(vm.Profiles[1].IsVisible); // right mode but not pinned
        Assert.True(vm.Profiles[2].IsVisible);  // both match
    }

    // ── No results / IsAnyFilterActive ───────────────────────────────────────

    [Fact]
    public void NoMatch_HasNoFilterResultsIsTrue()
    {
        AddProfile("Gaming");
        var vm = MakeViewModel();

        vm.PinnedFilter = true; // Gaming is not pinned

        Assert.True(vm.HasNoFilterResults);
    }

    [Fact]
    public void IsAnyFilterActive_TrueWhenModeChanged()
    {
        var vm = MakeViewModel();
        vm.ModePlayback = true;
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
        vm.ModePlayback    = true;
        vm.PinnedFilter    = true;
        vm.ScheduledFilter = true;

        vm.ClearFiltersCommand.Execute(null);

        Assert.False(vm.ModePlayback);
        Assert.False(vm.ModeRecording);
        Assert.False(vm.ModeBoth);
        Assert.False(vm.PinnedFilter);
        Assert.False(vm.ScheduledFilter);
        Assert.False(vm.IsAnyFilterActive);
        Assert.All(vm.Profiles, p => Assert.True(p.IsVisible));
    }
}
