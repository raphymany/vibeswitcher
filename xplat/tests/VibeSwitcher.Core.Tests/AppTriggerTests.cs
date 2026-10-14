using VibeSwitcher.Models;
using VibeSwitcher.Platform;
using VibeSwitcher.Services;
using VibeSwitcher.Tests;

namespace VibeSwitcher.Core.Tests;

public class AppTriggerMatchTests
{
    private static RunningProcess P(string? exe, string? appId = null, params string[] names) =>
        new(exe, names.Length > 0 ? names : exe == null ? [] : [Path.GetFileName(exe)], appId);

    [Fact]
    public void ProgramFile_MatchesThatFile_OrTheSameProgramName()
    {
        Assert.True(AppTriggerMatch.Matches("/usr/lib/firefox/firefox", P("/usr/lib/firefox/firefox")));
        // A launcher script or symlink: /usr/bin/firefox runs /usr/lib/firefox/firefox.
        Assert.True(AppTriggerMatch.Matches("/usr/bin/firefox", P("/usr/lib/firefox/firefox")));
        Assert.False(AppTriggerMatch.Matches("/usr/bin/firefox", P("/usr/bin/thunderbird")));
        // Linux paths are case-sensitive; names (like Windows) aren't.
        Assert.True(AppTriggerMatch.Matches("/opt/Discord/Discord", P("/opt/discord/discord")));
    }

    [Fact]
    public void LinuxShortensProcessNames_LongProgramNamesStillMatch()
    {
        var proc = P(null, null, "gnome-calculato"); // 15 characters, as the kernel keeps it
        Assert.True(AppTriggerMatch.Matches("/usr/bin/gnome-calculator", proc));
        Assert.False(AppTriggerMatch.Matches("/usr/bin/gnome-calc", proc));
    }

    [Fact]
    public void MacBundle_MatchesItsProgramsAndHelpers_NotOthers()
    {
        const string discord = "/Applications/Discord.app";
        Assert.True(AppTriggerMatch.Matches(discord, P("/Applications/Discord.app/Contents/MacOS/Discord", discord)));
        Assert.True(AppTriggerMatch.Matches(discord + "/", P("/Applications/Discord.app/Contents/Frameworks/Discord Helper.app/Contents/MacOS/Discord Helper", discord)));
        Assert.True(AppTriggerMatch.Matches("/applications/discord.app", P("/Applications/Discord.app/Contents/MacOS/Discord", discord)));
        Assert.False(AppTriggerMatch.Matches(discord, P("/Applications/Discord PTB.app/Contents/MacOS/Discord", "/Applications/Discord PTB.app")));
        // Bundles match by location only: a program merely named "Discord" elsewhere isn't it.
        Assert.False(AppTriggerMatch.Matches(discord, P("/usr/local/bin/Discord")));
    }

    [Fact]
    public void FlatpakAndSnap_MatchOnlyTheirId()
    {
        var maps = P("/app/bin/gnome-maps", "flatpak:org.gnome.Maps");
        Assert.True(AppTriggerMatch.Matches("flatpak:org.gnome.Maps", maps));
        Assert.False(AppTriggerMatch.Matches("flatpak:org.gnome.Weather", maps));
        Assert.False(AppTriggerMatch.Matches("flatpak:org.gnome.Maps", P("/usr/bin/gnome-maps")));
        Assert.True(AppTriggerMatch.Matches("snap:firefox", P("/snap/firefox/4793/usr/lib/firefox/firefox", "snap:firefox")));
        Assert.False(AppTriggerMatch.Matches("snap:firefox", P("/usr/lib/firefox/firefox")));
    }

    [Fact]
    public void AppImage_MatchesTheFileItRunsFrom()
    {
        const string file = "/home/sam/Apps/Obsidian.AppImage";
        Assert.True(AppTriggerMatch.Matches(file, P("/tmp/.mount_ObsidiXYZ/obsidian", file)));
        Assert.True(AppTriggerMatch.Matches(file, P("/tmp/.mount_Obs/Obsidian"))); // by name, like Windows
    }

