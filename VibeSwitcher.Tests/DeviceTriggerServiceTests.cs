using VibeSwitcher.Models;
using VibeSwitcher.Services;

namespace VibeSwitcher.Tests;

public class DeviceTriggerServiceTests
{
    private static AudioDeviceInfo Connected(string id, bool isPlayback = true) =>
        new(id, id, isPlayback, IsConnected: true);

    private static AudioDeviceInfo Disconnected(string id, bool isPlayback = true) =>
        new(id, id, isPlayback, IsConnected: false);

    private static DeviceProfile PlaybackProfile(string deviceId, bool trigger = true) => new()
    {
        Mode = ProfileMode.Playback,
        PlaybackDeviceId = deviceId,
        TriggerOnConnect = trigger,
    };

    private static DeviceProfile RecordingProfile(string deviceId, bool trigger = true) => new()
    {
        Mode = ProfileMode.Recording,
        RecordingDeviceId = deviceId,
        TriggerOnConnect = trigger,
    };

    private static DeviceProfile BothProfile(string playbackId, string recordingId, bool trigger = true) => new()
    {
        Mode = ProfileMode.Both,
        PlaybackDeviceId = playbackId,
        RecordingDeviceId = recordingId,
        TriggerOnConnect = trigger,
    };

    // ── basic trigger ──────────────────────────────────────────────────────────

    [Fact]
    public void Triggers_WhenPlaybackDeviceConnects()
    {
        var audio = new FakeAudioService { PlaybackResult = [] };
        var profile = PlaybackProfile("dev-A");
        var config = new FakeConfigService();
        config.SetConfig(new AppConfig { Profiles = [profile] });

        DeviceProfile? switched = null;
        using var svc = new DeviceTriggerService(audio, config, p => switched = p);

        audio.PlaybackResult = [Connected("dev-A")];
        audio.RaiseDevicesChanged();

        Assert.Equal(profile, switched);
    }

    [Fact]
    public void Triggers_WhenRecordingDeviceConnects()
    {
        var audio = new FakeAudioService { RecordingResult = [] };
        var profile = RecordingProfile("mic-B");
        var config = new FakeConfigService();
        config.SetConfig(new AppConfig { Profiles = [profile] });

        DeviceProfile? switched = null;
        using var svc = new DeviceTriggerService(audio, config, p => switched = p);

        audio.RecordingResult = [Connected("mic-B", isPlayback: false)];
        audio.RaiseDevicesChanged();

        Assert.Equal(profile, switched);
    }

    [Fact]
    public void Triggers_WhenBothMode_PlaybackConnects()
    {
        var audio = new FakeAudioService { PlaybackResult = [] };
        var profile = BothProfile("spk-1", "mic-1");
        var config = new FakeConfigService();
        config.SetConfig(new AppConfig { Profiles = [profile] });

        DeviceProfile? switched = null;
        using var svc = new DeviceTriggerService(audio, config, p => switched = p);

        audio.PlaybackResult = [Connected("spk-1")];
        audio.RaiseDevicesChanged();

        Assert.Equal(profile, switched);
    }

    [Fact]
    public void Triggers_WhenBothMode_RecordingConnects()
    {
        var audio = new FakeAudioService { RecordingResult = [] };
        var profile = BothProfile("spk-1", "mic-1");
        var config = new FakeConfigService();
        config.SetConfig(new AppConfig { Profiles = [profile] });

        DeviceProfile? switched = null;
        using var svc = new DeviceTriggerService(audio, config, p => switched = p);

        audio.RecordingResult = [Connected("mic-1", isPlayback: false)];
        audio.RaiseDevicesChanged();

        Assert.Equal(profile, switched);
    }

    // ── no trigger cases ───────────────────────────────────────────────────────

    [Fact]
    public void DoesNotTrigger_WhenTriggerOnConnectFalse()
    {
        var audio = new FakeAudioService { PlaybackResult = [] };
        var profile = PlaybackProfile("dev-A", trigger: false);
        var config = new FakeConfigService();
        config.SetConfig(new AppConfig { Profiles = [profile] });

        DeviceProfile? switched = null;
        using var svc = new DeviceTriggerService(audio, config, p => switched = p);

        audio.PlaybackResult = [Connected("dev-A")];
        audio.RaiseDevicesChanged();

        Assert.Null(switched);
    }

