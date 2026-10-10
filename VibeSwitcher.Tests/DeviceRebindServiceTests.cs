using VibeSwitcher.Models;
using VibeSwitcher.Services;

namespace VibeSwitcher.Tests;

public class DeviceRebindServiceTests
{
    private const string SpdifName = "SPDIF Interface (USB SPDIF Adapter)";
    private const string SpdifKey  = @"USB\VID_0BDA&PID_4E27&MI_00";
    private const string MicName   = "Microphone (Logitech BRIO)";
    private const string MicKey    = @"USB\VID_046D&PID_085E&MI_03";

    private static AudioDeviceInfo Pb(string id, string name = SpdifName, string? key = SpdifKey, bool connected = true) =>
        new(id, name, IsPlayback: true, IsConnected: connected, HardwareKey: key);

    private static AudioDeviceInfo Rec(string id, string name = MicName, string? key = MicKey, bool connected = true) =>
        new(id, name, IsPlayback: false, IsConnected: connected, HardwareKey: key);

    private static DeviceProfile Sonos(string pbId = "pb-old", string? recId = null) => new()
    {
        Name = "SONOS",
        PlaybackDeviceId = pbId,
        PlaybackDeviceName = SpdifName,
        PlaybackDeviceHardwareKey = SpdifKey,
        RecordingDeviceId = recId,
        RecordingDeviceName = recId == null ? null : MicName,
        RecordingDeviceHardwareKey = recId == null ? null : MicKey,
    };

    private static DeviceReconcileResult Run(AppConfig cfg, AudioDeviceInfo[] pb, AudioDeviceInfo[]? rec = null) =>
        DeviceRebindService.Reconcile(cfg, pb, rec ?? [], new FakeAppLogger());

    // ── rebinding ─────────────────────────────────────────────────────────────

    [Fact]
    public void Rebinds_WhenStoredIdGone_AndOneConnectedDeviceMatchesFingerprint()
    {
        var profile = Sonos();
        var cfg = new AppConfig { Profiles = [profile] };

        var result = Run(cfg, [Pb("pb-new")]);

        Assert.True(result.Rebound);
        Assert.True(result.ConfigChanged);
        Assert.Equal("pb-new", profile.PlaybackDeviceId);
    }

    [Fact]
    public void Rebinds_PlaybackAndRecording_Independently()
    {
        var profile = Sonos("pb-old", "rec-old");
        var cfg = new AppConfig { Profiles = [profile] };

        Run(cfg, [Pb("pb-new")], [Rec("rec-new")]);

        Assert.Equal("pb-new", profile.PlaybackDeviceId);
        Assert.Equal("rec-new", profile.RecordingDeviceId);
    }

    [Fact]
    public void DoesNotRebind_WhenTwoConnectedDevicesMatch()
    {
        var profile = Sonos();
        var cfg = new AppConfig { Profiles = [profile] };

        var result = Run(cfg, [Pb("pb-a"), Pb("pb-b")]);

        Assert.False(result.Rebound);
        Assert.Equal("pb-old", profile.PlaybackDeviceId);
    }

    [Fact]
    public void DoesNotRebind_WhenStoredDeviceStillPresent_EvenIfUnplugged()
    {
        // Same endpoint, just unplugged — it's still that device; a twin elsewhere must not steal the slot.
        var profile = Sonos();
        var cfg = new AppConfig { Profiles = [profile] };

        var result = Run(cfg, [Pb("pb-old", connected: false), Pb("pb-twin")]);

        Assert.False(result.Rebound);
        Assert.Equal("pb-old", profile.PlaybackDeviceId);
    }

    [Fact]
    public void DoesNotRebind_ToDisconnectedCandidate()
    {
        var profile = Sonos();
        var cfg = new AppConfig { Profiles = [profile] };

        var result = Run(cfg, [Pb("pb-new", connected: false)]);

        Assert.False(result.Rebound);
        Assert.Equal("pb-old", profile.PlaybackDeviceId);
    }

