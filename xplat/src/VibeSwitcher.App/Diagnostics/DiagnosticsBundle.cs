using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using VibeSwitcher.App.Hosting;
using VibeSwitcher.Platform;

namespace VibeSwitcher.App.Diagnostics;

// `vibeswitcher --diagnostics [file]`: one zip to send when asking for help —
//   report.txt     the self-test, the audio devices, and what the services said while being checked
//   system.txt     the system, the desktop, the screens, how this copy is installed
//   config.json    the settings (profile names, device names and ids, shortcuts — nothing else)
// Bluetooth addresses inside device ids (Linux names devices after them) are partly masked.
//   error.log …    the app's log and its two older copies
//   screenshots/   the app's windows as this computer draws them (when there's a screen)
internal static class DiagnosticsBundle
{
    // ~/Desktop/VibeSwitcher-diagnostics-<time>.zip (the home folder where there's no Desktop).
    public static string DefaultPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var desktop = Path.Combine(home, "Desktop");
        return Path.Combine(Directory.Exists(desktop) ? desktop : home, $"VibeSwitcher-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
    }

    // screenshots: draws the windows into a folder and returns notes about the screens (null: none).
    // Returns where the zip went: path, or the home folder when that place can't be written (macOS
    // asks before an app from Terminal writes to the Desktop — and may be told no).
    public static async Task<string> CreateAsync(string path, IHostPlatform platform, AppPaths paths,
        Func<string, Task<IReadOnlyList<string>>>? screenshots)
    {
        var work = Directory.CreateTempSubdirectory("vs-diagnostics-").FullName;
        try
        {
            var report = await SelfTest.RunAsync(platform, paths, showBanner: false);
            var text = new StringBuilder(report.Format());
            text.Append("\nAudio devices:\n");
            foreach (var d in report.Devices) text.Append("  ").Append(MaskAddresses(d)).Append('\n');
            text.Append("\nWhat the services said while being checked:\n");
            foreach (var l in report.Log) text.Append("  ").Append(MaskAddresses(l)).Append('\n');
            await File.WriteAllTextAsync(Path.Combine(work, "report.txt"), text.ToString());

            foreach (var name in new[] { "config.json", "error.log", "error.log.1", "error.log.2" })
            {
                var file = Path.Combine(paths.ConfigDir, name);
                if (File.Exists(file)) await File.WriteAllTextAsync(Path.Combine(work, name), MaskAddresses(ReadShared(file)));
            }

            var screens = new List<string>();
            if (screenshots != null)
            {
                var folder = Directory.CreateDirectory(Path.Combine(work, "screenshots")).FullName;
                try { screens.AddRange(await screenshots(folder)); }
                catch (Exception ex) { screens.Add($"Pictures of the windows failed: {ex.Message}"); }
            }
            await File.WriteAllTextAsync(Path.Combine(work, "system.txt"), SystemInfo(screens));

            var zip = Path.Combine(Path.GetTempPath(), $"vs-diagnostics-{Guid.NewGuid():N}.zip");
            ZipFile.CreateFromDirectory(work, zip, CompressionLevel.Optimal, includeBaseDirectory: false);
            try { return Place(zip, path); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                var home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Path.GetFileName(path));
                if (string.Equals(Path.GetFullPath(home), Path.GetFullPath(path), StringComparison.Ordinal)) throw;
                return Place(zip, home);
            }
            finally { try { File.Delete(zip); } catch (Exception) { } }
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch (Exception) { }
        }
    }

    private static string Place(string zip, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.Copy(zip, path, overwrite: true);
        return path;
    }

    // (The running app may be writing the log: read it without locking it.)
    private static string ReadShared(string file)
    {
        using var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(source);
        return reader.ReadToEnd();
    }

    // "bluez_output.AA_BB_CC_DD_EE_FF.1" / "AA:BB:CC:DD:EE:FF" → the first four bytes hidden (the last two
    // still tell one device from another).
    internal static string MaskAddresses(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text, @"\b(?:[0-9A-Fa-f]{2}([:_-])){3}[0-9A-Fa-f]{2}\1([0-9A-Fa-f]{2}\1[0-9A-Fa-f]{2})\b",
            m => $"xx{m.Groups[1].Value}xx{m.Groups[1].Value}xx{m.Groups[1].Value}xx{m.Groups[1].Value}{m.Groups[2].Value}");

    internal static string SystemInfo(IEnumerable<string> screens)
    {
        var install = InstallInfo.Current();
        var text = new StringBuilder();
        text.Append($"VibeSwitcher {Cli.Version} ({install.Kind}, {install.Location}{(install.Translocated ? ", translocated" : "")})\n");
        text.Append($"System: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})\n");
        if (OperatingSystem.IsLinux() && ReadOsRelease() is { } distro) text.Append($"Distribution: {distro}\n");
        text.Append($".NET: {RuntimeInformation.FrameworkDescription}\n");
        text.Append($"Time: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}\n");
        foreach (var name in new[] { "XDG_CURRENT_DESKTOP", "XDG_SESSION_TYPE", "DESKTOP_SESSION", "WAYLAND_DISPLAY", "DISPLAY", "LANG", "LC_ALL", "GDK_SCALE", "QT_SCALE_FACTOR" })
            if (Environment.GetEnvironmentVariable(name) is { Length: > 0 } value) text.Append($"{name}={value}\n");
        foreach (var s in screens) text.Append(s).Append('\n');
        return text.ToString();
    }

    private static string? ReadOsRelease()
    {
        try
        {
            return File.ReadLines("/etc/os-release").FirstOrDefault(l => l.StartsWith("PRETTY_NAME=", StringComparison.Ordinal))?[12..].Trim('"');
        }
        catch (Exception) { return null; }
    }
}
