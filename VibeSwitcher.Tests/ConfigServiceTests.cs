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

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Load_WhenNoConfigFile_SetsIsFirstRun()
    {
        var svc = new ConfigService(_dir);
        svc.Load();
        Assert.True(svc.IsFirstRun);
    }

    [Fact]
    public void Load_WhenNoConfigFile_ReturnsDefaultConfig()
    {
        var svc = new ConfigService(_dir);
        svc.Load();
        Assert.NotNull(svc.Current);
        Assert.Empty(svc.Current.Profiles);
    }

    [Fact]
    public void Save_ThenLoad_RoundTrip_Preserves_CloseToTray()
    {
        var svc = new ConfigService(_dir);
        svc.Load();
        svc.Current.CloseToTray = false; // flip away from default
        svc.SaveImmediate();

        var svc2 = new ConfigService(_dir);
        svc2.Load();
        Assert.False(svc2.Current.CloseToTray);
        Assert.False(svc2.IsFirstRun);
    }

    [Fact]
    public void Save_ThenLoad_RoundTrip_Preserves_Profile()
    {
        var svc = new ConfigService(_dir);
        svc.Load();
        svc.Current.Profiles.Add(new DeviceProfile { Name = "Gaming" });
        svc.SaveImmediate();

        var svc2 = new ConfigService(_dir);
        svc2.Load();
        Assert.Single(svc2.Current.Profiles);
        Assert.Equal("Gaming", svc2.Current.Profiles[0].Name);
    }

    [Fact]
    public void Load_CorruptPrimary_FallsBackToBackup()
    {
        var svc = new ConfigService(_dir);
        svc.Load();
        svc.Current.ShowNotifications = false; // flip away from default
        svc.SaveImmediate(); // first save: creates config.json (no backup yet)
        svc.SaveImmediate(); // second save: backs up config.json → config.json.bak, then overwrites config.json

        // Now corrupt the primary
        File.WriteAllText(Path.Combine(_dir, "config.json"), "{ NOT VALID JSON }}}");

        var svc2 = new ConfigService(_dir);
        svc2.Load();
        Assert.False(svc2.Current.ShowNotifications);
    }

    [Fact]
    public void Load_CorruptPrimaryAndBackup_SetsIsFirstRun()
    {
        File.WriteAllText(Path.Combine(_dir, "config.json"),     "{ BAD }");
        File.WriteAllText(Path.Combine(_dir, "config.json.bak"), "{ BAD }");

        var svc = new ConfigService(_dir);
        svc.Load();
        Assert.True(svc.IsFirstRun);
    }

    [Fact]
    public void Save_DoesNotLeaveTemporaryFile()
    {
        var svc = new ConfigService(_dir);
        svc.Load();
        svc.SaveImmediate();

        Assert.True(File.Exists(Path.Combine(_dir, "config.json")));
        Assert.False(File.Exists(Path.Combine(_dir, "config.json.tmp")));
    }

    [Fact]
    public void Load_WithConcurrentReader_DoesNotThrow()
    {
        var svc = new ConfigService(_dir);
        svc.Load();
        svc.SaveImmediate();

        using var fs = new FileStream(
            Path.Combine(_dir, "config.json"),
            FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        var ex = Record.Exception(() =>
        {
            var svc2 = new ConfigService(_dir);
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

        var svc = new ConfigService(_dir);
        svc.Load();
        Assert.Null(svc.Current.WindowLeft);
        Assert.Null(svc.Current.WindowTop);
    }

    [Fact]
    public void IconsDir_IsSubdirectoryOfBaseDir()
    {
        var svc = new ConfigService(_dir);
        Assert.StartsWith(_dir, svc.IconsDir, StringComparison.OrdinalIgnoreCase);
    }
}
