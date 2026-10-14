using System.Diagnostics;
using VibeSwitcher.Platform;
using VibeSwitcher.Platform.Linux.Apps;
using VibeSwitcher.Services;
using VibeSwitcher.Tests;

namespace VibeSwitcher.Platform.Linux.Tests;

// Runs on Linux only (real /proc, real files and symlinks); CI's Linux build runs it.
public sealed class LinuxFactAttribute : FactAttribute
{
    public LinuxFactAttribute()
    {
        if (!OperatingSystem.IsLinux()) Skip = "Linux only.";
    }
}

public sealed class LinuxProcessesTests : IDisposable
{
    private readonly string _proc = Path.Combine(Path.GetTempPath(), "vs-proc-" + Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string, string> _links = [];

    private void Process(int pid, string? exe, string comm, string cmdline, string cgroup = "0::/user.slice/user-1000.slice/session-2.scope\n", string? environ = null)
    {
        var dir = Path.Combine(_proc, pid.ToString());
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "comm"), comm + "\n");
        File.WriteAllText(Path.Combine(dir, "cmdline"), cmdline);
        File.WriteAllText(Path.Combine(dir, "cgroup"), cgroup);
        if (environ != null) File.WriteAllText(Path.Combine(dir, "environ"), environ);
        if (exe != null) _links[Path.Combine(dir, "exe")] = exe;
    }

    private LinuxProcesses Source() => new(_proc, p => _links.GetValueOrDefault(p));

    [Fact]
    public void ReadsProgramNamesAndSandboxes()
    {
        Process(100, "/usr/lib/firefox/firefox", "firefox", "/usr/lib/firefox/firefox\0-new-window\0");
        Process(101, "/app/bin/gnome-maps", "gnome-maps", "/app/bin/gnome-maps\0",
            "0::/user.slice/user-1000.slice/user@1000.service/app.slice/app-flatpak-org.gnome.Maps-4242.scope\n");
        Process(102, "/snap/spotify/80/usr/share/spotify/spotify", "spotify", "spotify\0",
            "0::/user.slice/user-1000.slice/user@1000.service/app.slice/snap.spotify.spotify-1f2e3d.scope\n");
        Process(103, "/tmp/.mount_ObsidiXYZ/obsidian", "obsidian", "/tmp/.mount_ObsidiXYZ/obsidian\0",
            environ: "HOME=/home/sam\0APPIMAGE=/home/sam/Apps/Obsidian.AppImage\0LANG=C\0");
        Process(104, null, "kworker/0:1", "");                       // kernel thread: left out
        Process(105, null, "sshd", "sshd: /usr/sbin/sshd\0");      // another user's: by name only
        Process(106, "/usr/bin/python3.12 (deleted)", "python3", "python3\0-u\0/usr/share/foo/foo.py\0");
        Process(107, "/usr/lib/wine/wine64-preloader", "Game.exe", "C:\\Games\\Game.exe\0");
        Process(108, "/opt/google/chrome/chrome", "chrome", "/opt/google/chrome/chrome\0",
            "0::/user.slice/user-1000.slice/user@1000.service/app.slice/app-gnome-google\\x2dchrome-4242.scope\n");
        // Chromium rewrites its command line into one string: argv[0] then names nothing.
        Process(109, "/opt/app/app", "app", "/opt/app/app --type=renderer --user-data-dir=/home/sam/x/foo\0");
        Directory.CreateDirectory(Path.Combine(_proc, "self"));   // not a pid

        var list = Source().Snapshot()!.ToDictionary(p => p.Names[0]);
        Assert.Equal(9, list.Count);
        Assert.Equal("/usr/lib/firefox/firefox", list["firefox"].ExePath);
        Assert.Equal("flatpak:org.gnome.Maps", list["gnome-maps"].AppId);
        Assert.Equal("snap:spotify", list["spotify"].AppId);
        Assert.Equal("/home/sam/Apps/Obsidian.AppImage", list["obsidian"].AppId);
        Assert.Null(list["sshd"].ExePath);
        Assert.Equal(["sshd"], list["sshd"].Names);
        Assert.Equal("/usr/bin/python3.12", list["python3.12"].ExePath);
        Assert.Equal(["python3.12", "python3"], list["python3.12"].Names);
        Assert.Equal("/usr/share/foo/foo.py", list["python3.12"].AppId); // the script it runs, by its path
        Assert.Equal(["wine64-preloader", "Game.exe"], list["wine64-preloader"].Names);
        Assert.Equal(["chrome", "google-chrome"], list["chrome"].Names);             // its desktop entry
        Assert.Equal(["app"], list["app"].Names);
    }

    [Fact]
    public void ACachedProcessThatExecsAnotherProgram_IsReadAgain()
    {
        Process(200, "/usr/bin/launcher", "launcher", "/usr/bin/launcher\0");
        var source = Source();
        Assert.Equal("/usr/bin/launcher", Assert.Single(source.Snapshot()!).ExePath);
        Process(200, "/opt/game/game", "game", "/opt/game/game\0");
        Assert.Equal("/opt/game/game", Assert.Single(source.Snapshot()!).ExePath);
        // Same program, new process name: a shell running a different script.
        Process(200, "/opt/game/game", "game-server", "/opt/game/game\0--server\0");
        Assert.Contains("game-server", Assert.Single(source.Snapshot()!).Names);
        Directory.Delete(Path.Combine(_proc, "200"), recursive: true);
        Assert.Empty(source.Snapshot()!);
    }

    [Fact]
    public void ProtonGameWithSpacesInItsPath_KeepsItsExeName()
    {
        // Wine puts the Windows path in argv[0]; Proton games have spaces there and often no arguments.
        Process(300, "/home/sam/.steam/proton/files/bin/wine64-preloader", "SomeLongGameNa",
            "Z:\\home\\sam\\.steam\\steamapps\\common\\Some Game\\SomeLongGameName.exe\0");
        var p = Assert.Single(Source().Snapshot()!);
        Assert.Contains("SomeLongGameName.exe", p.Names);
        Assert.True(AppTriggerMatch.Matches("SomeLongGameName.exe", p));
    }

    [Fact]
    public void ARelativeScript_IsFoundFromTheProcessesWorkingFolder()
    {
        Process(301, "/usr/bin/node", "node", "node\0--max-old-space-size=4096\0index.js\0");
        _links[Path.Combine(_proc, "301", "cwd")] = "/opt/myapp";
        Process(302, "/usr/bin/node", "node", "node\0-e\0console.log(1)\0"); // inline code: no script
        var list = Source().Snapshot()!;
        Assert.Equal("/opt/myapp/index.js", list.Single(p => p.Names.Count == 1 && p.AppId != null).AppId);
        Assert.Single(list, p => p.AppId == null);
        // Another app's "index.js" isn't this one.
        Assert.False(AppTriggerMatch.Matches("/opt/other/index.js", list.First(p => p.AppId != null)));
        Assert.True(AppTriggerMatch.Matches("/opt/myapp/index.js", list.First(p => p.AppId != null)));
    }

    [Fact]
    public void MissingProc_Unreadable() => Assert.Null(new LinuxProcesses(Path.Combine(_proc, "nope"), _ => null).Snapshot());

    [Theory]
    [InlineData("0::/user.slice/user-1000.slice/user@1000.service/app.slice/app-gnome-google\\x2dchrome-4242.scope", "google-chrome")]
    [InlineData("0::/user.slice/user-1000.slice/user@1000.service/app.slice/app-gnome-Some\\x20Game-77.scope", "Some Game")]
    [InlineData("0::/user.slice/user-1000.slice/user@1000.service/app.slice/app-org.kde.dolphin@a1b2c3.service", "org.kde.dolphin")]
    [InlineData("0::/user.slice/user-1000.slice/user@1000.service/app.slice/app-gnome-org.gnome.Nautilus-9.scope", "org.gnome.Nautilus")]
    [InlineData("0::/user.slice/user-1000.slice/session-2.scope", null)]
    [InlineData("0::/user.slice/user-1000.slice/user@1000.service/app.slice/app-org.gnome.Terminal.slice/vte-spawn-1.scope", null)]
    public void DesktopIds(string cgroup, string? id) => Assert.Equal(id, LinuxProcesses.DesktopId(cgroup + "\n"));

    [Theory]
    [InlineData("0::/user.slice/user-1000.slice/user@1000.service/app.slice/app-flatpak-com.discordapp.Discord-91.scope", "flatpak:com.discordapp.Discord")]
    [InlineData("1:name=systemd:/user.slice/user-1000.slice/user@1000.service/flatpak-org.gimp.GIMP-3.scope", "flatpak:org.gimp.GIMP")]
    [InlineData("0::/user.slice/user-1000.slice/user@1000.service/app.slice/snap.code.code-6a5b.scope", "snap:code")]
    [InlineData("0::/user.slice/user-1000.slice/user@1000.service/app.slice/snap.firefox_beta.firefox-6a5b.scope", "snap:firefox_beta")]
    [InlineData("0::/user.slice/user-1000.slice/user@1000.service/app.slice/app-gnome-firefox-1234.scope", null)]
    [InlineData("0::/system.slice/snapd.service", null)]
    public void SandboxIds(string cgroup, string? id) => Assert.Equal(id, LinuxProcesses.SandboxId(cgroup));

    public void Dispose()
    {
        try { Directory.Delete(_proc, recursive: true); } catch (IOException) { }
    }
}

