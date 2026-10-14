using System.IO;
using System.Reflection;
using VibeSwitcher.Helpers;
using VibeSwitcher.Platform;
using VibeSwitcher.Platform.Linux.Hid;
using VibeSwitcher.Platform.Linux.Hotkeys;
using VibeSwitcher.Platform.Linux.Processes;
using VibeSwitcher.Platform.MacOS.OS;
using VibeSwitcher.Services;

namespace VibeSwitcher.App.Hosting;

// What uninstalling needs from the system (tests stand in for it). Each step may block (a password
// prompt): called off the UI thread.
internal interface IUninstallSystem
{
    // Called just before the app's own files go: whatever the rest of the run (and quitting) will
    // need from them is loaded now, while they're still there.
    void PrepareToRemoveApp();
    bool MoveToTrash(string path, out string? error);
    // A real AppImage file (never a folder, or some other file a variable pointed at).
    bool IsAppImage(string path);
    void DeleteFile(string path);
    // App-menu entries (.desktop files) that start this AppImage — made by AppImage helpers such as
    // AppImageLauncher or Gear Lever, not by VibeSwitcher.
    IEnumerable<string> MenuEntriesFor(string appImage);
    // The headset permission rule the AppImage installed (one password prompt; nothing if none).
    RuleInstallResult RemoveHeadsetRule();
    // null when the package was removed, else why not.
    string? RemovePackage(string location);
    void RemoveGnomeShortcuts();
}

// Uninstalling this copy from Settings › Install & Updates (the Windows app runs its uninstaller):
// the app itself first — the one step that can be refused (a cancelled password prompt), and then
// nothing else is touched — then start at login, the GNOME shortcuts, and the settings folder when
// asked. The app quits afterwards.
//   macOS     the app to the Trash (it can be put back) and its login item
//   AppImage  the file, app-menu entries that start it, the headset rule it installed
//   .deb      the package, by the package manager (its own removal script takes the headset rule)
internal static class Uninstaller
{
    // null: done (the app should quit); otherwise why not — and nothing was changed. Once the app
    // is gone nothing stops it: a later step that fails is only logged.
    public static string? Run(InstallInfo install, IStartupService startup, IUninstallSystem system, IAppLogger logger)
    {
        if (install.Kind is not (InstallKind.MacApp or InstallKind.AppImage or InstallKind.Package))
            return "This copy of VibeSwitcher isn't installed, so there's nothing to uninstall.";
        if (install.Kind == InstallKind.MacApp && install.Translocated) return NotTrashed(install);
        if (install.Kind == InstallKind.AppImage && !system.IsAppImage(install.Location))
            return $"{install.Location} doesn't look like VibeSwitcher's AppImage, so it wasn't deleted. Quit VibeSwitcher, then delete its AppImage file yourself.";

        system.PrepareToRemoveApp();
        switch (install.Kind)
        {
            case InstallKind.MacApp:
                if (!system.MoveToTrash(install.Location, out var error))
                {
                    logger.Warning("Uninstaller", $"Moving {install.Location} to the Trash: {error}");
                    return NotTrashed(install);
                }
                break;
            case InstallKind.AppImage:
                try { system.DeleteFile(install.Location); }
                catch (Exception ex)
                {
                    logger.Warning("Uninstaller", ex.Message);
                    return $"The AppImage file couldn't be deleted ({ex.Message}). Quit VibeSwitcher, then delete {install.Location} yourself.";
                }
                Step(logger, "App-menu entries", () =>
                {
                    foreach (var entry in system.MenuEntriesFor(install.Location))
                        try { system.DeleteFile(entry); }
                        catch (Exception ex) { logger.Warning("Uninstaller", $"{entry}: {ex.Message}"); }
                });
                Step(logger, "Headset rule", () =>
                {
                    var rule = system.RemoveHeadsetRule();
                    if (rule != RuleInstallResult.Installed)
                        logger.Warning("Uninstaller", $"The headset permission rule was left in place ({rule}).");
                });
                break;
            case InstallKind.Package:
                if (system.RemovePackage(install.Location) is { } why) return why;
                break;
        }

        Step(logger, "Start at login", startup.Disable);
        if (!OperatingSystem.IsMacOS()) Step(logger, "GNOME shortcuts", system.RemoveGnomeShortcuts);
        logger.Info("Uninstaller", $"VibeSwitcher {install.Version} ({install.Kind}) was uninstalled.");
        return null;
    }

    private static string NotTrashed(InstallInfo install)
    {
        var location = install.Location.TrimEnd('/');
        var folder = location.LastIndexOf('/') is > 0 and var slash ? location[..slash] : "";
        var from = install.Translocated ? "where you put it"
            : folder == "/Applications" || folder.EndsWith("/Applications", StringComparison.Ordinal) ? "your Applications folder"
            : folder.Length > 0 ? folder : "its folder";
        return $"VibeSwitcher couldn't be moved to the Trash. Quit it, then drag it from {from} to the Trash.";
    }

