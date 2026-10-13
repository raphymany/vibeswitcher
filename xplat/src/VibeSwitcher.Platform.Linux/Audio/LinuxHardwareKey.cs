using System.Text.RegularExpressions;

namespace VibeSwitcher.Platform.Linux.Audio;

// USB details of the device behind an ALSA sound card, read from sysfs.
internal sealed record UsbAudioInfo(string VendorId, string ProductId, int? InterfaceNumber, string? Serial, string? DevPath);

internal interface IUsbSysfs
{
    UsbAudioInfo? ForAlsaCard(int card);
}

// Builds a Windows-shaped hardware instance path for a Linux audio device, e.g.
//   USB\VID_0BDA&PID_4E27&MI_00\<serial or bus path>
// so the linked Windows logic works unchanged: DeviceRebindService normalizes it to
// "USB\VID_0BDA&PID_4E27&MI_00" (dropping the last segment) and only rebinds USB keys, and
// DeviceTriggerService finds a headset by "VID_xxxx&PID_xxxx" inside it.
internal static partial class LinuxHardwareKey
{
    internal static string? InstancePath(PulseDevice device, IUsbSysfs? sysfs)
    {
        var bus = device.Prop("device.bus")?.ToLowerInvariant();
        var busPath = device.Prop("device.bus_path") ?? device.Prop("device.bus-path");

        if (bus == "usb" || bus == null)
        {
            var vid = Hex4(device.Prop("device.vendor.id"));
            var pid = Hex4(device.Prop("device.product.id"));
            int? iface = InterfaceFromBusPath(busPath);
            var serial = device.Prop("device.serial");
            bool fromSysfs = false;

            // PipeWire doesn't always copy the USB ids onto the node; the ALSA card's sysfs entry has them.
            if ((vid == null || pid == null) && sysfs != null && AlsaCard(device) is int card && sysfs.ForAlsaCard(card) is { } usb)
            {
                vid = Hex4(usb.VendorId);
                pid = Hex4(usb.ProductId);
                iface ??= usb.InterfaceNumber;
                serial ??= usb.Serial;
                busPath ??= usb.DevPath;
                fromSysfs = true;
            }

            if (vid != null && pid != null && (bus == "usb" || fromSysfs))
            {
                var mi = iface is int n ? $"&MI_{n:X2}" : "";
                return $@"USB\VID_{vid}&PID_{pid}{mi}\{Instance(serial ?? busPath)}";
            }
            return null;
        }

        if (bus == "bluetooth")
        {
            var mac = device.Prop("api.bluez5.address") ?? device.Prop("device.string");
            return mac != null && MacAddress().IsMatch(mac)
                ? $@"BTHENUM\DEV_{mac.Replace(":", "").ToUpperInvariant()}\0"
                : null;
        }

        if (bus == "pci")
        {
            var ven = Hex4(device.Prop("device.vendor.id"));
            var dev = Hex4(device.Prop("device.product.id"));
            return ven != null && dev != null ? $@"PCI\VEN_{ven}&DEV_{dev}\{Instance(busPath)}" : null;
        }

        return null;
    }

    // "0x0bda" / "0bda" / "bda" → "0BDA"; null when not a 16-bit hex id.
    internal static string? Hex4(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var s = id.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        return s.Length is >= 1 and <= 4 && ushort.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out var v)
            ? v.ToString("X4")
            : null;
    }

    // udev bus paths end with the USB interface: "pci-0000:00:14.0-usb-0:2:1.0" → config 1, interface 0.
    internal static int? InterfaceFromBusPath(string? busPath)
    {
        if (busPath == null) return null;
        var m = UsbInterfaceSuffix().Match(busPath);
        return m.Success && int.TryParse(m.Groups[1].Value, out var n) ? n : null;
    }

    private static int? AlsaCard(PulseDevice d) =>
        int.TryParse(d.Prop("alsa.card") ?? d.Prop("api.alsa.card"), out var c) && c >= 0 ? c : null;

    // The instance segment is dropped by normalization; it only has to be a single path segment.
    private static string Instance(string? s) =>
        string.IsNullOrWhiteSpace(s) ? "0" : s.Replace('\\', '_').Trim().ToUpperInvariant();

    [GeneratedRegex(@"-usb-[\d.:-]*:\d+\.(\d+)$")]
    private static partial Regex UsbInterfaceSuffix();

    [GeneratedRegex("^([0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2}$")]
    private static partial Regex MacAddress();
}

// Real sysfs: /sys/class/sound/cardN is a relative symlink into /sys/devices/…/1-2:1.0/sound/cardN,
// and that directory's `device` is another relative symlink ("../../../1-2:1.0") to the USB
// interface directory, whose parent is the USB device with idVendor/idProduct/serial. Relative
// links resolve against the link's *real* location, so the path is canonicalized one component at
// a time (like realpath) — resolving the text "class/sound/cardN/device" in one go lands nowhere.
internal sealed class UsbSysfs(string root = "/sys") : IUsbSysfs
{
    public UsbAudioInfo? ForAlsaCard(int card)
    {
        try
        {
            var ifaceDir = RealPath(Path.Combine(root, "class", "sound", $"card{card}", "device"));
            if (ifaceDir == null || !Directory.Exists(ifaceDir)) return null;

            var deviceDir = Directory.GetParent(ifaceDir)?.FullName;
            if (deviceDir == null) return null;
            var vid = Read(deviceDir, "idVendor");
            var pid = Read(deviceDir, "idProduct");
            if (vid == null || pid == null) return null; // not a USB sound card

            int? iface = int.TryParse(Read(ifaceDir, "bInterfaceNumber"), System.Globalization.NumberStyles.HexNumber, null, out var n) ? n : null;
            return new UsbAudioInfo(vid, pid, iface, Read(deviceDir, "serial"), Path.GetFileName(deviceDir));
        }
        catch (Exception)
        {
            return null; // sysfs is best-effort
        }
    }

    // Resolves every symlink along an absolute path, each relative target against the real
    // directory that contains the link. Null on a loop or a missing component.
    internal static string? RealPath(string path)
    {
        var pending = new Stack<string>(Split(path).Reverse());
        var resolved = new List<string>();
        int hops = 0;
        while (pending.Count > 0)
        {
            var part = pending.Pop();
            if (part == ".") continue;
            if (part == "..") { if (resolved.Count > 0) resolved.RemoveAt(resolved.Count - 1); continue; }

            var candidate = Join(resolved, part);
            var target = new FileInfo(candidate).LinkTarget;
            if (target == null)
            {
                if (!Path.Exists(candidate)) return null;
                resolved.Add(part);
                continue;
            }
            if (++hops > 40) return null;
            if (Path.IsPathRooted(target)) resolved.Clear();
            foreach (var p in Split(target).Reverse()) pending.Push(p);
        }
        return Join(resolved, null);
    }

    private static IEnumerable<string> Split(string p) =>
        p.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);

    private static string Join(List<string> parts, string? last)
    {
        var all = last == null ? parts : [.. parts, last];
        // Keep the original root form: "/" on Unix, "C:\" on Windows (tests).
        if (OperatingSystem.IsWindows() && all.Count > 0 && all[0].EndsWith(':'))
            return Path.Combine([all[0] + Path.DirectorySeparatorChar, .. all.Skip(1)]);
        return "/" + string.Join('/', all);
    }

    private static string? Read(string dir, string file)
    {
        var path = Path.Combine(dir, file);
        return File.Exists(path) ? File.ReadAllText(path).Trim() is { Length: > 0 } s ? s : null : null;
    }
}
