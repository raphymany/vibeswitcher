using System.IO;
using System.Text;

namespace VibeSwitcher.Platform;

// Where VibeSwitcher keeps its files on macOS and Linux — the counterparts of %APPDATA%\VibeSwitcher.
// The folder layout inside (config.json, error.log, Icons/, Sounds/) is the same as on Windows, so
// ConfigService and AppLogger (linked from the Windows app) are just pointed at ConfigDir.
//
// The single-instance lock and command socket live there too, not in a temp or runtime folder: the
// OS cleans those (macOS purges old /tmp and $TMPDIR files, systemd-tmpfiles /tmp), and which one is
// used depends on environment variables a launch from cron, ssh or another session may not have —
// either would let a second VibeSwitcher start next to a long-running one.
public sealed record AppPaths(string ConfigDir)
{
    // Socket addresses are limited to 104 bytes on macOS (108 on Linux), including the terminator.
    internal const int MaxSocketPathBytes = 100;

    public string LogDir => ConfigDir;
    public string InstanceLock => Path.Combine(ConfigDir, "vibeswitcher.lock");
    public string InstanceSocket => Path.Combine(ConfigDir, "vibeswitcher.sock");

    // False only for an extremely long home folder; the app then still runs single-instance, but
    // can't take commands from the command line.
    public bool SocketPathFits => Encoding.UTF8.GetByteCount(InstanceSocket) <= MaxSocketPathBytes;

    public static AppPaths Current() =>
        Resolve(OperatingSystem.IsMacOS(), Environment.GetEnvironmentVariable,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    // macOS: ~/Library/Application Support/VibeSwitcher. Linux: $XDG_CONFIG_HOME/VibeSwitcher (default ~/.config).
    internal static AppPaths Resolve(bool macOS, Func<string, string?> env, string home) =>
        new(macOS
            ? Path.Combine(home, "Library", "Application Support", "VibeSwitcher")
            : Path.Combine(Absolute(env("XDG_CONFIG_HOME")) ?? Path.Combine(home, ".config"), "VibeSwitcher"));

    // The XDG spec says relative values must be ignored.
    private static string? Absolute(string? p) => !string.IsNullOrWhiteSpace(p) && Path.IsPathRooted(p) ? p : null;
}
