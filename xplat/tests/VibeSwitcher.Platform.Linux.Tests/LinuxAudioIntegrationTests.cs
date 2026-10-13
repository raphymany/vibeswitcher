using VibeSwitcher.Audio;
using VibeSwitcher.Models;
using VibeSwitcher.Platform.Linux.Audio;
using VibeSwitcher.Platform.Linux.Processes;
using VibeSwitcher.Services;
using VibeSwitcher.Tests;

namespace VibeSwitcher.Platform.Linux.Tests;

// Runs only on Linux with VS_LINUX_AUDIO_IT=1, against a real (throwaway) PulseAudio or PipeWire
// server — see scripts/run-linux-audio-tests.sh, which CI runs for PulseAudio 16, PipeWire, and
// Ubuntu 22.04 with VS_PACTL_TEXT=1 (pactl's text output, as printed before 15.99).
public sealed class LinuxAudioFactAttribute : FactAttribute
{
    public LinuxAudioFactAttribute()
    {
        if (!OperatingSystem.IsLinux() || Environment.GetEnvironmentVariable("VS_LINUX_AUDIO_IT") != "1")
            Skip = "Needs a real sound server: run scripts/run-linux-audio-tests.sh on Linux.";
    }
}

// Creates virtual devices on the real server and removes them again.
internal sealed class RealPulse : IDisposable
{
    private readonly List<string> _modules = [];

    public static ProcessResult Pactl(params string[] args) =>
        ProcessRunner.Instance.Run("pactl", args, TimeSpan.FromSeconds(10));

    public static bool IsPipeWire => Pactl("info").StdOut.Contains("PipeWire", StringComparison.OrdinalIgnoreCase);

    // USB-looking properties, as udev gives a real USB headset.
    public static string UsbProps(string description, string vid = "0bda", string pid = "4e27") =>
        $"device.description='{description}' device.bus=usb device.vendor.id={vid} device.product.id={pid} " +
        "device.bus_path=pci-0000:00:14.0-usb-0:2:1.0";

    public string Sink(string name, string props) => Load("module-null-sink", $"sink_name={name}", $"sink_properties=\"{props}\"");

    // module-null-source can't take properties, so a test microphone is a remap of a hidden null
    // sink's monitor — a normal (non-monitor) source whose properties we choose.
    public string Source(string name, string props)
    {
        Load("module-null-sink", $"sink_name={name}_feed", "sink_properties=\"device.description='VS IT feed'\"");
        return Load("module-remap-source", $"master={name}_feed.monitor", $"source_name={name}", $"source_properties=\"{props}\"");
    }

    private string Load(params string[] args)
    {
        var r = Pactl(["load-module", .. args]);
        Assert.True(r.ExitCode == 0, $"load-module {string.Join(' ', args)} failed: {r.StdErr}");
        var id = r.StdOut.Trim();
        _modules.Add(id);
        return id;
    }

    public void Unload(string module)
    {
        Pactl("unload-module", module);
        _modules.Remove(module);
    }

    public static string? DefaultSink() => PactlParser.ParseInfo(Pactl("info").StdOut).DefaultSink;
    public static string? DefaultSource() => PactlParser.ParseInfo(Pactl("info").StdOut).DefaultSource;

    public void Dispose()
    {
        foreach (var m in _modules.ToList()) Pactl("unload-module", m);
    }
}

[Trait("Category", "LinuxAudio")]
public sealed class LinuxAudioIntegrationTests : IDisposable
{
    private readonly RealPulse _pulse = new();
    private readonly FakeSessionErrorTracker _errors = new();

    public void Dispose() => _pulse.Dispose();

    // VS_PACTL_TEXT=1 runs every test through pactl's text output (what pactl before 15.99 prints).
    internal static readonly bool TextMode = Environment.GetEnvironmentVariable("VS_PACTL_TEXT") == "1";

    private LinuxAudioService Service(bool listen = false) =>
        new(new FakeAppLogger(), _errors, ProcessRunner.Instance, new UsbSysfs(), listen, forceTextOutput: TextMode);

