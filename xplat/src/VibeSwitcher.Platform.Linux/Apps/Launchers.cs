namespace VibeSwitcher.Platform.Linux.Apps;

// Programs that aren't the app themselves: interpreters that run an app's script, and programs that
// open something else (a link, another app, or run it as another user).
internal static class Launchers
{
    private enum Family { Shell, Python, Perl, Ruby, Node, Gjs, Java }

    private static Family? FamilyOf(string fileName) => fileName switch
    {
        "sh" or "bash" or "dash" or "zsh" => Family.Shell,
        "node" or "nodejs" => Family.Node,
        "gjs" or "gjs-console" => Family.Gjs,
        "java" => Family.Java,
        _ when Versioned(fileName, "python") => Family.Python,
        _ when Versioned(fileName, "perl") => Family.Perl,
        _ when Versioned(fileName, "ruby") => Family.Ruby,
        _ => null,
    };

    public static bool IsInterpreter(string fileName) => FamilyOf(fileName) != null;

    // "python3", "python3.12", "perl5.38" …
    private static bool Versioned(string fileName, string prefix) =>
        fileName.StartsWith(prefix, StringComparison.Ordinal) && fileName[prefix.Length..].All(c => char.IsAsciiDigit(c) || c == '.');

    // xdg-open & co. open a link or file in whatever app handles it; pkexec/sudo run something as
    // another user. Their desktop entries aren't apps to watch for.
    public static bool IsOpener(string fileName) =>
        fileName is "xdg-open" or "gio" or "gtk-launch" or "gtk4-launch" or "kde-open" or "kde-open5" or "kioclient"
            or "kioclient5" or "exo-open" or "pkexec" or "sudo" or "gksu" or "kdesu";

    // The script an interpreter runs (each family's own options), or null for code given inline
    // (python -c, bash -lc, node -e …) or a module (python -m), which can't be told apart.
    public static string? ScriptOf(string interpreter, IReadOnlyList<string> args)
    {
        if (FamilyOf(interpreter) is not { } family) return null;
        for (int i = 0; i < args.Count; i++)
        {
            var a = args[i];
            if (a == "--") return i + 1 < args.Count ? args[i + 1] : null;
            if (a == "-") return null; // read from stdin
            // (For java a plain argument is a class name, not a file.)
            if (!a.StartsWith('-') && !a.StartsWith('+')) return family == Family.Java ? null : a;
            switch (family)
            {
                case Family.Shell:
                    // -c in any cluster (-c, -lc, -ec) means inline commands; -o/+o take a value.
                    if (!a.StartsWith("--", StringComparison.Ordinal) && a.Contains('c')) return null;
                    if (a is "-o" or "+o" or "--rcfile" or "--init-file") i++;
                    break;
                case Family.Python:
                    if (a is "-c" or "-m") return null;
                    if (a is "-W" or "-X") i++;
                    break;
                case Family.Perl:
                    if (a is "-e" or "-E") return null;
                    break;
                case Family.Ruby:
                    if (a == "-e") return null;
                    if (a is "-I" or "-r") i++;
                    break;
                case Family.Node:
                    if (a is "-e" or "-p" or "--eval" or "--print") return null;
                    if (a is "-r" or "--require" or "--import") i++;
                    break;
                case Family.Gjs:
                    if (a == "-c" || a == "--command") return null;
                    if (a is "-m" or "--module") return i + 1 < args.Count ? args[i + 1] : null;
                    if (a == "-I") i++;
                    break;
                case Family.Java:
                    if (a == "-jar") return i + 1 < args.Count ? args[i + 1] : null;
                    if (a is "-cp" or "-classpath" or "--class-path" or "-p" or "--module-path") i++;
                    break;
            }
        }
        return null;
    }

    // Wine's own programs and loaders: they run for every Windows program, so they never identify one.
    public static bool IsWinePart(string name) => WineParts.Contains(name);

    private static readonly HashSet<string> WineParts = new(StringComparer.OrdinalIgnoreCase)
    {
        "wine", "wine64", "wine-preloader", "wine64-preloader", "wineserver", "wine-stable", "wine-development",
        "services.exe", "winedevice.exe", "plugplay.exe", "explorer.exe", "rpcss.exe", "svchost.exe", "conhost.exe",
        "tabtip.exe", "wineboot.exe", "winemenubuilder.exe", "start.exe", "steam.exe", "rundll32.exe", "cmd.exe",
    };
}
