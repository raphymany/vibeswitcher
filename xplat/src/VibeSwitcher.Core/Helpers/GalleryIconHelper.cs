using System.IO;

namespace VibeSwitcher.Helpers;

public enum IconColor { Auto, Black, White }

public class GalleryItem
{
    public string Emoji { get; }
    public string Label { get; }
    public string[] Keywords { get; }

    public GalleryItem(string emoji, string label, string[] keywords)
    {
        Emoji = emoji;
        Label = label;
        Keywords = keywords;
    }
}

public class GalleryPickResult
{
    public GalleryItem? Item { get; init; }
    public bool BrowseFromDisk { get; init; }
    public IconColor IconColor { get; init; }
    public string? CustomIconPath { get; init; } // set when the user re-picks a saved upload
}

// Fork of the Windows GalleryIconHelper: the same gallery, name matching and .ico format. Drawing a
// glyph needs the UI toolkit, so the app supplies it (GlyphRenderer).
public static class GalleryIconHelper
{
    public static readonly IReadOnlyList<GalleryItem> Items = new[]
    {
        new GalleryItem("🎮", "Gaming",    new[] { "gaming", "game", "games" }),
        new GalleryItem("💼", "Work",      new[] { "work", "office", "business", "job" }),
        new GalleryItem("🎵", "Music",     new[] { "music", "audio" }),
        new GalleryItem("🎧", "Headset",   new[] { "headset", "headphones", "headphone" }),
        new GalleryItem("📡", "Streaming", new[] { "streaming", "stream", "broadcast", "twitch" }),
        new GalleryItem("📞", "Calls",     new[] { "calls", "call", "phone", "meeting" }),
        new GalleryItem("🎤", "Mic",       new[] { "mic", "microphone", "recording" }),
        new GalleryItem("🏠", "Home",      new[] { "home", "house" }),
        new GalleryItem("🔊", "Speakers",  new[] { "speakers", "speaker" }),
        new GalleryItem("🌙", "Night",     new[] { "night", "sleep", "evening" }),
        new GalleryItem("🎙️", "Podcast",  new[] { "podcast", "podcasting" }),
        new GalleryItem("🖥️", "Desktop",  new[] { "desktop", "pc", "computer" }),
    };

    // Draws a gallery item's emoji as a size×size PNG in the requested colour (Auto = its own
    // colours; Black/White = every visible pixel in that colour, alpha kept). Set by the app.
    public static Func<string, IconColor, int, byte[]>? GlyphRenderer { get; set; }

    public static GalleryItem? FindByName(string name)
    {
        var lower = name.Trim().ToLowerInvariant();
        return Items.FirstOrDefault(i => i.Keywords.Any(k => string.Equals(k, lower, StringComparison.OrdinalIgnoreCase)));
    }

    // The emoji as a PNG — shared by SaveGalleryIcon and the picker's live preview so the
    // on-screen thumbnail matches exactly what gets saved. UI thread only.
    public static byte[] RenderGlyphPng(string emoji, IconColor color, int size = 64)
    {
        var renderer = GlyphRenderer ?? throw new InvalidOperationException("Gallery icons can't be drawn here.");
        return renderer(emoji, color, size);
    }

    // Renders a gallery item's emoji to a 64×64 .ico file at destPath (one PNG frame, full quality).
    public static void SaveGalleryIcon(GalleryItem item, string destPath, IconColor color = IconColor.Black)
    {
        const int size = 64;
        var pngBytes = RenderGlyphPng(item.Emoji, color, size);
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
        WriteSingleFrameIco(pngBytes, size, destPath);
    }

    // Writes a minimal single-frame ICO file with an embedded PNG image.
    internal static void WriteSingleFrameIco(byte[] pngBytes, int size, string destPath)
    {
        using var fs = new FileStream(destPath, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);

        // ICONDIR header (6 bytes)
        bw.Write((short)0);  // reserved
        bw.Write((short)1);  // type: 1 = ICO
        bw.Write((short)1);  // number of images

        // ICONDIRENTRY (16 bytes) — offset = 6 header + 16 entry = 22
        bw.Write((byte)(size >= 256 ? 0 : size));  // width  (0 encodes 256)
        bw.Write((byte)(size >= 256 ? 0 : size));  // height
        bw.Write((byte)0);   // color count (0 = > 256 colours / PNG)
        bw.Write((byte)0);   // reserved
        bw.Write((short)1);  // planes
        bw.Write((short)32); // bits per pixel
        bw.Write(pngBytes.Length);  // size of image data
        bw.Write(22);              // offset to image data

        // Image data: raw PNG bytes
        bw.Write(pngBytes);
    }
}
