using System.Diagnostics;
using VibeSwitcher.Platform;
using VibeSwitcher.Platform.MacOS.Apps;
using VibeSwitcher.Services;
using VibeSwitcher.Tests;

namespace VibeSwitcher.Platform.MacOS.Tests;

// Paths work like a Mac's (macOS or Linux); CI's Linux and Mac builds run it.
public sealed class UnixFactAttribute : FactAttribute
{
    public UnixFactAttribute()
    {
        if (OperatingSystem.IsWindows()) Skip = "Needs Unix paths.";
    }
}

public class MacProcessesTests
{
    [Theory]
    [InlineData("/Applications/Discord.app/Contents/MacOS/Discord", "/Applications/Discord.app")]
    [InlineData("/Applications/Discord.app/Contents/Frameworks/Discord Helper (GPU).app/Contents/MacOS/Discord Helper (GPU)", "/Applications/Discord.app")]
    [InlineData("/Users/sam/Applications/Tool.APP/Contents/MacOS/tool", "/Users/sam/Applications/Tool.APP")]
    [InlineData("/Applications/Zoom.app", "/Applications/Zoom.app")]
    [InlineData("/usr/local/bin/ffmpeg", null)]
    [InlineData("/Applications/Not an app.application/x", null)]
    [InlineData("relative/X.app/Contents/MacOS/x", null)]
    [InlineData(null, null)]
    public void BundleOf(string? exe, string? bundle) => Assert.Equal(bundle, MacProcesses.BundleOf(exe));

    [Fact]
    public void Snapshot_NamesAndBundles_SkipsWhatItCantSee()
    {
        var paths = new Dictionary<int, string?>
        {
            [10] = "/Applications/Slack.app/Contents/MacOS/Slack",
            [11] = "/usr/sbin/cfprefsd",
            [12] = null,
        };
        var names = new Dictionary<int, string?> { [10] = "Slack", [11] = "cfprefsd", [12] = "kernel_task", [13] = null };
        var source = new MacProcesses(() => [0, 10, 11, 12, 13], pid => paths.GetValueOrDefault(pid), pid => names.GetValueOrDefault(pid));

        var list = source.Snapshot()!;
        Assert.Equal(3, list.Count);
        Assert.Equal("/Applications/Slack.app", list[0].AppId);
        Assert.Equal(["Slack"], list[0].Names);
        Assert.Null(list[1].AppId);
        Assert.Equal(["kernel_task"], list[2].Names);
        // A list that couldn't be read is reported as such (there's always at least launchd).
        Assert.Null(new MacProcesses(() => throw new InvalidOperationException(), _ => null, _ => null).Snapshot());
        Assert.Null(new MacProcesses(() => [], _ => null, _ => null).Snapshot());
    }
}

public sealed class MacAppCatalogTests : IDisposable
{
    // The real temp path (on a Mac /var/folders/… is really /private/var/folders/…).
    private readonly string _root = Path.Combine(FilePaths.RealPath(Path.GetTempPath()) ?? Path.GetTempPath(), "vs-macapps-" + Guid.NewGuid().ToString("N"));
    private readonly List<RunningProcess> _running = [];

    private sealed class Source(List<RunningProcess> list) : IRunningProcessSource
    {
        public IReadOnlyList<RunningProcess>? Snapshot() => list.ToList();
    }