    private static async Task<bool> WaitFor(Func<bool> condition, int ms = 8000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < until) { if (condition()) return true; await Task.Delay(50); }
        return condition();
    }

    [LinuxAudioFact]
    public void Lists_VirtualDevices_WithWindowsShapedUsbKey()
    {
        _pulse.Sink("vs_it_list", RealPulse.UsbProps("VS IT Speaker"));
        _pulse.Source("vs_it_mic", RealPulse.UsbProps("VS IT Mic", pid: "4e28"));
        using var svc = Service();

        var sink = Assert.Single(svc.GetPlaybackDevices(), d => d.Id == "vs_it_list");
        Assert.Equal("VS IT Speaker", sink.FriendlyName);
        Assert.True(sink.IsConnected);
        Assert.Equal(@"USB\VID_0BDA&PID_4E27&MI_00", sink.HardwareKey);
        Assert.Contains("VID_0BDA&PID_4E27", svc.GetAudioEndpointPath("vs_it_list"));

        var mic = Assert.Single(svc.GetRecordingDevices(), d => d.Id == "vs_it_mic");
        Assert.Equal(@"USB\VID_0BDA&PID_4E28&MI_00", mic.HardwareKey);
        Assert.DoesNotContain(svc.GetRecordingDevices(), d => d.Id.EndsWith(".monitor"));
        Assert.Empty(_errors.Errors);
    }

    [LinuxAudioFact]
    public async Task Apply_SwitchesDefaults_OnTheServer()
    {
        _pulse.Sink("vs_it_a", "device.description='VS IT A'");
        _pulse.Sink("vs_it_b", "device.description='VS IT B'");
        _pulse.Source("vs_it_in", "device.description='VS IT In'");
        using var svc = Service();

        var r = await svc.ApplyProfileAsync(new DeviceProfile { Name = "B", PlaybackDeviceId = "vs_it_b", RecordingDeviceId = "vs_it_in" });
        Assert.True(r.PlaybackApplied && r.RecordingApplied, $"{r}");
        Assert.True(await WaitFor(() => RealPulse.DefaultSink() == "vs_it_b"), $"default sink is {RealPulse.DefaultSink()}");
        Assert.True(await WaitFor(() => RealPulse.DefaultSource() == "vs_it_in"), $"default source is {RealPulse.DefaultSource()}");

        r = await svc.ApplyProfileAsync(new DeviceProfile { Name = "A", PlaybackDeviceId = "vs_it_a", RecordingDeviceId = "vs_it_gone" });
        Assert.True(r.PlaybackApplied);
        Assert.Equal("vs_it_gone", r.MissingRecordingId);
        Assert.True(await WaitFor(() => RealPulse.DefaultSink() == "vs_it_a"));
    }

    [LinuxAudioFact]
    public async Task Mute_DefaultOutputAndInput_RoundTrip()
    {
        _pulse.Sink("vs_it_mute", "device.description='VS IT Mute'");
        _pulse.Source("vs_it_mute_in", "device.description='VS IT Mute In'");
        using var svc = Service();
        await svc.ApplyProfileAsync(new DeviceProfile { Name = "M", PlaybackDeviceId = "vs_it_mute", RecordingDeviceId = "vs_it_mute_in" });
        Assert.True(await WaitFor(() => RealPulse.DefaultSink() == "vs_it_mute" && RealPulse.DefaultSource() == "vs_it_mute_in"));

        foreach (var playback in new[] { true, false })
        {
            Assert.True(svc.SetDefaultDeviceMute(playback, true));
            Assert.True(await WaitFor(() => svc.GetDefaultDeviceMute(playback) == true));
            Assert.True(svc.SetDefaultDeviceMute(playback, false));
            Assert.True(await WaitFor(() => svc.GetDefaultDeviceMute(playback) == false));
        }
    }

    [LinuxAudioFact]
    public async Task Events_PlugAndUnplug_RaiseDevicesChanged()
    {
        using var svc = Service(listen: true);
        await Task.Delay(1000); // subscriber + baseline
        int changed = 0;
        svc.DevicesChanged += () => Interlocked.Increment(ref changed);

        var module = _pulse.Sink("vs_it_hotplug", "device.description='VS IT Hotplug'");
        Assert.True(await WaitFor(() => Volatile.Read(ref changed) >= 1), "no event for a new device");

        int before = Volatile.Read(ref changed);
        _pulse.Unload(module);
        Assert.True(await WaitFor(() => Volatile.Read(ref changed) > before), "no event for a removed device");
        Assert.DoesNotContain(_errors.Errors, e => e.Code == VibeSwitcher.Helpers.ErrorCode.DeviceNotificationFailed);
    }

    [LinuxAudioFact]
    public async Task TestTone_IsHeardOnThatDevice_AndMicMeterMeasuresIt()
    {
        _pulse.Sink("vs_it_tone", "device.description='VS IT Tone'");
        using var svc = Service();
        float peak = await MeasureWhile("vs_it_tone.monitor", () => svc.TestSoundAsync("vs_it_tone"));
        // The tone's RMS is about 0.22/√2 ≈ 0.15 at full volume; allow for fades and resampling.
        Assert.InRange(peak, 0.05f, 0.5f);
    }

    [LinuxAudioFact]
    public async Task WavPlayer_PlaysOnDefaultOutput()
    {
        _pulse.Sink("vs_it_wav", "device.description='VS IT Wav'");
        using var svc = Service();
        await svc.ApplyProfileAsync(new DeviceProfile { Name = "W", PlaybackDeviceId = "vs_it_wav" });
        Assert.True(await WaitFor(() => RealPulse.DefaultSink() == "vs_it_wav"));

        var wav = Wav(TestTone.RenderPcm16(44100, 1), 44100, 1);
        float peak = await MeasureWhile("vs_it_wav.monitor", () => Task.Run(() => new PulseWavPlayer().PlayWav(wav)));
        Assert.InRange(peak, 0.05f, 0.5f);
    }

    [LinuxAudioFact]
    public async Task Rebind_DeviceComesBackUnderANewName()
    {
        var oldModule = _pulse.Sink("vs_it_old", RealPulse.UsbProps("VS Rebind Headset", pid: "4e29"));
        using var svc = Service();
        var config = new AppConfig();
        var before = Assert.Single(svc.GetPlaybackDevices(), d => d.Id == "vs_it_old");
        var profile = new DeviceProfile
        {
            Name = "Headset", PlaybackDeviceId = before.Id,
            PlaybackDeviceName = before.FriendlyName, PlaybackDeviceHardwareKey = before.HardwareKey,
        };
        config.Profiles.Add(profile);

        _pulse.Unload(oldModule);
        _pulse.Sink("vs_it_new", RealPulse.UsbProps("VS Rebind Headset", pid: "4e29"));
        await Task.Delay(300);

        var result = DeviceRebindService.Reconcile(config, svc.GetPlaybackDevices(), svc.GetRecordingDevices(), new FakeAppLogger());
        Assert.True(result.Rebound);
        Assert.Equal("vs_it_new", profile.PlaybackDeviceId);
    }

    [LinuxAudioFact]
    public void TextOutput_MatchesJson_OnThisServer()
    {
        _pulse.Sink("vs_it_text", RealPulse.UsbProps("VS IT (Text) Speaker"));
        var json = new PactlClient(ProcessRunner.Instance); // JSON when this pactl supports it
        var text = new PactlClient(ProcessRunner.Instance, forceText: true);

        var fromText = text.ListDevices(sinks: true).Single(d => d.Name == "vs_it_text");
        var fromDefault = json.ListDevices(sinks: true).Single(d => d.Name == "vs_it_text");
        Assert.Equal(fromDefault.Description, fromText.Description);
        Assert.Equal(fromDefault.Prop("device.vendor.id"), fromText.Prop("device.vendor.id"));
        Assert.Equal(fromDefault.IsAvailable, fromText.IsAvailable);
    }

    // Peak mic-meter level on `source` while `play` runs.
    private static async Task<float> MeasureWhile(string source, Func<Task> play)
    {
        using var svc = new LinuxAudioService(new FakeAppLogger(), new FakeSessionErrorTracker(), ProcessRunner.Instance, null, false, forceTextOutput: TextMode);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        float peak = 0;
        int blocks = 0;
        var meter = Task.Run(() => svc.RunMicLevelMonitor(source, cts.Token, l =>
        {
            Interlocked.Increment(ref blocks);
            lock (cts) peak = Math.Max(peak, l);
        }));
        Assert.True(await WaitFor(() => Volatile.Read(ref blocks) > 5), "the mic meter produced no levels");
        await play();
        await Task.Delay(400);
        cts.Cancel();
        await meter;
        lock (cts) return peak;
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