// Real processes: a copy of /bin/sleep under a unique, long name.
public sealed class RealProcessTests : IDisposable
{
    // Next to the tests rather than in /tmp, which may not allow running programs.
    private readonly string _dir = Path.Combine(AppContext.BaseDirectory, "vs-trig-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly List<Process> _started = [];

    private string StartCopyOfSleep(string name)
    {
        Directory.CreateDirectory(_dir);
        var exe = Path.Combine(_dir, name);
        File.Copy("/bin/sleep", exe);
        File.SetUnixFileMode(exe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        _started.Add(Process.Start(exe, "30"));
        return exe;
    }

    [LinuxFact]
    public void TheRealProcessList_HasOurProgram_ByItsFile()
    {
        var exe = StartCopyOfSleep("vibeswitcher-trigger-test"); // longer than the kernel's 15
        var source = new LinuxProcesses();
        RunningProcess? found = null;
        for (int i = 0; i < 50 && found == null; i++)
        {
            found = source.Snapshot()!.FirstOrDefault(p => p.ExePath == exe);
            if (found == null) Thread.Sleep(20);
        }
        Assert.NotNull(found);
        Assert.Contains("vibeswitcher-tr", found.Names); // comm, cut to 15 characters
        Assert.True(AppTriggerMatch.Matches(exe, found));
        Assert.Contains(source.Snapshot()!, p => p.ExePath == Environment.ProcessPath); // ourselves
    }

    [LinuxFact]
    public void TheWatcher_SeesARealLaunch()
    {
        var exe = Path.Combine(_dir, "vs-real-launch");
        using var watcher = new AppWatcherService(new FakeAppLogger(), new LinuxProcesses(), TimeSpan.FromMilliseconds(50));
        var fired = new List<string>();
        watcher.ProcessLaunched += t => { lock (fired) fired.Add(t); };
        watcher.UpdateWatchList([exe]);
        Thread.Sleep(150);
        StartCopyOfSleep("vs-real-launch");
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(5)) { lock (fired) if (fired.Count > 0) break; Thread.Sleep(20); }
        lock (fired) Assert.Equal([exe], fired);
    }

    public void Dispose()
    {
        foreach (var p in _started) { try { p.Kill(); p.WaitForExit(2000); } catch (Exception) { } p.Dispose(); }
        try { Directory.Delete(_dir, recursive: true); } catch (Exception) { }
    }
}

public class DesktopEntryTests
{
    private static string? Trigger(string text, Func<string, string?>? resolve = null) =>
        DesktopEntry.Parse(text, null)?.Trigger(resolve ?? (p => p.StartsWith('/') ? p : "/usr/bin/" + p));

