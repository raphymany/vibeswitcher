using VibeSwitcher.Platform.Linux.Audio;
using VibeSwitcher.Services;

namespace VibeSwitcher.Platform.Linux.Tests;

public class LinuxHardwareKeyTests
{
    private sealed class FakeSysfs(Dictionary<int, UsbAudioInfo> cards) : IUsbSysfs
    {
        public int Lookups;
        public UsbAudioInfo? ForAlsaCard(int card) { Lookups++; return cards.GetValueOrDefault(card); }
    }

    private static PulseDevice Dev(params (string Key, string Value)[] props) => new(
        0, "dev", "Dev", true, false, null, [], null, props.ToDictionary(p => p.Key, p => p.Value));

    [Fact]
    public void PulseAudioUsb_WindowsShapedPath_NormalizesLikeWindows()
    {
        var d = PactlParser.ParseJsonDevices(PactlParserTests.Fixture("pulseaudio-16/sinks.json"), sinks: true)[0];

        var path = LinuxHardwareKey.InstancePath(d, sysfs: null);

        Assert.Equal(@"USB\VID_0BDA&PID_4E27&MI_00\GENERIC_USB_AUDIO", path);
        // The same normalization the Windows app applies (and that DeviceRebindService compares).
        Assert.Equal(@"USB\VID_0BDA&PID_4E27&MI_00", AudioDeviceEnumerator.NormalizeHardwareKey(path));
    }

    [Fact]
    public void PipeWireUsb_0xIdsAndHyphenatedBusPath()
    {
        var d = PactlParser.ParseJsonDevices(PactlParserTests.Fixture("pipewire/sinks.json"), sinks: true)
            .Single(s => s.Name.Contains("usb"));
        Assert.Equal(@"USB\VID_0BDA&PID_4E27&MI_00\GENERIC_USB_AUDIO", LinuxHardwareKey.InstancePath(d, null));
    }

    [Fact]
    public void UsbHeadset_PathContainsVidPid_ForHeadsetDetection()
    {
        // DeviceTriggerService finds a HID headset's audio device by "VID_xxxx&PID_xxxx" inside the path.
        var d = PactlParser.ParseTextDevices(PactlParserTests.Fixture("text/sinks.txt"), sinks: true)[1];
        var path = LinuxHardwareKey.InstancePath(d, null)!;
        Assert.Contains("VID_046D&PID_0ABA", path);
    }

    [Fact]
    public void NoSerial_UsesBusPathAsInstance()
    {
        var d = Dev(("device.bus", "usb"), ("device.vendor.id", "0d8c"), ("device.product.id", "0014"),
            ("device.bus_path", "pci-0000:00:14.0-usb-0:3.2:1.0"));
        Assert.Equal(@"USB\VID_0D8C&PID_0014&MI_00\PCI-0000:00:14.0-USB-0:3.2:1.0", LinuxHardwareKey.InstancePath(d, null));
    }

    [Fact]
    public void MissingUsbIds_FallsBackToSysfsByAlsaCard()
    {
        var sysfs = new FakeSysfs(new() { [2] = new UsbAudioInfo("1b1c", "0a51", 3, null, "1-2") });
        var d = Dev(("api.alsa.card", "2"));

        Assert.Equal(@"USB\VID_1B1C&PID_0A51&MI_03\1-2", LinuxHardwareKey.InstancePath(d, sysfs));
    }

    [Fact]
    public void NoBus_NoSysfsMatch_IsNull()
    {
        var sysfs = new FakeSysfs(new());
        Assert.Null(LinuxHardwareKey.InstancePath(Dev(("alsa.card", "0")), sysfs));
        Assert.Null(LinuxHardwareKey.InstancePath(Dev(), sysfs));
        // Ids without a bus aren't trusted to be USB.
        Assert.Null(LinuxHardwareKey.InstancePath(Dev(("device.vendor.id", "0bda"), ("device.product.id", "4e27")), null));
    }

    [Fact]
    public void UsbBusWithIds_DoesNotTouchSysfs()
    {
        var sysfs = new FakeSysfs(new());
        LinuxHardwareKey.InstancePath(Dev(("device.bus", "usb"), ("device.vendor.id", "0bda"), ("device.product.id", "4e27"), ("alsa.card", "1")), sysfs);
        Assert.Equal(0, sysfs.Lookups);
    }