    [Fact]
    public void DoesNotTrigger_WhenProfileAlreadyActive()
    {
        var audio = new FakeAudioService { PlaybackResult = [] };
        var profile = PlaybackProfile("dev-A");
        var config = new FakeConfigService();
        config.SetConfig(new AppConfig
        {
            Profiles = [profile],
            ActiveProfileId = profile.Id,
        });

        DeviceProfile? switched = null;
        using var svc = new DeviceTriggerService(audio, config, p => switched = p);

        audio.PlaybackResult = [Connected("dev-A")];
        audio.RaiseDevicesChanged();

        Assert.Null(switched);
    }

    [Fact]
    public void DoesNotTrigger_WhenDeviceAlreadyConnectedAtStartup()
    {
        var audio = new FakeAudioService { PlaybackResult = [Connected("dev-A")] };
        var profile = PlaybackProfile("dev-A");
        var config = new FakeConfigService();
        config.SetConfig(new AppConfig { Profiles = [profile] });

        DeviceProfile? switched = null;
        using var svc = new DeviceTriggerService(audio, config, p => switched = p);

        // Raise event without changing the set — device was already there at construction
        audio.RaiseDevicesChanged();

        Assert.Null(switched);
    }

    [Fact]
    public void DoesNotTrigger_WhenUnrelatedDeviceConnects()
    {
        var audio = new FakeAudioService { PlaybackResult = [] };
        var profile = PlaybackProfile("dev-A");
        var config = new FakeConfigService();
        config.SetConfig(new AppConfig { Profiles = [profile] });

        DeviceProfile? switched = null;
        using var svc = new DeviceTriggerService(audio, config, p => switched = p);

        audio.PlaybackResult = [Connected("dev-OTHER")];
        audio.RaiseDevicesChanged();

        Assert.Null(switched);
    }

    [Fact]
    public void DoesNotTrigger_WhenWrongMode_PlaybackProfile_RecordingConnects()
    {
        var audio = new FakeAudioService { RecordingResult = [] };
        var profile = PlaybackProfile("dev-A");
        // Playback profile with PlaybackDeviceId "dev-A" should NOT trigger on a recording device
        profile.RecordingDeviceId = "mic-A";
        var config = new FakeConfigService();
        config.SetConfig(new AppConfig { Profiles = [profile] });

        DeviceProfile? switched = null;
        using var svc = new DeviceTriggerService(audio, config, p => switched = p);

        audio.RecordingResult = [Connected("mic-A", isPlayback: false)];
        audio.RaiseDevicesChanged();

        Assert.Null(switched);
    }

    // ── priority ordering ──────────────────────────────────────────────────────

    [Fact]
    public void PinnedProfile_TakesPriorityOverUnpinned()
    {
        var audio = new FakeAudioService { PlaybackResult = [] };
        var unpinned = new DeviceProfile
        {
            Mode = ProfileMode.Playback,
            PlaybackDeviceId = "dev-A",
            TriggerOnConnect = true,
            IsPinned = false,
            SortOrder = 0,
        };
        var pinned = new DeviceProfile
        {
            Mode = ProfileMode.Playback,
            PlaybackDeviceId = "dev-A",
            TriggerOnConnect = true,
            IsPinned = true,
            SortOrder = 1,
        };
        var config = new FakeConfigService();
        config.SetConfig(new AppConfig { Profiles = [unpinned, pinned] });

        DeviceProfile? switched = null;
        using var svc = new DeviceTriggerService(audio, config, p => switched = p);

        audio.PlaybackResult = [Connected("dev-A")];
        audio.RaiseDevicesChanged();

        Assert.Equal(pinned, switched);
    }

