using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VibeSwitcher.Helpers;

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
}

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

    public static GalleryItem? FindByName(string name)
    {
        var lower = name.Trim().ToLowerInvariant();
        return Items.FirstOrDefault(i => i.Keywords.Any(k => string.Equals(k, lower, StringComparison.OrdinalIgnoreCase)));
    }

    // Renders an emoji glyph to a 64×64 .ico file at destPath.
    // Writes the ICO format directly (single PNG-embedded frame) to preserve full
    // 64×64 quality — avoids Bitmap.GetHicon() which scales to the system icon size.
    // Must be called on the STA (UI) thread — uses WPF rendering pipeline.
    public static void SaveGalleryIcon(string emoji, string destPath)
    {
        const int size = 64;
        const double dpi = 96.0;
        const double pixelsPerDip = 1.0; // 96 DPI baseline

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var tf = new Typeface(new FontFamily("Segoe UI Emoji"),
                FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            var ft = new FormattedText(emoji, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, tf, size * 0.72, Brushes.Black, pixelsPerDip);
            var x = Math.Max(0, (size - ft.Width) / 2);
            var y = Math.Max(0, (size - ft.Height) / 2);
            dc.DrawText(ft, new Point(x, y));
        }

        var rtb = new RenderTargetBitmap(size, size, dpi, dpi, PixelFormats.Pbgra32);
        rtb.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var pngStream = new MemoryStream();
        encoder.Save(pngStream);
        var pngBytes = pngStream.ToArray();

        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
        WriteSingleFrameIco(pngBytes, size, destPath);
    }

    // Writes a minimal single-frame ICO file with an embedded PNG image.
    // Supported on Windows Vista+ for all sizes; avoids GDI+ quality loss from
    // GetHicon() which rescales bitmaps to the current system icon size.
    private static void WriteSingleFrameIco(byte[] pngBytes, int size, string destPath)
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

        // Image data: raw PNG bytes (Windows Vista+ reads embedded PNGs in ICO)
        bw.Write(pngBytes);
    }
}
