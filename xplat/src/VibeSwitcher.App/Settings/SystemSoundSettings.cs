using System.Diagnostics;
using System.IO;

namespace VibeSwitcher.App.Settings;

// "Sound Settings" (the Windows app opens Windows' Sound settings): the system's own sound settings
// — System Settings › Sound on macOS; on Linux the desktop's sound settings, or the PulseAudio
// volume control when the desktop has none.
internal static class SystemSoundSettings
{
    public static bool Open(out string error)
    {
        foreach (var (program, args) in Candidates(OperatingSystem.IsMacOS(), Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP")))
        {
            var path = OperatingSystem.IsMacOS() ? program : FindOnPath(program);
            if (path == null) continue;
            try
            {
                var start = new ProcessStartInfo(path) { UseShellExecute = false };
                foreach (var a in args) start.ArgumentList.Add(a);
                using var _ = Process.Start(start);
                error = "";
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
        error = "No sound settings app was found.";
        return false;
    }

    // What to try, in order (the desktop's own settings first).
    internal static IEnumerable<(string Program, string[] Args)> Candidates(bool macOS, string? desktop)
    {
        if (macOS)
        {
            yield return ("/usr/bin/open", ["x-apple.systempreferences:com.apple.Sound-Settings.extension"]);
            yield break;
        }
        var d = (desktop ?? "").ToUpperInvariant();
        if (d.Contains("KDE")) { yield return ("systemsettings", ["kcm_pulseaudio"]); yield return ("systemsettings5", ["kcm_pulseaudio"]); }
        if (d.Contains("CINNAMON")) yield return ("cinnamon-settings", ["sound"]);
        if (d.Contains("MATE")) yield return ("mate-volume-control", []);
        if (d.Contains("GNOME") || d.Contains("UNITY") || d.Contains("BUDGIE")) yield return ("gnome-control-center", ["sound"]);
        yield return ("pavucontrol", []);
        yield return ("gnome-control-center", ["sound"]);
        yield return ("systemsettings", ["kcm_pulseaudio"]);
    }

    private static string? FindOnPath(string program)
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, program);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
