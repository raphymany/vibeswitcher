using VibeSwitcher.Helpers;

namespace VibeSwitcher.Tests;

public class UploadLibraryTests : IDisposable
{
    private readonly string _libDir =
        Path.Combine(Path.GetTempPath(), "VibeSwitcherTests", "Lib-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_libDir, recursive: true); } catch { }
    }

    private string MakeSource(string name, byte[] bytes)
    {
        var dir = Path.Combine(_libDir, "..", "src-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void Save_CopiesFilePreservingName()
    {
        var src = MakeSource("MyIcon.ico", [1, 2, 3]);

        var saved = UploadLibrary.Save(src, _libDir);

        Assert.NotNull(saved);
        Assert.Equal("MyIcon.ico", Path.GetFileName(saved));
        Assert.True(File.Exists(saved));
    }

    [Fact]
    public void Save_SameNameSameSize_ReusesExistingEntry()
    {
        var src1 = MakeSource("dup.wav", [1, 2, 3, 4]);
        var src2 = MakeSource("dup.wav", [9, 9, 9, 9]); // same name, same length

        var first  = UploadLibrary.Save(src1, _libDir);
        var second = UploadLibrary.Save(src2, _libDir);

        Assert.Equal(first, second); // treated as the same upload — no duplicate
        Assert.Single(UploadLibrary.List(_libDir, "*.wav"));
    }

    [Fact]
    public void Save_SameNameDifferentSize_AddsSuffix()
    {
        var src1 = MakeSource("clash.ico", [1, 2, 3]);
        var src2 = MakeSource("clash.ico", [1, 2, 3, 4, 5]); // same name, different length

        var first  = UploadLibrary.Save(src1, _libDir);
        var second = UploadLibrary.Save(src2, _libDir);

        Assert.NotEqual(first, second);
        Assert.Equal(2, UploadLibrary.List(_libDir, "*.ico").Count);
    }

    [Fact]
    public void Save_MissingSource_ReturnsNull()
    {
        var result = UploadLibrary.Save(Path.Combine(_libDir, "nope.ico"), _libDir);
        Assert.Null(result);
    }

    [Fact]
    public void List_NonexistentDir_ReturnsEmpty()
    {
        Assert.Empty(UploadLibrary.List(Path.Combine(_libDir, "missing"), "*.ico"));
    }

    [Fact]
    public void Delete_RemovesFile()
    {
        var src = MakeSource("gone.ico", [1]);
        var saved = UploadLibrary.Save(src, _libDir)!;
        Assert.True(File.Exists(saved));

        UploadLibrary.Delete(saved);

        Assert.False(File.Exists(saved));
    }
}
