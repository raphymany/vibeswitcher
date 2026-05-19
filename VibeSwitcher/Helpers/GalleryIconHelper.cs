using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VibeSwitcher.Helpers;

public enum IconColor { Auto, Black, White }

public class GalleryItem
{
    public string Emoji { get; }
    public string Label { get; }
    public string[] Keywords { get; }

    // Non-null for items rendered via custom geometry rather than emoji.
    internal Action<DrawingContext, int>? CustomRenderer { get; }

    // Lazily-rendered preview bitmap for custom items; null for emoji items.
    // Lazy defers WPF rendering until the gallery dialog opens on the UI thread.
    private readonly Lazy<ImageSource?>? _previewLazy;
    public ImageSource? GalleryPreview => _previewLazy?.Value;

    public GalleryItem(string emoji, string label, string[] keywords)
    {
        Emoji = emoji;
        Label = label;
        Keywords = keywords;
    }

    internal GalleryItem(string label, string[] keywords, Action<DrawingContext, int> renderer)
    {
        Emoji = "";
        Label = label;
        Keywords = keywords;
        CustomRenderer = renderer;
        _previewLazy = new Lazy<ImageSource?>(() =>
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen()) renderer(dc, 64);
            var rtb = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();
            return rtb;
        });
    }
}

public class GalleryPickResult
{
    public GalleryItem? Item { get; init; }
    public bool BrowseFromDisk { get; init; }
    public IconColor IconColor { get; init; }
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
        new GalleryItem("Speakers", new[] { "speakers", "speaker" }, DrawSpeaker),
        new GalleryItem("🌙", "Night",     new[] { "night", "sleep", "evening" }),
        new GalleryItem("🎙️", "Podcast",  new[] { "podcast", "podcasting" }),
        new GalleryItem("🖥️", "Desktop",  new[] { "desktop", "pc", "computer" }),
    };

    public static GalleryItem? FindByName(string name)
    {
        var lower = name.Trim().ToLowerInvariant();
        return Items.FirstOrDefault(i => i.Keywords.Any(k => string.Equals(k, lower, StringComparison.OrdinalIgnoreCase)));
    }

    // Draws a physical bookshelf-speaker shape: rounded cabinet with a concentric-circle
    // driver (basket ring + dustcap + center dot) punched out of the face.
    // The hole in the cabinet is transparent, so Black/White color masks read correctly.
    private static void DrawSpeaker(DrawingContext dc, int size)
    {
        double s = size;
        double pad = s * 0.07;
        double cx = s / 2, cy = s / 2;
        double outerRadius  = s * 0.37;
        double dustcapRadius = s * 0.13;
        double cornerRadius  = s * 0.10;

        var cabinetBrush  = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
        var ringBrush     = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x77));
        var dustcapBrush  = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
        var centerBrush   = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));

        // Cabinet with the driver opening punched out (transparent hole)
        var cabinetRect = new RectangleGeometry(
            new Rect(pad, pad, s - pad * 2, s - pad * 2), cornerRadius, cornerRadius);
        var driverHole = new EllipseGeometry(new Point(cx, cy), outerRadius, outerRadius);
        dc.DrawGeometry(cabinetBrush, null,
            new CombinedGeometry(GeometryCombineMode.Exclude, cabinetRect, driverHole));

        // Basket frame ring (border of the driver opening)
        dc.DrawEllipse(null, new Pen(ringBrush, s * 0.04),
            new Point(cx, cy), outerRadius, outerRadius);

        // Dustcap (centre dome of the driver)
        dc.DrawEllipse(dustcapBrush, null, new Point(cx, cy), dustcapRadius, dustcapRadius);

        // Centre dot
        dc.DrawEllipse(centerBrush, null, new Point(cx, cy), s * 0.04, s * 0.04);
    }

    // Renders a gallery item to a 64×64 .ico file at destPath.
    // Writes the ICO format directly (single PNG-embedded frame) to preserve full
    // 64×64 quality — avoids Bitmap.GetHicon() which scales to the system icon size.
    // Must be called on the STA (UI) thread — uses WPF rendering pipeline.
    public static void SaveGalleryIcon(GalleryItem item, string destPath, IconColor color = IconColor.Black)
    {
        const int size = 64;
        const double dpi = 96.0;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            if (item.CustomRenderer != null)
            {
                item.CustomRenderer(dc, size);
            }
            else
            {
                const double pixelsPerDip = 1.0;
                var tf = new Typeface(new FontFamily("Segoe UI Emoji"),
                    FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
                var ft = new FormattedText(item.Emoji, CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, tf, size * 0.72, Brushes.Black, pixelsPerDip);
                var x = Math.Max(0, (size - ft.Width) / 2);
                var y = Math.Max(0, (size - ft.Height) / 2);
                dc.DrawText(ft, new Point(x, y));
            }
        }

        var rtb = new RenderTargetBitmap(size, size, dpi, dpi, PixelFormats.Pbgra32);
        rtb.Render(visual);

        BitmapSource bitmap = color switch
        {
            IconColor.Black => ApplyColorMask(rtb, Colors.Black),
            IconColor.White => ApplyColorMask(rtb, Colors.White),
            _               => rtb
        };

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var pngStream = new MemoryStream();
        encoder.Save(pngStream);
        var pngBytes = pngStream.ToArray();

        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
        WriteSingleFrameIco(pngBytes, size, destPath);
    }

    // Replaces all non-transparent pixels with the given color (pre-multiplied alpha preserved).
    private static WriteableBitmap ApplyColorMask(RenderTargetBitmap source, Color color)
    {
        int width = source.PixelWidth, height = source.PixelHeight;
        int stride = width * 4;
        var pixels = new byte[stride * height];
        source.CopyPixels(pixels, stride, 0);
        byte r = color.R, g = color.G, b = color.B;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            byte alpha = pixels[i + 3];
            if (alpha > 0)
            {
                // Pbgra32: B G R A — keep alpha, pre-multiply new color by alpha
                pixels[i + 0] = (byte)(b * alpha / 255);
                pixels[i + 1] = (byte)(g * alpha / 255);
                pixels[i + 2] = (byte)(r * alpha / 255);
            }
        }
        var wb = new WriteableBitmap(width, height, source.DpiX, source.DpiY, PixelFormats.Pbgra32, null);
        wb.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
        return wb;
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