    [Fact]
    public void WindowsProgramsUnderWine_MatchByTheirExeName()
    {
        // Wine names the process "Game.exe"; its argv[0] reads "C:\…\Game.exe".
        var game = P("/usr/lib/wine/wine64-preloader", null, "wine64-preloader", "Game.exe");
        Assert.True(AppTriggerMatch.Matches(@"C:\Games\Game.exe", game));
        Assert.True(AppTriggerMatch.Matches("Game.exe", game));
        // The kernel's 15-character name: "LongGameName.ex" for LongGameName.exe.
        Assert.True(AppTriggerMatch.Matches(@"C:\Games\LongGameName.exe", P(null, null, "LongGameName.ex")));
        Assert.False(AppTriggerMatch.Matches(@"C:\Games\LongGameNameX.exe", P(null, null, "LongGameName.ex")));
    }

    [Fact]
    public void MacAppsMovedByMacOS_StillMatch()
    {
        // A downloaded app run from a random read-only copy, and Safari's real home.
        Assert.True(AppTriggerMatch.Matches("/Applications/Foo.app",
            P("/private/var/folders/x/T/AppTranslocation/1234-ABCD/d/Foo.app/Contents/MacOS/Foo", "/private/var/folders/x/T/AppTranslocation/1234-ABCD/d/Foo.app")));
        Assert.True(AppTriggerMatch.Matches("/Applications/Safari.app",
            P("/System/Volumes/Preboot/Cryptexes/App/System/Applications/Safari.app/Contents/MacOS/Safari", "/System/Volumes/Preboot/Cryptexes/App/System/Applications/Safari.app")));
        Assert.False(AppTriggerMatch.Matches("/Applications/Foo.app",
            P("/private/var/folders/x/T/AppTranslocation/1234-ABCD/d/Bar.app/Contents/MacOS/Foo", "/private/var/folders/x/T/AppTranslocation/1234-ABCD/d/Bar.app")));
        // Elsewhere the same name isn't enough: another copy in Downloads is a different app.
        Assert.False(AppTriggerMatch.Matches("/Applications/Foo.app", P("/Users/sam/Downloads/Foo.app/Contents/MacOS/Foo", "/Users/sam/Downloads/Foo.app")));
    }

    [Fact]
    public void ConfigFromWindows_MatchesByProgramName()
    {
        Assert.True(AppTriggerMatch.Matches(@"C:\Users\Sam\AppData\Local\Discord\app-1.0\Discord.exe", P("/opt/discord/Discord")));
        Assert.False(AppTriggerMatch.Matches(@"C:\Games\Steam\steam.exe", P("/usr/bin/discord")));
    }

    [Theory]
    [InlineData(@"C:\Program Files\App\App.exe", "App")]
    [InlineData("/Applications/Visual Studio Code.app", "Visual Studio Code")]
    [InlineData("/Applications/Visual Studio Code.app/", "Visual Studio Code")]
    [InlineData("/home/sam/Obsidian-1.6.AppImage", "Obsidian-1.6")]
    [InlineData("/usr/bin/python3.12", "python3.12")]
    [InlineData("flatpak:org.gnome.Maps", "")]
    public void TriggerName(string trigger, string name) => Assert.Equal(name, AppTriggerMatch.TriggerName(trigger));

    [Theory]
    [InlineData("flatpak:com.discordapp.Discord", "Discord")]
    [InlineData("snap:spotify", "spotify")]
    [InlineData("/Applications/Slack.app", "Slack")]
    public void DisplayName(string trigger, string name) => Assert.Equal(name, AppTriggerMatch.DisplayName(trigger));

    [Fact]
    public void Nothing_MatchesNothing()
    {
        Assert.False(AppTriggerMatch.Matches("", P("/usr/bin/x")));
        Assert.False(AppTriggerMatch.Matches("/usr/bin/x", P(null)));
    }
}

internal sealed class FakeProcessSource : IRunningProcessSource
{
    public List<RunningProcess> Processes = [];
    public bool Throw;
    public bool Unreadable; // a list that couldn't be read completely
    public void Start(string exe, string? appId = null) { lock (this) Processes.Add(new RunningProcess(exe, [Path.GetFileName(exe)], appId)); }
    public void Stop(string exe) { lock (this) Processes.RemoveAll(p => p.ExePath == exe); }
    public IReadOnlyList<RunningProcess>? Snapshot()
    {
        lock (this) return Throw ? throw new InvalidOperationException("boom") : Unreadable ? null : Processes.ToList();
    }
}

