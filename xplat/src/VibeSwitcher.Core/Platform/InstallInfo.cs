using System.IO;
using System.Reflection;

namespace VibeSwitcher.Platform;

public enum InstallKind
{
    MacApp,      // a VibeSwitcher.app bundle (Applications or elsewhere)
    AppImage,    // a single-file AppImage
    Package,     // installed by the .deb package (/usr/lib/vibeswitcher)
    Development, // run from a build folder
}

// How this copy of VibeSwitcher was installed and where — for the Settings "Install" section (the
// counterpart of the Windows app's installed-vs-portable detection).
//
// Translocated: macOS runs a downloaded, unsigned app that hasn't been moved yet from a random
// read-only copy (".../AppTranslocation/<id>/d/VibeSwitcher.app") that disappears on restart —
// nothing (like the login item) may point there; the user should move the app to Applications.
public sealed record InstallInfo(InstallKind Kind, string Location, string Version, bool Translocated = false)
{
    public static InstallInfo Current() => Detect(
        Environment.ProcessPath ?? AppContext.BaseDirectory,
        TrustedAppImage(Environment.GetEnvironmentVariable, Environment.ProcessPath),
        OperatingSystem.IsMacOS(),
        AppVersion());

    internal static InstallInfo Detect(string processPath, string? appImage, bool macOS, string version)
    {
        if (macOS)
        {
            // …/VibeSwitcher.app/Contents/MacOS/VibeSwitcher → the bundle.
            int i = processPath.IndexOf(".app/Contents/MacOS/", StringComparison.Ordinal);
            if (i > 0)
                return new(InstallKind.MacApp, processPath[..(i + 4)], version,
                    Translocated: processPath.Contains("/AppTranslocation/", StringComparison.Ordinal));
        }
        else
        {
            if (appImage != null) return new(InstallKind.AppImage, appImage, version);
            if (processPath.StartsWith("/usr/lib/vibeswitcher/", StringComparison.Ordinal) ||
                processPath.StartsWith("/opt/vibeswitcher/", StringComparison.Ordinal))
                return new(InstallKind.Package, Path.GetDirectoryName(processPath)!, version);
        }
        return new(InstallKind.Development, Path.GetDirectoryName(processPath) ?? processPath, version);
    }

    // The AppImage file this process runs from, or null. $APPIMAGE alone isn't enough: child
    // processes inherit it, so VibeSwitcher started from a terminal or editor that is itself an
    // AppImage would see that program's file. The runtime also sets $APPDIR to the AppImage's mount,
    // and only our own process runs from inside it.
    public static string? TrustedAppImage(Func<string, string?> env, string? processPath)
    {
        var image = env("APPIMAGE");
        var dir = env("APPDIR");
        if (string.IsNullOrEmpty(image) || !Path.IsPathRooted(image) || string.IsNullOrEmpty(dir) || processPath == null) return null;
        var mount = dir.EndsWith('/') ? dir : dir + "/";
        return processPath.StartsWith(mount, StringComparison.Ordinal) ? image : null;
    }

    // The same version number the Windows app shows: the assembly's version (x.y.z).
    public static string AppVersion()
    {
        var asm = Assembly.GetEntryAssembly() ?? typeof(InstallInfo).Assembly;
        var v = asm.GetName().Version;
        return v == null ? "0.0.0" : $"{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}";
    }
}
