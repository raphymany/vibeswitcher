using System.Runtime.InteropServices;
using System.Text;
using VibeSwitcher.Platform.Linux.Processes;
using VibeSwitcher.Services;

namespace VibeSwitcher.Platform.Linux.Hid;

public enum HeadsetAccess
{
    NoHeadset,       // no supported headset connected
    Accessible,      // VibeSwitcher can talk to it (power on/off detection works)
    NeedsRule,       // connected, but Linux only lets root use it until the permission rule is installed
    NeedsReconnect,  // the rule is installed; unplug and replug the headset (or log in again) to apply it
    TurnedOff,       // the user switched the rule off (an empty file or a link to /dev/null): left alone
}

public enum RuleInstallResult { Installed, Cancelled, Failed, NoPrivilegeTool, TurnedOffByUser }

// Wireless-headset detection talks to the headset's USB dongle directly (hidraw). Linux gives only
// root access to those device files by default, so a udev rule grants the logged-in user access —
// to exactly the supported headset dongles (by USB vendor and product id), never to keyboards, mice
// or anything else, which would let any program read keystrokes. The .deb installs the rule
// (packaging/linux/, generated from RuleText and checked by a test); an AppImage can install it with
// one administrator prompt (pkexec).
public sealed class LinuxHidAccess
{
    public const string RuleFileName = "70-vibeswitcher-headsets.rules"; // before 73-seat-late, which applies "uaccess"
    internal const string InstallDir = "/etc/udev/rules.d";
    // udev's order: a file in an earlier folder replaces a same-named one in a later folder.
    internal static readonly string[] RuleDirs = [InstallDir, "/run/udev/rules.d", "/usr/lib/udev/rules.d", "/lib/udev/rules.d"];
    private const ushort UsbBus = 0x0003;
    private static readonly TimeSpan PromptTimeout = TimeSpan.FromMinutes(3);

    private readonly string _sysfs;
    private readonly Func<string, bool> _canOpen;
    private readonly Func<string, string?> _readFile;
    private readonly IProcessRunner _runner;

    public LinuxHidAccess() : this("/sys", CanReadWrite, ReadFile, ProcessRunner.Instance) { }

    internal LinuxHidAccess(string sysfs, Func<string, bool> canOpen, Func<string, string?> readFile, IProcessRunner runner)
    {
        _sysfs = sysfs;
        _canOpen = canOpen;
        _readFile = readFile;
        _runner = runner;
    }

    public static string RuleText() => RuleText(KnownHidHeadsets.All);

    internal static string RuleText(IEnumerable<HidHeadsetDescriptor> headsets)
    {
        var sb = new StringBuilder();
        sb.Append("# VibeSwitcher: lets the logged-in user talk to supported wireless headset dongles, for\n");
        sb.Append("# headset power on/off detection. Only these exact devices; generated from the app's list.\n");
        foreach (var (vid, pid) in headsets.Select(h => (h.VendorId, h.ProductId)).Distinct().OrderBy(k => k.VendorId).ThenBy(k => k.ProductId))
            sb.Append($"SUBSYSTEM==\"hidraw\", ATTRS{{idVendor}}==\"{vid:x4}\", ATTRS{{idProduct}}==\"{pid:x4}\", TAG+=\"uaccess\"\n");
        return sb.ToString();
    }

    // The current rule is the one udev uses: the first file with its name, in udev's order (an old
    // copy the AppImage left in /etc hides the .deb's up-to-date one in /usr/lib).
    public bool IsRuleInstalled() => EffectiveRule() is { } text && text.ReplaceLineEndings("\n") == RuleText();

    // udev's way of switching a rule off: an empty file (or a link to /dev/null, which reads empty)
    // with its name in an earlier folder. The user did that on purpose; VibeSwitcher won't undo it.
    public bool IsRuleTurnedOff() => EffectiveRule() is { Length: 0 };

    private string? EffectiveRule() => RuleDirs.Select(d => _readFile(d + "/" + RuleFileName)).FirstOrDefault(t => t != null);

    // Whether a connected supported headset needs the rule. /sys/class/hidraw/hidrawN/device/uevent
    // has HID_ID=<bus>:<vendor>:<product> (e.g. 0003:0000046D:00000ABA). Only USB (bus 0003) counts:
    // the rule matches USB dongles, so a headset connected over Bluetooth can't be helped by it.
    public HeadsetAccess Check()
    {
        var known = KnownHidHeadsets.All.Select(h => (h.VendorId, h.ProductId)).ToHashSet();
        bool found = false;
        IEnumerable<string> nodes;
        try { nodes = Directory.EnumerateDirectories(Path.Combine(_sysfs, "class", "hidraw")).ToList(); }
        catch (Exception) { return HeadsetAccess.NoHeadset; }
        foreach (var node in nodes)
        {
            var id = HidId(_readFile(Path.Combine(node, "device", "uevent")));
            if (id is not { Bus: UsbBus } usb || !known.Contains((usb.Vendor, usb.Product))) continue;
            found = true;
            if (_canOpen("/dev/" + Path.GetFileName(node))) return HeadsetAccess.Accessible;
        }
        if (!found) return HeadsetAccess.NoHeadset;
        return IsRuleTurnedOff() ? HeadsetAccess.TurnedOff
            : IsRuleInstalled() ? HeadsetAccess.NeedsReconnect
            : HeadsetAccess.NeedsRule;
    }

