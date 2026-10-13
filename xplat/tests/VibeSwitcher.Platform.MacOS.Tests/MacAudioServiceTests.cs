using VibeSwitcher.Audio;
using VibeSwitcher.Helpers;
using VibeSwitcher.Models;
using VibeSwitcher.Platform.MacOS.Audio;
using VibeSwitcher.Services;
using VibeSwitcher.Tests;

namespace VibeSwitcher.Platform.MacOS.Tests;

public class MacAudioServiceTests
{
    private readonly FakeCoreAudio _ca = new();
    private readonly FakeStreams _streams = new();
    private readonly FakeSessionErrorTracker _errors = new();
    private readonly RecordingLogger _log = new();

    private const string Speakers = "BuiltInSpeakerDevice";
    private const string Headset = "AppleUSBAudioEngine:Generic:USB Audio:14200000:2,1";
    private const string Mic = "BuiltInMicrophoneDevice";

    public MacAudioServiceTests()
    {
        _ca.Devices.Add(FakeCoreAudio.Device(10, Speakers, "MacBook Pro Speakers", transport: FakeCoreAudio.BuiltIn, jack: false));
        _ca.Devices.Add(FakeCoreAudio.Device(20, Headset, "USB Audio", output: true, input: true,
            transport: FakeCoreAudio.Usb, model: "USB Audio:0BDA:4E27"));
        _ca.Devices.Add(FakeCoreAudio.Device(30, Mic, "MacBook Pro Microphone", output: false, input: true, transport: FakeCoreAudio.BuiltIn));
        _ca.Devices.Add(FakeCoreAudio.Device(40, "HiddenDevice", "Hidden", hidden: true));
        _ca.Devices.Add(FakeCoreAudio.Device(50, "SubDevice", "Aggregate Part", canDefault: false));
        _ca.Defaults[DefaultRole.Output] = 10;
        _ca.Defaults[DefaultRole.SystemOutput] = 10;
        _ca.Defaults[DefaultRole.Input] = 30;
    }

    private MacAudioService Service(bool listen = false) =>
        new(_log, _errors, _ca, _streams, listen, debounce: TimeSpan.FromMilliseconds(20));

    private static DeviceProfile Profile(string? pb, string? rec) => new() { Name = "P", PlaybackDeviceId = pb, RecordingDeviceId = rec };

    // ── Listing ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Lists_OutputsAndInputs_ByStreams_SortedByName()
    {
        using var svc = Service();

        var outputs = svc.GetPlaybackDevices();
        Assert.Equal(["MacBook Pro Speakers", "USB Audio"], outputs.Select(d => d.FriendlyName));
        Assert.All(outputs, d => Assert.True(d.IsPlayback && d.IsConnected && !d.IsDisabled));

        var inputs = svc.GetRecordingDevices();
        Assert.Equal(["MacBook Pro Microphone", "USB Audio"], inputs.Select(d => d.FriendlyName));
        Assert.All(inputs, d => Assert.False(d.IsPlayback));
        // One USB headset = one Core Audio device, listed in both lists under the same id.
        Assert.Equal(Headset, inputs.Single(d => d.FriendlyName == "USB Audio").Id);
    }

    [Fact]
    public void HiddenAndNonDefaultableDevices_AreNotOffered()
    {
        using var svc = Service();
        Assert.DoesNotContain(svc.GetPlaybackDevices(), d => d.Id is "HiddenDevice" or "SubDevice");
    }

    [Fact]
    public void UsbDevice_GetsWindowsShapedKey()
    {
        using var svc = Service();
        Assert.Equal(@"USB\VID_0BDA&PID_4E27", svc.GetPlaybackDevices().Single(d => d.Id == Headset).HardwareKey);
        Assert.Null(svc.GetPlaybackDevices().Single(d => d.Id == Speakers).HardwareKey);
        Assert.Contains("VID_0BDA&PID_4E27", svc.GetAudioEndpointPath(Headset));
    }

    [Fact]
    public void UsbIds_FallBackToTheIoRegistry_Once()
    {
        _ca.Devices[1] = _ca.Devices[1] with { ModelUid = null };
        _ca.UsbByLocation[0x14200000] = (0x046D, 0x0ABA);
        using var svc = Service();

        Assert.Equal(@"USB\VID_046D&PID_0ABA", svc.GetPlaybackDevices().Single(d => d.Id == Headset).HardwareKey);
        svc.GetRecordingDevices();
        svc.GetPlaybackDevices();
        Assert.Equal(1, _ca.UsbLookups); // cached by location
    }

