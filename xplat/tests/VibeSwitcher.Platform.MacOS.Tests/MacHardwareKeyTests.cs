using System.Runtime.InteropServices;
using VibeSwitcher.Audio;
using VibeSwitcher.Platform.MacOS.Audio;
using VibeSwitcher.Platform.MacOS.Interop;
using VibeSwitcher.Services;

namespace VibeSwitcher.Platform.MacOS.Tests;

public class MacHardwareKeyTests
{
    [Theory]
    [InlineData("USB Audio Device:0D8C:0014", 0x0D8C, 0x0014)]
    [InlineData("PRO X Wireless:46D:ABA", 0x046D, 0x0ABA)]
    [InlineData("Name:With:Colons:1B1C:0A51", 0x1B1C, 0x0A51)]
    public void ModelUid_VendorProduct(string model, int vid, int pid) =>
        Assert.Equal(((ushort)vid, (ushort)pid), MacHardwareKey.FromModelUid(model));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("AppleHDAEngineOutput:1B,0,1,2:0")]  // built-in: not hex ids
    [InlineData("BlackHole_ModelUID")]
    [InlineData("Device:0000:0014")]                  // vendor 0 isn't real
    [InlineData("Device:12345:0014")]                 // too long
    public void ModelUid_NotUsbIds(string? model) => Assert.Null(MacHardwareKey.FromModelUid(model));

    [Theory]
    [InlineData("AppleUSBAudioEngine:Generic:USB Audio:14200000:2,1", 0x14200000u)]
    [InlineData("AppleUSBAudioEngine:Logitech:PRO X:00110000:1", 0x00110000u)]
    [InlineData("BuiltInSpeakerDevice", null)]
    [InlineData("AppleUSBAudioEngine:x:y:00000000:1", null)]
    [InlineData("AppleUSBAudioEngine:Apple Inc.:USB-C to 3.5mm:100000:1,2", 0x100000u)]   // Apple Silicon, unpadded
    [InlineData("AppleUSBAudioEngine:Focusrite:Scarlett 2i2 USB:Y8XXHG1234:1,2", null)]  // a serial number, not a location
    [InlineData("AppleUSBAudioEngine:Maker:ABCDEF12 Product:FA130000:1", 0xFA130000u)]   // hex-looking name ignored
    [InlineData("com.vendor.driver:12345678:1", null)]                                  // not Apple's driver
    public void Location_FromUid(string uid, uint? expected) => Assert.Equal(expected, MacHardwareKey.LocationFromUid(uid));

    [Fact]
    public void Usb_PrefersModelUid_ThenIoRegistry()
    {
        var d = FakeCoreAudio.Device(1, "AppleUSBAudioEngine:a:b:14200000:1", "A", transport: FakeCoreAudio.Usb, model: "A:0BDA:4E27");
        int lookups = 0;
        (ushort, ushort)? Lookup(uint _, string? product) { lookups++; Assert.Equal("b", product); return (0x1111, 0x2222); }

        Assert.Equal(@"USB\VID_0BDA&PID_4E27\APPLEUSBAUDIOENGINE:A:B:14200000:1", MacHardwareKey.InstancePath(d, Lookup));
        Assert.Equal(0, lookups);

        var noModel = d with { ModelUid = null };
        Assert.Equal(@"USB\VID_1111&PID_2222", AudioDeviceEnumerator.NormalizeHardwareKey(MacHardwareKey.InstancePath(noModel, Lookup)));
        Assert.Equal(1, lookups);
        Assert.Null(MacHardwareKey.InstancePath(noModel, null));
    }

    [Theory]
    [InlineData("AppleUSBAudioEngine:Generic:USB Audio:14200000:2,1", "USB Audio")]
    [InlineData("AppleUSBAudioEngine:Maker::14200000:1", null)]
    [InlineData("BuiltInSpeakerDevice", null)]
    public void Product_FromUid(string uid, string? expected) => Assert.Equal(expected, MacHardwareKey.ProductFromUid(uid));

    [Fact]
    public void ThirdPartyUsbDriver_NoGuessedKey()
    {
        var d = FakeCoreAudio.Device(1, "com.vendor.driver.uid:1", "Pro Interface", transport: FakeCoreAudio.Usb, model: "com.vendor.driver:2:1");
        Assert.Null(MacHardwareKey.InstancePath(d, (_, _) => (0x1234, 0x5678)));
    }

    [Fact]
    public void NonUsb_NoKey_SoNeverRebound()
    {
        Assert.Null(MacHardwareKey.InstancePath(FakeCoreAudio.Device(1, "BuiltInSpeakerDevice", "S", transport: FakeCoreAudio.BuiltIn, model: "X:0BDA:4E27"), null));
        Assert.Null(MacHardwareKey.InstancePath(FakeCoreAudio.Device(1, "BlackHole2ch_UID", "BH", transport: FakeCoreAudio.Virtual), null));
        var bt = FakeCoreAudio.Device(1, "00-1B-66-AA-BB-CC:output", "AirPods", transport: CoreAudio.TransportBluetooth);
        Assert.Equal(@"BTHENUM\DEV_001B66AABBCC", AudioDeviceEnumerator.NormalizeHardwareKey(MacHardwareKey.InstancePath(bt, null)));
    }
}

