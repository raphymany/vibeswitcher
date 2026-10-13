using VibeSwitcher.Helpers;
using VibeSwitcher.Models;
using VibeSwitcher.Platform.Linux.Audio;
using VibeSwitcher.Services;
using VibeSwitcher.Tests;

namespace VibeSwitcher.Platform.Linux.Tests;

public class LinuxAudioServiceTests
{
    private readonly FakePulse _pulse = new();
    private readonly FakeSessionErrorTracker _errors = new();

    public LinuxAudioServiceTests()
    {
        _pulse.Sinks.Add(Usb("alsa_output.usb-Headset-00.analog-stereo", "USB Headset"));
        var builtin = new FakeDevice { Name = "alsa_output.pci-builtin.analog-stereo", Description = "Built-in Audio" };
        builtin.Ports.Add(("analog-output-speaker", "availability unknown"));
        builtin.Ports.Add(("analog-output-headphones", "not available"));
        builtin.ActivePort = "analog-output-speaker";
        _pulse.Sinks.Add(builtin);
        var hdmi = new FakeDevice { Name = "alsa_output.pci-hdmi.hdmi-stereo", Description = "HDMI Output" };
        hdmi.Ports.Add(("hdmi-output-0", "not available"));
        _pulse.Sinks.Add(hdmi);

        _pulse.Sources.Add(new FakeDevice { Name = "alsa_output.usb-Headset-00.analog-stereo.monitor", Description = "Monitor of USB Headset", MonitorOf = "alsa_output.usb-Headset-00.analog-stereo" });
        _pulse.Sources.Add(Usb("alsa_input.usb-Headset-00.mono-fallback", "USB Headset Mic"));
        _pulse.Sources.Add(new FakeDevice { Name = "auto_null.monitor", Description = "Dummy", MonitorOf = "auto_null" });

        _pulse.DefaultSink = "alsa_output.pci-builtin.analog-stereo";
        _pulse.DefaultSource = "alsa_input.usb-Headset-00.mono-fallback";
    }

    private static FakeDevice Usb(string name, string description, string vid = "0bda", string pid = "4e27")
    {
        var d = new FakeDevice { Name = name, Description = description };
        d.Props["device.bus"] = "usb";
        d.Props["device.vendor.id"] = vid;
        d.Props["device.product.id"] = pid;
        d.Props["device.bus_path"] = "pci-0000:00:14.0-usb-0:2:1.0";
        return d;
    }

    private readonly RecordingLogger _log = new();

    private LinuxAudioService Service(bool listen = false, TimeSpan? cooldown = null) =>
        new(_log, _errors, _pulse, sysfs: null, listenForChanges: listen,
            debounce: TimeSpan.FromMilliseconds(20), watcherBackoff: _ => TimeSpan.FromMilliseconds(20),
            unavailableCooldown: cooldown ?? TimeSpan.Zero);

    internal sealed class RecordingLogger : IAppLogger
    {
        private readonly List<string> _lines = [];
        public IReadOnlyList<string> Warnings { get { lock (_lines) return _lines.ToList(); } }
        public void Info(string context, string message) { }
        public void Warning(string context, string message) { lock (_lines) _lines.Add($"{context}: {message}"); }
        public void Error(string context, string message) { lock (_lines) _lines.Add($"{context}: {message}"); }
        public void Error(string context, Exception ex) { lock (_lines) _lines.Add($"{context}: {ex.Message}"); }
        public void Debug(string context, string message) { }
    }

    private static DeviceProfile Profile(string? pb, string? rec) =>
        new() { Name = "P", PlaybackDeviceId = pb, RecordingDeviceId = rec };

    // ── Listing ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Playback_ConnectedFirstThenUnplugged_SortedByName()
    {
        using var svc = Service();
        var list = svc.GetPlaybackDevices();

        Assert.Equal(["Built-in Audio", "USB Headset", "HDMI Output"], list.Select(d => d.FriendlyName));
        Assert.All(list, d => Assert.True(d.IsPlayback));
        Assert.False(list.Single(d => d.FriendlyName == "HDMI Output").IsConnected);
        Assert.All(list, d => Assert.False(d.IsDisabled));
        Assert.Equal(@"USB\VID_0BDA&PID_4E27&MI_00", list.Single(d => d.FriendlyName == "USB Headset").HardwareKey);
        Assert.Null(list.Single(d => d.FriendlyName == "Built-in Audio").HardwareKey);
    }

