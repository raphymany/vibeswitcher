using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace VibeSwitcher.Platform.Linux.Apps;

// The process list for app triggers, read from /proc:
//   ExePath  /proc/<pid>/exe (the real program file, after symlinks and launcher scripts' exec)
//   Names    that file's name; the kernel's process name (comm, at most 15 characters); the name it
//            was started as (argv[0], Windows-style for Wine programs: "C:\…\Game.exe"); and the
//            desktop entry it was launched from ("google-chrome": desktops start each app in its own
//            scope named after it), which catches apps whose launcher script runs a differently
//            named program (every process the app starts shares that scope)
//   AppId    flatpak:<id> / snap:<name> from the process's cgroup (both run every app in its own
//            scope), the AppImage file a process runs from ($APPIMAGE), or for an interpreter
//            (python, node, java -jar…) the real path of the app script it runs
// Other users' processes have no readable exe; they still count by name. Kernel threads are left out.
// What's read for a process is cached while it keeps the same pid, program and process name, so a
// poll does two small reads for processes it has seen before. A list that couldn't be read
// completely is reported as null.
public sealed partial class LinuxProcesses : IRunningProcessSource
{
    private readonly string _proc;
    private readonly Func<string, string?> _readLink;
    private readonly object _lock = new();
    private Dictionary<(int Pid, string? Exe, string? Comm), RunningProcess> _cache = new();

    public LinuxProcesses() : this("/proc", ReadLink) { }

    internal LinuxProcesses(string procRoot, Func<string, string?> readLink)
    {
        _proc = procRoot;
        _readLink = readLink;
    }