    [Fact]
    public void ParsesTheMainGroup_Localized()
    {
        const string text = """
            # comment
            [Desktop Entry]
            Type=Application
            Name=Files
            Name[de]=Dateien
            Name[de_AT]=Dateien (AT)
            Exec=nautilus --new-window %U
            Icon=org.gnome.Nautilus

            [Desktop Action new-window]
            Name=New Window
            Exec=other
            """;
        var e = DesktopEntry.Parse(text, "de_DE.UTF-8")!;
        Assert.Equal("Dateien", e.Name);
        Assert.Equal("Dateien (AT)", DesktopEntry.Parse(text, "de_AT")!.Name);
        Assert.Equal("Files", DesktopEntry.Parse(text, null)!.Name);
        Assert.Equal("org.gnome.Nautilus", e.Icon);
        Assert.Equal("nautilus --new-window %U", e.Exec);
        Assert.Equal("/usr/bin/nautilus", e.Trigger(p => "/usr/bin/" + p));
    }

    [Theory]
    [InlineData("Exec=\"/opt/My App/app\" --flag %f", "/opt/My App/app")]
    [InlineData("Exec=env GDK_BACKEND=x11 BAMF_DESKTOP_FILE_HINT=/x.desktop /opt/discord/Discord", "/opt/discord/Discord")]
    [InlineData("Exec=/usr/bin/flatpak run --branch=stable --arch=x86_64 --command=spotify com.spotify.Client %U", "flatpak:com.spotify.Client")]
    [InlineData("Exec=env BAMF_DESKTOP_FILE_HINT=/var/lib/snapd/desktop/applications/code_code.desktop /snap/bin/code --force-user-env %F", "snap:code")]
    [InlineData("Exec=/snap/bin/gnome-calculator.calc", "snap:gnome-calculator")]
    [InlineData("Exec=steam %U", "/usr/bin/steam")]
    [InlineData("Exec=env -u WAYLAND_DISPLAY --chdir /opt/x /opt/x/run", "/opt/x/run")]
    [InlineData("Exec=flatpak run --branch stable --arch x86_64 org.gimp.GIMP %U", "flatpak:org.gimp.GIMP")]
    [InlineData("Exec=python3 -u /usr/share/foo/foo.py", "/usr/share/foo/foo.py")]                // a script app
    [InlineData("Exec=java -Xmx1G -jar /opt/tool/tool.jar", "/opt/tool/tool.jar")]
    [InlineData("Exec=env WINEPREFIX=/home/sam/.wine wine C:\\\\Games\\\\Game.exe", "C:\\Games\\Game.exe")] // a Windows program
    [InlineData("Exec=wine C:\\\\Games\\\\Game.exe +connect host:27015", "C:\\Games\\Game.exe")]       // "host:port" isn't a link
    [InlineData("Exec=python3 -W ignore::DeprecationWarning -E /opt/x/x.py", "/opt/x/x.py")]
    [InlineData("Exec=bash -e /opt/x/start.sh", "/opt/x/start.sh")]
    [InlineData("Exec=gjs -m /usr/share/x/main.js", "/usr/share/x/main.js")]
    [InlineData("Exec=myserver --listen localhost:8080", "/usr/bin/myserver")]
    public void TriggerFromExec(string exec, string trigger) =>
        Assert.Equal(trigger, Trigger($"[Desktop Entry]\nType=Application\nName=X\n{exec}\n"));

