using System.IO;
using VibeSwitcher.Helpers;

namespace VibeSwitcher.Tests;

public class IconHelperTests : IDisposable
{
    private readonly string _iconsDir;

    public IconHelperTests()
    {
        _iconsDir = Path.Combine(Path.GetTempPath(), $"VSIconsTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_iconsDir);
        SessionErrorTracker.Reset();
    }

    public void Dispose()
    {
        SessionErrorTracker.Reset();
        if (Directory.Exists(_iconsDir))
            Directory.Delete(_iconsDir, recursive: true);
    }

    [Fact]
    public void LoadIcon_NullPath_ReturnsDefaultIcon()
    {
        using var icon = IconHelper.LoadIcon(null, _iconsDir);
        Assert.NotNull(icon);
    }

    [Fact]
    public void LoadIcon_EmptyPath_ReturnsDefaultIcon()
    {
        using var icon = IconHelper.LoadIcon(string.Empty, _iconsDir);
        Assert.NotNull(icon);
    }

    [Fact]
    public void LoadIcon_PathOutsideIconsDir_RejectsAndReturnsDefault()
    {
        var outsidePath = Path.Combine(Path.GetTempPath(), "some-other.ico");

        using var icon = IconHelper.LoadIcon(outsidePath, _iconsDir);

        Assert.NotNull(icon);
        Assert.True(SessionErrorTracker.HasErrors, "Should record an IconLoadFailed error for rejected path");
    }

    [Fact]
    public void LoadIcon_TraversalPath_RejectsAndReturnsDefault()
    {
        var traversal = Path.Combine(_iconsDir, "..", "escape.ico");

        using var icon = IconHelper.LoadIcon(traversal, _iconsDir);

        Assert.NotNull(icon); // falls back to default, does not throw
    }

    [Fact]
    public void LoadIcon_NonExistentPathInsideDir_ReturnsDefault()
    {
        var missingPath = Path.Combine(_iconsDir, "ghost.ico");

        using var icon = IconHelper.LoadIcon(missingPath, _iconsDir);

        Assert.NotNull(icon);
    }

    [Fact]
    public void LoadIcon_CorruptIconFile_ReturnsDefaultAndRecordsError()
    {
        // A file that exists inside the icons dir but contains garbage bytes (not a valid ICO).
        var corruptPath = Path.Combine(_iconsDir, "corrupt.ico");
        File.WriteAllBytes(corruptPath, System.Text.Encoding.ASCII.GetBytes("NOT_AN_ICO_FILE"));

        using var icon = IconHelper.LoadIcon(corruptPath, _iconsDir);

        Assert.NotNull(icon);
        Assert.True(SessionErrorTracker.HasErrors, "Should record an IconLoadFailed error for corrupt file");
    }
}
