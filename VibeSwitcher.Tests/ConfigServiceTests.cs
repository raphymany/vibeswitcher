using System.IO;
using VibeSwitcher.Models;
using VibeSwitcher.Services;

namespace VibeSwitcher.Tests;

public class ConfigServiceTests : IDisposable
{
    private readonly string _dir;

    public ConfigServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"VSTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    private ConfigService MakeSvc(string? dir = null)
        => new(new FakeAppLogger(), new FakeSessionErrorTracker(), dir ?? _dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Load_WhenNoConfigFile_SetsIsFirstRun()
    {
        var svc = MakeSvc();
        svc.Load();
        Assert.True(svc.IsFirstRun);
    }

    [Fact]
    public void Load_WhenNoConfigFile_ReturnsDefaultConfig()
    {
        var svc = MakeSvc();
        svc.Load();
        Assert.NotNull(svc.Current);
        Assert.Empty(svc.Current.Profiles);
    }

    [Fact]
    public void Save_ThenLoad_RoundTrip_Preserves_CloseToTray()
    {
        var svc = MakeSvc();
        svc.Load();
        svc.Current.CloseToTray = false; // flip away from default
        svc.SaveImmediate();

        var svc2 = MakeSvc();
        svc2.Load();
        Assert.False(svc2.Current.CloseToTray);
        Assert.False(svc2.IsFirstRun);
    }

    [Fact]
    public void Save_ThenLoad_RoundTrip_Preserves_Profile()
    {
        var svc = MakeSvc();
        svc.Load();
        svc.Current.Profiles.Add(new DeviceProfile { Name = "Gaming" });
        svc.SaveImmediate();

        var svc2 = MakeSvc();
        svc2.Load();
        Assert.Single(svc2.Current.Profiles);
        Assert.Equal("Gaming", svc2.Current.Profiles[0].Name);
    }

    [Fact]
    public void Load_CorruptPrimary_FallsBackToBackup()
    {
        var svc = MakeSvc();
        svc.Load();
        svc.Current.ShowNotifications = false; // flip away from default
        svc.SaveImmediate(); // first save: creates config.json (no backup yet)
        svc.SaveImmediate(); // second save: backs up config.json → config.json.bak, then overwrites config.json

        // Now corrupt the primary
        File.WriteAllText(Path.Combine(_dir, "config.json"), "{ NOT VALID JSON }}}");

        var svc2 = MakeSvc();
        svc2.Load();
        Assert.False(svc2.Current.ShowNotifications);
    }

    [Fact]
    public void Load_CorruptPrimaryAndBackup_SetsIsFirstRun()
    {
        File.WriteAllText(Path.Combine(_dir, "config.json"),     "{ BAD }");
        File.WriteAllText(Path.Combine(_dir, "config.json.bak"), "{ BAD }");

        var svc = MakeSvc();
        svc.Load();
        Assert.True(svc.IsFirstRun);
    }

    [Fact]
    public void Save_DoesNotLeaveTemporaryFile()
    {
        var svc = MakeSvc();
        svc.Load();
        svc.SaveImmediate();

        Assert.True(File.Exists(Path.Combine(_dir, "config.json")));
        Assert.False(File.Exists(Path.Combine(_dir, "config.json.tmp")));
    }

    [Fact]
    public void Load_WithConcurrentReader_DoesNotThrow()
    {
        var svc = MakeSvc();
        svc.Load();
        svc.SaveImmediate();

        using var fs = new FileStream(
            Path.Combine(_dir, "config.json"),
            FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        var ex = Record.Exception(() =>
        {
            var svc2 = MakeSvc();
            svc2.Load();
        });
        Assert.Null(ex);
    }

    [Fact]
    public void Load_MigratesSentinelNegativeOneToNull()
    {
        File.WriteAllText(
            Path.Combine(_dir, "config.json"),
            """{"WindowLeft":-1,"WindowTop":-1,"Profiles":[]}""");

        var svc = MakeSvc();
        svc.Load();
        Assert.Null(svc.Current.WindowLeft);
        Assert.Null(svc.Current.WindowTop);
    }

    [Fact]
    public void Migrate_WindowLeft_NegativeOne_Nulled_WindowTop_NonSentinel_Preserved()
    {
        // -1 is the v1 sentinel for "not yet saved". Only WindowLeft = -1 should become null;
        // a real WindowTop value like 200.0 must pass through unchanged.
        File.WriteAllText(
            Path.Combine(_dir, "config.json"),
            """{"WindowLeft":-1,"WindowTop":200.0,"Profiles":[]}""");

        var svc = MakeSvc();
        svc.Load();

        Assert.Null(svc.Current.WindowLeft);
        Assert.Equal(200.0, svc.Current.WindowTop);
    }

    [Fact]
    public void IconsDir_IsSubdirectoryOfBaseDir()
    {
        var svc = MakeSvc();
        Assert.StartsWith(_dir, svc.IconsDir, StringComparison.OrdinalIgnoreCase);
    }
}