    [Fact]
    public void ARelativeScript_IsInTheEntrysWorkingFolder() =>
        Assert.Equal("/opt/tool/main.py", Trigger("[Desktop Entry]\nType=Application\nName=X\nPath=/opt/tool\nExec=python3 main.py\n"));

    [Theory]
    [InlineData("Exec=steam steam://rungameid/570")]            // a Steam game shortcut: the game is another program
    [InlineData("Exec=env LUTRIS_SKIP_INIT=1 lutris lutris:rungameid/3")]
    [InlineData("Exec=xdg-open heroic://launch/legendary/abc")]
    [InlineData("Exec=xdg-open /home/sam/notes.pdf")]
    [InlineData("Exec=sh -c \"cd /opt/x && ./run\"")]
    [InlineData("Exec=python3 -m http.server")]
    [InlineData("Exec=pkexec /usr/bin/gparted")]
    [InlineData("Exec=bash -lc \"cd /opt/x; ./run\"")]
    [InlineData("Exec=node -p 1+1")]
    [InlineData("Exec=java -cp /opt/x/lib Main")]
    // Wine's own menu entries: a shortcut run through Wine's start.exe, not a program name.
    [InlineData("Exec=env WINEPREFIX=\"/home/sam/.wine\" wine C:\\\\\\\\windows\\\\\\\\command\\\\\\\\start.exe /Unix /home/sam/.wine/dosdevices/c:/users/Public/Desktop/Game.lnk")]
    public void LinksOpenersAndInlineScripts_AreNotAppsToWatch(string exec) =>
        Assert.Null(Trigger($"[Desktop Entry]\nType=Application\nName=X\n{exec}\n"));

