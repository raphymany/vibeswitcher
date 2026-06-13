using System.IO;

namespace VibeSwitcher.Helpers;

public class AppLogger : IAppLogger
{
    public static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VibeSwitcher", "error.log");

    private const long MaxLogBytes = 1 * 1024 * 1024; // 1 MB
    private const int BackupCount = 2;

    private readonly string _effectivePath;
    // Serializes rotation + append across threads (logger is a shared singleton called from
    // UI, background, COM-notification and HID threads).
    private readonly object _writeLock = new();

    public AppLogger(string? logDir = null)
    {
        _effectivePath = logDir != null
            ? Path.Combine(logDir, "error.log")
            : LogPath;
        StartSession();
    }

    public void Debug(string context, string message) =>
        Console.Error.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [DEBUG] {context}: {message}");

    public void Info(string context, string message)    => Write("INFO",  context, message);
    public void Warning(string context, string message) => Write("WARN",  context, message);
    public void Error(string context, string message)   => Write("ERROR", context, message);
    public void Error(string context, Exception ex)     => Write("ERROR", context, ex.ToString());

    private void StartSession()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_effectivePath)!);
            File.WriteAllText(_effectivePath, string.Empty);
        }
        catch { }
    }

    private void Write(string level, string context, string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {context}: {message}";

        Console.Error.WriteLine(line);

        try
        {
            lock (_writeLock)
            {
                RotateIfNeeded();
                File.AppendAllText(_effectivePath, line + Environment.NewLine);
            }
        }
        catch { /* log write failure is non-fatal */ }
    }

    private void RotateIfNeeded()
    {
        if (!File.Exists(_effectivePath)) return;
        if (new FileInfo(_effectivePath).Length < MaxLogBytes) return;

        for (int i = BackupCount; i >= 1; i--)
        {
            var older = $"{_effectivePath}.{i}";
            var newer = i == 1 ? _effectivePath : $"{_effectivePath}.{i - 1}";
            if (File.Exists(older)) File.Delete(older);
            if (File.Exists(newer)) File.Move(newer, older);
        }
    }
}