public sealed class AppWatcherServiceForkTests : IDisposable
{
    private readonly FakeProcessSource _procs = new();
    private readonly AppWatcherService _watcher;
    private readonly List<string> _fired = [];

    public AppWatcherServiceForkTests()
    {
        _watcher = new AppWatcherService(new FakeAppLogger(), _procs, Timeout.InfiniteTimeSpan);
        _watcher.ProcessLaunched += t => _fired.Add(t);
    }

    [Fact]
    public void FiresOnLaunch_OnceWhileRunning_AgainAfterReopen()
    {
        _watcher.UpdateWatchList(["/usr/bin/spotify", "flatpak:com.discordapp.Discord"]);
        _watcher.PollCore();
        Assert.Empty(_fired);

        _procs.Start("/usr/share/spotify/spotify");
        _watcher.PollCore();
        _watcher.PollCore();
        Assert.Equal(["/usr/bin/spotify"], _fired);

        _procs.Start("/app/bin/discord", "flatpak:com.discordapp.Discord");
        _watcher.PollCore();
        Assert.Equal(["/usr/bin/spotify", "flatpak:com.discordapp.Discord"], _fired);

        _procs.Stop("/usr/share/spotify/spotify");
        _watcher.PollCore();
        _procs.Start("/usr/share/spotify/spotify");
        _watcher.PollCore();
        Assert.Equal(3, _fired.Count);
    }

    [Fact]
    public void AlreadyRunningWhenTheListChanges_DoesntFire_UntilReopened()
    {
        _procs.Start("/opt/obs/obs");
        _watcher.UpdateWatchList(["/opt/obs/obs"]);
        _watcher.PollCore();
        _watcher.PollCore();
        Assert.Empty(_fired);

        _procs.Stop("/opt/obs/obs");
        _watcher.PollCore();
        _procs.Start("/opt/obs/obs");
        _watcher.PollCore();
        Assert.Equal(["/opt/obs/obs"], _fired);
    }

    [Fact]
    public void TwoTriggersForOneLaunch_FireOnce_TheFirstInTheList()
    {
        // e.g. the same program listed in two profiles in different forms: one switch, like Windows.
        _watcher.UpdateWatchList(["/usr/bin/steam", @"C:\Program Files (x86)\Steam\steam.exe", "/usr/bin/vlc"]);
        _watcher.PollCore();
        _procs.Start("/usr/lib/steam/steam");
        _procs.Start("/usr/bin/vlc"); // a different launch in the same tick still fires
        _watcher.PollCore();
        _watcher.PollCore();
        Assert.Equal(["/usr/bin/steam", "/usr/bin/vlc"], _fired);
    }

    [Fact]
    public void ABroadTriggerListedFirst_DoesntSwallowASpecificLaunch_WhichSwitchesLast()
    {
        // Flatpak Steam and a game inside its sandbox start in the same tick.
        _watcher.UpdateWatchList(["flatpak:com.valvesoftware.Steam", "Game.exe"]);
        _watcher.PollCore();
        lock (_procs)
        {
            _procs.Processes.Add(new RunningProcess("/app/bin/steam", ["steam"], "flatpak:com.valvesoftware.Steam"));
            _procs.Processes.Add(new RunningProcess("/app/proton/wine64-preloader", ["wine64-preloader", "Game.exe"], "flatpak:com.valvesoftware.Steam"));
        }
        _watcher.PollCore();
        Assert.Equal(["flatpak:com.valvesoftware.Steam", "Game.exe"], _fired); // the game's profile ends up active
    }

    [Fact]
    public void AnUnreadableProcessList_IsSkipped_NotTakenAsEverythingClosed()
    {
        _procs.Start("/usr/bin/obs");
        _watcher.UpdateWatchList(["/usr/bin/obs"]);
        _watcher.PollCore();
        _procs.Unreadable = true;
        _watcher.PollCore();
        _procs.Unreadable = false;
        _watcher.PollCore();
        Assert.Empty(_fired); // obs never closed

        // Can't tell what's running when the list changes: nothing fires for it.
        _procs.Unreadable = true;
        _watcher.UpdateWatchList(["/usr/bin/obs", "/usr/bin/vlc"]);
        _procs.Unreadable = false;
        _procs.Start("/usr/bin/vlc");
        _watcher.PollCore();
        Assert.Empty(_fired);
    }