    [Fact]
    public void CoreAudioFailure_EmptyList_ReportedOnce()
    {
        _ca.Broken = true;
        using var svc = Service();
        Assert.Empty(svc.GetPlaybackDevices());
        Assert.Empty(svc.GetRecordingDevices());
        Assert.Equal(ErrorCode.AudioEnumerationFailed, Assert.Single(_errors.Errors).Code);
    }

    // ── Switching ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Apply_SetsOutputSystemOutputAndInput()
    {
        using var svc = Service();
        var r = await svc.ApplyProfileAsync(Profile(Headset, Headset));

        Assert.True(r.PlaybackApplied && r.RecordingApplied);
        Assert.Equal(20u, _ca.Defaults[DefaultRole.Output]);
        Assert.Equal(20u, _ca.Defaults[DefaultRole.SystemOutput]); // alert sounds follow, like Windows' roles
        Assert.Equal(20u, _ca.Defaults[DefaultRole.Input]);
    }

    [Fact]
    public async Task Apply_SkipsRolesAlreadySet()
    {
        _ca.Defaults[DefaultRole.SystemOutput] = 20;
        using var svc = Service();
        var r = await svc.ApplyProfileAsync(Profile(Speakers, Mic));

        Assert.True(r.PlaybackApplied && r.RecordingApplied);
        Assert.Equal(["set SystemOutput 10"], _ca.Calls);
    }

    [Fact]
    public async Task Apply_Missing_AndWrongDirection_AreMissing()
    {
        using var svc = Service();
        var r = await svc.ApplyProfileAsync(Profile("gone", Speakers)); // speakers have no input

        Assert.Equal("gone", r.MissingPlaybackId);
        Assert.Equal(Speakers, r.MissingRecordingId);
        Assert.Empty(_ca.Calls);
    }

    [Fact]
    public async Task Apply_Refused_SetFailed_KeepsTryingOtherRole()
    {
        _ca.RefuseDefault.Add(20);
        using var svc = Service();
        var r = await svc.ApplyProfileAsync(Profile(Headset, null));

        Assert.False(r.PlaybackApplied);
        Assert.True(r.PlaybackSetFailed);
        Assert.Equal(["set Output 20", "set SystemOutput 20"], _ca.Calls);
        Assert.All(_errors.Errors, e => Assert.Equal(ErrorCode.PolicySetDefaultFailed, e.Code));
        Assert.Equal(2, _errors.Count);
    }