    [Fact]
    public void LowerSortOrder_TakesPriorityAmongUnpinned()
    {
        var audio = new FakeAudioService { PlaybackResult = [] };
        var second = PlaybackProfile("dev-A");
        second.SortOrder = 1;
        var first = PlaybackProfile("dev-A");
        first.SortOrder = 0;
        var config = new FakeConfigService();
        config.SetConfig(new AppConfig { Profiles = [second, first] });

        DeviceProfile? switched = null;
        using var svc = new DeviceTriggerService(audio, config, p => switched = p);

        audio.PlaybackResult = [Connected("dev-A")];
        audio.RaiseDevicesChanged();

        Assert.Equal(first, switched);
    }

    // ── dispose ────────────────────────────────────────────────────────────────

    [Fact]
    public void DoesNotTrigger_AfterDispose()
    {
        var audio = new FakeAudioService { PlaybackResult = [] };
        var profile = PlaybackProfile("dev-A");
        var config = new FakeConfigService();
        config.SetConfig(new AppConfig { Profiles = [profile] });

        DeviceProfile? switched = null;
        var svc = new DeviceTriggerService(audio, config, p => switched = p);
        svc.Dispose();

        audio.PlaybackResult = [Connected("dev-A")];
        audio.RaiseDevicesChanged();

        Assert.Null(switched);
    }

    // ── device ID case-insensitivity ──────────────────────────────────────────

    [Fact]
    public void Triggers_CaseInsensitiveDeviceId()
    {
        var audio = new FakeAudioService { PlaybackResult = [] };
        var profile = PlaybackProfile("DEV-A");
        var config = new FakeConfigService();
        config.SetConfig(new AppConfig { Profiles = [profile] });

        DeviceProfile? switched = null;
        using var svc = new DeviceTriggerService(audio, config, p => switched = p);

        audio.PlaybackResult = [Connected("dev-a")];
        audio.RaiseDevicesChanged();

        Assert.Equal(profile, switched);
    }

    // ── null device ID ────────────────────────────────────────────────────────

    [Fact]
    public void DoesNotTrigger_WhenPlaybackDeviceIdIsNull()
    {
        var audio = new FakeAudioService { PlaybackResult = [] };
        var profile = new DeviceProfile
        {
            Mode = ProfileMode.Playback,
            PlaybackDeviceId = null,
            TriggerOnConnect = true,
        };
        var config = new FakeConfigService();
        config.SetConfig(new AppConfig { Profiles = [profile] });

        DeviceProfile? switched = null;
        using var svc = new DeviceTriggerService(audio, config, p => switched = p);

        audio.PlaybackResult = [Connected("dev-A")];
        audio.RaiseDevicesChanged();

        Assert.Null(switched);
    }

    // ── re-connection after disconnect ────────────────────────────────────────

    [Fact]
    public void Triggers_AgainAfterDeviceDisconnectsAndReconnects()
    {
        var audio = new FakeAudioService { PlaybackResult = [] };
        var profile = PlaybackProfile("dev-A");
        var config = new FakeConfigService();
        config.SetConfig(new AppConfig { Profiles = [profile] });

        int switchCount = 0;
        using var svc = new DeviceTriggerService(audio, config, _ => switchCount++);

        // First connect
        audio.PlaybackResult = [Connected("dev-A")];
        audio.RaiseDevicesChanged();
        Assert.Equal(1, switchCount);

        // Disconnect
        audio.PlaybackResult = [];
        audio.RaiseDevicesChanged();
        Assert.Equal(1, switchCount);

        // Reconnect — should trigger again
        audio.PlaybackResult = [Connected("dev-A")];
        audio.RaiseDevicesChanged();
        Assert.Equal(2, switchCount);
    }

    // ── property-change path (for always-ready devices) ───────────────────────

    [Fact]
    public void Triggers_ViaPropertyChange_WhenDeviceAlwaysReady()
    {
        // Device is already in the connected set from startup — simulates a USB dongle
        // that stays "ready" whether the headset is on or off.
        var audio = new FakeAudioService { PlaybackResult = [Connected("dev-A")] };
        var profile = PlaybackProfile("dev-A");
        var config = new FakeConfigService();
        config.SetConfig(new AppConfig { Profiles = [profile] });

        DeviceProfile? switched = null;
        using var svc = new DeviceTriggerService(audio, config, p => switched = p);

        // Device connect path would not fire (device was already in connectedIds).
        // Property change path should fire instead.
        audio.RaiseDevicePropertyChanged("dev-A");

        Assert.Equal(profile, switched);
    }

