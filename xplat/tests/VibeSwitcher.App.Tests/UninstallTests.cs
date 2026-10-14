using Avalonia.Headless.XUnit;
using VibeSwitcher.App.Dialogs;
using VibeSwitcher.App.Hosting;
using VibeSwitcher.Helpers;
using VibeSwitcher.Platform;
using VibeSwitcher.Platform.Linux.Hid;
using VibeSwitcher.Platform.Linux.Processes;
using VibeSwitcher.Tests;
using FakePlatform = VibeSwitcher.App.Tests.AppHostTests.FakePlatform;

namespace VibeSwitcher.App.Tests;

// Uninstalling from Settings › Install & Updates, on each kind of install.
[Collection("Shared UI settings")] // (the host registers the app's log)
public class UninstallTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("vs-uninstall-").FullName;

    public void Dispose()
    {
        AppLog.Register(null);
        AppErrors.Register(null);
        try { Directory.Delete(_dir, recursive: true); } catch (Exception) { }
    }

    internal sealed class FakeSystem : IUninstallSystem
    {
        public List<string> Done { get; } = [];
        public bool TrashWorks { get; set; } = true;
        public Exception? DeleteFails { get; set; }
        public List<string> MenuEntries { get; } = [];
        public RuleInstallResult Rule { get; set; } = RuleInstallResult.Installed;
        public string? PackageRefusal { get; set; }

        public bool IsAppImageFile { get; set; } = true;
        public Exception? MenuFails { get; set; }
        public ManualResetEventSlim? Gate { get; set; }
        public Exception? PackageFails { get; set; }

        public bool Prepared { get; private set; }
        public void PrepareToRemoveApp() => Prepared = true;
        public bool MoveToTrash(string path, out string? error)
        {
            Done.Add($"trash {path}");
            error = TrashWorks ? null : "no permission";
            return TrashWorks;
        }
        public bool IsAppImage(string path) => IsAppImageFile;
        public void DeleteFile(string path) { if (DeleteFails != null) throw DeleteFails; Done.Add($"delete {path}"); }
        public IEnumerable<string> MenuEntriesFor(string appImage) => MenuFails != null ? throw MenuFails : MenuEntries;
        public RuleInstallResult RemoveHeadsetRule() { Done.Add("headset rule"); return Rule; }
        public string? RemovePackage(string location)
        {
            Gate?.Wait(TimeSpan.FromSeconds(10));
            if (PackageFails != null) throw PackageFails;
            lock (Done) Done.Add("package");
            return PackageRefusal;
        }
        public void RemoveGnomeShortcuts() => Done.Add("gnome shortcuts");
    }

    private static FakeStartupService StartupOn()
    {
        var startup = new FakeStartupService();
        startup.Enable();
        return startup;
    }

    private static readonly InstallInfo Mac = new(InstallKind.MacApp, "/Applications/VibeSwitcher.app", "2.0.1");
    private static readonly InstallInfo AppImage = new(InstallKind.AppImage, "/home/alex/Apps/VibeSwitcher.AppImage", "2.0.1");
    private static readonly InstallInfo Package = new(InstallKind.Package, "/opt/vibeswitcher", "2.0.1");

    [Fact]
    public void AMacApp_GoesToTheTrash_WithItsLoginItem()
    {
        var system = new FakeSystem();
        var startup = StartupOn();
        Assert.Null(Uninstaller.Run(Mac, startup, system, new FakeAppLogger()));
        Assert.Equal("trash /Applications/VibeSwitcher.app", system.Done[0]);
        Assert.False(startup.StartupEnabled);
    }

    [Fact]
    public void WhenTheAppCantGo_NothingElseIsTouched()
    {
        var startup = StartupOn();
        var refused = new FakeSystem { TrashWorks = false };
        Assert.Contains("Trash", Uninstaller.Run(Mac, startup, refused, new FakeAppLogger()));
        // A translocated copy isn't even tried (it's a temporary copy macOS made).
        var translocated = new FakeSystem();
        Assert.NotNull(Uninstaller.Run(Mac with { Translocated = true }, startup, translocated, new FakeAppLogger()));
        Assert.Empty(translocated.Done);

        var undeletable = new FakeSystem { DeleteFails = new UnauthorizedAccessException("read-only") };
        Assert.Contains("read-only", Uninstaller.Run(AppImage, startup, undeletable, new FakeAppLogger()));
        Assert.DoesNotContain("headset rule", undeletable.Done);

        var cancelled = new FakeSystem { PackageRefusal = "Removing VibeSwitcher needs an administrator's password, which wasn't given. Nothing was removed." };
        Assert.Contains("password", Uninstaller.Run(Package, startup, cancelled, new FakeAppLogger()));
        Assert.Equal(new[] { "package" }, cancelled.Done);

        Assert.NotNull(Uninstaller.Run(new InstallInfo(InstallKind.Development, "/src", "2.0.1"), startup, new FakeSystem(), new FakeAppLogger()));
        Assert.True(startup.StartupEnabled); // (never switched off by a refused uninstall)
    }

    [Fact]
    public void AnAppImage_GoesWithItsMenuEntriesAndHeadsetRule()
    {
        var system = new FakeSystem { Rule = RuleInstallResult.Cancelled };
        system.MenuEntries.Add("/home/alex/.local/share/applications/appimagekit-vibeswitcher.desktop");
        var startup = StartupOn();
        var log = new FakeAppLogger();
        Assert.Null(Uninstaller.Run(AppImage, startup, system, log)); // (a kept rule doesn't stop it)
        Assert.Equal(new[]
        {
            "delete /home/alex/Apps/VibeSwitcher.AppImage",
            "delete /home/alex/.local/share/applications/appimagekit-vibeswitcher.desktop",
            "headset rule",
        }, system.Done.Take(3));
        Assert.False(startup.StartupEnabled);
        if (!OperatingSystem.IsMacOS()) Assert.Contains("gnome shortcuts", system.Done);
    }

    [Fact]
    public void APackage_IsRemovedByThePackageManager()
    {
        var system = new FakeSystem();
        Assert.Null(Uninstaller.Run(Package, new FakeStartupService(), system, new FakeAppLogger()));
        Assert.Equal("package", system.Done[0]);
    }

    [Fact]
    public void TheSettingsFolder_IsDeleted_WhenAsked()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_dir, "VibeSwitcher", "Icons")).Parent!.FullName;
        File.WriteAllText(Path.Combine(folder, "config.json"), "{}");
        Uninstaller.DeleteData(folder, null);
        Assert.False(Directory.Exists(folder));
        Uninstaller.DeleteData(folder, null); // (already gone: fine)
    }

    [Fact]
    public void OnlyWhatTheAppPutThere_IsDeleted()
    {
        // (XDG_CONFIG_HOME=$HOME makes the settings folder ~/VibeSwitcher — maybe the user's own,
        // which the app has been putting its files in.)
        var theirs = Directory.CreateDirectory(Path.Combine(_dir, "VibeSwitcher", "src")).Parent!.FullName;
        Directory.CreateDirectory(Path.Combine(theirs, "Icons", "Library"));
        foreach (var file in new[] { "config.json", "config.json.bak", "error.log", "instance-pc.lock", "notes.txt" })
            File.WriteAllText(Path.Combine(theirs, file), "x");
        var warnings = new List<string>();
        Uninstaller.DeleteData(theirs, warnings.Add);
        Assert.Equal(new[] { "notes.txt", "src" }, Directory.EnumerateFileSystemEntries(theirs).Select(Path.GetFileName).Order());
        Assert.Contains("notes.txt", Assert.Single(warnings));

        // …and nothing in a folder that links to somewhere else.
        File.WriteAllText(Path.Combine(theirs, "config.json"), "{}");
        var link = Path.Combine(_dir, "link");
        try { Directory.CreateSymbolicLink(link, theirs); }
        catch (Exception) { return; } // (Windows without developer mode: no links to test with)
        Uninstaller.DeleteData(link, null);
        Assert.True(Directory.Exists(link));
        Assert.True(File.Exists(Path.Combine(theirs, "config.json")));
    }

    [Fact]
    public void WhatsLoaded_BeforeTheAppGoes_AndLaterSteps_CantStopIt()
    {
        var system = new FakeSystem { MenuFails = new IOException("the folder went away") };
        var startup = StartupOn();
        Assert.Null(Uninstaller.Run(AppImage, startup, system, new FakeAppLogger()));
        Assert.True(system.Prepared);
        Assert.Contains("headset rule", system.Done); // (the steps after it still ran)
        Assert.False(startup.StartupEnabled);

        // A file that isn't an AppImage is never deleted.
        var notOne = new FakeSystem { IsAppImageFile = false };
        Assert.Contains("doesn't look like", Uninstaller.Run(AppImage, startup, notOne, new FakeAppLogger()));
        Assert.Empty(notOne.Done);
        Assert.False(notOne.Prepared);
    }

    [Fact]
    public void TheMacMessage_SaysWhereTheAppIs()
    {
        var refused = new FakeSystem { TrashWorks = false };
        Assert.Contains("your Applications folder", Uninstaller.Run(Mac, new FakeStartupService(), refused, new FakeAppLogger()));
        var elsewhere = Mac with { Location = "/Users/alex/Downloads/VibeSwitcher.app" };
        Assert.Contains("from /Users/alex/Downloads to the Trash", Uninstaller.Run(elsewhere, new FakeStartupService(), refused, new FakeAppLogger()));
    }

    [Fact]
    public void TheDialog_NamesTheDataFolderInUse()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Contains("(~/xdg/VibeSwitcher)", UninstallText.LinuxPackage.WithDataFolder(home + "/xdg/VibeSwitcher").DataNote);
        Assert.Contains("(/srv/cfg/VibeSwitcher)", UninstallText.LinuxPackage.WithDataFolder("/srv/cfg/VibeSwitcher").DataNote);
    }

    // ── The system's side ──

    private sealed class Runner : IProcessRunner
    {
        public readonly List<(string File, IReadOnlyList<string> Args)> Calls = [];
        public Func<string, IReadOnlyList<string>, ProcessResult> Answer = (_, _) => new ProcessResult(0, "", "");
        public ProcessResult Run(string file, IReadOnlyList<string> args, TimeSpan timeout)
        {
            Calls.Add((file, args));
            return Answer(file, args);
        }
        public IRunningProcess Start(string file, IReadOnlyList<string> args) => throw new NotSupportedException();
    }

    private SystemUninstall Real(Runner runner) => new(new FakeAppLogger(), runner, _dir, () => null);

    [Fact]
    public void APackage_IsOnlyRemoved_WhenThePackageManagerInstalledIt()
    {
        var runner = new Runner { Answer = (file, _) => file.EndsWith("dpkg-query") ? new(1, "", "no path found matching pattern /opt/vibeswitcher") : new(0, "", "") };
        Assert.Contains("wasn't installed by the system's package manager", Real(runner).RemovePackage("/opt/vibeswitcher"));
        Assert.Single(runner.Calls); // (no password prompt)

        // Another package's copy in the same place (or a name that only begins the same).
        runner = new Runner { Answer = (file, _) => file.EndsWith("dpkg-query") ? new(0, "vibeswitcher-git: /opt/vibeswitcher\n", "") : new(0, "", "") };
        Assert.NotNull(Real(runner).RemovePackage("/opt/vibeswitcher"));

        // Ours (dpkg may name it with its architecture): removed by dpkg, by its full path.
        foreach (var owner in new[] { "vibeswitcher: /opt/vibeswitcher", "other, vibeswitcher:amd64: /opt/vibeswitcher" })
        {
            runner = new Runner { Answer = (file, _) => file.EndsWith("dpkg-query") ? new(0, owner + "\n", "") : new(0, "", "") };
            Assert.Null(Real(runner).RemovePackage("/opt/vibeswitcher"));
            Assert.Equal("/usr/bin/pkexec", runner.Calls[1].File);
            Assert.Equal(["/usr/bin/dpkg", "--remove", "vibeswitcher"], runner.Calls[1].Args);
        }
    }

    [Theory]
    [InlineData(126, "", "password")]
    [InlineData(127, "Error executing command as another user: Not authorized", "password")]
    [InlineData(127, "Error executing command as another user: No authentication agent found.", "no way to ask")]
    [InlineData(2, "dpkg: error: requested operation requires superuser privilege", "superuser privilege")]
    public void WhyThePackageWasntRemoved_IsSaid(int exitCode, string stderr, string expected)
    {
        var runner = new Runner { Answer = (file, _) => file.EndsWith("dpkg-query") ? new(0, "vibeswitcher: /opt/vibeswitcher", "") : new(exitCode, "", stderr) };
        Assert.Contains(expected, Real(runner).RemovePackage("/opt/vibeswitcher"));
    }

    [Fact]
    public void OnlyMenuEntriesThatStartThisFile_AreFound()
    {
        const string app = "/home/alex/Apps/VibeSwitcher.AppImage";
        Assert.True(SystemUninstall.Starts("[Desktop Entry]\nExec=/home/alex/Apps/VibeSwitcher.AppImage %U\n", app));
        Assert.True(SystemUninstall.Starts("[Desktop Entry]\nTryExec=/home/alex/Apps/VibeSwitcher.AppImage", app));
        Assert.True(SystemUninstall.Starts("Exec=\"/home/alex/Apps/VibeSwitcher.AppImage\" --open\r\n", app));
        Assert.True(SystemUninstall.Starts("Exec=\"/home/alex/My Apps/Vibe.AppImage\"", "/home/alex/My Apps/Vibe.AppImage"));
        Assert.False(SystemUninstall.Starts("Exec=/home/alex/Apps/VibeSwitcher.AppImage.bak %U", app));
        Assert.False(SystemUninstall.Starts("Exec=/home/alex/Apps/VibeSwitcher.AppImage-2.0.0", app));
        Assert.False(SystemUninstall.Starts("Comment=Replaces /home/alex/Apps/VibeSwitcher.AppImage\nExec=/usr/bin/other", app));

        // Found in the user's applications folder.
        var dir = Directory.CreateDirectory(Path.Combine(_dir, "applications")).FullName;
        File.WriteAllText(Path.Combine(dir, "ours.desktop"), $"[Desktop Entry]\nExec={app}\n");
        File.WriteAllText(Path.Combine(dir, "backup.desktop"), $"[Desktop Entry]\nExec={app}.bak\n");
        Assert.Equal(new[] { Path.Combine(dir, "ours.desktop") }, Real(new Runner()).MenuEntriesFor(app));
    }

    [Fact]
    public void OnlyARealAppImage_CountsAsOne()
    {
        var file = Path.Combine(_dir, "VibeSwitcher.AppImage");
        File.WriteAllBytes(file, [0x7F, (byte)'E', (byte)'L', (byte)'F', 2, 1, 1, 0, (byte)'A', (byte)'I', 2, 0, 0]);
        Assert.True(Real(new Runner()).IsAppImage(file));
        File.WriteAllBytes(file, [0x7F, (byte)'E', (byte)'L', (byte)'F', 2, 1, 1, 0, 0, 0, 0, 0]); // (any program)
        Assert.False(Real(new Runner()).IsAppImage(file));
        Assert.False(Real(new Runner()).IsAppImage(_dir));
        Assert.False(Real(new Runner()).IsAppImage(Path.Combine(_dir, "missing")));
    }

    [AvaloniaFact]
    public async Task FromSettings_TheAppQuits_AndLeavesItsDataToBeDeleted_OrSaysWhyNot()
    {
        var config = new Services.ConfigService(new FakeAppLogger(), new FakeSessionErrorTracker(), _dir);
        config.Load();
        config.Current.StartMinimized = true;
        config.SaveImmediate();

        var refusing = new FakeSystem { PackageRefusal = "Nothing was removed." };
        var host = new AppHost(new FakePlatform(), new AppPaths(_dir), AppCommand.Open) { UninstallSystem = refusing };
        await host.StartAsync(showSplash: false);
        await host.UninstallAsync(Package, deleteData: true);
        Assert.Null(host.DataToDelete); // refused: still running, data kept
        Assert.Equal("ok", await host.HandleRemoteAsync(AppCommand.Open)); // (still answering)
        host.Windows.Window?.Close();
        host.Dispose();

        var system = new FakeSystem();
        host = new AppHost(new FakePlatform(), new AppPaths(_dir), AppCommand.Open) { UninstallSystem = system };
        await host.StartAsync(showSplash: false);
        await host.UninstallAsync(Package, deleteData: true);
        Assert.Equal(_dir, host.DataToDelete);
        // It quit.
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.HandleRemoteAsync(AppCommand.Open));
    }

    [AvaloniaFact]
    public async Task WhileItRuns_ASecondUninstallIsIgnored_AndQuitWaitsForIt()
    {
        using var gate = new ManualResetEventSlim();
        var system = new FakeSystem { Gate = gate, PackageRefusal = "Nothing was removed." };
        var platform = new FakePlatform();
        var host = new AppHost(platform, new AppPaths(_dir), AppCommand.Open) { UninstallSystem = system };
        await host.StartAsync(showSplash: false);
        var first = host.UninstallAsync(Package, deleteData: true);
        Assert.True(host.Uninstalling);
        await host.UninstallAsync(Package, deleteData: true); // (ignored: returns at once)

        // Quit from the menu during the password prompt: the app stays until it's answered…
        host.Tray.BuildEntries().Single(e => e.Label == "Quit").Run!();
        Assert.Equal("ok", await host.HandleRemoteAsync(AppCommand.Open));
        gate.Set();
        await first;
        Assert.Equal(new[] { "package" }, system.Done);
        // …then quits as asked, saying it's still installed (refused, so its data is kept).
        Assert.Contains(("VibeSwitcher Wasn't Uninstalled", "Nothing was removed."), platform.Notifier.Shown);
        Assert.Null(host.DataToDelete);
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.HandleRemoteAsync(AppCommand.Open));
    }

    [AvaloniaFact]
    public async Task WhenUninstallingFailsUnexpectedly_TheAppKeepsRunning()
    {
        var system = new FakeSystem { PackageFails = new InvalidOperationException("boom") };
        var host = new AppHost(new FakePlatform(), new AppPaths(_dir), AppCommand.Open) { UninstallSystem = system };
        await host.StartAsync(showSplash: false);
        await host.UninstallAsync(Package, deleteData: true);
        Assert.False(host.Uninstalling);
        Assert.Null(host.DataToDelete);
        Assert.Equal("ok", await host.HandleRemoteAsync(AppCommand.Open));
        // (…and it can be tried again.)
        system.PackageFails = null;
        await host.UninstallAsync(Package, deleteData: false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.HandleRemoteAsync(AppCommand.Open));
    }

    [Fact]
    public void LeftoverGnomeShortcuts_AreOnlyLookedFor_WhereGnomeKeepsThem()
    {
        var runner = new Runner { Answer = (_, _) => new ProcessResult(1, "", "No such schema") };
        Real(runner).RemoveGnomeShortcuts();
        var call = Assert.Single(runner.Calls);
        Assert.Equal(("gsettings", "list-keys"), (call.File, call.Args[0]));
    }
}
