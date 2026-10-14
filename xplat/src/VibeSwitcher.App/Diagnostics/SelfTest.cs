using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using VibeSwitcher.App.Dialogs;
using VibeSwitcher.App.Hosting;
using VibeSwitcher.Helpers;
using VibeSwitcher.Platform;
using VibeSwitcher.Platform.Linux.Hid;
using VibeSwitcher.Services;

namespace VibeSwitcher.App.Diagnostics;

internal enum CheckStatus { Pass, Info, Warn, Fail }

internal sealed record Check(string Name, CheckStatus Status, string Detail);

// What `vibeswitcher --selftest` found: one line per thing the app relies on.
internal sealed class SelfTestReport
{
    public string Header { get; init; } = "";
    public List<Check> Checks { get; } = [];
    // The audio devices, one per line (for the diagnostics zip).
    public List<string> Devices { get; } = [];
    // What the services logged while being checked.
    public List<string> Log { get; } = [];

    public int Problems => Checks.Count(c => c.Status == CheckStatus.Fail);
    public int Warnings => Checks.Count(c => c.Status == CheckStatus.Warn);
    public int ExitCode => Problems > 0 ? 1 : 0;

    public string Format()
    {
        var text = new StringBuilder(Header).Append('\n').Append('\n');
        foreach (var c in Checks)
        {
            var mark = c.Status switch { CheckStatus.Pass => "ok", CheckStatus.Info => "--", CheckStatus.Warn => "warn", _ => "FAIL" };
            text.Append($"  {mark,-5} {c.Name,-20} {c.Detail}\n");
        }
        text.Append('\n').Append(Problems == 0 && Warnings == 0 ? "Everything VibeSwitcher needs is here."
            : $"{Problems} problem{(Problems == 1 ? "" : "s")}, {Warnings} warning{(Warnings == 1 ? "" : "s")}.").Append('\n');
        return text.ToString();
    }
}

// The self-test: each service the app uses, checked the way the app uses it (nothing is changed —
// one test banner is shown, unless asked not to). Runs beside a running VibeSwitcher.
internal static class SelfTest
{
    // A test shortcut no one uses (Ctrl+Alt+Shift+F12), registered for a moment and let go.
    private const ushort ProbeId = 0x7F00;
    private const int ProbeModifiers = 0x1 | 0x2 | 0x4; // MOD_ALT | MOD_CONTROL | MOD_SHIFT
    private const int ProbeKey = 0x7B; // F12

