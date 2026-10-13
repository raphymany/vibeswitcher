using VibeSwitcher.Platform.Linux.Audio;

namespace VibeSwitcher.Platform.Linux.Tests;

public class PactlParserTests
{
    internal static string Fixture(string path) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", path));

    // ── Real PulseAudio 16.1 output (recorded) ──────────────────────────────────────────────────

    [Fact]
    public void Json_RealPulseAudio16_Sinks()
    {
        var sinks = PactlParser.ParseJsonDevices(Fixture("pulseaudio-16/sinks.json"), sinks: true);

        var s = Assert.Single(sinks);
        Assert.Equal("vs_usb", s.Name);
        Assert.Equal("USB Headset", s.Description);
        Assert.True(s.IsSink);
        Assert.False(s.Mute);
        Assert.Null(s.MonitorOfSink); // a sink's own "monitor_source" isn't a monitor-of
        Assert.Empty(s.Ports);
        Assert.True(s.IsAvailable);
        Assert.Equal("usb", s.Prop("device.bus"));
        Assert.Equal("pci-0000:00:14.0-usb-0:2:1.0", s.Prop("device.bus_path"));
    }

    [Fact]
    public void Json_RealPulseAudio16_MonitorSourceIsRecognised()
    {
        // pactl 16 reports a source's "monitor of sink" under the key "monitor_source".
        var src = Assert.Single(PactlParser.ParseJsonDevices(Fixture("pulseaudio-16/sources.json"), sinks: false));
        Assert.Equal("vs_usb", src.MonitorOfSink);
        Assert.True(src.IsMonitor);
    }

    [Fact]
    public void Text_RealPulseAudio16_MatchesJson()
    {
        var fromText = Assert.Single(PactlParser.ParseTextDevices(Fixture("pulseaudio-16/sinks.txt"), sinks: true));
        var fromJson = Assert.Single(PactlParser.ParseJsonDevices(Fixture("pulseaudio-16/sinks.json"), sinks: true));

        Assert.Equal(fromJson.Name, fromText.Name);
        Assert.Equal(fromJson.Description, fromText.Description);
        Assert.Equal(fromJson.Mute, fromText.Mute);
        Assert.Equal(fromJson.Properties.OrderBy(p => p.Key), fromText.Properties.OrderBy(p => p.Key));

        var src = Assert.Single(PactlParser.ParseTextDevices(Fixture("pulseaudio-16/sources.txt"), sinks: false));
        Assert.True(src.IsMonitor);
        Assert.Equal("vs_usb", src.MonitorOfSink);
    }

    [Fact]
    public void Info_RealPulseAudio16()
    {
        var info = PactlParser.ParseInfo(Fixture("pulseaudio-16/info.txt"));
        Assert.Equal("vs_usb", info.DefaultSink);
        Assert.Equal("vs_usb.monitor", info.DefaultSource);
        Assert.Equal("pulseaudio", info.ServerName);
    }

    [Fact]
    public void Info_NullDefaultsAreNull()
    {
        var info = PactlParser.ParseInfo("Server Name: x\nDefault Sink: (null)\nDefault Source: (null)\n");
        Assert.Null(info.DefaultSink);
        Assert.Null(info.DefaultSource);
    }

    [Fact]
    public void Subscribe_RealPulseAudio16_Events()
    {
        var events = Fixture("pulseaudio-16/subscribe.txt").Split('\n')
            .Select(PactlParser.ParseSubscribeEvent).Where(e => e != null).Select(e => e!.Value).ToList();

        Assert.Contains(("new", "sink"), events);
        Assert.Contains(("remove", "sink"), events);
        Assert.Contains(("change", "server"), events); // "#4294967295" (index -1) still parses
        Assert.Contains(("new", "client"), events);
    }

    [Theory]
    [InlineData("Event 'new' on sink #57", "new", "sink")]
    [InlineData("Event 'change' on sink-input #3\r", "change", "sink-input")]
    [InlineData("  Event 'remove' on card #2  ", "remove", "card")]
    public void Subscribe_ParsesLine(string line, string type, string facility) =>
        Assert.Equal((type, facility), PactlParser.ParseSubscribeEvent(line));

    [Theory]
    [InlineData("")]
    [InlineData("Connection failure: Connection refused")]
    [InlineData("Event new on sink #1")]
    public void Subscribe_IgnoresOtherLines(string line) => Assert.Null(PactlParser.ParseSubscribeEvent(line));

    // ── PipeWire-style JSON (0x-prefixed ids, device.bus-path, ports) ───────────────────────────

