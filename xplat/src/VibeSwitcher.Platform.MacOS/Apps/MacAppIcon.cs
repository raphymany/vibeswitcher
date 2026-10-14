using System.Buffers.Binary;
using System.Xml.Linq;

namespace VibeSwitcher.Platform.MacOS.Apps;

// An app's icon as PNG data, read from its bundle: the .icns file Contents/Info.plist names
// (CFBundleIconFile, in Contents/Resources, ".icns" sometimes left off) — or, when the plist isn't
// plain XML or names none, Resources/AppIcon.icns, <app name>.icns, or the only .icns there. An
// .icns file holds the icon at several sizes; modern ones as PNG pictures, which is what's used.
// Null when there's none (apps that keep their icon only in an asset catalog): the picker then
// shows no icon for it, as Windows does for a program without one.
public static class MacAppIcon
{
    public static byte[]? Png(string bundle, int pixels)
    {
        try
        {
            if (IcnsFile(bundle) is not { } icns) return null;
            using var stream = File.OpenRead(icns); // (only the entry headers and the chosen picture are read)
            return BestPng(stream, pixels);
        }
        catch (Exception)
        {
            return null;
        }
    }

    internal static string? IcnsFile(string bundle)
    {
        var resources = Path.Combine(bundle, "Contents", "Resources");
        if (PlistIconFile(Path.Combine(bundle, "Contents", "Info.plist")) is { } named)
        {
            var file = Path.Combine(resources, Path.GetFileName(named)); // (a name only: never a path out of the bundle)
            if (!file.EndsWith(".icns", StringComparison.OrdinalIgnoreCase)) file += ".icns";
            if (File.Exists(file)) return file;
        }
        var appName = Path.GetFileNameWithoutExtension(bundle.TrimEnd('/'));
        foreach (var guess in new[] { "AppIcon.icns", appName + ".icns" })
            if (File.Exists(Path.Combine(resources, guess))) return Path.Combine(resources, guess);
        try
        {
            var all = Directory.GetFiles(resources, "*.icns");
            return all.Length == 1 ? all[0] : null;
        }
        catch (Exception) { return null; }
    }

    // CFBundleIconFile from an XML property list (null for a binary one, or when it isn't there).
    internal static string? PlistIconFile(string plist)
    {
        try
        {
            if (new FileInfo(plist) is not { Exists: true, Length: <= 1 << 20 }) return null; // (an Info.plist is a few KB)
            using var stream = File.OpenRead(plist);
            var head = new byte[6];
            if (stream.Read(head, 0, 6) < 6 || System.Text.Encoding.ASCII.GetString(head) == "bplist") return null;
            stream.Position = 0;
            // (Its DOCTYPE line is skipped, never fetched.)
            using var reader = System.Xml.XmlReader.Create(stream, new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Ignore, XmlResolver = null });
            var doc = XDocument.Load(reader);
            var dict = doc.Root?.Element("dict");
            var key = dict?.Elements("key").FirstOrDefault(k => k.Value == "CFBundleIconFile");
            var value = key?.ElementsAfterSelf().FirstOrDefault();
            return value?.Name == "string" && value.Value.Trim().Length > 0 ? value.Value.Trim() : null;
        }
        catch (Exception) { return null; }
    }

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    // The .icns layout: "icns", the file's length, then entries — a 4-letter type, the entry's
    // length (both big-endian, header included) and its data. Of the PNG entries, the smallest at
    // least `pixels` wide, else the biggest (one over 8 MB isn't read).
    internal static byte[]? BestPng(byte[] icns, int pixels) => BestPng(new MemoryStream(icns, writable: false), pixels);

    internal static byte[]? BestPng(Stream icns, int pixels)
    {
        var head = new byte[24];
        if (!Read(icns, 0, head, 8) || head[0] != 'i' || head[1] != 'c' || head[2] != 'n' || head[3] != 's') return null;
        long end = Math.Min(icns.Length, BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(4)));
        var pngs = new List<(int Width, long Offset, long Length)>();
        for (long at = 8; at + 8 <= end;)
        {
            if (!Read(icns, at, head, 8)) break;
            long length = BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(4));
            if (length < 8 || length > end - at) break;
            // A PNG: the signature, then the IHDR chunk with the width at byte 16.
            if (length - 8 >= 24 && Read(icns, at + 8, head, 24) && head.AsSpan(0, 8).SequenceEqual(PngSignature))
                pngs.Add(((int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(16)), int.MaxValue), at + 8, length - 8));
            at += length;
        }
        if (pngs.Count == 0) return null;
        var best = pngs.Where(p => p.Width >= pixels).OrderBy(p => p.Width).Cast<(int, long, long)?>().FirstOrDefault()
            ?? pngs.OrderByDescending(p => p.Width).First();
        if (best.Item3 > MaxPng) return null;
        var png = new byte[best.Item3];
        return Read(icns, best.Item2, png, png.Length) ? png : null;
    }

    private const long MaxPng = 8 << 20;

    private static bool Read(Stream stream, long at, byte[] buffer, int count)
    {
        stream.Position = at;
        return stream.ReadAtLeast(buffer.AsSpan(0, count), count, throwOnEndOfStream: false) == count;
    }
}
