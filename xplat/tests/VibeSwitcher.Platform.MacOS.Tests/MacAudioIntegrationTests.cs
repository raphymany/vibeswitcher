using VibeSwitcher.Audio;
using VibeSwitcher.Models;
using VibeSwitcher.Platform.MacOS.Audio;
using VibeSwitcher.Tests;

namespace VibeSwitcher.Platform.MacOS.Tests;

// Real Core Audio. Runs only on macOS with VS_MAC_AUDIO_IT=1; CI installs the BlackHole 2ch and
// 16ch virtual devices first (each is both an output and an input, and BlackHole loops its output
// back to its input, so a played tone can be measured with the mic meter).
public sealed class MacAudioFactAttribute : FactAttribute
{
    public MacAudioFactAttribute()
    {
        if (!OperatingSystem.IsMacOS() || Environment.GetEnvironmentVariable("VS_MAC_AUDIO_IT") != "1")
            Skip = "Needs a Mac with the BlackHole virtual audio devices (CI).";
    }
}

[Trait("Category", "MacAudio")]
public sealed class MacAudioIntegrationTests : IDisposable
{
    private readonly FakeSessionErrorTracker _errors = new();
    private readonly CoreAudioSystem _ca = new();
    private readonly uint? _savedOut, _savedIn, _savedSys;

    public MacAudioIntegrationTests()
    {
        if (!OperatingSystem.IsMacOS()) return;
        _savedOut = _ca.GetDefault(DefaultRole.Output);
        _savedIn = _ca.GetDefault(DefaultRole.Input);
        _savedSys = _ca.GetDefault(DefaultRole.SystemOutput);
    }

    public void Dispose()
    {
        if (!OperatingSystem.IsMacOS()) return;
        if (_savedOut is uint o) _ca.SetDefault(DefaultRole.Output, o);
        if (_savedIn is uint i) _ca.SetDefault(DefaultRole.Input, i);
        if (_savedSys is uint s) _ca.SetDefault(DefaultRole.SystemOutput, s);
    }

    private MacAudioService Service(bool listen = false) =>
        new(new FakeAppLogger(), _errors, _ca, new AudioQueueStreams(), listen);

    private MacDevice BlackHole(string channels) =>
        _ca.GetDevices().SingleOrDefault(d => d.Name.StartsWith("BlackHole " + channels, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"BlackHole {channels} is not installed. Devices: {string.Join(", ", _ca.GetDevices().Select(d => d.Name))}");

    private static async Task<bool> WaitFor(Func<bool> condition, int ms = 8000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < until) { if (condition()) return true; await Task.Delay(50); }
        return condition();
    }

    [MacAudioFact]
    public void Lists_TheVirtualDevices_InBothLists()
    {
        var bh2 = BlackHole("2ch");
        Assert.True(bh2.HasOutput && bh2.HasInput && !bh2.Hidden);
        Assert.False(string.IsNullOrEmpty(bh2.Uid));

        using var svc = Service();
        var outDev = Assert.Single(svc.GetPlaybackDevices(), d => d.Id == bh2.Uid);
        Assert.Equal(bh2.Name, outDev.FriendlyName);
        Assert.Null(outDev.HardwareKey); // virtual, not USB
        Assert.Single(svc.GetRecordingDevices(), d => d.Id == bh2.Uid);
        Assert.Empty(_errors.Errors);
    }

    [MacAudioFact]
    public async Task Apply_SwitchesEveryDefault_OnTheSystem()
    {
        var bh2 = BlackHole("2ch");
        var bh16 = BlackHole("16ch");
        using var svc = Service();

        foreach (var (pb, rec) in new[] { (bh16, bh2), (bh2, bh16) })
        {
            var r = await svc.ApplyProfileAsync(new DeviceProfile { Name = "T", PlaybackDeviceId = pb.Uid, RecordingDeviceId = rec.Uid });
            Assert.True(r.PlaybackApplied && r.RecordingApplied, r.ToString());
            Assert.True(await WaitFor(() => _ca.GetDefault(DefaultRole.Output) == pb.ObjectId), "default output");
            Assert.True(await WaitFor(() => _ca.GetDefault(DefaultRole.SystemOutput) == pb.ObjectId), "system output");
            Assert.True(await WaitFor(() => _ca.GetDefault(DefaultRole.Input) == rec.ObjectId), "default input");
        }

        var missing = await svc.ApplyProfileAsync(new DeviceProfile { Name = "M", PlaybackDeviceId = "no-such-uid" });
        Assert.Equal("no-such-uid", missing.MissingPlaybackId);
    }

    [MacAudioFact]
    public async Task Listener_FiresOnDefaultChange()
    {
        var bh2 = BlackHole("2ch");
        var bh16 = BlackHole("16ch");
        _ca.SetDefault(DefaultRole.Output, bh2.ObjectId);
        await Task.Delay(300);

        int fired = 0;
        using (_ca.Listen(() => Interlocked.Increment(ref fired)))
        {
            Assert.Equal(0, _ca.SetDefault(DefaultRole.Output, bh16.ObjectId));
            Assert.True(await WaitFor(() => Volatile.Read(ref fired) > 0), "Core Audio never called the listener");
        }
    }