    [Fact]
    public void DoesNotRebind_WhenHardwareKeyDiffers()
    {
        var profile = Sonos();
        var cfg = new AppConfig { Profiles = [profile] };

        var result = Run(cfg, [Pb("pb-new", key: @"USB\VID_1234&PID_5678&MI_00")]);

        Assert.False(result.Rebound);
    }

    [Fact]
    public void DoesNotRebind_WhenNameDiffers()
    {
        var profile = Sonos();
        var cfg = new AppConfig { Profiles = [profile] };

        var result = Run(cfg, [Pb("pb-new", name: "Speakers (USB SPDIF Adapter)")]);

        Assert.False(result.Rebound);
    }

    [Fact]
    public void DoesNotRebind_WithoutFingerprint()
    {
        var profile = new DeviceProfile { Name = "Legacy", PlaybackDeviceId = "pb-old" };
        var cfg = new AppConfig { Profiles = [profile] };

        var result = Run(cfg, [Pb("pb-new")]);

        Assert.False(result.Rebound);
        Assert.False(result.ConfigChanged);
        Assert.Equal("pb-old", profile.PlaybackDeviceId);
    }

    [Fact]
    public void DoesNotRebind_OnNameAlone_WhenNoHardwareKeyWasStored()
    {
        var profile = Sonos();
        profile.PlaybackDeviceHardwareKey = null;
        var cfg = new AppConfig { Profiles = [profile] };

        var result = Run(cfg, [Pb("pb-new")]);

        Assert.False(result.Rebound);
        Assert.Equal("pb-old", profile.PlaybackDeviceId);
    }

    [Fact]
    public void DoesNotRebind_NonUsbDevices()
    {
        // Monitor #1 is off (its ID omitted as "not present"); identical monitor #2 shares name and
        // graphics-card key. Monitors keep their IDs, so this must never be treated as a re-issue.
        const string hdKey = @"HDAUDIO\FUNC_01&VEN_10DE&DEV_009E&SUBSYS_104387BE&REV_1001";
        var profile = new DeviceProfile
        {
            Name = "Monitor", PlaybackDeviceId = "mon-1",
            PlaybackDeviceName = "Odyssey G50A (NVIDIA High Definition Audio)", PlaybackDeviceHardwareKey = hdKey,
        };
        var cfg = new AppConfig { Profiles = [profile] };

        var result = Run(cfg, [Pb("mon-2", name: "Odyssey G50A (NVIDIA High Definition Audio)", key: hdKey)]);

        Assert.False(result.Rebound);
        Assert.Equal("mon-1", profile.PlaybackDeviceId);
    }

    [Fact]
    public void DoesNotRebind_ToTwinAlreadyBoundToAnotherProfile()
    {
        // Two identical adapters, one per profile. Unplugging #1 must not steal #2 from profile B.
        var a = Sonos("pb-1");
        var b = Sonos("pb-2");
        var cfg = new AppConfig { Profiles = [a, b] };

        var result = Run(cfg, [Pb("pb-2")]);

        Assert.False(result.Rebound);
        Assert.Equal("pb-1", a.PlaybackDeviceId);
        Assert.Equal("pb-2", b.PlaybackDeviceId);
    }

    [Fact]
    public void DoesNotRebind_WhenTwoDifferentOldDevicesClaimTheSameNewOne()
    {
        // Identical adapters #1 (profile A) and #2 (profile B) both unplugged; only one comes back
        // under a new ID. It could be either — neither profile may grab it.
        var a = Sonos("pb-1");
        var b = Sonos("pb-2");
        var cfg = new AppConfig { Profiles = [a, b] };
        cfg.DeviceAliases["pb-1"] = "Adapter 1";

        var result = Run(cfg, [Pb("pb-new")]);

        Assert.False(result.Rebound);
        Assert.Equal("pb-1", a.PlaybackDeviceId);
        Assert.Equal("pb-2", b.PlaybackDeviceId);
        Assert.False(cfg.DeviceAliases.ContainsKey("pb-new"));
    }

    [Fact]
    public void Rebinds_EveryProfileSharingTheReissuedDevice()
    {
        var a = Sonos();
        var b = Sonos();
        var cfg = new AppConfig { Profiles = [a, b] };

        var result = Run(cfg, [Pb("pb-new")]);

        Assert.True(result.Rebound);
        Assert.Equal("pb-new", a.PlaybackDeviceId);
        Assert.Equal("pb-new", b.PlaybackDeviceId);
    }