// The hand-written native bindings must match the C layouts and constants exactly.
public class NativeBindingTests
{
    [Fact]
    public void FourCC_MatchesTheSdkConstants()
    {
        Assert.Equal(0x64657623u, CoreAudio.HardwareDevices);       // 'dev#'
        Assert.Equal(0x676C6F62u, CoreAudio.ScopeGlobal);           // 'glob'
        Assert.Equal(0x6F757470u, CoreAudio.ScopeOutput);           // 'outp'
        Assert.Equal(0x696E7074u, CoreAudio.ScopeInput);            // 'inpt'
        Assert.Equal(0x644F7574u, CoreAudio.DefaultOutputDevice);   // 'dOut'
        Assert.Equal(0x64496E20u, CoreAudio.DefaultInputDevice);    // 'dIn '
        Assert.Equal(0x75736220u, CoreAudio.TransportUSB);          // 'usb '
        Assert.Equal(0x61716364u, AudioToolbox.QueuePropertyCurrentDevice); // 'aqcd'
        Assert.Equal(0x726E6C70u, CoreAudio.HardwareRunLoop);               // 'rnlp'
        Assert.Equal(0x73666C74u, CoreAudio.CanBeDefaultSystemDevice);      // 'sflt'
        Assert.Equal(0x766F6C6Du, CoreAudio.VolumeScalar);                  // 'volm'
        Assert.Equal(0x6C70636Du, AudioToolbox.FormatLinearPCM);    // 'lpcm'
        Assert.Equal("dev#", FourCC.ToText(CoreAudio.HardwareDevices));
    }

    [Fact]
    public void StructLayouts_MatchC()
    {
        Assert.Equal(12, Marshal.SizeOf<AudioObjectPropertyAddress>());
        Assert.Equal(40, Marshal.SizeOf<AudioStreamBasicDescription>());
        if (IntPtr.Size != 8) return; // macOS is 64-bit only
        Assert.Equal(56, Marshal.SizeOf<AudioQueueBuffer>());
        Assert.Equal(8, Marshal.OffsetOf<AudioQueueBuffer>(nameof(AudioQueueBuffer.AudioData)).ToInt32());
        Assert.Equal(16, Marshal.OffsetOf<AudioQueueBuffer>(nameof(AudioQueueBuffer.AudioDataByteSize)).ToInt32());
        Assert.Equal(16, Marshal.SizeOf<CFRange>());
    }

    [Theory]
    [InlineData(WavSampleFormat.S16, 2, 4u, 16u, 4u | 8u)]
    [InlineData(WavSampleFormat.F32, 1, 4u, 32u, 1u | 8u)]
    [InlineData(WavSampleFormat.S24, 2, 6u, 24u, 4u | 8u)]
    [InlineData(WavSampleFormat.U8, 1, 1u, 8u, 8u)]
    public void AudioFormat_Description(WavSampleFormat format, int channels, uint bytesPerFrame, uint bits, uint flags)
    {
        var d = AudioQueueStreams.Describe(format, 44100, channels);
        Assert.Equal(44100, d.SampleRate);
        Assert.Equal(AudioToolbox.FormatLinearPCM, d.FormatID);
        Assert.Equal((bytesPerFrame, bytesPerFrame, 1u, (uint)channels, bits, flags),
            (d.BytesPerFrame, d.BytesPerPacket, d.FramesPerPacket, d.ChannelsPerFrame, d.BitsPerChannel, d.FormatFlags));
    }
}
