using System.IO;
using VibeSwitcher.Helpers;

namespace VibeSwitcher.Tests;

public class IconHelperTests : IDisposable
{
    private readonly string _iconsDir;
    private readonly FakeSessionErrorTracker _errorTracker;

    public IconHelperTests()
    {
        _iconsDir = Path.Combine(Path.GetTempPath(), $"VSIconsTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_iconsDir);
        _errorTracker = new FakeSessionErrorTracker();
        AppLog.Register(new FakeAppLogger());
        AppErrors.Register(_errorTracker);
    }

    public void Dispose()
    {
        AppLog.Register(null);
        AppErrors.Register(null);
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
        Assert.True(_errorTracker.HasErrors, "Should record an IconLoadFailed error for rejected path");
    }

    [Fact]
    public void LoadIcon_TraversalPath_RejectsAndReturnsDefault()
    {
        var traversal = Path.Combine(_iconsDir, "..", "escape.ico");

        using var icon = IconHelper.LoadIcon(traversal, _iconsDir);

        Assert.NotNull(icon);
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
        var corruptPath = Path.Combine(_iconsDir, "corrupt.ico");
        File.WriteAllBytes(corruptPath, System.Text.Encoding.ASCII.GetBytes("NOT_AN_ICO_FILE"));

        using var icon = IconHelper.LoadIcon(corruptPath, _iconsDir);

        Assert.NotNull(icon);
        Assert.True(_errorTracker.HasErrors, "Should record an IconLoadFailed error for corrupt file");
    }
}
