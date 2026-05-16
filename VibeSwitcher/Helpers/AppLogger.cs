using System.IO;

namespace VibeSwitcher.Helpers;

public static class AppLogger
{
    public static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VibeSwitcher", "error.log");

    private const long MaxLogBytes = 1 * 1024 * 1024; // 1 MB
    private const int BackupCount = 2;

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
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            RotateIfNeeded();
            File.AppendAllText(LogPath, line + Environment.NewLine);
        }
        catch { /* log write failure is non-fatal */ }
    }

    private static void RotateIfNeeded()
    {
        if (!File.Exists(LogPath)) return;
        if (new FileInfo(LogPath).Length < MaxLogBytes) return;

        // Shift backups: .2 → delete, .1 → .2, error.log → .1
        for (int i = BackupCount; i >= 1; i--)
        {
            var older = $"{LogPath}.{i}";
            var newer = i == 1 ? LogPath : $"{LogPath}.{i - 1}";
            if (File.Exists(older)) File.Delete(older);
            if (File.Exists(newer)) File.Move(newer, older);
        }
    }
}