    private static void Step(IAppLogger logger, string name, Action step)
    {
        try { step(); }
        catch (Exception ex) { logger.Warning("Uninstaller", $"{name}: {ex.Message}"); }
    }

    // The settings folder, once the app has written everything it will (at quit): tried a few
    // times — a last background save may still be finishing. Only what VibeSwitcher puts there is
    // deleted, then the folder if that leaves it empty: XDG_CONFIG_HOME=$HOME, say, makes it
    // ~/VibeSwitcher, which may well be a folder of the user's own. (A link to elsewhere is left alone.)
    public static void DeleteData(string folder, Action<string>? warn)
    {
        var info = new DirectoryInfo(folder);
        if (!info.Exists) return;
        if (info.LinkTarget != null)
        {
            warn?.Invoke($"{folder} wasn't deleted: it's a link to another folder.");
            return;
        }
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                foreach (var entry in info.EnumerateFileSystemInfos().Where(e => IsOwn(e.Name)))
                {
                    if (entry is DirectoryInfo dir && dir.LinkTarget == null) dir.Delete(recursive: true);
                    else entry.Delete(); // (a file, or a link itself — never what it points to)
                }
                var others = info.EnumerateFileSystemInfos().Select(e => e.Name).ToList();
                if (others.Count == 0) info.Delete();
                else warn?.Invoke($"{folder} was kept: it also holds files that aren't VibeSwitcher's ({string.Join(", ", others.Take(5))}).");
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt == 4) warn?.Invoke($"The settings folder couldn't be deleted: {ex.Message}");
                else Thread.Sleep(200);
            }
        }
    }

    // What the app keeps in its settings folder.
    private static readonly HashSet<string> OwnNames =
    [
        "config.json", "config.json.bak", "config.json.tmp", "Icons", "Sounds",
        "no-tray-explained", "headset-access-offered", "xplat.json", ".DS_Store",
    ];

    private static bool IsOwn(string name) =>
        OwnNames.Contains(name)
        || name.StartsWith("error.log", StringComparison.Ordinal)
        || name.StartsWith(".selftest-", StringComparison.Ordinal)
        || name.StartsWith("instance-", StringComparison.Ordinal) && (name.EndsWith(".lock", StringComparison.Ordinal) || name.EndsWith(".sock", StringComparison.Ordinal));

    // Every assembly the app's code refers to, loaded now: .NET loads them on first use, which would
    // fail once their files are in the Trash or removed by the package manager. (A loaded file stays
    // readable: on macOS it was only moved, on Linux it's kept until the app exits.)
    internal static void LoadEverything()
    {
        var loaded = AppDomain.CurrentDomain.GetAssemblies();
        var seen = new HashSet<string>(loaded.Select(a => a.GetName().Name ?? ""));
        var queue = new Queue<Assembly>(loaded);
        while (queue.TryDequeue(out var assembly))
        {
            AssemblyName[] references;
            try { references = assembly.GetReferencedAssemblies(); }
            catch (Exception) { continue; }
            foreach (var name in references)
                if (seen.Add(name.Name ?? ""))
                    try { queue.Enqueue(Assembly.Load(name)); }
                    catch (Exception) { } // (one that isn't there was never going to be used)
        }
    }
}

// The CI check (AppHost.RunUninstallCheck): the real thing, except that a "package" is a copy of
// the app's folder, deleted directly.
internal sealed class UninstallCheckSystem(IUninstallSystem real) : IUninstallSystem
{
    public void PrepareToRemoveApp() => real.PrepareToRemoveApp();
    public bool MoveToTrash(string path, out string? error) => real.MoveToTrash(path, out error);
    public bool IsAppImage(string path) => real.IsAppImage(path);
    public void DeleteFile(string path) => real.DeleteFile(path);
    public IEnumerable<string> MenuEntriesFor(string appImage) => real.MenuEntriesFor(appImage);
    public RuleInstallResult RemoveHeadsetRule() => real.RemoveHeadsetRule();
    public void RemoveGnomeShortcuts() => real.RemoveGnomeShortcuts();

    public string? RemovePackage(string location)
    {
        Directory.Delete(location, recursive: true);
        return null;
    }
}