    [Fact]
    public void DoesNotRebind_WhenRebindNotAllowed_ButStillBackfills()
    {
        var gone = Sonos();
        var present = new DeviceProfile { Name = "Mic", RecordingDeviceId = "rec-1" };
        var cfg = new AppConfig { Profiles = [gone, present] };

        var result = DeviceRebindService.Reconcile(cfg, [Pb("pb-new")], [Rec("rec-1")], new FakeAppLogger(),
            allowRebind: false);

        Assert.False(result.Rebound);
        Assert.True(result.ConfigChanged);
        Assert.Equal("pb-old", gone.PlaybackDeviceId);
        Assert.Equal(MicName, present.RecordingDeviceName);
    }

    [Fact]
    public void NameAndKeyMatch_IsCaseInsensitive()
    {
        var profile = Sonos();
        var cfg = new AppConfig { Profiles = [profile] };

        var result = Run(cfg, [Pb("pb-new", name: SpdifName.ToUpperInvariant(), key: SpdifKey.ToLowerInvariant())]);

        Assert.True(result.Rebound);
    }

    // ── fingerprint backfill ───────────────────────────────────────────────────

    [Fact]
    public void Backfills_Fingerprint_WhenStoredDeviceIsPresent()
    {
        var profile = new DeviceProfile { Name = "Old config", PlaybackDeviceId = "pb-1", RecordingDeviceId = "rec-1" };
        var cfg = new AppConfig { Profiles = [profile] };

        var result = Run(cfg, [Pb("pb-1")], [Rec("rec-1")]);

        Assert.True(result.ConfigChanged);
        Assert.False(result.Rebound);
        Assert.Equal(SpdifName, profile.PlaybackDeviceName);
        Assert.Equal(SpdifKey, profile.PlaybackDeviceHardwareKey);
        Assert.Equal(MicName, profile.RecordingDeviceName);
        Assert.Equal(MicKey, profile.RecordingDeviceHardwareKey);
    }

    [Fact]
    public void NoChange_WhenFingerprintAlreadyCurrent()
    {
        var profile = Sonos("pb-1");
        var cfg = new AppConfig { Profiles = [profile] };

        var result = Run(cfg, [Pb("pb-1")]);

        Assert.False(result.ConfigChanged);
        Assert.False(result.Rebound);
    }

    [Fact]
    public void Backfill_KeepsKnownHardwareKey_WhenKeyReadFails()
    {
        var profile = Sonos("pb-1");
        var cfg = new AppConfig { Profiles = [profile] };

        Run(cfg, [Pb("pb-1", key: null)]);

        Assert.Equal(SpdifKey, profile.PlaybackDeviceHardwareKey);
    }

    [Fact]
    public void Backfill_FollowsWindowsRename()
    {
        var profile = Sonos("pb-1");
        var cfg = new AppConfig { Profiles = [profile] };

        var result = Run(cfg, [Pb("pb-1", name: "Living Room (USB SPDIF Adapter)")]);

        Assert.True(result.ConfigChanged);
        Assert.Equal("Living Room (USB SPDIF Adapter)", profile.PlaybackDeviceName);
    }

    [Fact]
    public void LeavesUnassignedSlotsAlone()
    {
        var profile = new DeviceProfile { Name = "Empty" };
        var cfg = new AppConfig { Profiles = [profile] };

        var result = Run(cfg, [Pb("pb-1")], [Rec("rec-1")]);

        Assert.False(result.ConfigChanged);
        Assert.Null(profile.PlaybackDeviceId);
        Assert.Null(profile.RecordingDeviceId);
    }

    // ── aliases ────────────────────────────────────────────────────────────────

    [Fact]
    public void MovesAlias_ToNewId_OnRebind()
    {
        var profile = Sonos();
        var cfg = new AppConfig { Profiles = [profile] };
        cfg.DeviceAliases["pb-old"] = "SONOS";

        Run(cfg, [Pb("pb-new")]);

        Assert.Equal("SONOS", cfg.DeviceAliases["pb-new"]);
        Assert.False(cfg.DeviceAliases.ContainsKey("pb-old"));
    }