    private string App(string relative)
    {
        var bundle = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.Combine(bundle, "Contents", "MacOS"));
        return bundle;
    }

    private MacAppCatalog Catalog(string? own = null) =>
        new(new Source(_running), [Path.Combine(_root, "Applications"), Path.Combine(_root, "System", "Applications"), Path.Combine(_root, "missing")], own);

    [UnixFact]
    public void Installed_AppsAndAppFoldersOneDeep_SortedWithRunningMarked()
    {
        var slack = App("Applications/Slack.app");
        App("Applications/Microsoft Office/Microsoft Word.app");
        App("System/Applications/Calculator.app");
        var self = App("Applications/VibeSwitcher.app");
        Directory.CreateDirectory(Path.Combine(_root, "Applications", "Not an app"));
        _running.Add(new RunningProcess(slack + "/Contents/MacOS/Slack", ["Slack"], slack));

        var apps = Catalog(own: self).Installed();
        Assert.Equal(["Calculator", "Microsoft Word", "Slack"], apps.Select(a => a.DisplayName));
        Assert.Equal(slack, apps[2].Trigger);
        Assert.True(apps[2].IsRunning);
        Assert.False(apps[0].IsRunning);
    }

    [Fact]
    public void Running_AppBundlesOnly_NotSystemParts()
    {
        _running.Add(new RunningProcess("/Applications/Slack.app/Contents/MacOS/Slack", ["Slack"], "/Applications/Slack.app"));
        _running.Add(new RunningProcess("/Applications/Slack.app/Contents/Frameworks/Slack Helper.app/Contents/MacOS/Slack Helper", ["Slack Helper"], "/Applications/Slack.app"));
        _running.Add(new RunningProcess("/System/Applications/Music.app/Contents/MacOS/Music", ["Music"], "/System/Applications/Music.app"));
        _running.Add(new RunningProcess("/System/Library/CoreServices/Dock.app/Contents/MacOS/Dock", ["Dock"], "/System/Library/CoreServices/Dock.app"));
        _running.Add(new RunningProcess("/usr/libexec/trustd", ["trustd"]));

        var running = Catalog().Running();
        Assert.Equal(["Music", "Slack"], running.Select(a => a.DisplayName));
        Assert.All(running, a => Assert.True(a.IsRunning));
    }

    [UnixFact]
    public void TriggerForFile_AnythingInsideAnApp_IsTheApp()
    {
        var app = App("Applications/Zoom.app");
        var cat = Catalog();
        Assert.Equal(app, cat.TriggerForFile(app));
        Assert.Equal(app, cat.TriggerForFile(app + "/Contents/MacOS"));
        var tool = Path.Combine(_root, "tool");
        File.WriteAllText(tool, "");
        Assert.Equal(tool, cat.TriggerForFile(tool));
        Assert.Null(cat.TriggerForFile(Path.Combine(_root, "Gone.app")));
        Assert.Null(cat.TriggerForFile(""));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }
}

// On a real Mac: libproc lists processes, and an app launched from a bundle is detected as that app.
[Trait("Category", "MacOS")]
public sealed class MacRealProcessTests : IDisposable
{
    private readonly string _dir = Path.Combine(FilePaths.RealPath(Path.GetTempPath()) ?? Path.GetTempPath(), "vs-mac-" + Guid.NewGuid().ToString("N")[..8]);
    private Process? _started;

    [MacFact]
    public void TheRealProcessList_HasUs()
    {
        var list = new MacProcesses().Snapshot()!;
        Assert.True(list.Count > 20);
        Assert.Contains(list, p => p.ExePath == Environment.ProcessPath);
        Assert.Contains(list, p => p.Names.Contains("launchd"));
    }

    [MacFact]
    public void AnAppStartedFromItsBundle_IsThatApp_AndTheWatcherSeesIt()
    {
        var bundle = Path.Combine(_dir, "VS Trigger Test.app");
        var exe = Path.Combine(bundle, "Contents", "MacOS", "vstriggertest");
        Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
        File.Copy("/bin/sleep", exe);

        using var watcher = new AppWatcherService(new FakeAppLogger(), new MacProcesses(), TimeSpan.FromMilliseconds(50));
        var fired = new List<string>();
        watcher.ProcessLaunched += t => { lock (fired) fired.Add(t); };
        watcher.UpdateWatchList([bundle]);
        Thread.Sleep(150);
        _started = Process.Start(exe, "30");

        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(5)) { lock (fired) if (fired.Count > 0) break; Thread.Sleep(20); }
        lock (fired) Assert.Equal([bundle], fired);
        // /var/folders/… is /private/var/folders/… for real: the bundle is matched either way.
        var found = new MacProcesses().Snapshot()!.First(p => p.Names.Contains("vstriggertest"));
        Assert.EndsWith("/VS Trigger Test.app", found.AppId);
    }

    [MacFact]
    public void TheRealApplicationsFolder_IsListed()
    {
        var apps = new MacAppCatalog(new MacProcesses()).Installed();
        Assert.Contains(apps, a => a.Trigger == "/System/Applications/Calculator.app" && a.DisplayName == "Calculator");
        // Finder and the Dock are parts of macOS, not apps to pick.
        Assert.DoesNotContain(new MacAppCatalog(new MacProcesses()).Running(), a => a.Trigger.EndsWith("/Finder.app") || a.Trigger.EndsWith("/Dock.app"));
    }

    [MacFact]
    public void HeadsetDetection_StartsAndStops_OnARealMac()
    {
        _ = HidSharp.DeviceList.Local.GetHidDevices().ToList();
        using var service = new HidHeadsetService(new FakeAppLogger());
        service.Start();
    }

    public void Dispose()
    {
        try { _started?.Kill(); _started?.WaitForExit(2000); } catch (Exception) { }
        _started?.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch (Exception) { }
    }
}
