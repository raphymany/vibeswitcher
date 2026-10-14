using VibeSwitcher.Platform.Linux.Hid;
using VibeSwitcher.Platform.Linux.Processes;
using VibeSwitcher.Services;
using VibeSwitcher.Tests;

namespace VibeSwitcher.Platform.Linux.Tests;

public sealed class HeadsetAccessTests : IDisposable
{
    private readonly string _sys = Path.Combine(Path.GetTempPath(), "vs-sys-" + Guid.NewGuid().ToString("N"));
    private readonly HashSet<string> _openable = [];
    private readonly Dictionary<string, string> _files = [];
    private readonly RecordingRunner _runner = new();

    private sealed class RecordingRunner : IProcessRunner
    {
        public readonly List<(string File, IReadOnlyList<string> Args)> Calls = [];
        public int ExitCode;
        public bool Missing;
        public ProcessResult Run(string file, IReadOnlyList<string> args, TimeSpan timeout)
        {
            if (Missing) throw new ToolNotFoundException(file, new FileNotFoundException());
            Calls.Add((file, args));
            return new ProcessResult(ExitCode, "", "");
        }
        public IRunningProcess Start(string file, IReadOnlyList<string> args) => throw new NotSupportedException();
    }

    private LinuxHidAccess Access() =>
        new(_sys, p => _openable.Contains(p), p => _files.TryGetValue(p, out var t) ? t : File.Exists(p) ? File.ReadAllText(p) : null, _runner);