    public IReadOnlyList<RunningProcess>? Snapshot()
    {
        lock (_lock)
        {
            var list = new List<RunningProcess>();
            var next = new Dictionary<(int, string?, string?), RunningProcess>();
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(_proc))
                {
                    if (!int.TryParse(Path.GetFileName(dir), NumberStyles.None, CultureInfo.InvariantCulture, out int pid)) continue;
                    var exe = Exe(dir);
                    var comm = ReadText(Path.Combine(dir, "comm"))?.TrimEnd('\n');
                    var key = (pid, exe, comm);
                    if (!_cache.TryGetValue(key, out var process))
                    {
                        process = Read(dir, exe, comm);
                        if (process == null) continue;
                    }
                    next[key] = process;
                    list.Add(process);
                }
            }
            catch (Exception) { return null; } // /proc unreadable: nothing can be concluded
            _cache = next;
            return list;
        }
    }

    private string? Exe(string dir)
    {
        var target = _readLink(Path.Combine(dir, "exe"));
        // A program replaced while running (e.g. updated) reads as "/path (deleted)".
        const string deleted = " (deleted)";
        return target != null && target.EndsWith(deleted, StringComparison.Ordinal) ? target[..^deleted.Length] : target;
    }

    private RunningProcess? Read(string dir, string? exe, string? comm)
    {
        var args = Args(Path.Combine(dir, "cmdline"));
        if (exe == null && args.Count == 0) return null; // kernel thread (or already gone)

        var names = new List<string>(4);
        void AddName(string? n)
        {
            if (!string.IsNullOrEmpty(n) && !names.Contains(n, StringComparer.OrdinalIgnoreCase)) names.Add(n);
        }
        AddName(exe == null ? null : Path.GetFileName(exe));
        AddName(comm);
        // argv[0] — unless a program rewrote its command line into one descriptive string
        // ("sshd: /usr/sbin/sshd", Chromium's "--type=renderer …"), which names nothing. A Windows
        // program's path under Wine ("Z:\…\Some Game\Game.exe") keeps its spaces and still counts.
        if (args.Count > 0 && (args.Count > 1 || !args[0].Contains(' ') || args[0].EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
            AddName(FileName(args[0]));

        var cgroup = ReadText(Path.Combine(dir, "cgroup"));
        string? appId = SandboxId(cgroup);
        if (appId == null) AddName(DesktopId(cgroup));
        if (appId == null && exe != null && exe.Contains("/.mount_", StringComparison.Ordinal))
            appId = AppImageOf(Path.Combine(dir, "environ"));
        // An interpreter running an app's script: the script's file identifies it (by its full path —
        // a bare "main.py" or "index.js" would match any other script of that name).
        if (appId == null && exe != null && args.Count > 1 && Launchers.IsInterpreter(Path.GetFileName(exe))
            && Launchers.ScriptOf(Path.GetFileName(exe), args.Skip(1).ToList()) is { } script)
            appId = ScriptPath(dir, script);
        return new RunningProcess(exe, names, appId);
    }

    // The script's real path; a relative one is relative to the process's working folder.
    private string? ScriptPath(string dir, string script)
    {
        if (!script.StartsWith('/'))
        {
            if (_readLink(Path.Combine(dir, "cwd")) is not { } cwd) return null;
            script = cwd.TrimEnd('/') + "/" + script;
        }
        return FilePaths.RealPath(script) ?? script;
    }

    private static string FileName(string path) => path[(path.LastIndexOfAny(['/', '\\']) + 1)..];

    // "…/app-flatpak-org.example.App-12345.scope" (also the older "flatpak-<id>-<n>.scope") or
    // "…/snap.firefox.firefox-<uuid>.scope" in /proc/<pid>/cgroup.
    internal static string? SandboxId(string? cgroup)
    {
        if (string.IsNullOrEmpty(cgroup)) return null;
        var flatpak = FlatpakScope().Match(cgroup);
        if (flatpak.Success) return AppTriggerMatch.FlatpakPrefix + flatpak.Groups["id"].Value;
        var snap = SnapScope().Match(cgroup);
        return snap.Success ? AppTriggerMatch.SnapPrefix + snap.Groups["name"].Value : null;
    }

    // The desktop entry an app was launched from, from its systemd scope/service name:
    // "app[-<launcher>]-<desktop id>-<n>.scope" or "app[-<launcher>]-<desktop id>@<n>.service", with
    // '-' inside the id escaped as \x2d (e.g. "app-gnome-google\x2dchrome-4242.scope").
    internal static string? DesktopId(string? cgroup)
    {
        if (string.IsNullOrEmpty(cgroup)) return null;
        var m = AppUnit().Match(cgroup);
        if (!m.Success) return null;
        var id = m.Groups["rest"].Value.Split('-')[^1];
        return id.Length == 0 ? null : Unescape(id);
    }

    // systemd's \xNN escapes ("\x2d" for '-', "\x20" for a space).
    private static string Unescape(string s) =>
        UnitEscape().Replace(s, m => ((char)Convert.ToByte(m.Groups[1].Value, 16)).ToString());

    [GeneratedRegex(@"\\x([0-9a-fA-F]{2})")]
    private static partial Regex UnitEscape();

    [GeneratedRegex(@"(?:^|/)(?:app-)?flatpak-(?<id>[A-Za-z0-9_.\-]+?)-\d+\.scope\b", RegexOptions.Multiline)]
    private static partial Regex FlatpakScope();

    // Snap names may carry an instance key: "firefox_beta".
    [GeneratedRegex(@"(?:^|/)snap\.(?<name>[a-z0-9][a-z0-9\-_]*)\.[^/\n]*\.scope\b", RegexOptions.Multiline)]
    private static partial Regex SnapScope();

    [GeneratedRegex(@"/app-(?<rest>[^/\n]+?)(?:-[0-9A-Za-z]+\.scope|@[0-9A-Za-z]+\.service)$", RegexOptions.Multiline)]
    private static partial Regex AppUnit();

    private static string? AppImageOf(string environPath)
    {
        var env = ReadText(environPath);
        if (env == null) return null;
        foreach (var entry in env.Split('\0'))
            if (entry.StartsWith("APPIMAGE=", StringComparison.Ordinal) && entry.Length > 9) return entry[9..];
        return null;
    }

    private static List<string> Args(string cmdlinePath)
    {
        var text = ReadText(cmdlinePath);
        return string.IsNullOrEmpty(text) ? [] : text.TrimEnd('\0').Split('\0').ToList();
    }

    private static string? ReadText(string path)
    {
        try { return File.ReadAllText(path, Encoding.UTF8); }
        catch (Exception) { return null; }
    }

    // readlink(2) directly: other users' processes can't be read, and that's the common case — an
    // error code, not an exception per process per poll.
    [DllImport("libc", SetLastError = true, EntryPoint = "readlink")]
    private static extern nint ReadLinkNative([MarshalAs(UnmanagedType.LPUTF8Str)] string path, byte[] buf, nint size);

    [ThreadStatic] private static byte[]? s_linkBuffer;

    private static string? ReadLink(string path)
    {
        try
        {
            var buf = s_linkBuffer ??= new byte[4096];
            nint n = ReadLinkNative(path, buf, buf.Length);
            return n > 0 && n < buf.Length ? Encoding.UTF8.GetString(buf, 0, (int)n) : null;
        }
        catch (Exception) { return null; }
    }
}