    [Fact]
    public void Recording_HidesMonitorsAndDummy()
    {
        using var svc = Service();
        var d = Assert.Single(svc.GetRecordingDevices());
        Assert.Equal("alsa_input.usb-Headset-00.mono-fallback", d.Id);
        Assert.False(d.IsPlayback);
    }

    [Fact]
    public void OldPactlWithoutJson_FallsBackToText_AndRemembers()
    {
        _pulse.JsonSupported = false;
        using var svc = Service();

        Assert.Equal(3, svc.GetPlaybackDevices().Count);
        Assert.Single(svc.GetRecordingDevices());
        // JSON was tried once, then text only.
        Assert.Single(_pulse.Calls, c => c.Contains("-f json"));
    }

    [Fact]
    public void ServerDown_EmptyList_ReportedOncePerOutage()
    {
        _pulse.ServerDown = true;
        using var svc = Service();

        Assert.Empty(svc.GetPlaybackDevices());
        Assert.Empty(svc.GetRecordingDevices());
        Assert.Equal(ErrorCode.AudioEnumerationFailed, Assert.Single(_errors.Errors).Code);

        _pulse.ServerDown = false;
        Assert.NotEmpty(svc.GetPlaybackDevices());
        _pulse.ServerDown = true;
        svc.GetPlaybackDevices();
        Assert.Equal(2, _errors.Count); // a new outage is reported again
    }

    [Fact]
    public void PactlMissing_ExplainsWhatToInstall()
    {
        _pulse.PactlMissing = true;
        using var svc = Service();

        Assert.Empty(svc.GetPlaybackDevices());
        Assert.Contains("pulseaudio-utils", Assert.Single(_errors.Errors).Message);
    }

    // ── Switching ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Apply_SetsBothDefaults()
    {
        _pulse.DefaultSource = null;
        using var svc = Service();

        var r = await svc.ApplyProfileAsync(Profile("alsa_output.usb-Headset-00.analog-stereo", "alsa_input.usb-Headset-00.mono-fallback"));

        Assert.True(r.PlaybackApplied);
        Assert.True(r.RecordingApplied);
        Assert.Null(r.MissingPlaybackId);
        Assert.Equal("alsa_output.usb-Headset-00.analog-stereo", _pulse.DefaultSink);
        Assert.Equal("alsa_input.usb-Headset-00.mono-fallback", _pulse.DefaultSource);
    }

    [Fact]
    public async Task Apply_AlreadyDefault_StillSetsIt()
    {
        // On PipeWire the reported default can be the *effective* one while the user's configured
        // choice is a device that was unplugged; setting it again makes the profile's choice stick.
        using var svc = Service();
        var r = await svc.ApplyProfileAsync(Profile("alsa_output.pci-builtin.analog-stereo", "alsa_input.usb-Headset-00.mono-fallback"));

        Assert.True(r.PlaybackApplied);
        Assert.True(r.RecordingApplied);
        Assert.Contains("pactl set-default-sink alsa_output.pci-builtin.analog-stereo", _pulse.Calls);
        Assert.Contains("pactl set-default-source alsa_input.usb-Headset-00.mono-fallback", _pulse.Calls);
    }

    [Fact]
    public async Task Apply_DeviceNameStartingWithDash_IsNeverAnOption()
    {
        _pulse.Sinks.Add(new FakeDevice { Name = "-s", Description = "Odd" });
        using var svc = Service();
        var r = await svc.ApplyProfileAsync(Profile("-s", null));
        Assert.False(r.PlaybackApplied);
        Assert.True(r.PlaybackSetFailed);
        Assert.DoesNotContain(_pulse.Calls, c => c.Contains("set-default-sink"));
    }