    private void Hidraw(int n, string hidId)
    {
        var dir = Path.Combine(_sys, "class", "hidraw", $"hidraw{n}", "device");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "uevent"), $"DRIVER=hid-generic\nHID_ID={hidId}\nHID_NAME=Some Device\n");
    }

    private static readonly HidHeadsetDescriptor Headset = KnownHidHeadsets.All[0];
    private static string HeadsetId => $"0003:0000{Headset.VendorId:X4}:0000{Headset.ProductId:X4}";

    [Fact]
    public void TheRule_CoversExactlyTheSupportedHeadsets()
    {
        var rule = LinuxHidAccess.RuleText();
        var lines = rule.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(l => !l.StartsWith('#')).ToList();
        var distinct = KnownHidHeadsets.All.Select(h => (h.VendorId, h.ProductId)).Distinct().ToList();
        Assert.Equal(distinct.Count, lines.Count);
        foreach (var (vid, pid) in distinct)
            Assert.Contains($"SUBSYSTEM==\"hidraw\", ATTRS{{idVendor}}==\"{vid:x4}\", ATTRS{{idProduct}}==\"{pid:x4}\", TAG+=\"uaccess\"", lines);
        // Never a whole vendor (that would include its keyboards and mice).
        Assert.All(lines, l => Assert.Contains("idProduct", l));
        Assert.DoesNotContain('\r', rule);
    }

    // The .deb installs packaging/linux/<rule file>. Regenerate it with VS_UPDATE_RULE=1 after
    // changing the supported-headset list.
    [Fact]
    public void ThePackagedRule_IsUpToDate()
    {
        var file = Path.Combine(RepoXplat(), "packaging", "linux", LinuxHidAccess.RuleFileName);
        if (Environment.GetEnvironmentVariable("VS_UPDATE_RULE") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, LinuxHidAccess.RuleText());
        }
        Assert.True(File.Exists(file), $"{file} is missing: run the tests once with VS_UPDATE_RULE=1.");
        Assert.Equal(LinuxHidAccess.RuleText(), File.ReadAllText(file).ReplaceLineEndings("\n"));
    }

    private static string RepoXplat()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "VibeSwitcher.XPlat.sln"))) dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("xplat folder not found");
    }

    [Theory]
    [InlineData("HID_ID=0003:0000046D:00000ABA", 3, 0x046D, 0x0ABA)]
    [InlineData("DRIVER=x\nHID_ID=0005:00001038:000012AD\n", 5, 0x1038, 0x12AD)]
    public void ReadsTheDeviceIds(string uevent, int bus, int vid, int pid) =>
        Assert.Equal(((ushort)bus, (ushort)vid, (ushort)pid), LinuxHidAccess.HidId(uevent));

    [Theory]
    [InlineData(null)]
    [InlineData("HID_NAME=x")]
    [InlineData("HID_ID=bogus")]
    [InlineData("HID_ID=0003:FFFFFFFF:00000001")]
    public void BadIds_Null(string? uevent) => Assert.Null(LinuxHidAccess.HidId(uevent));

    [Fact]
    public void Check_NoHeadset_Accessible_NeedsRule_NeedsReconnect()
    {
        Assert.Equal(HeadsetAccess.NoHeadset, Access().Check()); // no hidraw at all
        Hidraw(0, "0003:0000046D:0000C52B");                     // a Logitech receiver (mouse/keyboard): not ours
        Assert.Equal(HeadsetAccess.NoHeadset, Access().Check());

        Hidraw(3, HeadsetId);
        Assert.Equal(HeadsetAccess.NeedsRule, Access().Check());
        _files["/usr/lib/udev/rules.d/" + LinuxHidAccess.RuleFileName] = LinuxHidAccess.RuleText().ReplaceLineEndings("\r\n");
        Assert.Equal(HeadsetAccess.NeedsReconnect, Access().Check());
        _openable.Add("/dev/hidraw3");
        Assert.Equal(HeadsetAccess.Accessible, Access().Check());
    }

    [Fact]
    public void ARuleTheUserSwitchedOff_IsLeftAlone()
    {
        Hidraw(3, HeadsetId);
        // udev's way: an empty file (or a link to /dev/null, which reads empty) in /etc.
        _files["/etc/udev/rules.d/" + LinuxHidAccess.RuleFileName] = "";
        _files["/usr/lib/udev/rules.d/" + LinuxHidAccess.RuleFileName] = LinuxHidAccess.RuleText();
        var access = Access();
        Assert.True(access.IsRuleTurnedOff());
        Assert.Equal(HeadsetAccess.TurnedOff, access.Check());
        Assert.Equal(RuleInstallResult.TurnedOffByUser, access.Install());
        Assert.Equal(RuleInstallResult.Installed, access.Remove()); // nothing of ours to remove
        Assert.Empty(_runner.Calls);
    }

    [Fact]
    public void Remove_OnlyRemovesOurOwnFile()
    {
        _files["/etc/udev/rules.d/" + LinuxHidAccess.RuleFileName] = "# VibeSwitcher: an older version\n";
        Assert.Equal(RuleInstallResult.Installed, Access().Remove());
        var args = Assert.Single(_runner.Calls).Args;
        Assert.Equal("/bin/sh", args[0]);
        Assert.Contains("udevadm control --reload-rules", args[2]); // (a plugged-in dongle loses access now)
        Assert.Contains("udevadm trigger --subsystem-match=hidraw --action=change", args[2]);
        Assert.Equal("/etc/udev/rules.d/" + LinuxHidAccess.RuleFileName, args[^1]); // (an argument, never in the script)
    }

    [Fact]
    public void AHeadsetOnBluetooth_IsntOneTheRuleCanHelp()
    {
        Hidraw(5, $"0005:0000{Headset.VendorId:X4}:0000{Headset.ProductId:X4}");
        Assert.Equal(HeadsetAccess.NoHeadset, Access().Check());
    }

    [Fact]
    public void AnOutdatedRule_DoesntCountAsInstalled_EvenWithACurrentOneBehindIt()
    {
        _files["/etc/udev/rules.d/" + LinuxHidAccess.RuleFileName] = "# old\n";
        Assert.False(Access().IsRuleInstalled());
        // udev uses the /etc file: the .deb's current copy in /usr/lib is hidden by it.
        _files["/usr/lib/udev/rules.d/" + LinuxHidAccess.RuleFileName] = LinuxHidAccess.RuleText();
        Assert.False(Access().IsRuleInstalled());
        _files.Remove("/etc/udev/rules.d/" + LinuxHidAccess.RuleFileName);
        Assert.True(Access().IsRuleInstalled());
    }

    [Fact]
    public void Install_OneAdminPrompt_RuleWrittenAndApplied()
    {
        Assert.Equal(RuleInstallResult.Installed, Access().Install());
        var (file, args) = Assert.Single(_runner.Calls);
        Assert.Equal("pkexec", file);
        Assert.Equal("/bin/sh", args[0]);
        Assert.Contains("udevadm trigger --subsystem-match=hidraw", args[2]);
        Assert.Contains("udevadm settle", args[2]); // applied before it returns
        Assert.StartsWith("command -v udevadm", args[2]);
        // Written to a new file that replaces the old one, never through it (it could be a link).
        Assert.Contains("mktemp", args[2]);
        Assert.Contains("mv -f \"$tmp\" \"$2\"", args[2]);
        Assert.DoesNotContain("\r", args[2]);
        // The rule and the destination are arguments, never part of the script text.
        Assert.Equal(LinuxHidAccess.RuleText(), args[4]);
        Assert.Equal("/etc/udev/rules.d/" + LinuxHidAccess.RuleFileName, args[5]);
        Assert.DoesNotContain("uaccess", args[2]);
    }

    [Theory]
    [InlineData(126, RuleInstallResult.Cancelled)]
    [InlineData(127, RuleInstallResult.Cancelled)]
    [InlineData(1, RuleInstallResult.Failed)]
    public void Install_Outcomes(int exit, RuleInstallResult result)
    {
        _runner.ExitCode = exit;
        Assert.Equal(result, Access().Install());
    }

    [Fact]
    public void NoPkexec_SaysSo()
    {
        _runner.Missing = true;
        Assert.Equal(RuleInstallResult.NoPrivilegeTool, Access().Install());
    }

    // HidSharp's Linux side (hidraw + libudev) loads and lists devices without throwing, and the
    // headset service starts and stops cleanly with no headset attached.
    [LinuxFact]
    public void HeadsetDetection_StartsAndStops_OnRealLinux()
    {
        _ = HidSharp.DeviceList.Local.GetHidDevices().ToList();
        using var service = new HidHeadsetService(new FakeAppLogger());
        service.Start();
    }

    public void Dispose()
    {
        try { Directory.Delete(_sys, recursive: true); } catch (IOException) { }
    }
}