    [MacAudioFact]
    public async Task Mute_RoundTrip_WhereTheDeviceHasAControl()
    {
        var bh2 = BlackHole("2ch");
        using var svc = Service();
        await svc.ApplyProfileAsync(new DeviceProfile { Name = "M", PlaybackDeviceId = bh2.Uid, RecordingDeviceId = bh2.Uid });

        int tested = 0;
        foreach (var playback in new[] { true, false })
        {
            if (svc.GetDefaultDeviceMute(playback) == null) continue; // neither a mute nor a volume control
            tested++;
            try
            {
                Assert.True(svc.SetDefaultDeviceMute(playback, true));
                Assert.True(await WaitFor(() => svc.GetDefaultDeviceMute(playback) == true));
            }
            finally
            {
                // Never leave BlackHole muted: the tone tests use it.
                Assert.True(svc.SetDefaultDeviceMute(playback, false));
            }
            Assert.True(await WaitFor(() => svc.GetDefaultDeviceMute(playback) == false));
        }
        Assert.True(tested > 0, "BlackHole exposes mute/volume controls; none were found");
    }

    [MacAudioFact]
    public async Task TestTone_PlaysOnThatDevice_AndTheMicMeterHearsIt()
    {
        var bh2 = BlackHole("2ch");
        using var svc = Service();
        var (blocks, peak) = await MeasureWhile(svc, bh2.Uid, () => svc.TestSoundAsync(bh2.Uid));
        Assert.True(blocks > 10, $"the mic meter produced only {blocks} levels");
        AssertHeard(peak);
    }

    [MacAudioFact]
    public async Task VeryShortSound_IsPlayedInFull()
    {
        // A mute blip is under 0.2 s; it must not be cut off before the device has started.
        var bh2 = BlackHole("2ch");
        using var svc = Service();
        var blip = TestTone.RenderPcm16(48000, 2)[..(48000 * 2 * 2 / 10)]; // the first 0.1 s
        var streams = new AudioQueueStreams();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var (_, peak) = await MeasureWhile(svc, bh2.Uid, () => Task.Run(() =>
            streams.PlayPcm(bh2.Uid, blip, WavSampleFormat.S16, 48000, 2)));
        Assert.True(sw.Elapsed >= TimeSpan.FromMilliseconds(100), "returned before the sound could have played");
        AssertHeard(peak, min: 0.02f);
    }

    // A level of exactly 0 means the input delivered digital silence — on macOS that is what a
    // process without microphone permission gets, rather than an error.
    private static void AssertHeard(float peak, float min = 0.05f)
    {
        Assert.True(peak > 0f, "The input delivered pure silence: the test process probably lacks macOS microphone permission (TCC).");
        Assert.InRange(peak, min, 0.5f);
    }

    [MacAudioFact]
    public async Task WavPlayer_PlaysOnTheDefaultOutput()
    {
        var bh2 = BlackHole("2ch");
        using var svc = Service();
        await svc.ApplyProfileAsync(new DeviceProfile { Name = "W", PlaybackDeviceId = bh2.Uid });
        var wav = Wav(TestTone.RenderPcm16(44100, 1), 44100, 1);

        var (_, peak) = await MeasureWhile(svc, bh2.Uid, () => Task.Run(() => new MacWavPlayer().PlayWav(wav)));
        AssertHeard(peak);
    }

    [MacAudioFact]
    public void IoRegistryLookup_UnknownLocation_IsNull() => Assert.Null(_ca.UsbIdsForLocation(0xFFFFFFFF));

    [MacAudioFact]
    public async Task DisposeWhileListening_IsClean()
    {
        var svc = Service(listen: true);
        await Task.Delay(200);
        svc.Dispose();
        _ca.SetDefault(DefaultRole.Output, BlackHole("16ch").ObjectId);
        await Task.Delay(800); // a late Core Audio callback must not reach a disposed service
    }

    private static async Task<(int Blocks, float Peak)> MeasureWhile(MacAudioService svc, string inputUid, Func<Task> play)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        float peak = 0;
        int blocks = 0;
        var meter = Task.Run(() => svc.RunMicLevelMonitor(inputUid, cts.Token, l =>
        {
            Interlocked.Increment(ref blocks);
            lock (cts) peak = Math.Max(peak, l);
        }));
        Assert.True(await WaitFor(() => Volatile.Read(ref blocks) > 5), "the mic meter produced no levels");
        await play();
        await Task.Delay(400);
        cts.Cancel();
        await meter;
        lock (cts) return (blocks, peak);
    }

    private static byte[] Wav(byte[] pcm, int rate, int channels)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8); w.Write(36 + pcm.Length); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)channels);
        w.Write(rate); w.Write(rate * channels * 2); w.Write((short)(channels * 2)); w.Write((short)16);
        w.Write("data"u8); w.Write(pcm.Length); w.Write(pcm);
        return ms.ToArray();
    }
}