internal sealed class SystemUninstall(IAppLogger logger, IProcessRunner runner, string dataHome, Func<IGlobalKeyGrabber?> liveGrabber)
    : IUninstallSystem
{
    private const string DpkgQuery = "/usr/bin/dpkg-query", Dpkg = "/usr/bin/dpkg", Pkexec = "/usr/bin/pkexec";

    public SystemUninstall(IAppLogger logger, Func<IGlobalKeyGrabber?> liveGrabber)
        : this(logger, ProcessRunner.Instance, DefaultDataHome(), liveGrabber) { }

    private static string DefaultDataHome() =>
        Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } dh ? dh
            : Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? "", ".local/share");

    public void PrepareToRemoveApp() => Uninstaller.LoadEverything();

    public bool MoveToTrash(string path, out string? error) => MacTrash.MoveToTrash(path, out error);

    // An ELF program with the AppImage mark ("AI", type 2) after its magic number.
    public bool IsAppImage(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            Span<byte> head = stackalloc byte[11];
            using var file = File.OpenRead(path);
            return file.ReadAtLeast(head, head.Length, throwOnEndOfStream: false) == head.Length
                && head[0] == 0x7F && head[1] == (byte)'E' && head[2] == (byte)'L' && head[3] == (byte)'F'
                && head[8] == (byte)'A' && head[9] == (byte)'I' && head[10] == 2;
        }
        catch (Exception) { return false; }
    }

    public void DeleteFile(string path) => File.Delete(path);

    public IEnumerable<string> MenuEntriesFor(string appImage)
    {
        var dir = Path.Combine(dataHome, "applications");
        if (!Directory.Exists(dir)) return [];
        return Directory.EnumerateFiles(dir, "*.desktop")
            .Where(file =>
            {
                try { return new FileInfo(file).Length < 64 * 1024 && Starts(File.ReadAllText(file), appImage); }
                catch (Exception) { return false; }
            })
            .ToList();
    }

    // Whether a .desktop file's Exec= or TryExec= runs exactly this file (not one whose name merely
    // begins the same, such as a ".bak" copy).
    internal static bool Starts(string desktopFile, string program) =>
        desktopFile.Split('\n').Select(line => line.Trim()).Any(line =>
            (line.StartsWith("Exec=", StringComparison.Ordinal) || line.StartsWith("TryExec=", StringComparison.Ordinal))
            && FirstWord(line[(line.IndexOf('=') + 1)..]) == program);

    // The program a desktop-entry command line runs: its first word, unquoted.
    private static string FirstWord(string commandLine)
    {
        var text = commandLine.TrimStart();
        if (!text.StartsWith('"')) return text.Split([' ', '\t'], 2)[0];
        var word = new System.Text.StringBuilder();
        for (int i = 1; i < text.Length; i++)
        {
            if (text[i] == '"') return word.ToString();
            if (text[i] == '\\' && i + 1 < text.Length) i++;
            word.Append(text[i]);
        }
        return ""; // (unterminated: not a match)
    }

    public RuleInstallResult RemoveHeadsetRule() => new LinuxHidAccess().Remove();

    public string? RemovePackage(string location)
    {
        // Only a copy the package manager put there is removed by it: one copied in by hand, or
        // installed some other way, isn't dpkg's (and dpkg would "remove" it without a word).
        if (!OwnedByPackage(location))
            return $"This copy of VibeSwitcher wasn't installed by the system's package manager, so it can't be removed from here. Remove it the way it was installed (or delete {location} as an administrator).";
        try
        {
            // (Waited for however long the prompt stays open: pkexec runs as root, so a timeout
            // couldn't stop it — the user could still say yes after the app had said "not removed".)
            var result = runner.Run(Pkexec, [Dpkg, "--remove", "vibeswitcher"], Timeout.InfiniteTimeSpan);
            if (result.ExitCode != 0) logger.Warning("Uninstaller", $"pkexec dpkg: exit code {result.ExitCode}: {result.StdErr.Trim()}");
            return result.ExitCode switch
            {
                0 => null,
                // pkexec: 126 = the prompt was dismissed, 127 = not authorized — or nothing to ask with.
                127 when result.StdErr.Contains("authentication agent", StringComparison.OrdinalIgnoreCase) => NoPrompt,
                126 or 127 => "Removing VibeSwitcher needs an administrator's password, which wasn't given. Nothing was removed.",
                _ => $"The package manager couldn't remove VibeSwitcher: {LastLine(result.StdErr) ?? $"exit code {result.ExitCode}"}",
            };
        }
        catch (ToolNotFoundException) { return NoPrompt; }
        catch (Exception ex)
        {
            logger.Warning("Uninstaller", ex.Message);
            return $"The package manager couldn't remove VibeSwitcher: {ex.Message}";
        }
    }

    private const string NoPrompt =
        "This system has no way to ask for an administrator's password here. Remove VibeSwitcher with your package manager instead (for example: sudo apt remove vibeswitcher).";

    // dpkg-query -S answers "pkg[, pkg…]: path" for a path a package installed (a package's name
    // may carry its architecture: "vibeswitcher:amd64").
    private bool OwnedByPackage(string location)
    {
        try
        {
            var result = runner.Run(DpkgQuery, ["-S", location], TimeSpan.FromSeconds(10));
            return result.ExitCode == 0 && result.StdOut.Split('\n').Any(line =>
                line.IndexOf(": ", StringComparison.Ordinal) is > 0 and var end
                && line[..end].Split(',').Any(name => name.Trim().Split(':')[0] == "vibeswitcher"));
        }
        catch (Exception) { return false; }
    }

    public void RemoveGnomeShortcuts()
    {
        // The running bridge's own way when it's in use (it holds the lock its updates take);
        // otherwise any left from earlier runs.
        if (liveGrabber() is GnomeShortcutBridge live) live.RemoveAll();
        else if (GnomeShortcutBridge.IsAvailable(runner)) GnomeShortcutBridge.RemoveAllEntries(logger, runner);
    }

    private static string? LastLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
}