    [Fact]
    public void Json_PipeWire_PortsAndAvailability()
    {
        var sinks = PactlParser.ParseJsonDevices(Fixture("pipewire/sinks.json"), sinks: true);
        Assert.Equal(4, sinks.Count);

        var builtin = sinks.Single(s => s.Name == "alsa_output.pci-0000_00_1f.3.analog-stereo");
        Assert.True(builtin.Mute);
        Assert.Equal(2, builtin.Ports.Count);
        Assert.Equal(PortAvailability.Unknown, builtin.Ports[0].Availability);
        Assert.Equal(PortAvailability.NotAvailable, builtin.Ports[1].Availability);
        Assert.Equal("analog-output-speaker", builtin.ActivePort);
        Assert.True(builtin.IsAvailable); // speakers have no jack sensing → still present

        var hdmi = sinks.Single(s => s.Name.Contains("hdmi"));
        Assert.False(hdmi.IsAvailable); // nothing plugged into the only port → "unplugged", like Windows

        var bt = sinks.Single(s => s.Name.StartsWith("bluez"));
        Assert.Equal(PortAvailability.Available, Assert.Single(bt.Ports).Availability);
    }

    [Fact]
    public void Json_PipeWire_Sources()
    {
        var sources = PactlParser.ParseJsonDevices(Fixture("pipewire/sources.json"), sinks: false);
        Assert.True(sources.Single(s => s.Name.EndsWith(".monitor")).IsMonitor);
        var usbMic = sources.Single(s => s.Name.StartsWith("alsa_input.usb"));
        Assert.False(usbMic.IsMonitor);
        Assert.Null(usbMic.MonitorOfSink); // "n/a"
        Assert.True(usbMic.Mute);
        Assert.False(sources.Single(s => s.Name.StartsWith("alsa_input.pci")).IsAvailable);
    }

    // ── Text output (older pactl, or when the JSON is unusable) ─────────────────────────────────

    [Fact]
    public void Text_Sinks_PortsPropertiesEscapes()
    {
        var sinks = PactlParser.ParseTextDevices(Fixture("text/sinks.txt"), sinks: true);
        Assert.Equal(2, sinks.Count);

        var builtin = sinks[0];
        Assert.Equal(0u, builtin.Index);
        Assert.Equal("Built-in Audio Analog Stereo", builtin.Description);
        Assert.Equal(2, builtin.Ports.Count);
        Assert.Equal("Headphones (Front)", builtin.Ports[1].Description); // parentheses inside the description
        Assert.Equal(PortAvailability.NotAvailable, builtin.Ports[1].Availability);
        Assert.Equal("analog-output-speaker", builtin.ActivePort);
        Assert.Equal("Realtek ALC256", builtin.Prop("alsa.mixer_name"));

        var headset = sinks[1];
        Assert.True(headset.Mute);
        Assert.Equal("PRO X Wireless Gaming Headset \"Stereo\"", headset.Description);
        Assert.Equal("PRO X Wireless Gaming Headset \"Stereo\"", headset.Prop("device.description")); // unescaped
        Assert.Equal(PortAvailability.Unknown, Assert.Single(headset.Ports).Availability);
    }

    [Fact]
    public void Text_Sources()
    {
        var sources = PactlParser.ParseTextDevices(Fixture("text/sources.txt"), sinks: false);
        Assert.True(sources[0].IsMonitor);
        Assert.False(sources[1].IsMonitor);
        Assert.True(sources[1].IsAvailable); // internal mic: availability unknown
    }

    [Fact]
    public void Text_UcmPortNamesWithSpaces()
    {
        // Sound cards using ALSA UCM (most laptops from the last few years) name ports like "[Out] Speaker".
        const string text = "Sink #3\n\tName: alsa_output.pci-0000_00_1f.3-platform-skl_hda_dsp_generic.HiFi__Speaker__sink\n" +
            "\tDescription: Speaker\n\tMute: no\n\tPorts:\n" +
            "\t\t[Out] Speaker: Speaker (type: Speaker, priority: 100, availability unknown)\n" +
            "\t\t[Out] Headphones: Headphones (type: Headphones, priority: 200, availability group: Headphone, not available)\n" +
            "\tActive Port: [Out] Speaker\n";

        var d = Assert.Single(PactlParser.ParseTextDevices(text, sinks: true));
        Assert.Equal(["[Out] Speaker", "[Out] Headphones"], d.Ports.Select(p => p.Name));
        Assert.Equal(PortAvailability.NotAvailable, d.Ports[1].Availability);
        Assert.Equal("[Out] Speaker", d.ActivePort);
    }

    [Fact]
    public void Text_CrLfAndEmpty()
    {
        Assert.Empty(PactlParser.ParseTextDevices("", sinks: true));
        var crlf = Fixture("text/sinks.txt").Replace("\n", "\r\n");
        Assert.Equal(2, PactlParser.ParseTextDevices(crlf, sinks: true).Count);
    }

    [Fact]
    public void Json_SkipsEntriesWithoutName() =>
        Assert.Empty(PactlParser.ParseJsonDevices("[{\"index\":1}]", sinks: true));

    [Fact]
    public void Json_NotAnArray_Throws() =>
        Assert.Throws<FormatException>(() => PactlParser.ParseJsonDevices("{}", sinks: true));
}
