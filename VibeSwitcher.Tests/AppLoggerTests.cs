using System.IO;
using VibeSwitcher.Helpers;

namespace VibeSwitcher.Tests;

public class AppLoggerTests : IDisposable
{
    private readonly string _logDir;
    private readonly string _logFile;
    private readonly AppLogger _logger;

    public AppLoggerTests()
    {
        _logDir  = Path.Combine(Path.GetTempPath(), $"VSLogTest_{Guid.NewGuid():N}");
        _logFile = Path.Combine(_logDir, "error.log");
        _logger  = new AppLogger(_logDir);
    }

    public void Dispose()
    {
        TryDelete(_logFile);
        TryDelete(_logFile + ".1");
        TryDelete(_logFile + ".2");
        TryDeleteDir(_logDir);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void TryDeleteDir(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { }
    }

    [Fact]
    public void Info_WritesInfoLevel()
    {
        _logger.Info("Ctx", "hello");
        Assert.Contains("[INFO]", File.ReadAllText(_logFile));
    }

    [Fact]
    public void Warning_WritesWarnLevel()
    {
        _logger.Warning("Ctx", "warn");
        Assert.Contains("[WARN]", File.ReadAllText(_logFile));
    }

    [Fact]
    public void Error_WritesErrorLevel()
    {
        _logger.Error("Ctx", "err");
        Assert.Contains("[ERROR]", File.ReadAllText(_logFile));
    }

    [Fact]
    public void Write_IncludesContextAndMessage()
    {
        _logger.Info("MyContext", "my message");
        var content = File.ReadAllText(_logFile);
        Assert.Contains("MyContext", content);
        Assert.Contains("my message", content);
    }

    [Fact]
    public void Write_IsNonFatalWhenFileLocked()
    {
        using var fs = new FileStream(_logFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var ex = Record.Exception(() => _logger.Info("Test", "locked write"));
        Assert.Null(ex);
    }

    [Fact]
    public void Rotate_WhenFileExceedsMaxSize_CreatesBackupDotOne()
    {
        File.WriteAllBytes(_logFile, new byte[1024 * 1024 + 1]);

        _logger.Info("Test", "trigger rotation");

        Assert.True(File.Exists(_logFile + ".1"), "Backup .1 must be created after rotation");
    }

    [Fact]
    public void Rotate_OldBackupShiftedToTwo()
    {
        File.WriteAllBytes(_logFile,        new byte[1024 * 1024 + 1]);
        File.WriteAllText(_logFile + ".1",  "old backup 1");

        _logger.Info("Test", "trigger rotation");

        Assert.True(File.Exists(_logFile + ".2"), "Old .1 should be moved to .2");
    }

    [Fact]
    public void LogPath_IsUnderAppDataVibeSwitcher()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        Assert.StartsWith(appData, AppLogger.LogPath, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("VibeSwitcher", AppLogger.LogPath);
    }
}