    [Fact]
    public async Task Apply_CoreAudioDown_ThrowsLikeWindows()
    {
        _ca.Broken = true;
        using var svc = Service();
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ApplyProfileAsync(Profile(Headset, null)));
        Assert.Equal(ErrorCode.AudioServiceUnavailable, Assert.Single(_errors.Errors).Code);
    }

    // ── Mute ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Mute_ActsOnTheCurrentDefaults()
    {
        _ca.Mutes[(10, false)] = false;
        _ca.Mutes[(30, true)] = false;
        using var svc = Service();

        Assert.True(svc.SetDefaultDeviceMute(playback: true, mute: true));
        Assert.True(_ca.Mutes[(10, false)]);
        Assert.True(svc.GetDefaultDeviceMute(playback: true));

        Assert.True(svc.SetDefaultDeviceMute(playback: false, mute: true));
        Assert.True(_ca.Mutes[(30, true)]);
        Assert.True(svc.GetDefaultDeviceMute(playback: false));
    }

    [Fact]
    public void Mute_NoControlOrNoDefault_FailsSoftly()
    {
        using var svc = Service();
        Assert.Null(svc.GetDefaultDeviceMute(playback: true)); // the fake speakers have no mute control
        Assert.False(svc.SetDefaultDeviceMute(playback: true, mute: true));

        _ca.Defaults.Remove(DefaultRole.Input);
        Assert.Null(svc.GetDefaultDeviceMute(playback: false));
        Assert.False(svc.SetDefaultDeviceMute(playback: false, mute: true));
    }

    // ── Tone, meter, sounds ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TestSound_PlaysTheToneOnThatDevice()
    {
        using var svc = Service();
        await svc.TestSoundAsync(Headset);

        var p = Assert.Single(_streams.Played);
        Assert.Equal(Headset, p.Device);
        Assert.Equal(WavSampleFormat.S16, p.Format);
        Assert.Equal((48000, 2), (p.Rate, p.Channels));
        Assert.Equal(TestTone.RenderPcm16(48000, 2), p.Data);
    }

    [Fact]
    public async Task TestSound_Failure_LoggedNotThrown()
    {
        _streams.PlayError = new InvalidOperationException("Audio device is not available");
        using var svc = Service();
        await svc.TestSoundAsync(Headset);
        Assert.Contains(_log.Lines, l => l.StartsWith("MacAudioService.TestSound"));
    }

    [Fact]
    public void MicMeter_RmsPerBlock_UntilCancelled()
    {
        _streams.RecordSamples = [.. Enumerable.Repeat(0f, 160), .. Enumerable.Repeat(0.5f, 160), .. Enumerable.Repeat(-0.25f, 160)];
        using var svc = Service();
        using var cts = new CancellationTokenSource();
        var levels = new List<float>();

        svc.RunMicLevelMonitor(Mic, cts.Token, l => { levels.Add(l); if (levels.Count == 3) cts.Cancel(); });

        Assert.Equal([0f, 0.5f, 0.25f], levels.Select(l => MathF.Round(l, 4)));
        Assert.Equal(Mic, _streams.CapturedFrom);
        Assert.Empty(_log.Lines);
    }

    [Fact]
    public void MicMeter_InputStops_LoggedAndReturns()
    {
        using var svc = Service();
        svc.RunMicLevelMonitor(Mic, CancellationToken.None, _ => { });
        Assert.Contains(_log.Lines, l => l.StartsWith("MacAudioService.RunMicLevelMonitor"));
    }

    [Fact]
    public void WavPlayer_PlaysOnDefaultOutput()
    {
        var wav = new byte[44 + 400];
        "RIFF"u8.CopyTo(wav); BitConverter.GetBytes(436).CopyTo(wav, 4); "WAVEfmt "u8.CopyTo(wav.AsSpan(8));
        BitConverter.GetBytes(16).CopyTo(wav, 16); BitConverter.GetBytes((short)1).CopyTo(wav, 20); BitConverter.GetBytes((short)1).CopyTo(wav, 22);
        BitConverter.GetBytes(44100).CopyTo(wav, 24); BitConverter.GetBytes(88200).CopyTo(wav, 28);
        BitConverter.GetBytes((short)2).CopyTo(wav, 32); BitConverter.GetBytes((short)16).CopyTo(wav, 34);
        "data"u8.CopyTo(wav.AsSpan(36)); BitConverter.GetBytes(400).CopyTo(wav, 40);

        new MacWavPlayer(_streams).PlayWav(wav);

        var p = Assert.Single(_streams.Played);
        Assert.Null(p.Device);
        Assert.Equal((44100, 1, 400), (p.Rate, p.Channels, p.Data.Length));
    }

    // ── Change events ───────────────────────────────────────────────────────────────────────────

    private static async Task<bool> WaitFor(Func<bool> condition, int ms = 3000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < until) { if (condition()) return true; await Task.Delay(10); }
        return condition();
    }

    // A listening service records its baseline in the background; wait for it before changing anything.
    private async Task<MacAudioService> ListeningService()
    {
        int before = Volatile.Read(ref _ca.GetDevicesCalls);
        var svc = Service(listen: true);
        Assert.True(await WaitFor(() => Volatile.Read(ref _ca.GetDevicesCalls) > before), "no baseline");
        await Task.Delay(50);
        return svc;
    }

    [Fact]
    public async Task Events_DevicePluggedIn_DevicesChanged_Debounced()
    {
        using var svc = await ListeningService();
        int changed = 0;
        svc.DevicesChanged += () => Interlocked.Increment(ref changed);

        lock (_ca.Gate) _ca.Devices.Add(FakeCoreAudio.Device(60, "NewUID", "New Speaker"));
        for (int i = 0; i < 5; i++) _ca.Fire();

        Assert.True(await WaitFor(() => changed > 0));
        await Task.Delay(100);
        Assert.Equal(1, changed);
    }

    [Fact]
    public async Task Events_DeviceRemoved_DevicesChanged()
    {
        using var svc = await ListeningService();
        int changed = 0;
        svc.DevicesChanged += () => Interlocked.Increment(ref changed);

        lock (_ca.Gate) _ca.Devices.RemoveAt(1);
        _ca.Fire();
        Assert.True(await WaitFor(() => changed > 0));
    }

    [Fact]
    public async Task Events_HeadphonesIntoTheJack_PropertyChanged_UnplugNothing()
    {
        using var svc = await ListeningService();
        var props = new List<string>();
        int changed = 0;
        svc.DevicePropertyChanged += id => { lock (props) props.Add(id); };
        svc.DevicesChanged += () => Interlocked.Increment(ref changed);

        lock (_ca.Gate) _ca.Devices[0] = _ca.Devices[0] with { JackConnected = true };
        _ca.Fire();
        Assert.True(await WaitFor(() => { lock (props) return props.Count > 0; }));
        lock (props) Assert.Equal([Speakers], props);

        lock (_ca.Gate) _ca.Devices[0] = _ca.Devices[0] with { JackConnected = false };
        _ca.Fire();
        await Task.Delay(150);
        lock (props) Assert.Single(props);
        Assert.Equal(0, changed);
    }

    [Fact]
    public async Task Events_DefaultChangeOnly_RaisesNothing()
    {
        using var svc = await ListeningService();
        int events = 0;
        svc.DevicesChanged += () => Interlocked.Increment(ref events);
        svc.DevicePropertyChanged += _ => Interlocked.Increment(ref events);

        lock (_ca.Gate) _ca.Defaults[DefaultRole.Output] = 20;
        _ca.Fire();
        await Task.Delay(150);
        Assert.Equal(0, events);
    }

    [Fact]
    public void Listen_Refused_Recorded()
    {
        _ca.ListenFails = true;
        using var svc = Service(listen: true);
        Assert.Equal(ErrorCode.DeviceNotificationFailed, Assert.Single(_errors.Errors).Code);
    }

    [Fact]
    public async Task Dispose_Unsubscribes_AndRaisesNothingAfter()
    {
        var svc = await ListeningService();
        Assert.Equal(1, _ca.ListenerCount);
        int changed = 0;
        svc.DevicesChanged += () => Interlocked.Increment(ref changed);

        svc.Dispose();
        Assert.Equal(0, _ca.ListenerCount);
        lock (_ca.Gate) _ca.Devices.Clear();
        _ca.Fire();
        await Task.Delay(100);
        Assert.Equal(0, changed);
        Assert.Empty(svc.GetPlaybackDevices());
    }

    [Fact]
    public async Task Events_DeviceGainsAMicrophone_OrIsRenamed_DevicesChanged()
    {
        using var svc = await ListeningService();
        int changed = 0;
        svc.DevicesChanged += () => Interlocked.Increment(ref changed);

        // A Bluetooth headset switching to its call profile: same device, now with an input.
        lock (_ca.Gate) _ca.Devices[0] = _ca.Devices[0] with { HasInput = true, CanBeDefaultInput = true, InputChannels = 1 };
        _ca.Fire();
        Assert.True(await WaitFor(() => changed == 1));

        lock (_ca.Gate) _ca.Devices[2] = _ca.Devices[2] with { Name = "Renamed Mic" };
        _ca.Fire();
        Assert.True(await WaitFor(() => changed == 2));
    }

    // ── Fixes from review ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Apply_DeviceThatCantPlaySoundEffects_SwitchesOutputOnly()
    {
        _ca.Devices.Add(FakeCoreAudio.Device(80, "LoopbackUID", "Loopback", canSystem: false));
        using var svc = Service();
        var r = await svc.ApplyProfileAsync(Profile("LoopbackUID", null));

        Assert.True(r.PlaybackApplied);
        Assert.False(r.PlaybackSetFailed);
        Assert.Equal(["set Output 80"], _ca.Calls);
        Assert.Equal(10u, _ca.Defaults[DefaultRole.SystemOutput]);
    }

    [Fact]
    public async Task Apply_FailureMessage_NamesTheRoleInPlainWords()
    {
        _ca.RefuseDefault.Add(20);
        using var svc = Service();
        await svc.ApplyProfileAsync(Profile(Headset, null));
        Assert.Contains(_errors.Errors, e => e.Message.Contains("as the sound effects device"));
        Assert.Contains(_errors.Errors, e => e.Message.Contains("as the output device"));
    }

    [Fact]
    public void UsbIds_CachedPerDevice_NotPerPort()
    {
        // Two headsets used in the same USB port at different times: same location, different UIDs.
        const string a = "AppleUSBAudioEngine:Maker:Headset A:14200000:2,1";
        const string b = "AppleUSBAudioEngine:Maker:Headset B:14200000:2,1";
        _ca.Devices.Clear();
        _ca.Devices.Add(FakeCoreAudio.Device(1, a, "Headset A", transport: FakeCoreAudio.Usb));
        _ca.UsbByLocation[0x14200000] = (0x1111, 0x0001);
        using var svc = Service();
        Assert.Equal(@"USB\VID_1111&PID_0001", svc.GetPlaybackDevices().Single().HardwareKey);

        _ca.Devices[0] = FakeCoreAudio.Device(2, b, "Headset B", transport: FakeCoreAudio.Usb);
        _ca.UsbByLocation[0x14200000] = (0x2222, 0x0002);
        Assert.Equal(@"USB\VID_2222&PID_0002", svc.GetPlaybackDevices().Single().HardwareKey); // not A's ids
    }

    [Fact]
    public void UsbIds_NotFound_NotRescannedOnEveryListing()
    {
        _ca.Devices[1] = _ca.Devices[1] with { ModelUid = null }; // and no registry entry
        using var svc = Service();
        svc.GetPlaybackDevices();
        svc.GetPlaybackDevices();
        svc.GetRecordingDevices();
        Assert.Equal(1, _ca.UsbLookups);
        Assert.Null(svc.GetPlaybackDevices().Single(d => d.Id == Headset).HardwareKey);
    }

    [Fact]
    public void MicMeter_CapturesEveryInputChannel()
    {
        _ca.Devices.Add(FakeCoreAudio.Device(90, "InterfaceUID", "4-in Interface", output: false, input: true, inputChannels: 4));
        _streams.RecordSamples = [.. Enumerable.Repeat(0.5f, 160)];
        using var svc = Service();
        using var cts = new CancellationTokenSource();
        svc.RunMicLevelMonitor("InterfaceUID", cts.Token, _ => cts.Cancel());
        Assert.Equal(4, _streams.CapturedChannels);
    }

    [Fact]
    public void Mute_Failure_IsLogged()
    {
        using var svc = Service();
        Assert.False(svc.SetDefaultDeviceMute(playback: true, mute: true)); // speakers: no control in the fake
        Assert.Contains(_log.Lines, l => l.Contains("no mute or volume control"));
        _ca.Defaults.Remove(DefaultRole.Input);
        Assert.False(svc.SetDefaultDeviceMute(playback: false, mute: true));
        Assert.Contains(_log.Lines, l => l.Contains("No default input device"));
    }

    // ── The linked Windows rebind logic, fed by this backend ────────────────────────────────────

    [Fact]
    public void Rebind_UsbHeadsetMovedToAnotherPort_ProfileFollows()
    {
        using var svc = Service();
        var before = svc.GetPlaybackDevices().Single(d => d.Id == Headset);
        var config = new AppConfig();
        var profile = new DeviceProfile
        {
            Name = "Headset", PlaybackDeviceId = before.Id,
            PlaybackDeviceName = before.FriendlyName, PlaybackDeviceHardwareKey = before.HardwareKey,
        };
        config.Profiles.Add(profile);

        // Same headset, other USB port: macOS gives it a new UID (the location id changed).
        const string moved = "AppleUSBAudioEngine:Generic:USB Audio:14300000:2,1";
        _ca.Devices[1] = _ca.Devices[1] with { Uid = moved, ObjectId = 70 };

        var result = DeviceRebindService.Reconcile(config, svc.GetPlaybackDevices(), svc.GetRecordingDevices(), new FakeAppLogger());
        Assert.True(result.Rebound);
        Assert.Equal(moved, profile.PlaybackDeviceId);
    }
}
