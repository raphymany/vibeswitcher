namespace VibeSwitcher.Platform.Linux.Apps;

// An app's icon file from its desktop entry's Icon= value, found the way app menus find it (the
// freedesktop icon theme layout, simplified): a full path is used as it is; a name is looked up in
// the user's icon theme, then "hicolor" (where apps install their icons), in every icon folder
// ($XDG_DATA_HOME/icons, ~/.icons, each $XDG_DATA_DIRS/icons, the Flatpak ones), then in
// /usr/share/pixmaps. PNG and SVG only. Of the sizes found: the smallest PNG at least as big as
// asked for, else an SVG, else the biggest PNG. Safe to call from any thread.
public sealed class LinuxIconLookup
{
    private readonly Func<string, string?> _env;
    private readonly IReadOnlyList<string>? _baseDirsOverride;
    private readonly IReadOnlyList<string>? _pixmapsOverride;
    private readonly Lazy<Dictionary<string, List<Candidate>>> _index;

    public LinuxIconLookup() : this(Environment.GetEnvironmentVariable, null, null) { }

    internal LinuxIconLookup(Func<string, string?> env, IReadOnlyList<string>? baseDirs, IReadOnlyList<string>? pixmapDirs)
    {
        _env = env;
        _baseDirsOverride = baseDirs;
        _pixmapsOverride = pixmapDirs;
        _index = new Lazy<Dictionary<string, List<Candidate>>>(BuildIndex, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    // One icon file: its folder's size (0 for "scalable") and the theme's rank (0 = the user's theme,
    // 1 = hicolor, 2 = pixmaps).
    private sealed record Candidate(string Path, int Size, bool Svg, int Rank);

    public string? Find(string? icon, int size)
    {
        if (string.IsNullOrWhiteSpace(icon)) return null;
        icon = icon.Trim();
        if (icon.StartsWith('/')) return IsImage(icon) && File.Exists(icon) ? icon : null;
        // (Some entries name the file: "app.png". The spec says not to, but it happens.)
        var name = IsImage(icon) || icon.EndsWith(".xpm", StringComparison.OrdinalIgnoreCase) ? Path.GetFileNameWithoutExtension(icon) : icon;
        if (!_index.Value.TryGetValue(name, out var found)) return null;
        var best = found.Min(c => c.Rank);
        var ranked = found.Where(c => c.Rank == best).ToList();
        // (SVGs: the "scalable" one first; then one drawn for at least this size — themes like
        // Papirus also have simplified ones for 16 px.)
        return ranked.Where(c => !c.Svg && c.Size >= size).OrderBy(c => c.Size).ThenBy(c => c.Path, StringComparer.Ordinal).FirstOrDefault()?.Path
            ?? ranked.Where(c => c.Svg)
                .OrderBy(c => c.Size == 0 ? 0 : c.Size >= size ? 1 : 2)
                .ThenBy(c => c.Size >= size ? c.Size : -c.Size)
                .ThenBy(c => c.Path, StringComparer.Ordinal).FirstOrDefault()?.Path
            ?? ranked.Where(c => !c.Svg).OrderByDescending(c => c.Size).ThenBy(c => c.Path, StringComparer.Ordinal).FirstOrDefault()?.Path;
    }

    private static bool IsImage(string path) =>
        path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);

    // Every app icon in the themes that count, by name — read once (a few thousand file names).
    private Dictionary<string, List<Candidate>> BuildIndex()
    {
        var index = new Dictionary<string, List<Candidate>>(StringComparer.Ordinal);
        var themes = new List<string>();
        if (UserTheme() is { } theme && theme != "hicolor") themes.Add(theme);
        themes.Add("hicolor");
        for (int rank = 0; rank < themes.Count; rank++)
            foreach (var root in BaseDirs().Select(b => Path.Combine(b, themes[rank])))
                foreach (var (dir, dirSize) in AppDirs(root))
                    foreach (var file in Files(dir))
                        Add(index, file, dirSize, themes[rank] == "hicolor" ? 1 : 0);
        foreach (var dir in PixmapDirs())
            foreach (var file in Files(dir))
                Add(index, file, 0, 2);
        return index;
    }

    private static void Add(Dictionary<string, List<Candidate>> index, string file, int dirSize, int rank)
    {
        if (!IsImage(file)) return;
        var svg = file.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);
        var name = Path.GetFileNameWithoutExtension(file);
        if (!index.TryGetValue(name, out var list)) index[name] = list = [];
        // (A pixmap's size isn't in its folder name: counted as big enough.)
        list.Add(new Candidate(file, rank == 2 ? int.MaxValue : dirSize, svg, rank));
    }