    [Fact]
    public void DoesNotTrigger_ViaPropertyChange_WhenProfileAlreadyActive()
    {
        var audio = new FakeAudioService { PlaybackResult = [Connected("dev-A")] };
        var profile = PlaybackProfile("dev-A");
        var config = new FakeConfigService();
        config.SetConfig(new AppConfig { Profiles = [profile], ActiveProfileId = profile.Id });

        DeviceProfile? switched = null;
        using var svc = new DeviceTriggerService(audio, config, p => switched = p);

        audio.RaiseDevicePropertyChanged("dev-A");

        Assert.Null(switched);
    }

    [Fact]
    public void DoesNotTrigger_ViaPropertyChange_WhenUnrelatedDevice()
    {
        var audio = new FakeAudioService { PlaybackResult = [Connected("dev-A")] };
        var profile = PlaybackProfile("dev-A");
        var config = new FakeConfigService();
        config.SetConfig(new AppConfig { Profiles = [profile] });

        DeviceProfile? switched = null;
        using var svc = new DeviceTriggerService(audio, config, p => switched = p);

        audio.RaiseDevicePropertyChanged("dev-OTHER");

        Assert.Null(switched);
    }

    [Fact]
    public void DoesNotTrigger_ViaPropertyChange_WithinCooldownWindow()
    {
        var audio = new FakeAudioService { PlaybackResult = [Connected("dev-A")] };
        var profile = PlaybackProfile("dev-A");
        var config = new FakeConfigService();
        config.SetConfig(new AppConfig { Profiles = [profile] });

        int switchCount = 0;
        using var svc = new DeviceTriggerService(audio, config, _ => switchCount++);

        audio.RaiseDevicePropertyChanged("dev-A"); // first — fires
        audio.RaiseDevicePropertyChanged("dev-A"); // immediate repeat — suppressed by cooldown

        Assert.Equal(1, switchCount);
    }

    [Fact]
    public void DoesNotTrigger_ViaPropertyChange_AfterDispose()
    {
        var audio = new FakeAudioService { PlaybackResult = [Connected("dev-A")] };
        var profile = PlaybackProfile("dev-A");
        var config = new FakeConfigService();
        config.SetConfig(new AppConfig { Profiles = [profile] });

        DeviceProfile? switched = null;
        var svc = new DeviceTriggerService(audio, config, p => switched = p);
        svc.Dispose();

        audio.RaiseDevicePropertyChanged("dev-A");

        Assert.Null(switched);
    }

    // ── multiple simultaneous newly-connected devices ─────────────────────────

    [Fact]
    public void OnlyOneSwitchFires_WhenMultipleProfilesMatchSimultaneousConnects()
    {
        var audio = new FakeAudioService { PlaybackResult = [] };
        var profileA = new DeviceProfile
        {
            Mode = ProfileMode.Playback,
            PlaybackDeviceId = "dev-A",
            TriggerOnConnect = true,
            IsPinned = false,
            SortOrder = 0,
        };
        var profileB = new DeviceProfile
        {
            Mode = ProfileMode.Playback,
            PlaybackDeviceId = "dev-B",
            TriggerOnConnect = true,
            IsPinned = false,
            SortOrder = 1,
        };
        var config = new FakeConfigService();
        config.SetConfig(new AppConfig { Profiles = [profileA, profileB] });

        int switchCount = 0;
        DeviceProfile? switched = null;
        using var svc = new DeviceTriggerService(audio, config, p => { switched = p; switchCount++; });

        // Both devices appear simultaneously
        audio.PlaybackResult = [Connected("dev-A"), Connected("dev-B")];
        audio.RaiseDevicesChanged();

        Assert.Equal(1, switchCount);
        Assert.Equal(profileA, switched); // lower SortOrder wins
    }
}
