using System.Security;
using System.Text;
using VibeSwitcher.Helpers;
using VibeSwitcher.Services;

namespace VibeSwitcher.Platform.MacOS.OS;

// "Start at login" on macOS: a per-user LaunchAgent (~/Library/LaunchAgents/<label>.plist) that opens
// the app when the user logs in. The macOS counterpart of the Windows StartupService (registry Run
// key), with the same behaviour: the app starts with no arguments, and the agent is re-pointed at
// the app on every launch if it has moved.
//
// macOS 13+ lists it under System Settings › General › Login Items › "Allow in the Background"
// (with a one-time "Background Items Added" notice), named after the app thanks to
// AssociatedBundleIdentifiers. If the user switches it off there, macOS ignores the agent even though
// the file exists — VibeSwitcher can't see that switch without the newer Service Management API.
public sealed class MacStartupService : IStartupService
{
    internal const string BundleId = "com.raphymany.vibeswitcher";
    internal const string Label = BundleId + ".login";
    private readonly IAppLogger _logger;
    private readonly ISessionErrorTracker _errorTracker;
    private readonly string _agentsDir;
    private readonly Func<string?> _launchPath;

    public MacStartupService(IAppLogger logger, ISessionErrorTracker errorTracker)
        : this(logger, errorTracker,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents"),
            () => Environment.ProcessPath) { }

    internal MacStartupService(IAppLogger logger, ISessionErrorTracker errorTracker, string agentsDir, Func<string?> launchPath)
    {
        _logger = logger;
        _errorTracker = errorTracker;
        _agentsDir = agentsDir;
        _launchPath = launchPath;
    }

    private string AgentPath => Path.Combine(_agentsDir, Label + ".plist");

    public bool IsStartupEnabled()
    {
        try
        {
            return File.Exists(AgentPath);
        }
        catch (Exception ex)
        {
            _logger.Error("MacStartupService.IsStartupEnabled", ex);
            _errorTracker.Record(ErrorCode.StartupRegistryReadFailed, "Start at Login Check Failed",
                $"Could not read the login item: {ex.Message}");
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
                _logger.Error("MacStartupService.Enable", "Could not resolve executable path");
                _errorTracker.Record(ErrorCode.StartupPathResolutionFailed, "Startup Path Unavailable",
                    "Could not determine the application path — 'Start at login' was not enabled.");
                return;
            }
            if (IsTranslocated(path))
            {
                _errorTracker.Record(ErrorCode.StartupPathResolutionFailed, "Move VibeSwitcher to Applications",
                    "macOS is running VibeSwitcher from a temporary copy because it hasn't been moved out of the folder " +
                    "it was downloaded to. Drag VibeSwitcher into Applications, open it from there, then turn on 'Start at login'.");
                return;
            }
            Write(path);
        }
        catch (Exception ex)
        {
            _logger.Error("MacStartupService.Enable", ex);
            _errorTracker.Record(ErrorCode.StartupRegistryFailed, "Start at Login Failed",
                $"Could not enable start at login: {ex.Message}");
        }
    }

    private void Write(string path)
    {
        Directory.CreateDirectory(_agentsDir);
        var tmp = AgentPath + ".tmp";
        File.WriteAllText(tmp, Plist(path), new UTF8Encoding(false));
        // launchd refuses agents that others can write to ("bad permissions").
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        File.Move(tmp, AgentPath, overwrite: true);
    }

    public void Disable()
    {
        try
        {
            if (File.Exists(AgentPath)) File.Delete(AgentPath);
        }
        catch (Exception ex)
        {
            _logger.Error("MacStartupService.Disable", ex);
            _errorTracker.Record(ErrorCode.StartupRegistryFailed, "Start at Login Failed",
                $"Could not disable start at login: {ex.Message}");
        }
    }

    public void RefreshRegistryPath()
    {
        try
        {
            if (!File.Exists(AgentPath)) return;
            var path = _launchPath();
            // A temporary (translocated) copy must never replace a good path.
            if (string.IsNullOrEmpty(path) || IsTranslocated(path)) return;
            if (File.ReadAllText(AgentPath) == Plist(path)) return;
            _logger.Info("MacStartupService.RefreshRegistryPath", "Login item outdated; updating to the current app location.");
            Write(path);
        }
        catch (Exception ex)
        {
            _logger.Warning("MacStartupService.RefreshRegistryPath", ex.Message);
            _errorTracker.Record(ErrorCode.StartupRegistryFailed, "Start at Login Failed",
                $"Could not update the login item: {ex.Message}");
        }
    }

    internal static bool IsTranslocated(string path) => path.Contains("/AppTranslocation/", StringComparison.Ordinal);

    // An app bundle is opened through Launch Services (`open -a <bundle>`), the way the Dock or
    // Finder start it; a bare executable (development builds) is run directly.
    internal static string[] ProgramArguments(string path)
    {
        int i = path.IndexOf(".app/Contents/MacOS/", StringComparison.Ordinal);
        return i > 0 ? ["/usr/bin/open", "-a", path[..(i + 4)]] : [path];
    }

    // LimitLoadToSessionType=Aqua: only for a real GUI login (not SSH sessions).
    internal static string Plist(string path)
    {
        var args = string.Concat(ProgramArguments(path).Select(a => $"\n        <string>{SecurityElement.Escape(a)}</string>"));
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
                <key>Label</key>
                <string>{Label}</string>
                <key>AssociatedBundleIdentifiers</key>
                <array>
                    <string>{BundleId}</string>
                </array>
                <key>ProgramArguments</key>
                <array>{args}
                </array>
                <key>RunAtLoad</key>
                <true/>
                <key>ProcessType</key>
                <string>Interactive</string>
                <key>LimitLoadToSessionType</key>
                <string>Aqua</string>
            </dict>
            </plist>

            """;
    }
}
