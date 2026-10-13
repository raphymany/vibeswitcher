using System.Globalization;
using System.Text.RegularExpressions;
using VibeSwitcher.Platform.MacOS.Interop;

namespace VibeSwitcher.Platform.MacOS.Audio;

// Builds a Windows-shaped hardware instance path for a macOS audio device, e.g.
//   USB\VID_0BDA&PID_4E27\<device uid>
// so the linked Windows logic works unchanged: DeviceRebindService normalizes it to
// "USB\VID_0BDA&PID_4E27" and only rebinds USB keys; DeviceTriggerService finds a headset by
// "VID_xxxx&PID_xxxx" inside it.
//
// Apple's USB audio driver names devices "AppleUSBAudioEngine:<maker>:<product>:<serial or location>:<interfaces>"
// and reports a model UID of "<product>:<VID>:<PID>" (hex). Without a serial number, the 4th field
// is the USB location id of the port — so the same headset in another port gets a new UID, exactly
// the case re-binding exists for. USB ids come from the model UID, or failing that from the IO
// registry entry at that location. Devices from other (third-party) drivers have no reliable ids
// in their UIDs, so they get no key rather than a guessed one.
internal static partial class MacHardwareKey
{
    private const string AppleUsbPrefix = "AppleUSBAudioEngine:";

    internal static string? InstancePath(MacDevice d, Func<uint, string?, (ushort Vendor, ushort Product)?>? usbLookup)
    {
        if (d.TransportType == CoreAudio.TransportUSB)
        {
            if (!d.Uid.StartsWith(AppleUsbPrefix, StringComparison.Ordinal)) return null;
            var ids = FromModelUid(d.ModelUid);
            if (ids == null && usbLookup != null && LocationFromUid(d.Uid) is uint location)
                ids = usbLookup(location, ProductFromUid(d.Uid));
            return ids is var (vid, pid) ? $@"USB\VID_{vid:X4}&PID_{pid:X4}\{Instance(d.Uid)}" : null;
        }
        if (d.TransportType == CoreAudio.TransportBluetooth || d.TransportType == CoreAudio.TransportBluetoothLE)
        {
            // Bluetooth device UIDs start with the address: "00-1B-66-AA-BB-CC:output".
            var m = BluetoothAddress().Match(d.Uid);
            return m.Success ? $@"BTHENUM\DEV_{m.Value.Replace("-", "").Replace(":", "").ToUpperInvariant()}\0" : null;
        }
        return null;
    }

    // "USB Audio Device:0D8C:0014" → (0x0D8C, 0x0014): the last two ':'-separated hex fields.
    internal static (ushort Vendor, ushort Product)? FromModelUid(string? modelUid)
    {
        if (string.IsNullOrWhiteSpace(modelUid)) return null;
        var parts = modelUid.Split(':');
        if (parts.Length < 3) return null;
        return Hex16(parts[^2]) is ushort v && Hex16(parts[^1]) is ushort p && v != 0 ? (v, p) : null;
    }

    // The location id in an Apple USB audio UID's 4th field: "AppleUSBAudioEngine:Generic:USB Audio:14200000:2,1"
    // → 0x14200000. Apple Silicon locations may print without leading zeros ("100000"), so 5–8 hex
    // digits count; a serial number in that field (letters beyond F, or other lengths) doesn't.
    // Read from the back: location and interfaces are always the last two fields, so a colon inside
    // the product name can't shift them.
    internal static uint? LocationFromUid(string uid)
    {
        if (!uid.StartsWith(AppleUsbPrefix, StringComparison.Ordinal)) return null;
        var parts = uid.Split(':');
        if (parts.Length < 5) return null;
        var field = parts[^2];
        return field.Length is >= 5 and <= 8
            && uint.TryParse(field, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var loc) && loc != 0
            ? loc
            : null;
    }

    // The product name: everything between the maker (2nd field) and the last two fields.
    internal static string? ProductFromUid(string uid)
    {
        if (!uid.StartsWith(AppleUsbPrefix, StringComparison.Ordinal)) return null;
        var parts = uid.Split(':');
        if (parts.Length < 5) return null;
        var product = string.Join(':', parts[2..^2]);
        return product.Trim().Length > 0 ? product : null;
    }

    private static ushort? Hex16(string s) =>
        s.Length is >= 1 and <= 4 && ushort.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? v : null;

    // The instance segment is dropped by normalization; it only has to be one path segment.
    private static string Instance(string uid) => uid.Replace('\\', '_').ToUpperInvariant();

    [GeneratedRegex("^([0-9A-Fa-f]{2}[-:]){5}[0-9A-Fa-f]{2}")]
    private static partial Regex BluetoothAddress();
}
