using System.IO;
using VibeSwitcher.Controls;
using VibeSwitcher.Helpers;

namespace VibeSwitcher.Tests;

// The xplat forks the shared Settings logic uses: icon loading and the gallery's icon files (their
// drawing is the app's, tested there), and the logo animation setting.
public class SettingsLogicTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"VSSettingsLogic_{Guid.NewGuid():N}");
    private readonly FakeSessionErrorTracker _errors = new();

    public SettingsLogicTests()
    {
        Directory.CreateDirectory(_dir);
        AppLog.Register(new FakeAppLogger());
        AppErrors.Register(_errors);
    }

    public void Dispose()
    {
        IconHelper.ImageFactory = null;
        GalleryIconHelper.GlyphRenderer = null;
        AppLog.Register(null);
        AppErrors.Register(null);
        Directory.Delete(_dir, recursive: true);
    }

    private sealed class Picture(byte[] data) : System.Windows.Media.ImageSource
    {
        public byte[] Data { get; } = data;
    }

    [Fact]
    public void TheDefaultIcon_IsTheWindowsAppIcon()
    {
        using var icon = IconHelper.LoadIcon(null, _dir);
        Assert.True(IconHelper.IsIcoFile(icon.Data));
        Assert.Equal(File.ReadAllBytes(Path.Combine(RepoRoot(), "VibeSwitcher", "Resources", "Icons", "vs-icon.ico")), icon.Data);

        icon.Data[0] = 0xFF; // one caller changing its copy doesn't affect the next
        using var again = IconHelper.GetDefaultIcon();
        Assert.True(IconHelper.IsIcoFile(again.Data));
    }

    [Fact]
    public void AHugeFileInTheIconsFolder_IsNotRead()
    {
        var path = Path.Combine(_dir, "huge.ico");
        using (var f = File.Create(path)) f.SetLength(IconHelper.MaxIconBytes + 1);
        using var icon = IconHelper.LoadIcon(path, _dir);
        Assert.NotEqual(IconHelper.MaxIconBytes + 1, icon.Data.Length); // the default icon instead
        Assert.True(_errors.HasErrors);
    }

    [Fact]
    public void AnIconInTheIconsFolder_IsLoadedAsIs()
    {
        var path = Path.Combine(_dir, "mine.ico");
        GalleryIconHelper.WriteSingleFrameIco([1, 2, 3, 4], 64, path);
        using var icon = IconHelper.LoadIcon(path, _dir);
        Assert.Equal(File.ReadAllBytes(path), icon.Data);
        Assert.False(_errors.HasErrors);
    }

    [Theory]
    [InlineData(new byte[] { 0, 0, 1, 0 })]                                         // too short
    [InlineData(new byte[] { 0, 0, 2, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 })] // a cursor, not an icon
    [InlineData(new byte[] { 0, 0, 1, 0, 0, 0 })]                                   // no pictures
    [InlineData(new byte[] { 0, 0, 1, 0, 1, 0, 64, 64, 0, 0, 1, 0, 32, 0, 100, 0, 0, 0, 22, 0, 0, 0 })] // picture past the end
    public void NotAnIcon_IsRefused(byte[] data) => Assert.False(IconHelper.IsIcoFile(data));

    [Fact]
    public void WithoutTheApp_ThereAreNoPictures_AndGalleryIconsCantBeDrawn()
    {
        using var icon = IconHelper.GetDefaultIcon();
        Assert.Null(IconHelper.ToImageSource(icon));
        Assert.Throws<InvalidOperationException>(() =>
            GalleryIconHelper.SaveGalleryIcon(GalleryIconHelper.Items[0], Path.Combine(_dir, "x.ico")));
        Assert.False(File.Exists(Path.Combine(_dir, "x.ico")));
    }

    [Fact]
    public void Pictures_AreMadeByTheApp()
    {
        IconHelper.ImageFactory = data => new Picture(data);
        using var icon = IconHelper.GetDefaultIcon();
        Assert.Same(icon.Data, Assert.IsType<Picture>(IconHelper.ToImageSource(icon)).Data);

        IconHelper.ImageFactory = _ => throw new InvalidDataException("bad");
        Assert.Throws<InvalidDataException>(() => IconHelper.ToImageSource(icon));
        Assert.True(_errors.HasErrors);
    }

    [Fact]
    public void GalleryIcons_AreOnePngPictureIconFiles()
    {
        byte[] png = [0x89, (byte)'P', (byte)'N', (byte)'G', 1, 2, 3];
        (string Emoji, IconColor Color, int Size)? asked = null;
        GalleryIconHelper.GlyphRenderer = (emoji, color, size) => { asked = (emoji, color, size); return png; };

        var path = Path.Combine(_dir, "sub", "Gaming.ico");
        GalleryIconHelper.SaveGalleryIcon(GalleryIconHelper.FindByName(" Games ")!, path, IconColor.White);

        Assert.Equal(("🎮", IconColor.White, 64), asked);
        var data = File.ReadAllBytes(path);
        Assert.True(IconHelper.IsIcoFile(data));
        Assert.Equal(64, data[6]);
        Assert.Equal(png, data[22..]);
    }

    [Fact]
    public void LogoAnimation_Setting()
    {
        Assert.Equal(LogoAnimator.AnimMode.Reduced, LogoAnimator.Parse("Reduced"));
        Assert.Equal(LogoAnimator.AnimMode.Static, LogoAnimator.Parse("Static"));
        Assert.Equal(LogoAnimator.AnimMode.Full, LogoAnimator.Parse("whatever"));

        var before = LogoAnimator.Mode;
        int changes = 0;
        void Changed() => changes++;
        LogoAnimator.ModeChanged += Changed;
        try
        {
            LogoAnimator.Mode = LogoAnimator.AnimMode.Static;
            LogoAnimator.Mode = LogoAnimator.AnimMode.Static;
            Assert.Equal(before == LogoAnimator.AnimMode.Static ? 0 : 1, changes);
        }
        finally
        {
            LogoAnimator.ModeChanged -= Changed;
            LogoAnimator.Mode = before;
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "xplat", "forks.txt"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
