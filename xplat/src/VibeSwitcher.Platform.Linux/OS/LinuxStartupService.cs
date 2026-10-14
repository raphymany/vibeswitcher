using System.Text;
using VibeSwitcher.Helpers;
using VibeSwitcher.Services;

namespace VibeSwitcher.Platform.Linux.OS;

// "Start at login" on Linux: an XDG autostart entry (~/.config/autostart/vibeswitcher.desktop), which
// GNOME, KDE, Cinnamon, XFCE and the others all honour. The Linux counterpart of the Windows
// StartupService (registry Run key), with the same behaviour: the entry runs the app with no
// arguments, and is re-pointed at the app on every launch if the app has moved.
public sealed class LinuxStartupService : IStartupService
{
    private const string FileName = "vibeswitcher.desktop";
    private readonly IAppLogger _logger;
    private readonly ISessionErrorTracker _errorTracker;
    private readonly string _autostartDir;
    private readonly Func<string?> _launchPath;

    public LinuxStartupService(IAppLogger logger, ISessionErrorTracker errorTracker)
        : this(logger, errorTracker, DefaultAutostartDir(), DefaultLaunchPath) { }

    internal LinuxStartupService(IAppLogger logger, ISessionErrorTracker errorTracker, string autostartDir, Func<string?> launchPath)
    {
        _logger = logger;
        _errorTracker = errorTracker;
        _autostartDir = autostartDir;
        _launchPath = launchPath;
    }

    private string EntryPath => Path.Combine(_autostartDir, FileName);

    private static string DefaultAutostartDir()
    {
        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var config = !string.IsNullOrWhiteSpace(xdg) && Path.IsPathRooted(xdg)
            ? xdg
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(config, "autostart");
    }

    // What to run at login: the AppImage file itself (the process path is a temporary mount that
    // changes every launch) when this process really runs from one, otherwise the executable.
    internal static string? DefaultLaunchPath() =>
        Platform.InstallInfo.TrustedAppImage(Environment.GetEnvironmentVariable, Environment.ProcessPath) ?? Environment.ProcessPath;

    public bool IsStartupEnabled()
    {
        try
        {
            if (!File.Exists(EntryPath)) return false;
            // A desktop's own "disable" toggle writes Hidden=true (or the GNOME-specific key) instead of
            // deleting. (Spaces around '=' are allowed by the spec.)
            foreach (var line in File.ReadAllLines(EntryPath))
            {
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                var key = line[..eq].Trim();
                var value = line[(eq + 1)..].Trim();
                if ((key == "Hidden" && value == "true") || (key == "X-GNOME-Autostart-enabled" && value == "false")) return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error("LinuxStartupService.IsStartupEnabled", ex);
            _errorTracker.Record(ErrorCode.StartupRegistryReadFailed, "Start at Login Check Failed",
                $"Could not read the autostart entry: {ex.Message}");
            return false;
        }
    }

    public void Enable()
    {
        try
        {
            var path = _launchPath();
            if (string.IsNullOrEmpty(path))
            {
                _logger.Error("LinuxStartupService.Enable", "Could not resolve executable path");
                _errorTracker.Record(ErrorCode.StartupPathResolutionFailed, "Startup Path Unavailable",
                    "Could not determine the application path — 'Start at login' was not enabled.");
                return;
            }
            if (path.IndexOfAny(['\n', '\r']) >= 0)
            {
                _errorTracker.Record(ErrorCode.StartupPathResolutionFailed, "Startup Path Unavailable",
                    "The application's folder name contains a line break, which an autostart entry can't hold — 'Start at login' was not enabled.");
                return;
            }
            Directory.CreateDirectory(_autostartDir);
            var tmp = EntryPath + ".tmp";
            File.WriteAllText(tmp, Entry(path), new UTF8Encoding(false));
            File.Move(tmp, EntryPath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.Error("LinuxStartupService.Enable", ex);
            _errorTracker.Record(ErrorCode.StartupRegistryFailed, "Start at Login Failed",
                $"Could not enable start at login: {ex.Message}");
        }
    }

    public void Disable()
    {
        try
        {
            if (File.Exists(EntryPath)) File.Delete(EntryPath);
        }
        catch (Exception ex)
        {
            _logger.Error("LinuxStartupService.Disable", ex);
            _errorTracker.Record(ErrorCode.StartupRegistryFailed, "Start at Login Failed",
                $"Could not disable start at login: {ex.Message}");
        }
    }

    // Called on every launch: if the entry exists but runs an old path (the AppImage was moved or
    // updated under a new name), point it at this copy — as the Windows app does for its Run key.
    public void RefreshRegistryPath()
    {
        try
        {
            if (!IsStartupEnabled()) return;
            var path = _launchPath();
            if (string.IsNullOrEmpty(path)) return;
            var expected = "Exec=" + ExecValue(path);
            if (File.ReadAllLines(EntryPath).Any(l => l == expected)) return;
            _logger.Info("LinuxStartupService.RefreshRegistryPath", "Autostart entry outdated; updating to the current app location.");
            Enable();
        }
        catch (Exception ex)
        {
            _logger.Warning("LinuxStartupService.RefreshRegistryPath", ex.Message);
            _errorTracker.Record(ErrorCode.StartupRegistryFailed, "Start at Login Failed",
                $"Could not update the autostart entry: {ex.Message}");
        }
    }

    // (Normalised to "\n": the source may be checked out with Windows line endings, and the raw
    // string would carry them into the file.)
    internal static string Entry(string path) =>
        $"""
        [Desktop Entry]
        Type=Application
        Name=VibeSwitcher
        Comment=Switch audio devices with profiles and hotkeys
        Exec={ExecValue(path)}
        Icon={LinuxAppIdentity.AppId}
        Terminal=false
        X-GNOME-Autostart-enabled=true

        """.ReplaceLineEndings("\n");

    // The desktop-entry Exec rules: an argument with reserved characters is double-quoted, with
    // " ` $ \ backslash-escaped inside the quotes; then, because the value is also a desktop-file
    // string, every backslash is doubled again; and a literal % is written %%.
    internal static string ExecValue(string path)
    {
        const string reserved = " \t\n\"'\\><~|&;$*?#()`";
        string arg = path.Any(c => reserved.Contains(c))
            ? "\"" + new StringBuilder(path).Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("`", "\\`").Replace("$", "\\$") + "\""
            : path;
        return arg.Replace("\\", "\\\\").Replace("%", "%%");
    }
}