    public static async Task<SelfTestReport> RunAsync(IHostPlatform platform, AppPaths paths, bool showBanner = true)
    {
        var install = InstallInfo.Current();
        var report = new SelfTestReport
        {
            Header = $"VibeSwitcher {Cli.Version} self-test — {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}), "
                + $"{install.Kind} at {install.Location}, {DateTime.Now:yyyy-MM-dd HH:mm}",
        };
        var logger = new CollectingLogger(report.Log);
        var errors = new SessionErrorTracker();
        var checks = report.Checks;

        Add(checks, "Settings folder", () =>
        {
            Directory.CreateDirectory(paths.ConfigDir);
            var probe = Path.Combine(paths.ConfigDir, $".selftest-{Environment.ProcessId}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return (CheckStatus.Pass, $"{paths.ConfigDir} (writable)");
        });

        Add(checks, "Settings file", () =>
        {
            var file = Path.Combine(paths.ConfigDir, "config.json");
            if (!File.Exists(file)) return (CheckStatus.Info, "none yet (made on the first start)");
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var profiles = doc.RootElement.TryGetProperty("Profiles", out var list) && list.ValueKind == JsonValueKind.Array ? list.GetArrayLength() : 0;
            return (CheckStatus.Pass, $"readable, {profiles} profile{(profiles == 1 ? "" : "s")}");
        });

        if (OperatingSystem.IsLinux())
            checks.Add(new Check("Desktop", CheckStatus.Info,
                $"{Env("XDG_CURRENT_DESKTOP") ?? "unknown"}, {Env("XDG_SESSION_TYPE") ?? (Env("WAYLAND_DISPLAY") != null ? "wayland" : Env("DISPLAY") != null ? "x11" : "no display")}"));

        IPlatformAudioService? audio = null;
        Add(checks, "Audio devices", () =>
        {
            audio = platform.CreateAudio(logger, errors);
            var playback = audio.GetPlaybackDevices();
            var recording = audio.GetRecordingDevices();
            foreach (var d in playback.Concat(recording))
                report.Devices.Add($"{(d.IsPlayback ? "playback " : "recording")}  {d.FriendlyName}  [{d.Id}]{(d.IsConnected ? "" : " (unplugged)")}{(d.IsDisabled ? " (disabled)" : "")}  key={d.HardwareKey ?? "-"}");
            if (playback.Count == 0 && recording.Count == 0)
                return (CheckStatus.Fail, Message(report.Log.LastOrDefault(l => l.StartsWith("WARN") || l.StartsWith("ERROR"))) ?? "no devices found");
            return (CheckStatus.Pass, $"{playback.Count} playback, {recording.Count} recording");
        });
        if (audio != null)
            Add(checks, "Mute", () => audio.GetDefaultDeviceMute(playback: true) is { } muted
                ? (CheckStatus.Pass, $"readable (speakers {(muted ? "muted" : "on")})")
                : (CheckStatus.Warn, "can't read the default device's mute"));
        audio?.Dispose();

        checks.Add(OperatingSystem.IsLinux()
            ? Settings.SystemSoundSettings.FindOnPath("pacat") != null
                ? new Check("Sounds", CheckStatus.Pass, "pacat (switch sounds, test tone)")
                : new Check("Sounds", CheckStatus.Fail, "pacat is missing — install the 'pulseaudio-utils' package")
            : new Check("Sounds", CheckStatus.Pass, "Audio Toolbox"));

        await AddAsync(checks, "Global shortcuts", async () =>
        {
            var before = report.Log.Count;
            using var grabber = await platform.CreateKeyGrabberAsync(logger, lookOnly: true);
            // (Linux logs which way it set shortcuts up: X11, the portal, GNOME's, or none and why.)
            var how = report.Log.Skip(before)
                .Select(l => l.IndexOf("Global shortcuts: ", StringComparison.Ordinal) is var i and >= 0 ? l[(i + 18)..] : null)
                .LastOrDefault(s => s != null);
            // (The desktop's shortcut service would ask the user about a test shortcut: not tried.)
            if (grabber.GetType().Name == "PortalKeyGrabber")
                return (CheckStatus.Pass, "the desktop's shortcut service (approve VibeSwitcher's shortcuts when it asks)");
            var result = grabber.Register(ProbeId, ProbeModifiers, ProbeKey, out var error);
            grabber.Unregister(ProbeId);
            return result switch
            {
                GrabResult.Ok => (CheckStatus.Pass, how ?? "work"),
                GrabResult.InUse => (CheckStatus.Pass, $"{how ?? "work"} (the test key is taken by another app)"),
                GrabResult.Unsupported => (CheckStatus.Warn, error ?? how ?? "not available on this desktop"),
                _ => (CheckStatus.Fail, error ?? "couldn't set a shortcut"),
            };
        });

        await AddAsync(checks, "Banners", async () =>
        {
            var notifier = platform.CreateNotifier(logger);
            if (notifier == null) return (CheckStatus.Warn, "none on this system");
            if (notifier is BannerNotifier) return (CheckStatus.Pass, "drawn by the app");
            try
            {
                if (!showBanner) return (CheckStatus.Info, "not tried");
                return await notifier.ShowAsync("VibeSwitcher self-test", "If you can see this, notification banners work.")
                    ? (CheckStatus.Pass, "a test banner was sent — check it appeared")
                    // (Banners are a nicety: everything else works without them.)
                    : (CheckStatus.Warn, Message(report.Log.LastOrDefault()) ?? "the desktop's notification service didn't answer");
            }
            finally { (notifier as IDisposable)?.Dispose(); }
        });

        await AddAsync(checks, OperatingSystem.IsMacOS() ? "Menu bar" : "Tray", async () =>
        {
            if (OperatingSystem.IsMacOS()) return (CheckStatus.Pass, "the app's own menu-bar item");
            return await platform.IsTrayAvailableAsync() switch
            {
                true => (CheckStatus.Pass, "the desktop shows tray icons"),
                false => (CheckStatus.Warn, "this desktop shows no tray icons (GNOME: install the AppIndicator extension) — closing Settings quits"),
                null => (CheckStatus.Warn, "couldn't tell (no desktop session bus)"),
            };
        });

        Add(checks, "Start at login", () => (CheckStatus.Info, platform.CreateStartup(logger, errors).IsStartupEnabled() ? "on" : "off"));

        Add(checks, "Wireless headsets", () =>
        {
            if (platform.HeadsetAccess() is not { } access) return (CheckStatus.Info, "no permission needed");
            return access.Check() switch
            {
                HeadsetAccess.NoHeadset => (CheckStatus.Info, "no supported receiver connected"),
                HeadsetAccess.Accessible => (CheckStatus.Pass, "a supported receiver is connected and readable"),
                HeadsetAccess.NeedsRule => (CheckStatus.Warn, "a supported receiver is connected but needs the permission rule (offered when auto-switch is on)"),
                HeadsetAccess.NeedsReconnect => (CheckStatus.Warn, "the permission rule is in place — unplug and replug the receiver"),
                _ => (CheckStatus.Info, "headset access is turned off on this computer"),
            };
        });

        Add(checks, "Apps (for triggers)", () =>
        {
            if (platform.CreateProcesses().Snapshot() is not { } running) return (CheckStatus.Fail, "the process list can't be read");
            var catalog = PlatformApps.Catalog();
            var installed = catalog?.Installed().Count ?? 0;
            return (CheckStatus.Pass, $"{installed} installed apps listed, {running.Count} processes running");
        });

        return report;
    }

    // A logged line without its "WARN Context: " start.
    internal static string? Message(string? logLine)
    {
        if (logLine == null) return null;
        var parts = logLine.Split(' ', 2); // "WARN", "Context: message"
        return parts.Length == 2 && parts[0] is "INFO" or "WARN" or "ERROR" && parts[1].IndexOf(": ", StringComparison.Ordinal) is var colon and > 0
            ? parts[1][(colon + 2)..]
            : logLine;
    }

    private static string? Env(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : null;

    private static void Add(List<Check> checks, string name, Func<(CheckStatus, string)> check)
    {
        try
        {
            var (status, detail) = check();
            checks.Add(new Check(name, status, detail));
        }
        catch (Exception ex) { checks.Add(new Check(name, CheckStatus.Fail, ex.Message)); }
    }

    private static async Task AddAsync(List<Check> checks, string name, Func<Task<(CheckStatus, string)>> check)
    {
        try
        {
            var (status, detail) = await check();
            checks.Add(new Check(name, status, detail));
        }
        catch (Exception ex) { checks.Add(new Check(name, CheckStatus.Fail, ex.Message)); }
    }

    // The services' own messages, kept for the report (the app's log is the running app's).
    private sealed class CollectingLogger(List<string> lines) : IAppLogger
    {
        private void Add(string level, string context, string message) { lock (lines) lines.Add($"{level} {context}: {message}"); }
        public void Info(string context, string message) => Add("INFO", context, message);
        public void Warning(string context, string message) => Add("WARN", context, message);
        public void Error(string context, string message) => Add("ERROR", context, message);
        public void Error(string context, Exception ex) => Add("ERROR", context, ex.Message);
        public void Debug(string context, string message) { }
    }
}
