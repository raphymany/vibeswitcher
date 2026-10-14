namespace VibeSwitcher.Platform.Linux.Apps;

// The app picker's lists on Linux:
//   Installed  every app in the app menu: desktop entries from $XDG_DATA_HOME and $XDG_DATA_DIRS,
//              plus the Flatpak and Snap folders (searched even when they're missing from
//              $XDG_DATA_DIRS); the first entry with a given id wins, as in app menus
//   Running    installed apps that are running, plus running Flatpak/Snap/AppImage apps and
//              programs under /opt or the home folder (not system services)
// A program's trigger is its real file (symlinks resolved), so it matches what /proc reports.
public sealed class LinuxAppCatalog : IAppCatalog
{
    private readonly IRunningProcessSource _processes;
    private readonly Func<string, string?> _env;
    private readonly IReadOnlyList<string> _own; // this app: its program, and its AppImage file
    private readonly IReadOnlyList<string>? _dirsOverride;
    private readonly object _lock = new();
    private IReadOnlyList<AppEntry>? _installed;

    public LinuxAppCatalog(IRunningProcessSource processes)
        : this(processes, Environment.GetEnvironmentVariable, Environment.ProcessPath, null) { }

    // ownPath: this app's program; when run as an AppImage, $APPIMAGE (the file it runs from) counts too.

    internal LinuxAppCatalog(IRunningProcessSource processes, Func<string, string?> env, string? ownPath, IReadOnlyList<string>? applicationDirs)
    {
        _processes = processes;
        _env = env;
        _own = new[] { ownPath, env("APPIMAGE") }.Where(p => !string.IsNullOrEmpty(p)).Select(p => p!).ToList();
        _dirsOverride = applicationDirs;
    }

    public IReadOnlyList<AppEntry> Installed(CancellationToken ct = default)
    {
        var running = _processes.Snapshot() ?? [];
        _ownProcess = running.FirstOrDefault(p => p.ExePath != null && _own.Contains(p.ExePath, StringComparer.Ordinal)) ?? _ownProcess;
        var byId = new Dictionary<string, (string Trigger, DesktopEntry Entry)>(StringComparer.Ordinal);
        var lang = _env("LC_ALL") is { Length: > 0 } all ? all : _env("LC_MESSAGES") is { Length: > 0 } msg ? msg : _env("LANG");
        foreach (var dir in ApplicationDirs())
        {
            ct.ThrowIfCancellationRequested();
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(dir, "*.desktop", SearchOption.AllDirectories).ToList(); }
            catch (Exception) { continue; }
            foreach (var file in files)
            {
                // Desktop-file id: the path under applications/ with '/' → '-'.
                var id = Path.GetRelativePath(dir, file).Replace('/', '-');
                if (byId.ContainsKey(id)) continue;
                DesktopEntry? entry;
                try { entry = DesktopEntry.Parse(File.ReadAllText(file), lang); }
                catch (Exception) { continue; }
                // A hidden entry still claims its id (that's how users hide an app).
                if (entry == null) continue;
                var trigger = entry.Hidden ? "" : entry.Trigger(Resolve) ?? "";
                byId[id] = (trigger, entry);
            }
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<AppEntry>();
        foreach (var (trigger, entry) in byId.Values)
        {
            if (trigger.Length == 0 || IsSelf(trigger) || !seen.Add(trigger)) continue;
            list.Add(new AppEntry(trigger, entry.Name, entry.Icon, running.Any(p => AppTriggerMatch.Matches(trigger, p))));
        }
        list.Sort((a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.DisplayName, b.DisplayName));
        lock (_lock) _installed = list;
        return list;
    }

    public IReadOnlyList<AppEntry> Running()
    {
        IReadOnlyList<AppEntry>? installed;
        lock (_lock) installed = _installed;
        installed ??= Installed();
        var processes = _processes.Snapshot() ?? [];
        _ownProcess = processes.FirstOrDefault(p => p.ExePath != null && _own.Contains(p.ExePath, StringComparer.Ordinal)) ?? _ownProcess;
        var result = installed.Where(a => processes.Any(p => AppTriggerMatch.Matches(a.Trigger, p)))
            .Select(a => a with { IsRunning = true }).ToList();
        var seen = new HashSet<string>(result.Select(a => a.Trigger), StringComparer.Ordinal);
        foreach (var p in processes)
        {
            // A Windows game is itself even inside an installed launcher's sandbox (Flatpak Steam).
            var windows = WindowsProgram(p);
            if (windows == null && installed.Any(a => AppTriggerMatch.Matches(a.Trigger, p))) continue;
            if (windows == null && IsWine(p)) continue; // Wine's own parts and loaders
            var trigger = windows ?? p.AppId ?? (IsUserProgram(p.ExePath) ? p.ExePath : null);
            if (trigger == null || IsSelf(trigger) || !seen.Add(trigger)) continue;
            result.Add(new AppEntry(trigger, AppTriggerMatch.DisplayName(trigger), null, true));
        }
        result.Sort((a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.DisplayName, b.DisplayName));
        return result;
    }