    [Fact]
    public void KeepsOldAlias_WhenAnotherProfileStillReferencesOldId()
    {
        var profile = Sonos();
        var legacy = new DeviceProfile { Name = "No fingerprint", PlaybackDeviceId = "pb-old" };
        var cfg = new AppConfig { Profiles = [profile, legacy] };
        cfg.DeviceAliases["pb-old"] = "SONOS";

        Run(cfg, [Pb("pb-new")]);

        Assert.Equal("SONOS", cfg.DeviceAliases["pb-new"]);
        Assert.Equal("SONOS", cfg.DeviceAliases["pb-old"]);
    }

    [Fact]
    public void MovesAlias_Cleanly_WhenSeveralProfilesShareTheDevice()
    {
        var a = Sonos();
        var b = Sonos();
        var cfg = new AppConfig { Profiles = [a, b] };
        cfg.DeviceAliases["pb-old"] = "SONOS";

        Run(cfg, [Pb("pb-new")]);

        Assert.Equal("SONOS", cfg.DeviceAliases["pb-new"]);
        Assert.False(cfg.DeviceAliases.ContainsKey("pb-old"));
    }

    [Fact]
    public void DoesNotOverwrite_ExistingAliasOnNewId()
    {
        var profile = Sonos();
        var cfg = new AppConfig { Profiles = [profile] };
        cfg.DeviceAliases["pb-old"] = "Old name";
        cfg.DeviceAliases["pb-new"] = "Chosen name";

        Run(cfg, [Pb("pb-new")]);

        Assert.Equal("Chosen name", cfg.DeviceAliases["pb-new"]);
    }

    // ── instance service ───────────────────────────────────────────────────────

    [Fact]
    public void Service_RaisesDevicesRebound_OnlyWhenSomethingWasRebound()
    {
        var profile = Sonos();
        var config = new FakeConfigService();
        config.SetConfig(new AppConfig { Profiles = [profile] });
        var audio = new FakeAudioService { PlaybackResult = [Pb("pb-old")] };
        var svc = new DeviceRebindService(audio, config, new FakeAppLogger());
        int raised = 0;
        svc.DevicesRebound += () => raised++;

        Assert.False(svc.Reconcile());   // still present — nothing to do
        Assert.Equal(0, raised);

        audio.PlaybackResult = [Pb("pb-new")];
        Assert.True(svc.Reconcile());
        Assert.Equal(1, raised);
        Assert.Equal("pb-new", profile.PlaybackDeviceId);
    }

    // ── hardware key normalisation ─────────────────────────────────────────────

    [Theory]
    [InlineData(@"{1}.USB\VID_0BDA&PID_4E27&MI_00\8&404667A&0&0000", @"USB\VID_0BDA&PID_4E27&MI_00")]
    [InlineData(@"{1}.usb\vid_046d&pid_0aba&mi_00\7&2cf0e016&0&0000", @"USB\VID_046D&PID_0ABA&MI_00")]
    [InlineData(@"{1}.ROOT\STEAMSTREAMINGSPEAKERS\0000", @"ROOT\STEAMSTREAMINGSPEAKERS")]
    [InlineData(@"USB\VID_0BDA&PID_4E27", @"USB\VID_0BDA&PID_4E27")]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    public void NormalizeHardwareKey_DropsPrefixAndInstanceSuffix(string? input, string? expected)
    {
        Assert.Equal(expected, AudioDeviceEnumerator.NormalizeHardwareKey(input));
    }

    [Fact]
    public void NormalizeHardwareKey_SamePhysicalModel_OnDifferentPorts_Matches()
    {
        var portA = AudioDeviceEnumerator.NormalizeHardwareKey(@"{1}.USB\VID_046D&PID_0ABA&MI_00\7&2CF0E016&0&0000");
        var portB = AudioDeviceEnumerator.NormalizeHardwareKey(@"{1}.USB\VID_046D&PID_0ABA&MI_00\7&3B39E24B&0&0000");
        Assert.Equal(portA, portB);
    }
}
