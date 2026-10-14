using System.IO;
using VibeSwitcher.Platform;

namespace VibeSwitcher.App.Settings;

// One "Built with" row on the About page.
public sealed record BuiltWithItem(string Name, string Detail);

// The Settings, About and FAQ texts that name Windows things, in each system's words: "Start with
// Windows" becomes "Open at login" (macOS) / "Start at login" (Linux), the tray is the menu bar on
// macOS, the PC is the computer. Everything else is the Windows app's text, unchanged.
// (Mac is settable so tests can draw both systems' pages on one machine; set it before a window opens.)
public static class Wording
{
    internal static bool Mac { get; set; } = OperatingSystem.IsMacOS();

    // ── Sidebar ──
    public static string TrayNav => Mac ? "Menu Bar" : "Tray";

    // ── Startup ──
    public static string StartAtLogin => Mac ? "Open at login" : "Start at login";
    public static string StartMinimized => Mac ? "Start hidden in the menu bar" : "Start minimized to tray";
    public static string CloseToTray => Mac ? "Close to the menu bar" : "Close to tray";
    public const string CatchUpDetail =
        "If a scheduled switch was missed while the computer was off or asleep, fire it on wake or launch (within 2 hours)";

    // ── Appearance ──
    public const string FollowSystem = "Follow system";

    // ── Notifications ──
    public const string NotificationsDetail =
        "Master switch for notification banners. Per-profile Silent still applies on top when this is on; turning it off also hides device-unavailable warnings.";

    // ── Tray ──
    public static string LeftClickCycles => Mac ? "Left-click menu bar icon cycles profiles" : "Left-click tray icon cycles profiles";
    public static string TrayMenuHeader => Mac ? "MENU ITEMS" : "TRAY MENU ITEMS";
    public static string TrayMenuDetail => Mac
        ? "Choose which items show in the menu bar icon's menu. Settings and Quit are always shown."
        : "Choose which items show in the tray icon's menu. Settings and Quit are always shown.";

    // ── Mini Window ──
    public static string MiniDetail =>
        $"Mini Mode shrinks VibeSwitcher into a small profile switcher you can keep in a corner of your screen. Enter it with the title bar button, the {TrayMenu}, or the shortcut below.";

    // ── Install & Updates ──
    public static string BrowseInstall => Mac ? "Show in Finder" : "Browse install location";

    // The Uninstall card's text, and why the button is off when it is (null: it's on).
    public static string UninstallDetail(InstallKind kind) => kind switch
    {
        InstallKind.MacApp => "Moves VibeSwitcher to the Trash — no password needed. Your profiles and settings are kept unless you choose to delete them too.",
        InstallKind.Package => "Removes the VibeSwitcher package from this computer — you'll be asked for your password. Your profiles and settings are kept unless you choose to delete them too.",
        _ => "Removes VibeSwitcher from this computer — no password needed. Your profiles and settings are kept unless you choose to delete them too.",
    };

    public static string? UninstallBlocked(InstallInfo install) =>
        install.Translocated
            ? "macOS is running this copy from a temporary location. Move VibeSwitcher to your Applications folder first, then uninstall it from there."
            : install.Kind == InstallKind.Development
                ? "You're running a development build — there's nothing to uninstall. To remove it, just delete its build folder."
                : null;

    // ── Supported headsets dialog ──
    public static string SystemName => Mac ? "macOS" : "Linux";
    public static string HeadsetsIntro =>
        $"Auto-switch works for any device — USB, Bluetooth, and more. When your playback device connects, {SystemName} notifies VibeSwitcher and the switch happens automatically.";
    public static string HeadsetsDongles =>
        $"The list below covers wireless headsets whose USB dongle stays plugged in permanently. {SystemName} can't detect when these headsets power on or off, so VibeSwitcher reads the dongle's HID packets directly to catch the exact moment the headset turns on.";

    // ── Icon gallery ── (where the profile icon shows: the taskbar on Windows)
    public static string IconColorHint => Mac
        ? "Black works best on light menu bars · White works best on dark menu bars"
        : "Black works best on light panels · White works best on dark panels";

    // ── About ──
    public static string Platform => Mac ? "macOS 14 or later" : "Linux";

    public static IReadOnlyList<BuiltWithItem> BuiltWith => Mac
        ?
        [
            new("Avalonia / .NET 10", "Cross-platform user interface"),
            new("Core Audio", "Device switching and change events"),
            new("Audio Toolbox", "Test tones, mic meter and switch sounds"),
            new("Carbon", "Global hotkeys"),
            new("HidSharp", "Wireless headset detection"),
            new("Disk image", "Drag-to-Applications installer"),
        ]
        :
        [
            new("Avalonia / .NET 10", "Cross-platform user interface"),
            new("PulseAudio (pactl)", "Device switching on PulseAudio and PipeWire"),
            new("pacat / parec", "Test tones, mic meter and switch sounds"),
            new("Tmds.DBus", "Notifications and Wayland shortcuts"),
            new("HidSharp", "Wireless headset detection"),
            new("AppImage / .deb", "Linux packages"),
        ];

    // ── FAQ ──
    public static string TrayMenu => Mac ? "menu bar menu" : "tray menu";
    public static string AppKind => Mac ? "a macOS menu bar app" : "a Linux system tray app";
    // Where config.json is (Linux: $XDG_CONFIG_HOME when set), with the home folder written as "~".
    public static string ConfigPath => Home(Path.Combine(
        AppPaths.Resolve(Mac, Environment.GetEnvironmentVariable, HomeFolder).ConfigDir, "config.json"), HomeFolder);

    private static string HomeFolder => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    internal static string Home(string path, string home)
    {
        path = path.Replace('\\', '/'); // (Unix paths whatever system the tests run on)
        home = home.Replace('\\', '/').TrimEnd('/');
        return home.Length > 0 && path.StartsWith(home + "/", StringComparison.Ordinal) ? "~" + path[home.Length..] : path;
    }
    public static string LivesIn => Mac
        ? "VibeSwitcher lives in the menu bar. Left-click the icon to cycle to the next profile (or open the window instead — choose in "
        : "VibeSwitcher lives in the system tray. Left-click the icon to cycle to the next profile (or open the window instead — choose in ";
    public static string TrayFaqHeader => Mac ? "MENU BAR ICON BASICS" : "TRAY ICON BASICS";
    public static string HotkeysWorkWhen => Mac
        ? "Hotkeys work globally — even when VibeSwitcher's window is closed. "
        : "Hotkeys work globally — even when VibeSwitcher is minimized to the tray. ";
    public static string TrayLink => $"Settings → {TrayNav}";
    public static string DeviceNamesIntro => Mac
        ? "Device names can be unclear (\"USB Audio CODEC\", \"Display Audio\"). "
        : "Device names can be a mouthful (\"Family 17h/19h HD Audio Controller Analog Stereo\"). ";
    public static string StartupFaq => Mac
        ? " you can have VibeSwitcher open when you log in, start hidden in the menu bar, and keep running in the menu bar when the window is closed — so your hotkeys, schedules, and auto-switching are always active."
        : " you can have VibeSwitcher start when you log in, begin minimized to the tray, and keep running in the tray when the window is closed — so your hotkeys, schedules, and auto-switching are always active.";
}
