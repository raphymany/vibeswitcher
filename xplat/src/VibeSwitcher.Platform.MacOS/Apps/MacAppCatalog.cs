namespace VibeSwitcher.Platform.MacOS.Apps;

// The app picker's lists on macOS:
//   Installed  the .app bundles in the Applications folders (/Applications, its Utilities, the system
//              apps, ~/Applications), including one folder deep (e.g. "/Applications/Microsoft Office/…")
//   Running    running app bundles, apart from the system's own background parts
// An app's trigger is its bundle path; its name is the bundle's name, as Finder shows it.
public sealed class MacAppCatalog : IAppCatalog
{
    private readonly IRunningProcessSource _processes;
    private readonly IReadOnlyList<string> _roots;
    private readonly string? _ownBundle;

    public MacAppCatalog(IRunningProcessSource processes)
        : this(processes, DefaultRoots(), MacProcesses.BundleOf(Environment.ProcessPath)) { }

    internal MacAppCatalog(IRunningProcessSource processes, IReadOnlyList<string> roots, string? ownBundle)
    {
        _processes = processes;
        _roots = roots;
        _ownBundle = ownBundle;
    }

    private static IReadOnlyList<string> DefaultRoots() =>
    [
        "/Applications", "/Applications/Utilities", "/System/Applications", "/System/Applications/Utilities",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications"),
    ];

    public IReadOnlyList<AppEntry> Installed(CancellationToken ct = default)
    {
        var running = _processes.Snapshot() ?? [];
        var bundles = new List<string>();
        foreach (var root in _roots)
        {
            ct.ThrowIfCancellationRequested();
            // Real paths (no symlinks), as the process list reports them.
            if (FilePaths.RealPath(root) is not { } real) continue;
            foreach (var dir in SafeDirs(real))
            {
                if (IsBundle(dir)) bundles.Add(dir);
                else bundles.AddRange(SafeDirs(dir).Where(IsBundle)); // a folder of apps
            }
        }
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<AppEntry>();
        // Each app by its real location: /Applications/Safari.app, for one, links to where it really is.
        foreach (var bundle in bundles.Select(b => FilePaths.RealPath(b) ?? b))
        {
            if (!seen.Add(bundle) || IsSelf(bundle)) continue;
            list.Add(new AppEntry(bundle, AppTriggerMatch.DisplayName(bundle), null, running.Any(p => AppTriggerMatch.Matches(bundle, p))));
        }
        list.Sort((a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.DisplayName, b.DisplayName));
        return list;
    }

    public IReadOnlyList<AppEntry> Running()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<AppEntry>();
        foreach (var p in _processes.Snapshot() ?? [])
        {
            var bundle = p.AppId;
            if (bundle == null || IsSystemPart(bundle) || IsTemporary(bundle) || IsSelf(bundle) || !seen.Add(bundle)) continue;
            list.Add(new AppEntry(bundle, AppTriggerMatch.DisplayName(bundle), null, true));
        }
        list.Sort((a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.DisplayName, b.DisplayName));
        return list;
    }

    // A file inside an app (or the app itself) → the app; another program file → itself. Stored as
    // its real path (e.g. /private/var/… for /var/…), which is what the process list reports.
    public string? TriggerForFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (FilePaths.RealPath(Path.GetFullPath(path).TrimEnd('/')) is not { } full) return null;
        if (MacProcesses.BundleOf(full) is { } bundle) return Directory.Exists(bundle) ? bundle : null;
        return File.Exists(full) ? full : null;
    }

    // Background parts of macOS itself (Dock, Spotlight, login window…); the system's own apps live
    // in /System/Applications and stay.
    private static bool IsSystemPart(string bundle) =>
        bundle.StartsWith("/System/Library/", StringComparison.OrdinalIgnoreCase)
        || bundle.StartsWith("/Library/Apple/", StringComparison.OrdinalIgnoreCase)
        || bundle.StartsWith("/usr/", StringComparison.OrdinalIgnoreCase);

    // A downloaded app macOS runs from a random read-only copy (App Translocation): that path changes
    // every launch, so it can't be stored. It's listed under Installed once moved to Applications.
    private static bool IsTemporary(string bundle) => bundle.Contains("/AppTranslocation/", StringComparison.Ordinal);

    private bool IsSelf(string bundle) => _ownBundle != null && string.Equals(bundle, _ownBundle, StringComparison.OrdinalIgnoreCase);

    private static bool IsBundle(string dir) => dir.EndsWith(".app", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> SafeDirs(string dir)
    {
        try { return Directory.EnumerateDirectories(dir).ToList(); }
        catch (Exception) { return []; }
    }
}