    [Fact]
    public void EmptyList_OrAFailingProcessList_FiresNothing()
    {
        _procs.Start("/usr/bin/x");
        _watcher.UpdateWatchList([]);
        _watcher.PollCore();
        _procs.Throw = true;
        _watcher.UpdateWatchList(["/usr/bin/y"]);
        _watcher.PollCore();
        _procs.Throw = false;
        Assert.Empty(_fired);
    }

    [Fact]
    public void RealTimer_Polls()
    {
        using var w = new AppWatcherService(new FakeAppLogger(), _procs, TimeSpan.FromMilliseconds(20));
        var fired = new List<string>();
        w.ProcessLaunched += t => { lock (fired) fired.Add(t); };
        w.UpdateWatchList(["/usr/bin/z"]);
        Thread.Sleep(100);
        _procs.Start("/usr/bin/z");
        var until = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < until) { lock (fired) if (fired.Count > 0) break; Thread.Sleep(10); }
        lock (fired) Assert.Equal(["/usr/bin/z"], fired);
    }

    public void Dispose() => _watcher.Dispose();
}

public sealed class AppTriggerServiceForkTests : IDisposable
{
    private readonly FakeConfigService _config = new();
    private readonly FakeProcessSource _procs = new();
    private readonly AppWatcherService _watcher;
    private readonly List<DeviceProfile> _switched = [];

    public AppTriggerServiceForkTests() =>
        _watcher = new AppWatcherService(new FakeAppLogger(), _procs, Timeout.InfiniteTimeSpan);

    private DeviceProfile Profile(string name, params string[] triggers)
    {
        var p = new DeviceProfile { Name = name, AppTriggers = [.. triggers] };
        _config.Current.Profiles.Add(p);
        return p;
    }

    private AppTriggerService Service() => new(_config, _watcher, p => _switched.Add(p), new FakeAppLogger());

    [Fact]
    public void LaunchingAnApp_SwitchesToItsProfile_UnlessAlreadyActive()
    {
        var gaming = Profile("Gaming", "flatpak:com.valvesoftware.Steam");
        using var svc = Service();
        _watcher.PollCore();
        _procs.Start("/app/bin/steam", "flatpak:com.valvesoftware.Steam");
        _watcher.PollCore();
        Assert.Equal([gaming], _switched);

        _config.Current.ActiveProfileId = gaming.Id;
        svc.HandleLaunch("flatpak:com.valvesoftware.Steam");
        Assert.Single(_switched);
    }

    [Fact]
    public void TwoFlatpaksFromOnePublisher_EachSwitchesToItsOwnProfile()
    {
        // Windows' file-name matching would read both as "flatpak:org.gnome".
        var maps = Profile("Maps", "flatpak:org.gnome.Maps");
        var music = Profile("Music", "flatpak:org.gnome.Music");
        using var svc = Service();
        svc.HandleLaunch("flatpak:org.gnome.Music");
        svc.HandleLaunch("flatpak:org.gnome.Maps");
        Assert.Equal([music, maps], _switched);
    }

    [Fact]
    public void ProgramNameFallback_LikeWindows()
    {
        var p = Profile("Chat", @"C:\Apps\Discord\Discord.exe");
        using var svc = Service();
        svc.HandleLaunch("/opt/discord/Discord");
        Assert.Equal([p], _switched);
    }

    [Fact]
    public void RefreshWatchList_PicksUpChanges()
    {
        var p = Profile("Video");
        using var svc = Service();
        _watcher.PollCore();
        p.AppTriggers.Add("/usr/bin/vlc");
        svc.RefreshWatchList();
        _watcher.PollCore();
        _procs.Start("/usr/bin/vlc");
        _watcher.PollCore();
        Assert.Equal([p], _switched);
    }

    public void Dispose() => _watcher.Dispose();
}