    [Fact]
    public void Bluetooth_IsNotUsb_SoNeverRebound()
    {
        var bt = PactlParser.ParseJsonDevices(PactlParserTests.Fixture("pipewire/sinks.json"), sinks: true).Single(s => s.Name.StartsWith("bluez"));
        var path = LinuxHardwareKey.InstancePath(bt, null);
        Assert.Equal(@"BTHENUM\DEV_001B66AABBCC\0", path);
        Assert.StartsWith(@"BTHENUM\", AudioDeviceEnumerator.NormalizeHardwareKey(path));
    }

    [Fact]
    public void Pci_BuiltInAudio()
    {
        var pci = PactlParser.ParseTextDevices(PactlParserTests.Fixture("text/sinks.txt"), sinks: true)[0];
        Assert.Equal(@"PCI\VEN_8086&DEV_A0C8", AudioDeviceEnumerator.NormalizeHardwareKey(LinuxHardwareKey.InstancePath(pci, null)));
    }

    [Theory]
    [InlineData("0x0bda", "0BDA")]
    [InlineData("0BDA", "0BDA")]
    [InlineData("bda", "0BDA")]
    [InlineData("12345", null)]
    [InlineData("zz", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Hex4(string? input, string? expected) => Assert.Equal(expected, LinuxHardwareKey.Hex4(input));

    [Theory]
    [InlineData("pci-0000:00:14.0-usb-0:2:1.0", 0)]
    [InlineData("pci-0000:00:14.0-usb-0:3.2:1.3", 3)]
    [InlineData("pci-0000:00:14.0-usb-0:1:1.12", 12)]
    [InlineData("pci-0000:00:1f.3", null)]
    [InlineData(null, null)]
    public void InterfaceFromBusPath(string? busPath, int? expected) =>
        Assert.Equal(expected, LinuxHardwareKey.InterfaceFromBusPath(busPath));

    [Fact]
    public void RealSysfs_ReadsUsbCard_ThroughRelativeLinks()
    {
        // sysfs is Linux-only and its names contain ':' — this runs on the Linux and macOS CI runners.
        if (OperatingSystem.IsWindows()) return;

        // The real layout, with the same relative links the kernel creates:
        //   class/sound/card4        -> ../../devices/usb1/1-2/1-2:1.0/sound/card4
        //   …/sound/card4/device     -> ../../../1-2:1.0
        var root = Directory.CreateTempSubdirectory("vs-sysfs").FullName;
        try
        {
            var deviceDir = Path.Combine(root, "devices", "usb1", "1-2");
            var ifaceDir = Path.Combine(deviceDir, "1-2:1.0");
            var realCard = Path.Combine(ifaceDir, "sound", "card4");
            Directory.CreateDirectory(realCard);
            File.WriteAllText(Path.Combine(deviceDir, "idVendor"), "0bda\n");
            File.WriteAllText(Path.Combine(deviceDir, "idProduct"), "4e27\n");
            File.WriteAllText(Path.Combine(deviceDir, "serial"), "ABC123\n");
            File.WriteAllText(Path.Combine(ifaceDir, "bInterfaceNumber"), "0a\n");
            File.CreateSymbolicLink(Path.Combine(realCard, "device"), "../../../1-2:1.0");
            Directory.CreateDirectory(Path.Combine(root, "class", "sound"));
            File.CreateSymbolicLink(Path.Combine(root, "class", "sound", "card4"), "../../devices/usb1/1-2/1-2:1.0/sound/card4");

            // A PCI card: device link to a non-USB parent (no idVendor) → not USB.
            var pciCard = Path.Combine(root, "devices", "pci0000:00", "0000:00:1f.3", "sound", "card0");
            Directory.CreateDirectory(pciCard);
            File.CreateSymbolicLink(Path.Combine(pciCard, "device"), "../../../0000:00:1f.3");
            File.CreateSymbolicLink(Path.Combine(root, "class", "sound", "card0"), "../../devices/pci0000:00/0000:00:1f.3/sound/card0");

            var info = new UsbSysfs(root).ForAlsaCard(4);

            Assert.NotNull(info);
            Assert.Equal("0bda", info!.VendorId);
            Assert.Equal("4e27", info.ProductId);
            Assert.Equal(10, info.InterfaceNumber);
            Assert.Equal("ABC123", info.Serial);
            Assert.Equal("1-2", info.DevPath);
            Assert.Null(new UsbSysfs(root).ForAlsaCard(9)); // no such card
            Assert.Null(new UsbSysfs(root).ForAlsaCard(0)); // PCI card

            // And end to end: a PipeWire node with only its ALSA card number gets a USB key.
            var node = new PulseDevice(0, "n", "N", true, false, null, [], null,
                new Dictionary<string, string> { ["api.alsa.card"] = "4" });
            Assert.Equal(@"USB\VID_0BDA&PID_4E27&MI_0A\ABC123", LinuxHardwareKey.InstancePath(node, new UsbSysfs(root)));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void RealPath_ResolvesChainsAndRejectsLoops()
    {
        if (OperatingSystem.IsWindows()) return;
        var root = Directory.CreateTempSubdirectory("vs-realpath").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "a", "b"));
            File.CreateSymbolicLink(Path.Combine(root, "l1"), "a/b");
            File.CreateSymbolicLink(Path.Combine(root, "l2"), "l1");
            File.CreateSymbolicLink(Path.Combine(root, "loop1"), "loop2");
            File.CreateSymbolicLink(Path.Combine(root, "loop2"), "loop1");

            var real = UsbSysfs.RealPath(root)!; // the temp folder may itself sit behind a link (macOS: /var → /private/var)
            Assert.Equal(Path.Combine(real, "a", "b"), UsbSysfs.RealPath(Path.Combine(root, "l2")));
            Assert.Equal(Path.Combine(real, "a"), UsbSysfs.RealPath(Path.Combine(root, "l2", "..")));
            Assert.Null(UsbSysfs.RealPath(Path.Combine(root, "loop1")));
            Assert.Null(UsbSysfs.RealPath(Path.Combine(root, "missing", "x")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
