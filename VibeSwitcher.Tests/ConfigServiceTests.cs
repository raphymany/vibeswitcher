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
    public void Save_ThenLoad_RoundTrip_Preserves_CompactModeSettings()
    {
        var svc = MakeSvc();
        svc.Load();
        svc.Current.CompactMode = true;
        svc.Current.CompactHotkey = new HotkeyDefinition { VirtualKeyCode = 77, UseCtrl = true, UseShift = true };
        svc.Current.CompactHotkeyEnabled = true;
        svc.Current.CompactAlwaysOnTop = true;
        svc.Current.CompactTranslucent = true;
        svc.Current.CompactWindowLeft = 120.5;
        svc.Current.CompactWindowTop = 240.5;
        var keepId = Guid.NewGuid();
        svc.Current.CompactLayout = "Grid";
        svc.Current.CompactProfileIds = new List<Guid> { keepId };
        svc.Current.CompactIntroShown = true;
        svc.SaveImmediate();

        var svc2 = MakeSvc();
        svc2.Load();
        Assert.True(svc2.Current.CompactMode);
        Assert.NotNull(svc2.Current.CompactHotkey);
        Assert.Equal(77, svc2.Current.CompactHotkey!.VirtualKeyCode);
        Assert.True(svc2.Current.CompactHotkey.UseCtrl);
        Assert.True(svc2.Current.CompactHotkey.UseShift);
        Assert.True(svc2.Current.CompactAlwaysOnTop);
        Assert.True(svc2.Current.CompactTranslucent);
        Assert.Equal(120.5, svc2.Current.CompactWindowLeft);
        Assert.Equal(240.5, svc2.Current.CompactWindowTop);
        Assert.Equal("Grid", svc2.Current.CompactLayout);
        Assert.Equal(new List<Guid> { keepId }, svc2.Current.CompactProfileIds);
        Assert.True(svc2.Current.CompactIntroShown);
    }

    [Fact]
    public void Load_OldConfigWithoutCompactFields_UsesDefaults()
    {
        // A config written before the mini-mode feature must load with safe defaults.
        File.WriteAllText(Path.Combine(_dir, "config.json"), """{ "ConfigVersion": 1, "Profiles": [], "CloseToTray": false }""");

        var svc = MakeSvc();
        svc.Load();
        Assert.False(svc.Current.CompactMode);
        Assert.Null(svc.Current.CompactHotkey);
        Assert.True(svc.Current.CompactHotkeyEnabled);
        Assert.False(svc.Current.CompactAlwaysOnTop);
        Assert.False(svc.Current.CompactTranslucent);
        Assert.Null(svc.Current.CompactWindowLeft);
        Assert.Null(svc.Current.CompactWindowTop);
        Assert.Equal("Rows", svc.Current.CompactLayout);
        Assert.Empty(svc.Current.CompactProfileIds);
        Assert.False(svc.Current.CompactIntroShown);
    }

    [Fact]
    public void TryImport_RejectsNonVibeSwitcherJson()
    {
        var svc = MakeSvc();
        svc.Load();
        svc.Current.Profiles.Add(new DeviceProfile { Name = "Keep" });
        svc.SaveImmediate();

        var foreign = Path.Combine(_dir, "foreign.json");
        File.WriteAllText(foreign, """{ "hello": "world", "count": 3 }""");

        bool ok = svc.TryImport(foreign, out var error);
        Assert.False(ok);
        Assert.NotNull(error);
        // The existing config must be untouched.
        Assert.Single(svc.Current.Profiles);
        Assert.Equal("Keep", svc.Current.Profiles[0].Name);
    }

    [Fact]
    public void TryImport_AcceptsValidExportedConfig()
    {
        var src = MakeSvc();
        src.Load();
        src.Current.Profiles.Add(new DeviceProfile { Name = "Imported" });
        var exportPath = Path.Combine(_dir, "export.json");
        src.ExportTo(exportPath);

        var dest = MakeSvc(Path.Combine(_dir, "dest"));
        dest.Load();
        bool ok = dest.TryImport(exportPath, out var error);
        Assert.True(ok);
        Assert.Null(error);
        Assert.Single(dest.Current.Profiles);
        Assert.Equal("Imported", dest.Current.Profiles[0].Name);
    }

    [Fact]
    public void TryImport_RejectsJsonWithProfilesKeyButNoConfigVersion()
    {
        // A foreign file that happens to carry a "profiles" property but lacks the VibeSwitcher
        // ConfigVersion marker must be rejected — it must not overwrite the user's real config.
        var svc = MakeSvc();
        svc.Load();
        svc.Current.Profiles.Add(new DeviceProfile { Name = "Keep" });
        svc.SaveImmediate();

        var foreign = Path.Combine(_dir, "foreign2.json");
        File.WriteAllText(foreign, """{ "profiles": [], "name": "some other tool" }""");

        bool ok = svc.TryImport(foreign, out var error);
        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Single(svc.Current.Profiles);
        Assert.Equal("Keep", svc.Current.Profiles[0].Name);
    }

    [Fact]
    public void ResetSettingsToDefaults_PreservesUserDataButResetsPreferences()
    {
        var svc = MakeSvc();
        svc.Load();
        var profile = new DeviceProfile { Name = "Keep" };
        svc.Current.Profiles.Add(profile);
        svc.Current.ActiveProfileId = profile.Id;
        svc.Current.DeviceAliases["dev1"] = "My Device";
        svc.Current.CompactIntroShown = true;
        var lastEval = new DateTime(2026, 6, 13, 9, 0, 0);
        svc.Current.LastSchedulerEvaluation = lastEval;
        // Flip preferences away from their defaults.
        svc.Current.CloseToTray = false;      // default true
        svc.Current.SchedulerCatchUp = false; // default true
        svc.Current.Theme = "Dark";           // default "Auto"

        svc.ResetSettingsToDefaults();

        // User data and dedup/intro state are preserved.
        Assert.Single(svc.Current.Profiles);
        Assert.Equal("Keep", svc.Current.Profiles[0].Name);
        Assert.Equal(profile.Id, svc.Current.ActiveProfileId);
        Assert.Equal("My Device", svc.Current.DeviceAliases["dev1"]);
        Assert.True(svc.Current.CompactIntroShown);
        Assert.Equal(lastEval, svc.Current.LastSchedulerEvaluation);
        // Preferences return to their defaults.
        Assert.True(svc.Current.CloseToTray);
        Assert.True(svc.Current.SchedulerCatchUp);
        Assert.Equal("Auto", svc.Current.Theme);
    }

    [Fact]
    public void TryImport_ClearsCustomSoundPathOutsideManagedFolder()
    {
        // An imported config must not be able to point the app at an arbitrary file on disk; a custom
        // sound path outside the importer's managed Sounds folder is dropped.
        var src = MakeSvc();
        src.Load();
        src.Current.Profiles.Add(new DeviceProfile
        {
            Name = "P",
            SoundOverride = true,
            SoundTone = "Custom",
            SoundCustomPath = @"C:\Windows\Media\ding.wav",
        });
        var exportPath = Path.Combine(_dir, "export-sound.json");
        src.ExportTo(exportPath);

        var dest = MakeSvc(Path.Combine(_dir, "dest-sound"));
        dest.Load();
        bool ok = dest.TryImport(exportPath, out _);
        Assert.True(ok);
        Assert.Single(dest.Current.Profiles);
        Assert.Null(dest.Current.Profiles[0].SoundCustomPath);
    }

    [Fact]
    public void TryImport_KeepsCustomSoundPathInsideManagedFolder()
    {
        // A sound that already lives in the importer's managed Sounds folder must survive import.
        var dest = MakeSvc(Path.Combine(_dir, "dest-keep"));
        dest.Load();
        Directory.CreateDirectory(dest.SoundsDir);
        var managed = Path.Combine(dest.SoundsDir, "keep.wav");
        File.WriteAllBytes(managed, new byte[] { 0x52, 0x49, 0x46, 0x46 });

        var src = MakeSvc(Path.Combine(_dir, "src-keep"));
        src.Load();
        src.Current.Profiles.Add(new DeviceProfile
        {
            Name = "P", SoundOverride = true, SoundTone = "Custom", SoundCustomPath = managed,
        });
        var exportPath = Path.Combine(_dir, "export-keep.json");
        src.ExportTo(exportPath);

        bool ok = dest.TryImport(exportPath, out _);
        Assert.True(ok);
        Assert.Equal(managed, dest.Current.Profiles[0].SoundCustomPath);
    }

    [Fact]
    public void Load_ClampsOutOfRangeScheduleAndMode()
    {
        File.WriteAllText(Path.Combine(_dir, "config.json"),
            """{ "ConfigVersion": 1, "Profiles": [ { "Name": "P", "Mode": 99, "Schedules": [ { "Hour": 25, "Minute": 70, "ReminderMinutes": 99999 } ] } ] }""");

        var svc = MakeSvc();
        svc.Load();
        var p = svc.Current.Profiles[0];
        Assert.Equal(ProfileMode.Both, p.Mode); // invalid enum value falls back to Both
        Assert.Equal(23, p.Schedules[0].Hour);
        Assert.Equal(59, p.Schedules[0].Minute);
        Assert.True(p.Schedules[0].ReminderMinutes <= 24 * 60 - 1);
    }

    [Fact]
    public void Load_NullsDanglingActiveProfileId()
    {
        File.WriteAllText(Path.Combine(_dir, "config.json"),
            """{ "ConfigVersion": 1, "Profiles": [], "ActiveProfileId": "11111111-1111-1111-1111-111111111111" }""");

        var svc = MakeSvc();
        svc.Load();
        Assert.Null(svc.Current.ActiveProfileId);
    }

    [Fact]
    public void TrayMenuFlags_DefaultTrue_AndRoundTrip()
    {
        var svc = MakeSvc();
        svc.Load();
        Assert.True(svc.Current.TrayShowAbout);
        Assert.True(svc.Current.TrayShowFaq);
        Assert.True(svc.Current.TrayShowMiniMode);
        Assert.True(svc.Current.TrayShowSoundSettings);

        svc.Current.TrayShowAbout = false;
        svc.Current.TrayShowSoundSettings = false;
        svc.SaveImmediate();

        var svc2 = MakeSvc();
        svc2.Load();
        Assert.False(svc2.Current.TrayShowAbout);
        Assert.True(svc2.Current.TrayShowFaq);
        Assert.True(svc2.Current.TrayShowMiniMode);
        Assert.False(svc2.Current.TrayShowSoundSettings);
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
