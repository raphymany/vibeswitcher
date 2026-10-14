using System.Diagnostics;
using System.IO;

namespace VibeSwitcher.App.Settings;

// Opens web links and folders with the system's own apps — what the Windows app does with
// Process.Start (shell execute) and explorer.exe: `open` on macOS, `xdg-open` on Linux.
internal static class SystemOpener
{
    // Web pages only (the links in About and the FAQ): any other kind of address could start
    // whatever program the system has registered for it.
    public static bool OpenUrl(string url, out string error)
    {
        if (!IsWebUrl(url)) { error = $"'{url}' isn't a web address."; return false; }
        return Run(UrlCommand(OperatingSystem.IsMacOS(), url), out error);
    }

    public static bool OpenFolder(string folder, out string error)
    {
        if (!Path.IsPathRooted(folder) || !Directory.Exists(folder)) { error = $"'{folder}' doesn't exist."; return false; }
        return Run(OpenCommand(OperatingSystem.IsMacOS(), folder), out error);
    }

    // A document (the error log) in the system's app for it.
    public static bool OpenFile(string file, out string error)
    {
        if (!Path.IsPathRooted(file) || !File.Exists(file)) { error = $"'{file}' doesn't exist."; return false; }
        return Run(OpenCommand(OperatingSystem.IsMacOS(), file), out error);
    }

    // Shows a file or app in its folder: selected in Finder on macOS (opening a .app would start it);
    // its folder on Linux (file managers differ in how to select a file).
    public static bool Reveal(string path, out string error)
    {
        if (!Path.IsPathRooted(path) || !(File.Exists(path) || Directory.Exists(path))) { error = $"'{path}' doesn't exist."; return false; }
        return Run(RevealCommand(OperatingSystem.IsMacOS(), path), out error);
    }

    internal static bool IsWebUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    internal static (string Program, string[] Args) UrlCommand(bool macOS, string url) =>
        macOS ? ("/usr/bin/open", [url]) : ("xdg-open", [url]);

    // A folder or document, opened with its default app.
    internal static (string Program, string[] Args) OpenCommand(bool macOS, string path) =>
        macOS ? ("/usr/bin/open", [path]) : ("xdg-open", [path]);

    internal static (string Program, string[] Args) RevealCommand(bool macOS, string path) =>
        macOS ? ("/usr/bin/open", ["-R", path]) : ("xdg-open", [ParentOf(path)]);

    // (Unix paths whatever system the tests run on.)
    private static string ParentOf(string path)
    {
        var p = path.TrimEnd('/');
        int i = p.LastIndexOf('/');
        return i > 0 ? p[..i] : "/";
    }

    private static bool Run((string Program, string[] Args) command, out string error)
    {
        var path = Path.IsPathRooted(command.Program) ? command.Program : SystemSoundSettings.FindOnPath(command.Program);
        if (path == null) { error = $"{command.Program} wasn't found."; return false; }
        try
        {
            var start = new ProcessStartInfo(path) { UseShellExecute = false };
            foreach (var a in command.Args) start.ArgumentList.Add(a);
            using var _ = Process.Start(start);
            error = "";
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
