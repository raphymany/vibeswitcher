using System.Text;

namespace VibeSwitcher.Platform.Linux.Apps;

// A Linux app's desktop entry (the .desktop file app menus are built from), per the freedesktop
// Desktop Entry spec: just what the app picker needs.
internal sealed record DesktopEntry(string Name, string? Exec, string? TryExec, string? Icon, string? FlatpakId, string? SnapName, bool Hidden,
    string? WorkingDir = null)
{
    // Null unless it's a [Desktop Entry] of Type=Application.
    public static DesktopEntry? Parse(string text, string? language)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        bool inMain = false;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r').Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            if (line[0] == '[')
            {
                inMain = line == "[Desktop Entry]";
                continue;
            }
            if (!inMain) continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line[..eq].Trim();
            if (!values.ContainsKey(key)) values[key] = Unescape(line[(eq + 1)..].Trim());
        }

        bool hidden = IsTrue(values.GetValueOrDefault("NoDisplay")) || IsTrue(values.GetValueOrDefault("Hidden"));
        var name = LocalizedName(values, language);
        // A user hides a system app with a minimal file of just "Hidden=true" (no Type or Name):
        // it still counts, so it can shadow the system entry.
        if (!hidden && (values.GetValueOrDefault("Type") != "Application" || string.IsNullOrWhiteSpace(name))) return null;
        return new DesktopEntry(name ?? "", values.GetValueOrDefault("Exec"), values.GetValueOrDefault("TryExec"),
            values.GetValueOrDefault("Icon"), values.GetValueOrDefault("X-Flatpak"), values.GetValueOrDefault("X-SnapInstanceName"), hidden,
            values.GetValueOrDefault("Path"));
    }

    // Name[de_DE] / Name[de] / Name, for LANG=de_DE.UTF-8.
    private static string? LocalizedName(Dictionary<string, string> values, string? language)
    {
        if (!string.IsNullOrEmpty(language))
        {
            var lang = language.Split('.', '@')[0];
            if (values.TryGetValue($"Name[{lang}]", out var full)) return full;
            var shortLang = lang.Split('_')[0];
            if (values.TryGetValue($"Name[{shortLang}]", out var part)) return part;
        }
        return values.GetValueOrDefault("Name");
    }

    private static bool IsTrue(string? v) => string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);

    // \s \n \t \r \\ in values.
    private static string Unescape(string v)
    {
        if (!v.Contains('\\')) return v;
        var sb = new StringBuilder(v.Length);
        for (int i = 0; i < v.Length; i++)
        {
            if (v[i] == '\\' && i + 1 < v.Length)
            {
                char c = v[++i];
                sb.Append(c switch { 's' => ' ', 'n' => '\n', 't' => '\t', 'r' => '\r', _ => c });
            }
            else sb.Append(v[i]);
        }
        return sb.ToString();
    }

    // A link: "scheme://…" ("steam://rungameid/570", "heroic://…", "https://…"), or one of the
    // launcher schemes written without slashes ("lutris:rungameid/3", "mailto:…"). Not ordinary
    // arguments with a colon: "-W ignore::Warning", "host:8080", "C:\…".
    private static bool IsLink(string arg)
    {
        int colon = arg.IndexOf(':');
        if (colon < 2 || !char.IsAsciiLetter(arg[0])) return false;
        for (int i = 1; i < colon; i++)
            if (!(char.IsAsciiLetterOrDigit(arg[i]) || arg[i] is '+' or '.' or '-')) return false;
        return arg.AsSpan(colon).StartsWith("://") || LinkSchemes.Contains(arg[..colon]);
    }

    private static readonly HashSet<string> LinkSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "steam", "lutris", "heroic", "legendary", "itch", "bottles", "mailto", "magnet", "tel", "sms", "file",
    };

    // The Exec line split into arguments (quoted arguments, backslash escapes inside quotes) with
    // the field codes (%f %U %i …) removed.
    public static List<string> SplitExec(string exec)
    {
        var args = new List<string>();
        var cur = new StringBuilder();
        bool quoted = false, any = false;
        for (int i = 0; i < exec.Length; i++)
        {
            char c = exec[i];
            if (quoted)
            {
                if (c == '\\' && i + 1 < exec.Length) cur.Append(exec[++i]);
                else if (c == '"') quoted = false;
                else cur.Append(c);
            }
            else if (c == '"') { quoted = true; any = true; }
            else if (c is ' ' or '\t')
            {
                if (any) { args.Add(cur.ToString()); cur.Clear(); any = false; }
            }
            else { cur.Append(c); any = true; }
        }
        if (any) args.Add(cur.ToString());

        var result = new List<string>(args.Count);
        foreach (var a in args)
        {
            if (a.Length == 2 && a[0] == '%') continue; // a field code on its own
            result.Add(a.Replace("%%", "%"));
        }
        return result;
    }

    // What a profile stores for this app (see AppTriggerMatch), or null if it isn't an app that can
    // be recognised running (not installed, a link like "steam steam://rungameid/…", a file opener).
    //   resolve: a program name on $PATH → its absolute path (null if not installed)
    public string? Trigger(Func<string, string?> resolve)
    {
        if (Hidden) return null;
        if (!string.IsNullOrEmpty(FlatpakId)) return AppTriggerMatch.FlatpakPrefix + FlatpakId;
        if (!string.IsNullOrEmpty(SnapName)) return AppTriggerMatch.SnapPrefix + SnapName;
        if (!string.IsNullOrEmpty(TryExec) && resolve(TryExec) == null) return null; // not installed
        if (string.IsNullOrWhiteSpace(Exec)) return null;

        var args = SplitExec(Exec);
        // A shortcut to a link (a Steam/Lutris/Heroic game, a web page) starts whatever handles the
        // link; the game itself is a different program, picked from the running apps instead.
        if (args.Any(IsLink)) return null;

        int i = 0;
        // "env [-u VAR] [VAR=value …] program"
        if (i < args.Count && Path.GetFileName(args[i]) == "env")
        {
            i++;
            while (i < args.Count && (args[i].Contains('=') || args[i].StartsWith('-')))
            {
                if (args[i] is "-u" or "--unset" or "-C" or "--chdir" or "-S" or "--split-string") i++; // takes a value
                i++;
            }
        }
        if (i >= args.Count) return null;
        var program = args[i];
        var file = Path.GetFileName(program);
        var rest = args.Skip(i + 1).ToList();

        // "flatpak run [options] org.example.App"
        if (file == "flatpak" && rest.Count > 0 && rest[0] == "run")
        {
            string? id = null;
            for (int k = 1; k < rest.Count && id == null; k++)
            {
                if (rest[k] is "--branch" or "--arch" or "--command" or "--runtime" or "--runtime-version" or "--cwd") k++;
                else if (!rest[k].StartsWith('-')) id = rest[k];
            }
            return id == null ? null : AppTriggerMatch.FlatpakPrefix + id;
        }
        // /snap/bin/<snap>[.<app>]
        if (program.StartsWith("/snap/bin/", StringComparison.Ordinal))
            return AppTriggerMatch.SnapPrefix + file.Split('.')[0];
        if (Launchers.IsOpener(file)) return null;
        // A Windows program under Wine: matched by its name, as Wine names the process after it.
        // Wine's own menu entries run "start.exe /Unix …/Game.lnk": a shortcut, not a program name.
        if (file is "wine" or "wine64" or "wine-stable" or "wine-development")
        {
            if (rest.Any(a => a.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))) return null;
            return rest.FirstOrDefault(a => a.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                && !Launchers.IsWinePart(a[(a.LastIndexOfAny(['/', '\\']) + 1)..]));
        }
        // An app that's a script: the script is what identifies it (the interpreter runs many).
        if (Launchers.IsInterpreter(file))
        {
            if (Launchers.ScriptOf(file, rest) is not { } script) return null;
            // A relative script is relative to the entry's working folder (Path=).
            if (!script.StartsWith('/') && !string.IsNullOrEmpty(WorkingDir)) script = WorkingDir.TrimEnd('/') + "/" + script;
            return resolve(script);
        }

        return resolve(program);
    }
}