    [Fact]
    public void FlatpakAndSnapKeys_Win()
    {
        Assert.Equal("flatpak:org.gnome.Maps", Trigger("[Desktop Entry]\nType=Application\nName=Maps\nExec=gnome-maps\nX-Flatpak=org.gnome.Maps\n"));
        Assert.Equal("snap:firefox", Trigger("[Desktop Entry]\nType=Application\nName=Firefox\nExec=firefox\nX-SnapInstanceName=firefox\n"));
    }

    [Fact]
    public void NotAnApp_NotInstalled_OrHidden()
    {
        Assert.Null(DesktopEntry.Parse("[Desktop Entry]\nType=Link\nName=Docs\nURL=https://x\n", null));
        Assert.Null(DesktopEntry.Parse("[Desktop Entry]\nType=Application\nExec=x\n", null)); // no name
        Assert.Null(Trigger("[Desktop Entry]\nType=Application\nName=X\nTryExec=gone\nExec=gone\n", _ => null));
        Assert.Null(Trigger("[Desktop Entry]\nType=Application\nName=X\n"));
        Assert.True(DesktopEntry.Parse("[Desktop Entry]\nType=Application\nName=X\nExec=x\nNoDisplay=true\n", null)!.Hidden);
        // The minimal file a user writes to hide a system app still counts (and has no trigger).
        var hide = DesktopEntry.Parse("[Desktop Entry]\nHidden=true\n", null)!;
        Assert.True(hide.Hidden);
        Assert.Null(hide.Trigger(p => p));
    }

    [Fact]
    public void ExecQuotingAndEscapes()
    {
        Assert.Equal(["/a b/c", "say \"hi\"", "100%", "-x"], DesktopEntry.SplitExec("\"/a b/c\" \"say \\\"hi\\\"\" 100%% %u -x %F"));
        Assert.Equal("Line\tTab Space", DesktopEntry.Parse("[Desktop Entry]\nType=Application\nName=Line\\tTab\\sSpace\nExec=x\n", null)!.Name);
    }
}

public sealed class LinuxAppCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vs-apps-" + Guid.NewGuid().ToString("N"));
    private readonly FakeProcessSource _procs = new();

