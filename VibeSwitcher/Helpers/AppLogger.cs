using System.IO;

namespace VibeSwitcher.Helpers;

public static class AppLogger
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VibeSwitcher", "error.log");

    public static void Error(string context, Exception ex)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}: {ex}";

        // Always print to terminal (visible when running via dotnet run)
        Console.Error.WriteLine(line);

        // Also append to log file so errors persist between runs
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, line + Environment.NewLine);
        }
        catch { /* log write failure is non-fatal */ }
    }
}
