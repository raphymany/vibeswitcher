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
        pngStream.Seek(0, SeekOrigin.Begin);

        using var sdBitmap = new System.Drawing.Bitmap(pngStream);
        var hIcon = sdBitmap.GetHicon();
        try
        {
            using var tempIcon = System.Drawing.Icon.FromHandle(hIcon);
            using var iconStream = new MemoryStream();
            tempIcon.Save(iconStream);
            iconStream.Seek(0, SeekOrigin.Begin);
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            File.WriteAllBytes(destPath, iconStream.ToArray());
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