    private string Dir(string name) { var d = Path.Combine(_root, name); Directory.CreateDirectory(d); return d; }
    private static void Entry(string dir, string file, string body) =>
        File.WriteAllText(Path.Combine(dir, file), "[Desktop Entry]\nType=Application\n" + body);

    private string Program(string relative)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "#!/bin/sh\n");
        // (A program is executable: picking a file in the app picker checks it.)
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        return path;
    }

    private LinuxAppCatalog Catalog(params string[] dirs) =>
        new(_procs, k => k switch { "PATH" => Path.Combine(_root, "bin"), "HOME" => Path.Combine(_root, "home"), "LANG" => "C", _ => null },
            ownPath: Path.Combine(_root, "bin", "vibeswitcher"), dirs);

    [LinuxFact]
    public void Installed_FromDesktopEntries_FirstIdWins_HiddenStayHidden_SortedWithRunningMarked()
    {
        var user = Dir("user/applications");
        var system = Dir("system/applications");
        var vlc = Program("bin/vlc");
        Program("bin/gimp");
        Program("bin/vibeswitcher");
        Entry(system, "vlc.desktop", "Name=VLC media player\nExec=vlc --started-from-file %U\nIcon=vlc\n");
        Entry(system, "gimp.desktop", "Name=GIMP\nExec=gimp\n");
        Entry(user, "gimp.desktop", "Name=GIMP\nExec=gimp\nNoDisplay=true\n");      // the user hid it
        Entry(system, "maps.desktop", "Name=Maps\nExec=/usr/bin/flatpak run org.gnome.Maps\n");
        Entry(system, "vibeswitcher.desktop", "Name=VibeSwitcher\nExec=vibeswitcher\n"); // itself
        Entry(system, "broken.desktop", "Name=Gone\nExec=not-installed\n");
        Directory.CreateDirectory(Path.Combine(system, "kde"));
        Entry(Path.Combine(system, "kde"), "kate.desktop", "Name=Kate\nExec=" + Program("opt/kate/kate") + "\n");
        _procs.Start(vlc);

        var apps = Catalog(user, system).Installed();
        Assert.Equal(["Kate", "Maps", "VLC media player"], apps.Select(a => a.DisplayName));
        Assert.Equal(vlc, apps[2].Trigger);
        Assert.Equal("vlc", apps[2].Icon);
        Assert.True(apps[2].IsRunning);
        Assert.Equal("flatpak:org.gnome.Maps", apps[1].Trigger);
        Assert.False(apps[1].IsRunning);
    }

    [LinuxFact]
    public void Running_InstalledAppsPlusSandboxedAndUserPrograms_NotSystemServices()
    {
        var apps = Dir("apps");
        var vlc = Program("bin/vlc");
        Entry(apps, "vlc.desktop", "Name=VLC\nExec=vlc\n");
        _procs.Start(vlc);
        _procs.Start("/app/bin/x", "flatpak:org.example.Tool");
        _procs.Start(Path.Combine(_root, "home", "Games", "game.x86_64"));
        _procs.Start("/usr/lib/systemd/systemd");
        _procs.Start("/usr/bin/pipewire");

        var running = Catalog(apps).Running();
        Assert.Equal(["game.x86_64", "Tool", "VLC"], running.Select(a => a.DisplayName));
        Assert.All(running, a => Assert.True(a.IsRunning));
    }

    [LinuxFact]
    public void AMinimalHideFile_HidesTheSystemApp()
    {
        var user = Dir("user2/applications");
        var system = Dir("system2/applications");
        Program("bin/gimp");
        Entry(system, "gimp.desktop", "Name=GIMP\nExec=gimp\n");
        File.WriteAllText(Path.Combine(user, "gimp.desktop"), "[Desktop Entry]\nHidden=true\n");
        Assert.Empty(Catalog(user, system).Installed());
    }

    [LinuxFact]
    public void Running_WindowsGamesByTheirExeName_AndNeverItself()
    {
        var apps = Dir("apps3");
        Entry(apps, "steam.desktop", "Name=Steam\nExec=flatpak run com.valvesoftware.Steam\nX-Flatpak=com.valvesoftware.Steam\n");
        var proton = Path.Combine(_root, "home", ".steam/proton/files/bin");
        _procs.Add(new RunningProcess(proton + "/wine64-preloader", ["wine64-preloader", "Game.exe"]));
        _procs.Add(new RunningProcess(proton + "/wine64-preloader", ["wine64-preloader", "services.exe"]));  // Wine itself
        _procs.Add(new RunningProcess(proton + "/wineserver", ["wineserver"]));                               // Wine itself
        _procs.Add(new RunningProcess(proton + "/wine64-preloader", ["wine64-preloader", "LongGameNameHe"])); // name cut short
        _procs.Add(new RunningProcess("/app/bin/steam", ["steam"], "flatpak:com.valvesoftware.Steam"));
        // A game inside Flatpak Steam's sandbox is still itself.
        _procs.Add(new RunningProcess("/home/sam/.var/app/com.valvesoftware.Steam/proton/wine64-preloader",
            ["wine64-preloader", "Other.exe"], "flatpak:com.valvesoftware.Steam"));
        _procs.Add(new RunningProcess("/tmp/.mount_VibeXYZ/usr/bin/vibeswitcher", ["vibeswitcher"], "/home/sam/VibeSwitcher.AppImage"));
        var catalog = new LinuxAppCatalog(_procs,
            k => k switch { "HOME" => Path.Combine(_root, "home"), "APPIMAGE" => "/home/sam/VibeSwitcher.AppImage", _ => null },
            ownPath: "/tmp/.mount_VibeXYZ/usr/bin/vibeswitcher", [apps]);
        var running = catalog.Running();
        Assert.Equal(["Game", "Other", "Steam"], running.Select(a => a.DisplayName));
        Assert.Equal("Game.exe", running[0].Trigger);
        Assert.Equal("flatpak:com.valvesoftware.Steam", running[2].Trigger);
    }

    [LinuxFact]
    public void TriggerForFile()
    {
        var apps = Dir("apps");
        var app = Program("opt/app/app");
        Entry(apps, "app.desktop", "Name=App\nExec=" + app + "\n");
        var cat = Catalog(apps);
        Assert.Equal(app, cat.TriggerForFile(Path.Combine(apps, "app.desktop")));
        Assert.Equal(app, cat.TriggerForFile(app));
        Assert.Null(cat.TriggerForFile(Path.Combine(_root, "missing")));
        // A symlink resolves to the real file, as the process list reports it.
        var link = Path.Combine(Dir("bin"), "app-link");
        File.CreateSymbolicLink(link, app);
        Assert.Equal(app, cat.TriggerForFile(link));
        // Only something that runs: not a document; an AppImage even before it's marked executable.
        var notes = Path.Combine(_root, "notes.txt");
        File.WriteAllText(notes, "");
        Assert.Null(cat.TriggerForFile(notes));
        var appImage = Path.Combine(_root, "Tool.AppImage");
        File.WriteAllText(appImage, "");
        Assert.Equal(appImage, cat.TriggerForFile(appImage));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }
}

internal sealed class FakeProcessSource : IRunningProcessSource
{
    private readonly List<RunningProcess> _list = [];
    public void Start(string exe, string? appId = null) { lock (_list) _list.Add(new RunningProcess(exe, [Path.GetFileName(exe)], appId)); }
    public void Add(RunningProcess p) { lock (_list) _list.Add(p); }
    public IReadOnlyList<RunningProcess>? Snapshot() { lock (_list) return _list.ToList(); }
}