    public string? TriggerForFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (path.EndsWith(".desktop", StringComparison.Ordinal))
        {
            try { return DesktopEntry.Parse(File.ReadAllText(path), null)?.Trigger(Resolve); }
            catch (Exception) { return null; }
        }
        return File.Exists(path) && IsProgram(path) ? RealPath(Path.GetFullPath(path)) : null;
    }

    // Something that can run: an executable file, or an AppImage / Windows program (a download may
    // not be marked executable yet). Not a document or a picture.
    private static bool IsProgram(string path)
    {
        if (path.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return true;
        if (OperatingSystem.IsWindows()) return false;
        try { return (File.GetUnixFileMode(path) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0; }
        catch (Exception) { return false; }
    }

    // A Windows program under Wine/Proton (a game, say): its "Game.exe" name, which is what Wine names
    // the process and how it's matched — the Wine loader's own file says nothing. Wine's own parts
    // are left out.
    private static string? WindowsProgram(RunningProcess p) =>
        p.Names.FirstOrDefault(n => n.Length > 4 && n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !Launchers.IsWinePart(n));

    // Any part of Wine/Proton itself: a Windows-style process name, or one of its loaders.
    private static bool IsWine(RunningProcess p) =>
        p.Names.Any(n => n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".ex", StringComparison.OrdinalIgnoreCase) || Launchers.IsWinePart(n));

    // Programs a user installed themselves: /opt, /usr/local, the home folder (AppImages, games…).
    private bool IsUserProgram(string? exe)
    {
        if (exe == null) return false;
        var home = _env("HOME");
        return exe.StartsWith("/opt/", StringComparison.Ordinal) || exe.StartsWith("/usr/local/", StringComparison.Ordinal)
            || (!string.IsNullOrEmpty(home) && exe.StartsWith(home.TrimEnd('/') + "/", StringComparison.Ordinal));
    }

    // This app: its own program or AppImage, or anything that would match this very process (e.g. a
    // launcher script that runs it).
    private bool IsSelf(string trigger) =>
        _own.Contains(trigger, StringComparer.Ordinal)
        || (_ownProcess != null && AppTriggerMatch.Matches(trigger, _ownProcess));

    private RunningProcess? _ownProcess;

    private IEnumerable<string> ApplicationDirs()
    {
        if (_dirsOverride != null) return _dirsOverride;
        var home = _env("HOME") ?? "";
        var dataHome = _env("XDG_DATA_HOME") is { Length: > 0 } dh ? dh : Path.Combine(home, ".local/share");
        var dataDirs = (_env("XDG_DATA_DIRS") is { Length: > 0 } dd ? dd : "/usr/local/share:/usr/share").Split(':', StringSplitOptions.RemoveEmptyEntries);
        return new[] { dataHome }.Concat(dataDirs)
            .Concat([Path.Combine(dataHome, "flatpak/exports/share"), "/var/lib/flatpak/exports/share"])
            .Select(d => Path.Combine(d, "applications"))
            .Append("/var/lib/snapd/desktop/applications")
            .Distinct(StringComparer.Ordinal);
    }

    // A program as a desktop entry names it → its real file.
    private string? Resolve(string program)
    {
        if (program.Contains('/')) return File.Exists(program) ? RealPath(program) : null;
        foreach (var dir in (_env("PATH") ?? "/usr/local/bin:/usr/bin:/bin").Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, program);
            if (File.Exists(candidate)) return RealPath(candidate);
        }
        return null;
    }

    // Every symlink on the way resolved (/bin → usr/bin too), the way /proc/<pid>/exe reports it.
    private static string RealPath(string path)
    {
        try { return FilePaths.RealPath(path) ?? path; }
        catch (Exception) { return path; }
    }
}
