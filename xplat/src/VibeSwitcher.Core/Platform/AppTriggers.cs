namespace VibeSwitcher.Platform;

// App launch triggers on macOS and Linux. A profile's AppTriggers (the Windows app stores full .exe
// paths there) hold one of these "trigger" strings:
//   /path/to/program                    a program file (Linux; an AppImage file is one too)
//   /Applications/Some App.app          a Mac app bundle
//   flatpak:org.example.App             a Flatpak app
//   snap:appname                        a Snap app
//   C:\Program Files\App\App.exe        from a config imported from Windows: matched by name
// Windows matches by process name only; that isn't reliable here (Linux shortens process names, Mac
// apps are bundles, Flatpak/Snap apps run inside wrappers), so the program's location, bundle or
// sandbox id is matched first, with the program name as the fallback.

// One running process, as trigger matching sees it.
//   ExePath: the program file (null when the system won't say, e.g. another user's process)
//   Names:   names it goes by — program file name, process name… (matched case-insensitively)
//   AppId:   what it belongs to — Mac app bundle path, "flatpak:<id>", "snap:<name>", or the
//            AppImage file it runs from, or the app script an interpreter runs
public sealed record RunningProcess(string? ExePath, IReadOnlyList<string> Names, string? AppId = null);

// The system's process list (one snapshot per call; never throws). Null when it couldn't be read
// completely — then nothing can be concluded about what's running (an app missing from a partial
// list hasn't really closed).
public interface IRunningProcessSource
{
    IReadOnlyList<RunningProcess>? Snapshot();
}

public static class AppTriggerMatch
{
    public const string FlatpakPrefix = "flatpak:";
    public const string SnapPrefix = "snap:";

    public static bool Matches(string trigger, RunningProcess process)
    {
        if (string.IsNullOrWhiteSpace(trigger)) return false;
        if (IsSandboxId(trigger)) return string.Equals(trigger, process.AppId, StringComparison.Ordinal);
        if (IsBundle(trigger))
        {
            var bundle = trigger.TrimEnd('/');
            var appId = process.AppId?.TrimEnd('/');
            // macOS paths are case-insensitive by default.
            if (string.Equals(bundle, appId, StringComparison.OrdinalIgnoreCase)
                || (process.ExePath?.StartsWith(bundle + "/", StringComparison.OrdinalIgnoreCase) ?? false))
                return true;
            // The same app run from where macOS put it: a downloaded app moved to a random read-only
            // folder (App Translocation), or Safari's real home (a "cryptex").
            return appId != null
                && (appId.Contains("/AppTranslocation/", StringComparison.Ordinal) || appId.Contains("/Cryptexes/", StringComparison.Ordinal))
                && string.Equals(FileName(appId), FileName(bundle), StringComparison.OrdinalIgnoreCase);
        }
        if (trigger.StartsWith('/')
            && (string.Equals(trigger, process.ExePath, StringComparison.Ordinal) || string.Equals(trigger, process.AppId, StringComparison.Ordinal)))
            return true;
        // Same program name: what Windows matches by. Also covers launcher scripts and symlinks
        // (/usr/bin/firefox → /usr/lib/firefox/firefox) and configs imported from Windows.
        var name = TriggerName(trigger);
        if (name.Length == 0) return false;
        var file = FileName(trigger);
        return process.Names.Any(n => NameMatches(name, file, n));
    }

    private static string FileName(string path)
    {
        var p = path.TrimEnd('/', '\\');
        return p[(p.LastIndexOfAny(['/', '\\']) + 1)..];
    }

    // The program name a trigger stands for: its last path part (either separator), without .exe,
    // .app or .AppImage. Empty for a Flatpak/Snap id (those only match by id).
    public static string TriggerName(string trigger)
    {
        if (IsSandboxId(trigger)) return "";
        var name = trigger.TrimEnd('/', '\\');
        name = name[(name.LastIndexOfAny(['/', '\\']) + 1)..];
        foreach (var ext in (string[])[".exe", ".app", ".AppImage"])
            if (name.Length > ext.Length && name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                return name[..^ext.Length];
        return name;
    }

    // A friendly name for showing a trigger when nothing better is known.
    public static string DisplayName(string trigger) =>
        trigger.StartsWith(FlatpakPrefix, StringComparison.Ordinal) ? LastDotPart(trigger[FlatpakPrefix.Length..])
        : trigger.StartsWith(SnapPrefix, StringComparison.Ordinal) ? trigger[SnapPrefix.Length..]
        : TriggerName(trigger);

    private static string LastDotPart(string id) => id[(id.LastIndexOf('.') + 1)..];

    public static bool IsSandboxId(string trigger) =>
        trigger.StartsWith(FlatpakPrefix, StringComparison.Ordinal) || trigger.StartsWith(SnapPrefix, StringComparison.Ordinal);

    public static bool IsBundle(string trigger) =>
        trigger.StartsWith('/') && trigger.TrimEnd('/').EndsWith(".app", StringComparison.OrdinalIgnoreCase);

    // Process names compare like the trigger's name: without ".exe" (Windows programs under Wine are
    // named "Game.exe"). Linux keeps only the first 15 characters of a process's own name, so a
    // 15-character process name also matches a longer program file name that starts the same way
    // ("LongGameName.ex" ↔ "LongGameName.exe", "gnome-calculato" ↔ "gnome-calculator").
    private static bool NameMatches(string triggerName, string triggerFile, string processName)
    {
        var name = processName.Length > 4 && processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? processName[..^4] : processName;
        if (string.Equals(triggerName, name, StringComparison.OrdinalIgnoreCase)) return true;
        return processName.Length == 15
            && ((triggerFile.Length > 15 && triggerFile.StartsWith(processName, StringComparison.OrdinalIgnoreCase))
                || (triggerName.Length > 15 && triggerName.StartsWith(processName, StringComparison.OrdinalIgnoreCase)));
    }
}

// An app the user can pick for a trigger.
//   Trigger:     what's stored in the profile (see above)
//   DisplayName: what the picker shows
//   Icon:        an icon file path or icon-theme name (null if none; the UI shows a default)
public sealed record AppEntry(string Trigger, string DisplayName, string? Icon, bool IsRunning);

// The apps the picker offers, per system.
public interface IAppCatalog
{
    // Installed apps (slow-ish: call off the UI thread), with IsRunning set.
    IReadOnlyList<AppEntry> Installed(CancellationToken ct = default);
    // Running apps a user would recognise (not system services).
    IReadOnlyList<AppEntry> Running();
    // The trigger for a file the user browsed to (a program, app bundle, desktop entry…), or null
    // when it can't be one.
    string? TriggerForFile(string path);
}