    [Fact]
    public void BrokenJson_FallsBackToText()
    {
        _pulse.BrokenJson = true;
        using var svc = Service();
        Assert.Equal(3, svc.GetPlaybackDevices().Count);
        Assert.Empty(_errors.Errors);
    }

    [Fact]
    public void ServerDown_LaterCallsFailFast_ForTheCooldown()
    {
        _pulse.ServerDown = true;
        using var svc = Service(cooldown: TimeSpan.FromMinutes(1));

        Assert.Empty(svc.GetPlaybackDevices());
        int calls = _pulse.Calls.Count;
        Assert.Empty(svc.GetPlaybackDevices());
        Assert.Empty(svc.GetRecordingDevices());
        Assert.Null(svc.GetAudioEndpointPath("x"));
        Assert.Equal(calls, _pulse.Calls.Count); // no pactl runs (and no timeouts) during the cooldown
    }

    [Fact]
    public async Task Apply_MissingAndUnpluggedDevices_ReportedMissing()
    {
        using var svc = Service();
        var r = await svc.ApplyProfileAsync(Profile("alsa_output.pci-hdmi.hdmi-stereo", "gone"));

        Assert.False(r.PlaybackApplied);
        Assert.Equal("alsa_output.pci-hdmi.hdmi-stereo", r.MissingPlaybackId); // present but unplugged
        Assert.Equal("gone", r.MissingRecordingId);
        Assert.False(r.PlaybackSetFailed);
        Assert.Equal("alsa_output.pci-builtin.analog-stereo", _pulse.DefaultSink);
    }

    [Fact]
    public async Task Apply_MonitorIsNeverARecordingDevice()
    {
        using var svc = Service();
        var r = await svc.ApplyProfileAsync(Profile(null, "alsa_output.usb-Headset-00.analog-stereo.monitor"));
        Assert.Equal("alsa_output.usb-Headset-00.analog-stereo.monitor", r.MissingRecordingId);
    }

    [Fact]
    public async Task Apply_Refused_SetFailedAndRecorded()
    {
        _pulse.RefuseDefault.Add("alsa_output.usb-Headset-00.analog-stereo");
        using var svc = Service();

        var r = await svc.ApplyProfileAsync(Profile("alsa_output.usb-Headset-00.analog-stereo", null));

        Assert.False(r.PlaybackApplied);
        Assert.True(r.PlaybackSetFailed);
        Assert.Null(r.MissingPlaybackId);
        Assert.Equal(ErrorCode.PolicySetDefaultFailed, Assert.Single(_errors.Errors).Code);
    }

    [Fact]
    public async Task Apply_EmptyProfile_DoesNothing()
    {
        using var svc = Service();
        var r = await svc.ApplyProfileAsync(Profile(null, ""));
        Assert.Equal(new ProfileSwitchResult(false, false, null, null), r);
    }

