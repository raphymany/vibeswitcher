using VibeSwitcher.Platform.Linux.Apps;

namespace VibeSwitcher.Platform.Linux.Tests;

// Finding an app-menu entry's icon file, in icon folders built on disk here.
public class LinuxIconLookupTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("vs-icons-").FullName;
    private readonly string _icons;
    private readonly string _pixmaps;
    private readonly string _config;

    public LinuxIconLookupTests()
    {
        _icons = Path.Combine(_root, "share", "icons");
        _pixmaps = Path.Combine(_root, "pixmaps");
        _config = Path.Combine(_root, "config");
        Directory.CreateDirectory(_pixmaps);
        Directory.CreateDirectory(_config);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Icon(string relative)
    {
        var path = Path.GetFullPath(Path.Combine(_icons, relative)); // (the system's separators)
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
        return path;
    }

    private LinuxIconLookup Lookup() =>
        new(name => name switch { "XDG_CONFIG_HOME" => _config, "HOME" => _root, _ => null }, [_icons], [_pixmaps]);

    [Fact]
    public void ThePngAtLeastAsBigAsAskedFor_ThenSvg_ThenTheBiggest()
    {
        Icon("hicolor/16x16/apps/editor.png");
        var at48 = Icon("hicolor/48x48/apps/editor.png");
        Icon("hicolor/128x128/apps/editor.png");
        var svg = Icon("hicolor/scalable/apps/editor.svg");
        Icon("hicolor/symbolic/apps/editor-symbolic.svg");
        var lookup = Lookup();
        Assert.Equal(at48, lookup.Find("editor", 36));
        Assert.Equal(svg, lookup.Find("editor", 512)); // no PNG that big: the SVG

        Icon("hicolor/16x16/apps/tiny.png");
        var biggest = Icon("hicolor/24x24/apps/tiny.png");
        Assert.Equal(biggest, Lookup().Find("tiny", 36));

        // SVGs: "scalable" first, then one drawn for at least the size (not a simplified 16 px one).
        Icon("Papirus/16x16/apps/viewer.svg");
        var bigSvg = Icon("Papirus/48x48/apps/viewer.svg");
        Icon("Papirus/64x64/apps/viewer.svg");
        File.WriteAllText(Path.Combine(_config, "kdeglobals"), "[Icons]\nTheme=Papirus\n");
        Assert.Equal(bigSvg, Lookup().Find("viewer", 36));
        var scalable = Icon("Papirus/scalable/apps/viewer.svg");
        Assert.Equal(scalable, Lookup().Find("viewer", 36));
        File.Delete(Path.Combine(_config, "kdeglobals"));

        // HiDPI folders count double.
        var retina = Icon("hicolor/24x24@2/apps/sharp.png");
        Icon("hicolor/32x32/apps/sharp.png");
        Assert.Equal(retina, Lookup().Find("sharp", 40));
    }

    [Fact]
    public void TheUsersTheme_ComesFirst_ThenHicolor_ThenPixmaps()
    {
        Icon("hicolor/48x48/apps/terminal.png");
        var themed = Icon("Breeze/apps/48/terminal.svg"); // KDE's layout
        File.WriteAllText(Path.Combine(_config, "kdeglobals"), "[General]\nTheme=Wrong\n[Icons]\nTheme=Breeze\n");
        Assert.Equal(themed, Lookup().Find("terminal", 36));

        // GTK's settings file names it on other desktops.
        File.Delete(Path.Combine(_config, "kdeglobals"));
        var gtk = Icon("Papirus/64x64/apps/terminal.png");
        Directory.CreateDirectory(Path.Combine(_config, "gtk-3.0"));
        File.WriteAllText(Path.Combine(_config, "gtk-3.0", "settings.ini"), "[Settings]\ngtk-icon-theme-name = \"Papirus\"\n");
        Assert.Equal(gtk, Lookup().Find("terminal", 36));

        var pixmap = Path.Combine(_pixmaps, "oldapp.png");
        File.WriteAllText(pixmap, "");
        Assert.Equal(pixmap, Lookup().Find("oldapp", 36));
        Assert.Null(Lookup().Find("missing", 36));
    }

    [Fact]
    public void FileNamesAndPaths_AreAccepted()
    {
        var png = Icon("hicolor/48x48/apps/named.png");
        Assert.Equal(png, Lookup().Find("named.png", 36)); // a name with its extension
        Assert.Null(Lookup().Find("", 36));
        Assert.Null(Lookup().Find(null, 36));
        if (OperatingSystem.IsWindows()) return; // (full paths are Linux ones)
        Assert.Equal(png, Lookup().Find(png, 36));
        Assert.Null(Lookup().Find(png + ".missing.png", 36));
        Assert.Null(Lookup().Find("/usr/share/pixmaps/thing.xpm", 36)); // XPM isn't drawn
    }

    [Theory]
    [InlineData("48x48", 48)]
    [InlineData("256x256@2", 512)]
    [InlineData("48", 48)]
    [InlineData("scalable", 0)]
    [InlineData("symbolic", null)]
    [InlineData("48x48@x", null)]
    [InlineData("0x0", null)]
    public void FolderSizes(string folder, int? size) => Assert.Equal(size, LinuxIconLookup.SizeOf(folder));

    [Fact]
    public void ThemeNames_NeverLeaveTheIconFolders()
    {
        var file = Path.Combine(_config, "settings.ini");
        File.WriteAllText(file, "[Settings]\ngtk-icon-theme-name=../../etc\n");
        Assert.Null(LinuxIconLookup.Setting(file, "[Settings]", "gtk-icon-theme-name"));
        File.WriteAllText(file, "[Settings]\ngtk-icon-theme-name=Yaru\n");
        Assert.Equal("Yaru", LinuxIconLookup.Setting(file, "[Settings]", "gtk-icon-theme-name"));
        Assert.Null(LinuxIconLookup.Setting(Path.Combine(_config, "missing.ini"), "[Settings]", "x"));
    }
}
