using System.IO;
using VibeSwitcher.Helpers;

namespace VibeSwitcher.Tests;

public class AppLoggerTests : IDisposable
{
    private readonly string _logFile;

    public AppLoggerTests()
    {
        _logFile = Path.Combine(Path.GetTempPath(), $"VSLogTest_{Guid.NewGuid():N}.log");
        AppLogger._logPathOverride = _logFile;
    }

    public void Dispose()
    {
        AppLogger._logPathOverride = null;
        TryDelete(_logFile);
        TryDelete(_logFile + ".1");
        TryDelete(_logFile + ".2");
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    [Fact]
    public void Info_WritesInfoLevel()
    {
        AppLogger.Info("Ctx", "hello");
        Assert.Contains("[INFO]", File.ReadAllText(_logFile));
    }

    [Fact]
    public void Warning_WritesWarnLevel()
    {
        AppLogger.Warning("Ctx", "warn");
        Assert.Contains("[WARN]", File.ReadAllText(_logFile));
    }

    [Fact]
    public void Error_WritesErrorLevel()
    {
        AppLogger.Error("Ctx", "err");
        Assert.Contains("[ERROR]", File.ReadAllText(_logFile));
    }

    [Fact]
    public void Write_IncludesContextAndMessage()
    {
        AppLogger.Info("MyContext", "my message");
        var content = File.ReadAllText(_logFile);
        Assert.Contains("MyContext", content);
        Assert.Contains("my message", content);
    }

    [Fact]
    public void Write_IsNonFatalWhenFileLocked()
    {
        using var fs = new FileStream(_logFile, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        var ex = Record.Exception(() => AppLogger.Info("Test", "locked write"));
        Assert.Null(ex);
    }

    [Fact]
    public void Rotate_WhenFileExceedsMaxSize_CreatesBackupDotOne()
    {
        // Pre-fill with slightly more than 1 MB
        File.WriteAllBytes(_logFile, new byte[1024 * 1024 + 1]);

        AppLogger.Info("Test", "trigger rotation");

        Assert.True(File.Exists(_logFile + ".1"), "Backup .1 must be created after rotation");
    }

    [Fact]
    public void Rotate_OldBackupShiftedToTwo()
    {
        File.WriteAllBytes(_logFile,         new byte[1024 * 1024 + 1]);
        File.WriteAllText(_logFile + ".1",   "old backup 1");

        AppLogger.Info("Test", "trigger rotation");

        Assert.True(File.Exists(_logFile + ".2"), "Old .1 should be moved to .2");
    }

    [Fact]
    public void LogPath_IsUnchangedByOverride()
    {
        // LogPath is the real production path; override should not change it
        Assert.DoesNotContain("VSLogTest", AppLogger.LogPath);
    }
}