    internal static (ushort Bus, ushort Vendor, ushort Product)? HidId(string? uevent)
    {
        if (uevent == null) return null;
        foreach (var line in uevent.Split('\n'))
        {
            if (!line.StartsWith("HID_ID=", StringComparison.Ordinal)) continue;
            var parts = line[7..].Trim().Split(':');
            if (parts.Length == 3
                && ushort.TryParse(parts[0], System.Globalization.NumberStyles.HexNumber, null, out var b)
                && uint.TryParse(parts[1], System.Globalization.NumberStyles.HexNumber, null, out var v) && v <= ushort.MaxValue
                && uint.TryParse(parts[2], System.Globalization.NumberStyles.HexNumber, null, out var p) && p <= ushort.MaxValue)
                return (b, (ushort)v, (ushort)p);
        }
        return null;
    }

    // Installs (or updates) the rule with one administrator prompt, and applies it to connected
    // devices (waiting until udev has, so a Check right after sees the result). Blocks while the
    // prompt is open: call it off the UI thread.
    public RuleInstallResult Install()
    {
        if (IsRuleTurnedOff()) return RuleInstallResult.TurnedOffByUser;
        // The rule goes in as an argument (never through the shell's parser), written with printf to a
        // new file that then replaces the old one — never written through whatever is there (a link).
        // A missing udevadm exits 3, so pkexec's own 126/127 still mean "not authorized"; the final
        // wait for udev is best-effort (the rule is in place either way).
        const string script = """
            command -v udevadm >/dev/null || exit 3
            tmp=$(mktemp "$2.XXXXXX") || exit 1
            if printf '%s' "$1" > "$tmp" && chmod 0644 "$tmp" && mv -f "$tmp" "$2"; then :; else rm -f "$tmp"; exit 1; fi
            udevadm control --reload-rules && udevadm trigger --subsystem-match=hidraw --action=change || exit 1
            udevadm settle --timeout=10 || true
            """;
        return RunPrivileged(["/bin/sh", "-c", script.ReplaceLineEndings("\n"), "vibeswitcher", RuleText(), InstallDir + "/" + RuleFileName]);
    }

    // Removes the rule the AppImage installed (for uninstalling from inside the app) — only if it's
    // VibeSwitcher's, never a user's own file switching it off.
    public RuleInstallResult Remove()
    {
        var path = InstallDir + "/" + RuleFileName;
        if (_readFile(path) is not { } text || !text.StartsWith("# VibeSwitcher:", StringComparison.Ordinal))
            return RuleInstallResult.Installed; // nothing of ours there
        // (udev reloads its rules and applies them again, so a dongle that's plugged in loses access
        // now rather than at the next restart.)
        const string script = """
            rm -f "$1" || exit 1
            if command -v udevadm >/dev/null; then
              udevadm control --reload-rules && udevadm trigger --subsystem-match=hidraw --action=change || true
            fi
            """;
        return RunPrivileged(["/bin/sh", "-c", script.ReplaceLineEndings("\n"), "vibeswitcher", path]);
    }

    private RuleInstallResult RunPrivileged(string[] args)
    {
        try
        {
            var r = _runner.Run("pkexec", args, PromptTimeout);
            // pkexec: 126 = the user dismissed the prompt, 127 = not authorized.
            return r.ExitCode switch
            {
                0 => RuleInstallResult.Installed,
                126 or 127 => RuleInstallResult.Cancelled,
                _ => RuleInstallResult.Failed,
            };
        }
        catch (ToolNotFoundException) { return RuleInstallResult.NoPrivilegeTool; }
        catch (Exception) { return RuleInstallResult.Failed; }
    }

    private static string? ReadFile(string path)
    {
        try { return File.ReadAllText(path); }
        catch (Exception) { return null; }
    }

    private const int R_OK = 4, W_OK = 2;

    [DllImport("libc", SetLastError = true, EntryPoint = "access")]
    private static extern int Access([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int mode);

    private static bool CanReadWrite(string path)
    {
        try { return Access(path, R_OK | W_OK) == 0; }
        catch (Exception) { return false; }
    }
}
