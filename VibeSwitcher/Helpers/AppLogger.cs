using System.IO;

namespace VibeSwitcher.Helpers;

public static class AppLogger
{
    public static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VibeSwitcher", "error.log");

    // Overrides LogPath — set by Initialize() for portable mode, or by tests.
    internal static volatile string? _logPathOverride;

    private static string EffectivePath => _logPathOverride ?? LogPath;

    private const long MaxLogBytes = 1 * 1024 * 1024; // 1 MB
    private const int BackupCount = 2;

    // Call once at startup before StartSession() when running in portable mode.
    public static void Initialize(string baseDir)
    {
        _logPathOverride = Path.Combine(baseDir, "error.log");
    }

    // Truncate the log at startup so each session starts with a clean file.
    public static void StartSession()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(EffectivePath)!);
            File.WriteAllText(EffectivePath, string.Empty);
        }
        catch { }
    }

    public static void Info(string context, string message)    => Write("INFO",  context, message);
    public static void Warning(string context, string message) => Write("WARN",  context, message);
    public static void Error(string context, string message)   => Write("ERROR", context, message);
    public static void Error(string context, Exception ex)     => Write("ERROR", context, ex.ToString());

    private static void Write(string level, string context, string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {context}: {message}";

        Console.Error.WriteLine(line);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(EffectivePath)!);
            RotateIfNeeded();
            File.AppendAllText(EffectivePath, line + Environment.NewLine);
        }
        catch { /* log write failure is non-fatal */ }
    }

    private static void RotateIfNeeded()
    {
        var path = EffectivePath;
        if (!File.Exists(path)) return;
        if (new FileInfo(path).Length < MaxLogBytes) return;

        // Shift backups: .2 → delete, .1 → .2, error.log → .1
        for (int i = BackupCount; i >= 1; i--)
        {
            var older = $"{path}.{i}";
            var newer = i == 1 ? path : $"{path}.{i - 1}";
            if (File.Exists(older)) File.Delete(older);
            if (File.Exists(newer)) File.Move(newer, older);
        }
    }
}