    // A theme's app-icon folders and their sizes: "48x48/apps", "256x256@2/apps", "scalable/apps"
    // (hicolor, Adwaita, Papirus…) and "apps/48" (Breeze). Symbolic (one-colour) icons are left out.
    private static IEnumerable<(string Dir, int Size)> AppDirs(string themeRoot)
    {
        foreach (var sub in Dirs(themeRoot))
        {
            var part = Path.GetFileName(sub);
            if (part.Contains("symbolic", StringComparison.OrdinalIgnoreCase)) continue;
            if (part == "apps")
            {
                foreach (var sized in Dirs(sub))
                    if (SizeOf(Path.GetFileName(sized)) is { } s) yield return (sized, s);
            }
            else if (SizeOf(part) is { } s && Directory.Exists(Path.Combine(sub, "apps")))
                yield return (Path.Combine(sub, "apps"), s);
        }
    }

    // "48x48" → 48, "256x256@2" → 512, "48" → 48, "scalable" → 0 (SVG); null for anything else.
    internal static int? SizeOf(string dirName)
    {
        if (dirName == "scalable") return 0;
        var scale = 1;
        var at = dirName.IndexOf('@');
        if (at >= 0)
        {
            if (!int.TryParse(dirName[(at + 1)..], out scale) || scale < 1) return null;
            dirName = dirName[..at];
        }
        var x = dirName.IndexOf('x');
        return int.TryParse(x >= 0 ? dirName[..x] : dirName, out var n) && n > 0 ? n * scale : null;
    }

    private IEnumerable<string> BaseDirs()
    {
        if (_baseDirsOverride != null) return _baseDirsOverride;
        var home = _env("HOME") ?? "";
        var dataHome = _env("XDG_DATA_HOME") is { Length: > 0 } dh ? dh : Path.Combine(home, ".local/share");
        var dataDirs = (_env("XDG_DATA_DIRS") is { Length: > 0 } dd ? dd : "/usr/local/share:/usr/share").Split(':', StringSplitOptions.RemoveEmptyEntries);
        return new[] { dataHome }.Concat(dataDirs)
            .Concat([Path.Combine(dataHome, "flatpak/exports/share"), "/var/lib/flatpak/exports/share"])
            .Select(d => Path.Combine(d, "icons"))
            .Prepend(Path.Combine(home, ".icons"))
            .Distinct(StringComparer.Ordinal);
    }

    private IEnumerable<string> PixmapDirs() => _pixmapsOverride ?? ["/usr/share/pixmaps"];

    // The icon theme the desktop uses, where it's written in a file: GTK's settings.ini (Cinnamon,
    // MATE, Xfce and others write it there; GNOME keeps it in its own settings store) or KDE's
    // kdeglobals. Null when neither says.
    private string? UserTheme()
    {
        var home = _env("HOME") ?? "";
        var config = _env("XDG_CONFIG_HOME") is { Length: > 0 } ch ? ch : Path.Combine(home, ".config");
        return Setting(Path.Combine(config, "kdeglobals"), "[Icons]", "Theme")
            ?? Setting(Path.Combine(config, "gtk-3.0", "settings.ini"), "[Settings]", "gtk-icon-theme-name")
            ?? Setting(Path.Combine(config, "gtk-4.0", "settings.ini"), "[Settings]", "gtk-icon-theme-name");
    }

    internal static string? Setting(string file, string section, string key)
    {
        string[] lines;
        try { lines = File.ReadAllLines(file); }
        catch (Exception) { return null; }
        var inSection = false;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith('[')) { inSection = line == section; continue; }
            if (!inSection) continue;
            var eq = line.IndexOf('=');
            if (eq > 0 && line[..eq].Trim() == key)
            {
                var value = line[(eq + 1)..].Trim().Trim('"');
                // (Only a plain folder name: never a path out of the icon folders.)
                return value.Length > 0 && !value.Contains('/') && value != "." && value != ".." ? value : null;
            }
        }
        return null;
    }

    private static IEnumerable<string> Dirs(string dir)
    {
        try { return Directory.EnumerateDirectories(dir).ToList(); }
        catch (Exception) { return []; }
    }

    private static IEnumerable<string> Files(string dir)
    {
        try { return Directory.EnumerateFiles(dir).ToList(); }
        catch (Exception) { return []; }
    }
}