    [Fact]
    public async Task Apply_ServerDown_ThrowsLikeWindows_AndRecords()
    {
        _pulse.ServerDown = true;
        using var svc = Service();

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ApplyProfileAsync(Profile("x", null)));
        Assert.Equal(ErrorCode.AudioServiceUnavailable, Assert.Single(_errors.Errors).Code);
    }

    // ── Mute ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Mute_DefaultDevices_RoundTrip()
    {
        using var svc = Service();
        Assert.False(svc.GetDefaultDeviceMute(playback: true));

        Assert.True(svc.SetDefaultDeviceMute(playback: true, mute: true));
        Assert.True(_pulse.Sinks.Single(s => s.Name == _pulse.DefaultSink).Mute);
        Assert.True(svc.GetDefaultDeviceMute(playback: true));

        Assert.True(svc.SetDefaultDeviceMute(playback: false, mute: true));
        Assert.True(svc.GetDefaultDeviceMute(playback: false));
        Assert.True(svc.SetDefaultDeviceMute(playback: false, mute: false));
        Assert.False(svc.GetDefaultDeviceMute(playback: false));
    }

    [Fact]
    public void Mute_NoMicrophone_IsUnknown_AndNothingIsMuted()
    {
        _pulse.DefaultSource = "alsa_output.usb-Headset-00.analog-stereo.monitor";
        using var svc = Service();
        Assert.Null(svc.GetDefaultDeviceMute(playback: false));

        // Muting the monitor would silence desktop recording (OBS etc.), not a microphone.
        Assert.False(svc.SetDefaultDeviceMute(playback: false, mute: true));
        Assert.False(_pulse.Sources.Single(s => s.Name.EndsWith(".monitor") && !s.Name.StartsWith("auto")).Mute);
        Assert.DoesNotContain(_pulse.Calls, c => c.Contains("set-source-mute"));
    }

    [Fact]
    public void Mute_ServerDown_FailsSoftly()
    {
        _pulse.ServerDown = true;
        using var svc = Service();
        Assert.Null(svc.GetDefaultDeviceMute(playback: true));
        Assert.False(svc.SetDefaultDeviceMute(playback: true, mute: true));
    }

    // ── Hardware path ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EndpointPath_FromListing_CachedForRepeatedQueries()
    {
        using var svc = Service();
        var path = svc.GetAudioEndpointPath("alsa_input.usb-Headset-00.mono-fallback");
        Assert.Equal(@"USB\VID_0BDA&PID_4E27&MI_00\PCI-0000:00:14.0-USB-0:2:1.0", path);

        int calls = _pulse.Calls.Count;
        svc.GetAudioEndpointPath("alsa_input.usb-Headset-00.mono-fallback");
        svc.GetAudioEndpointPath("alsa_output.pci-builtin.analog-stereo");
        Assert.Equal(calls, _pulse.Calls.Count);

        Assert.Null(svc.GetAudioEndpointPath("unknown"));
        int afterUnknown = _pulse.Calls.Count;
        Assert.Null(svc.GetAudioEndpointPath("unknown")); // throttled: no second re-list within a second
        Assert.Equal(afterUnknown, _pulse.Calls.Count);
    }

    // ── Test tone, sounds, mic meter ────────────────────────────────────────────────────────────

    [Fact]
    public async Task TestSound_PlaysToneOnThatDevice()
    {
        using var svc = Service();
        await svc.TestSoundAsync("alsa_output.usb-Headset-00.analog-stereo");

        var (args, data) = Assert.Single(_pulse.Played);
        Assert.Contains("--device=alsa_output.usb-Headset-00.analog-stereo", args);
        Assert.Contains("--format=s16le", args);
        Assert.Contains("--rate=48000", args);
        Assert.Contains("--channels=2", args);
        Assert.Equal(48000 / 2 * 2 * 2, data.Length); // 0.5 s, stereo, 16-bit
    }

    [Fact]
    public async Task TestSound_Failure_IsSwallowedAndLogged()
    {
        _pulse.PacatExitCode = 1;
        using var svc = Service();
        await svc.TestSoundAsync("alsa_output.usb-Headset-00.analog-stereo"); // no throw, like Windows
        Assert.Contains(_log.Warnings, w => w.StartsWith("LinuxAudioService.TestSound") && w.Contains("pacat failed"));
    }

    [Fact]
    public void MicMonitor_ReportsRmsPerBlock_UntilCancelled()
    {
        // 16 kHz → 160-sample (10 ms) blocks: one silent, one at 0.5, one at -0.25.
        _pulse.RecordSamples = [.. Enumerable.Repeat(0f, 160), .. Enumerable.Repeat(0.5f, 160), .. Enumerable.Repeat(-0.25f, 160)];
        using var svc = Service();
        using var cts = new CancellationTokenSource();
        var levels = new List<float>();

        svc.RunMicLevelMonitor("alsa_input.usb-Headset-00.mono-fallback", cts.Token, l =>
        {
            levels.Add(l);
            if (levels.Count == 3) cts.Cancel();
        });

        Assert.Equal([0f, 0.5f, 0.25f], levels.Select(l => MathF.Round(l, 4)));
        Assert.Contains(_pulse.Calls, c => c.StartsWith("parec") && c.Contains("--device=alsa_input.usb-Headset-00.mono-fallback"));
    }

    [Fact]
    public void MicMonitor_StreamEnds_ReturnsAfterLogging()
    {
        _pulse.RecordSamples = new float[50]; // less than one block, then EOF
        using var svc = Service();
        int calls = 0;
        svc.RunMicLevelMonitor("mic", CancellationToken.None, _ => calls++);
        Assert.Equal(0, calls);
        Assert.Contains(_log.Warnings, w => w.StartsWith("LinuxAudioService.RunMicLevelMonitor") && w.Contains("parec stopped"));
    }

    [Fact]
    public void WavPlayer_PlaysOnDefaultOutput_WithTheFilesFormat()
    {
        var player = new PulseWavPlayer(_pulse);
        var wav = Wav(rate: 44100, channels: 1, bits: 16, dataBytes: 8820);

        player.PlayWav(wav);

        var (args, data) = Assert.Single(_pulse.Played);
        Assert.DoesNotContain(args, a => a.StartsWith("--device"));
        Assert.Contains("--rate=44100", args);
        Assert.Contains("--channels=1", args);
        Assert.Equal(8820, data.Length);
    }

    [Fact]
    public void Playback_StalledServer_KilledAtTheDeadline()
    {
        _pulse.PacatStalls = true;
        var streams = new PulseStreams(_pulse) { PlaybackSlack = TimeSpan.FromMilliseconds(100) };
        var sw = System.Diagnostics.Stopwatch.StartNew();

        Assert.Throws<TimeoutException>(() => streams.PlayPcm(null, new byte[4800], VibeSwitcher.Audio.WavSampleFormat.S16, 48000, 1, "t"));

        Assert.Equal(1, _pulse.PacatKills);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void WavPlayer_RejectsNonWav_LikeSoundPlayer() =>
        Assert.Throws<InvalidDataException>(() => new PulseWavPlayer(_pulse).PlayWav([1, 2, 3]));

    internal static byte[] Wav(int rate, int channels, int bits, int dataBytes, ushort tag = 1)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8); w.Write(36 + dataBytes); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write(tag); w.Write((short)channels);
        w.Write(rate); w.Write(rate * channels * bits / 8); w.Write((short)(channels * bits / 8)); w.Write((short)bits);
        w.Write("data"u8); w.Write(dataBytes); w.Write(new byte[dataBytes]);
        return ms.ToArray();
    }

    // ── Change events ───────────────────────────────────────────────────────────────────────────

    private static async Task<bool> WaitFor(Func<bool> condition, int ms = 3000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < until) { if (condition()) return true; await Task.Delay(10); }
        return condition();
    }

    private async Task<LinuxAudioService> ListeningService()
    {
        var svc = Service(listen: true);
        Assert.True(await WaitFor(() => _pulse.SubscriberCount == 1), "subscriber never started");
        await Task.Delay(50); // let the baseline snapshot settle
        return svc;
    }

    [Fact]
    public async Task Events_NewDevice_RaisesDevicesChanged_Debounced()
    {
        using var svc = await ListeningService();
        int changed = 0;
        svc.DevicesChanged += () => Interlocked.Increment(ref changed);

        lock (_pulse.Gate) _pulse.Sinks.Add(Usb("alsa_output.usb-New-00.analog-stereo", "New Speaker", pid: "1111"));
        for (int i = 0; i < 5; i++) _pulse.Emit("Event 'new' on sink #99");

        Assert.True(await WaitFor(() => changed > 0));
        await Task.Delay(100);
        Assert.Equal(1, changed); // a burst becomes one event
    }

    [Fact]
    public async Task Events_JackUnplugged_RaisesDevicesChanged()
    {
        using var svc = await ListeningService();
        int changed = 0;
        svc.DevicesChanged += () => Interlocked.Increment(ref changed);

        var builtin = _pulse.Sinks.Single(s => s.Name.Contains("builtin"));
        lock (_pulse.Gate) builtin.Ports[0] = ("analog-output-speaker", "not available"); // every port now unplugged
        _pulse.Emit("Event 'change' on card #0");

        Assert.True(await WaitFor(() => changed > 0));
    }

    [Fact]
    public async Task Events_JackPluggedIn_RaisesDevicePropertyChanged()
    {
        using var svc = await ListeningService();
        var props = new List<string>();
        int changed = 0;
        svc.DevicePropertyChanged += id => { lock (props) props.Add(id); };
        svc.DevicesChanged += () => Interlocked.Increment(ref changed);

        var builtin = _pulse.Sinks.Single(s => s.Name.Contains("builtin"));
        lock (_pulse.Gate)
        {
            builtin.Ports[1] = ("analog-output-headphones", "available");
            builtin.ActivePort = "analog-output-headphones"; // headphones plugged into the jack
        }
        _pulse.Emit("Event 'change' on sink #1");

        Assert.True(await WaitFor(() => { lock (props) return props.Count > 0; }));
        lock (props) Assert.Equal(["alsa_output.pci-builtin.analog-stereo"], props);
        Assert.Equal(0, changed);
    }

    [Fact]
    public async Task Events_JackUnpluggedOrManualPortSwitch_RaisesNoPropertyChange()
    {
        var builtin = _pulse.Sinks.Single(s => s.Name.Contains("builtin"));
        builtin.Ports[1] = ("analog-output-headphones", "available");
        builtin.ActivePort = "analog-output-headphones";
        using var svc = await ListeningService();
        int events = 0;
        svc.DevicePropertyChanged += _ => Interlocked.Increment(ref events);
        svc.DevicesChanged += () => Interlocked.Increment(ref events);

        lock (_pulse.Gate) builtin.ActivePort = "analog-output-speaker"; // picked in the desktop's sound menu
        _pulse.Emit("Event 'change' on sink #1");
        await Task.Delay(150);
        lock (_pulse.Gate)
        {
            builtin.Ports[1] = ("analog-output-headphones", "not available"); // headphones pulled out
        }
        _pulse.Emit("Event 'change' on card #0");
        await Task.Delay(150);

        Assert.Equal(0, events);
    }

    [Fact]
    public async Task Events_VolumeChangeOnly_RaisesNothing_ButIsLookedAt()
    {
        using var svc = await ListeningService();
        int events = 0;
        svc.DevicesChanged += () => Interlocked.Increment(ref events);
        svc.DevicePropertyChanged += _ => Interlocked.Increment(ref events);
        int lists = _pulse.Calls.Count(c => c.Contains("list sources"));

        _pulse.Emit("Event 'change' on sink #0");
        // A refresh lists sinks, then sources: wait for the second half before counting.
        Assert.True(await WaitFor(() => _pulse.Calls.Count(c => c.Contains("list sources")) > lists), "the change was never examined");
        await Task.Delay(50);
        int afterSink = _pulse.Calls.Count;
        _pulse.Emit("Event 'change' on sink-input #12"); // an app's stream: not even looked at

        await Task.Delay(150);
        Assert.Equal(0, events);
        Assert.Equal(afterSink, _pulse.Calls.Count);
    }

    [Fact]
    public async Task Events_ServerRestart_ResubscribesAndRefreshes_WithoutAnError()
    {
        using var svc = await ListeningService();
        int changed = 0;
        svc.DevicesChanged += () => Interlocked.Increment(ref changed);
        _pulse.Emit("Event 'change' on server #-1"); // the subscriber has heard from the server

        lock (_pulse.Gate) _pulse.Sinks.RemoveAt(0);
        _pulse.DropSubscribers(); // sound server went away and came back (e.g. a PipeWire restart)

        Assert.True(await WaitFor(() => _pulse.SubscriberCount == 1), "did not resubscribe");
        Assert.True(await WaitFor(() => changed > 0), "no refresh after resubscribing");
        Assert.DoesNotContain(_errors.Errors, e => e.Code == ErrorCode.DeviceNotificationFailed);
    }

    [Fact]
    public async Task Events_RestartDuringCooldown_StillRefreshes()
    {
        var svc = Service(listen: true, cooldown: TimeSpan.FromMinutes(1));
        using var _ = svc;
        Assert.True(await WaitFor(() => _pulse.SubscriberCount == 1));
        await Task.Delay(50);
        int changed = 0;
        svc.DevicesChanged += () => Interlocked.Increment(ref changed);

        // The server goes down; a read fails and starts the fail-fast cooldown…
        _pulse.ServerDown = true;
        Assert.Empty(svc.GetPlaybackDevices());
        // …it comes back with a device gone, and the listener reconnects.
        _pulse.ServerDown = false;
        lock (_pulse.Gate) _pulse.Sinks.RemoveAt(0);
        _pulse.DropSubscribers();

        Assert.True(await WaitFor(() => changed > 0), "the refresh after reconnecting was swallowed by the cooldown");
        Assert.Equal(2, svc.GetPlaybackDevices().Count); // and the app can read devices again right away
    }

    [Fact]
    public async Task Events_ServerDownAtStart_ReportsOnce_ThenRecovers()
    {
        _pulse.ServerDown = true;
        using var svc = Service(listen: true);
        int changed = 0;
        svc.DevicesChanged += () => Interlocked.Increment(ref changed);

        Assert.True(await WaitFor(() => _errors.Errors.Any(e => e.Code == ErrorCode.DeviceNotificationFailed)));
        await Task.Delay(150); // several more retries
        Assert.Single(_errors.Errors, e => e.Code == ErrorCode.DeviceNotificationFailed);
        Assert.Single(_log.Warnings, w => w.StartsWith("LinuxAudioService.Snapshot")); // logged once, not per retry

        _pulse.ServerDown = false;
        Assert.True(await WaitFor(() => _pulse.SubscriberCount == 1));
        _pulse.Emit("Event 'new' on sink #3");
        Assert.True(await WaitFor(() => changed > 0)); // no baseline before → any successful look raises
    }

    [Fact]
    public async Task Events_NewOutageAfterRecovery_IsReportedAgain()
    {
        using var svc = await ListeningService();
        _pulse.Emit("Event 'change' on server #-1");
        await Task.Delay(50);

        _pulse.ServerDown = true;
        _pulse.DropSubscribers();
        Assert.True(await WaitFor(() => _errors.Errors.Count(e => e.Code == ErrorCode.DeviceNotificationFailed) == 1));

        _pulse.ServerDown = false;
        Assert.True(await WaitFor(() => _pulse.SubscriberCount == 1));
        _pulse.Emit("Event 'change' on server #-1");
        await Task.Delay(50);

        _pulse.ServerDown = true;
        _pulse.DropSubscribers();
        Assert.True(await WaitFor(() => _errors.Errors.Count(e => e.Code == ErrorCode.DeviceNotificationFailed) == 2));
    }

    [Fact]
    public async Task Dispose_StopsListening_AndRaisesNothingAfter()
    {
        var svc = await ListeningService();
        int changed = 0;
        svc.DevicesChanged += () => Interlocked.Increment(ref changed);

        svc.Dispose();
        Assert.Equal(0, _pulse.SubscriberCount);
        lock (_pulse.Gate) _pulse.Sinks.Clear();
        _pulse.Emit("Event 'remove' on sink #0");
        await Task.Delay(100);
        Assert.Equal(0, changed);
        Assert.Empty(svc.GetPlaybackDevices());
    }

    // ── The linked Windows rebind logic, fed by this backend ────────────────────────────────────

    [Fact]
    public void Rebind_UsbDeviceReappearsUnderNewName_ProfileFollows()
    {
        using var svc = Service();
        var config = new AppConfig();
        var profile = new DeviceProfile
        {
            Name = "Headset",
            PlaybackDeviceId = "alsa_output.usb-Headset-00.analog-stereo",
            PlaybackDeviceName = "USB Headset",
            PlaybackDeviceHardwareKey = @"USB\VID_0BDA&PID_4E27&MI_00",
        };
        config.Profiles.Add(profile);

        // PipeWire renamed the node (e.g. a ".2" suffix after re-plugging); same hardware, same name.
        _pulse.Sinks[0].Name = "alsa_output.usb-Headset-00.analog-stereo.2";

        var result = DeviceRebindService.Reconcile(config, svc.GetPlaybackDevices(), svc.GetRecordingDevices(), new FakeAppLogger());

        Assert.True(result.Rebound);
        Assert.Equal("alsa_output.usb-Headset-00.analog-stereo.2", profile.PlaybackDeviceId);
    }
}
