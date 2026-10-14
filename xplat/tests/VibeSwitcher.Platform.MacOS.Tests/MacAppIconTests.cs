using System.Buffers.Binary;
using System.Text;
using VibeSwitcher.Platform.MacOS.Apps;

namespace VibeSwitcher.Platform.MacOS.Tests;

// App icons for the app picker, read from .app bundles built on disk here.
public class MacAppIconTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("vs-macicon-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    // A PNG's start: the signature and an IHDR chunk giving its width (enough for the .icns reader).
    private static byte[] Png(int width)
    {
        var png = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(png, 0);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(8), 13);
        Encoding.ASCII.GetBytes("IHDR").CopyTo(png, 12);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(16), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(20), (uint)width);
        return png;
    }

    private static byte[] Icns(params (string Type, byte[] Data)[] entries)
    {
        var body = new List<byte>();
        foreach (var (type, data) in entries)
        {
            body.AddRange(Encoding.ASCII.GetBytes(type));
            var length = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(length, (uint)(data.Length + 8));
            body.AddRange(length);
            body.AddRange(data);
        }
        var file = new byte[8 + body.Count];
        Encoding.ASCII.GetBytes("icns").CopyTo(file, 0);
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(4), (uint)file.Length);
        body.CopyTo(file, 8);
        return file;
    }

    private static readonly byte[] Sample = Icns(("ic07", Png(128)), ("is32", new byte[40]), ("ic11", Png(32)), ("ic12", Png(64)));

    [Theory]
    [InlineData(36, 64)]   // the smallest at least that big
    [InlineData(16, 32)]
    [InlineData(64, 64)]
    [InlineData(300, 128)] // none big enough: the biggest
    public void TheBestPicture_IsChosen_AmongThePngOnes(int pixels, int width)
    {
        var png = MacAppIcon.BestPng(Sample, pixels)!;
        Assert.Equal(width, (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16)));
        Assert.Equal(33, png.Length);
    }

    [Fact]
    public void NotAnIcnsFile_OrNoPngInIt_GivesNothing()
    {
        Assert.Null(MacAppIcon.BestPng(Encoding.ASCII.GetBytes("nope, not an icon"), 36));
        Assert.Null(MacAppIcon.BestPng(Icns(("is32", new byte[40])), 36));
        // A broken entry length ends the reading without failing.
        var broken = Icns(("ic12", Png(64)));
        BinaryPrimitives.WriteUInt32BigEndian(broken.AsSpan(12), 9999);
        Assert.Null(MacAppIcon.BestPng(broken, 36));
        // A length near 4 GB doesn't overflow into a read elsewhere; the good entry before it still counts.
        var huge = Icns(("ic12", Png(64)), ("ic07", Png(128)));
        BinaryPrimitives.WriteUInt32BigEndian(huge.AsSpan(8 + 8 + 33 + 4), 0xFFFFFFF0);
        Assert.Equal(64, (int)BinaryPrimitives.ReadUInt32BigEndian(MacAppIcon.BestPng(huge, 36)!.AsSpan(16)));
    }

    private string Bundle(string name, string? plist, params string[] icnsFiles)
    {
        var bundle = Path.Combine(_dir, name + ".app");
        var resources = Path.Combine(bundle, "Contents", "Resources");
        Directory.CreateDirectory(resources);
        if (plist != null) File.WriteAllText(Path.Combine(bundle, "Contents", "Info.plist"), plist);
        foreach (var icns in icnsFiles) File.WriteAllBytes(Path.Combine(resources, icns), Sample);
        return bundle;
    }

    private static string Plist(string iconFile) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <dict>
            <key>CFBundleName</key>
            <string>Tool</string>
            <key>CFBundleIconFile</key>
            <string>{iconFile}</string>
        </dict>
        </plist>
        """;

    [Fact]
    public void TheIconFile_IsTheOneInfoPlistNames()
    {
        // Named without ".icns", as many apps do; another .icns is there too.
        var bundle = Bundle("Tool", Plist("ToolIcon"), "ToolIcon.icns", "Document.icns");
        Assert.Equal(Path.Combine(bundle, "Contents", "Resources", "ToolIcon.icns"), MacAppIcon.IcnsFile(bundle));
        Assert.NotNull(MacAppIcon.Png(bundle, 36));

        // A name with a path in it stays inside the bundle.
        bundle = Bundle("Sneaky", Plist("../../../Sneaky/Contents/Resources/ToolIcon"), "ToolIcon.icns");
        Assert.Equal(Path.Combine(bundle, "Contents", "Resources", "ToolIcon.icns"), MacAppIcon.IcnsFile(bundle));
    }

    [Fact]
    public void WithoutAReadablePlist_TheUsualNamesAreTried()
    {
        // A binary plist (not read): AppIcon.icns.
        var bundle = Bundle("Binary", "bplist00\u0001\u0002", "AppIcon.icns", "Other.icns");
        Assert.EndsWith("AppIcon.icns", MacAppIcon.IcnsFile(bundle));
        // The app's own name.
        bundle = Bundle("Named", null, "Named.icns", "Other.icns");
        Assert.EndsWith("Named.icns", MacAppIcon.IcnsFile(bundle));
        // The only one there.
        bundle = Bundle("Single", Plist("Missing"), "Whatever.icns");
        Assert.EndsWith("Whatever.icns", MacAppIcon.IcnsFile(bundle));
        // Several, none of them the obvious one; or none at all: no icon.
        Assert.Null(MacAppIcon.IcnsFile(Bundle("Unclear", null, "A.icns", "B.icns")));
        Assert.Null(MacAppIcon.Png(Bundle("Empty", null), 36));
        Assert.Null(MacAppIcon.Png(Path.Combine(_dir, "Missing.app"), 36));
    }
}
