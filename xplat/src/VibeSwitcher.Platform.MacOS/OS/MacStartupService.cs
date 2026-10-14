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
// (with a one-time "Background Items Added" notice). AssociatedBundleIdentifiers asks macOS to show
// it under the app's name; macOS only honours that when app and agent are signed by the same
// developer, and BundleId must match the bundle's Info.plist (written by the packaging step). If the
// user switches it off there, macOS ignores the agent even though the file exists — VibeSwitcher
// can't see that switch without the newer Service Management API (planned for app wiring).
public sealed class MacStartupService : IStartupService
{
    internal const string BundleId = "com.raphymany.vibeswitcher";
    internal const string Label = BundleId + ".login";
    private readonly IAppLogger _logger;
    private readonly ISessionErrorTracker _errorTracker;
    private readonly string _agentsDir;
    private readonly Func<string?> _launchPath;
    private readonly Func<string?> _appAssembly;

    public MacStartupService(IAppLogger logger, ISessionErrorTracker errorTracker)
        : this(logger, errorTracker,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents"),
            () => Environment.ProcessPath) { }

    internal MacStartupService(IAppLogger logger, ISessionErrorTracker errorTracker, string agentsDir, Func<string?> launchPath,
        Func<string?>? appAssembly = null)
    {
        _appAssembly = appAssembly ?? (() => System.Reflection.Assembly.GetEntryAssembly()?.Location);
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
        File.WriteAllText(tmp, Plist(path, _appAssembly()), new UTF8Encoding(false));
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
            // A temporary (translocated) copy must never replace a good path, nor a development build
            // run with the dotnet command an installed copy (it only takes over when turned on there).
            if (string.IsNullOrEmpty(path) || IsTranslocated(path) || Path.GetFileName(path) == "dotnet") return;
            if (File.ReadAllText(AgentPath) == Plist(path, _appAssembly())) return;
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

    internal static bool IsTranslocated(string path) => Platform.InstallInfo.IsTranslocatedPath(path);

    // An app bundle is opened through Launch Services (`open -a <bundle>`), the way the Dock or
    // Finder start it; a bare executable (development builds) is run directly — and one started with
    // the dotnet command (`dotnet VibeSwitcher.dll`) is the dotnet host with the app's assembly.
    internal static string[] ProgramArguments(string path, string? appAssembly = null)
    {
        int i = path.IndexOf(".app/Contents/MacOS/", StringComparison.Ordinal);
        if (i > 0) return ["/usr/bin/open", "-a", path[..(i + 4)]];
        return Path.GetFileName(path) == "dotnet" && appAssembly is { Length: > 0 } && appAssembly.EndsWith(".dll", StringComparison.Ordinal)
            ? [path, appAssembly]
            : [path];
    }

    // LimitLoadToSessionType=Aqua: only for a real GUI login (not SSH sessions).
    internal static string Plist(string path, string? appAssembly)
    {
        var args = string.Concat(ProgramArguments(path, appAssembly).Select(a => $"\n        <string>{SecurityElement.Escape(a)}</string>"));
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

            """.ReplaceLineEndings("\n"); // the source may be checked out with Windows line endings
    }
}
