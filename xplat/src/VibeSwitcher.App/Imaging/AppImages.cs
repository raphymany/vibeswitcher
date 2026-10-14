using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using VibeSwitcher.Helpers;

namespace VibeSwitcher.App.Imaging;

// A picture the shared view models hold (their ImageSource), as an Avalonia bitmap.
public sealed class AvaloniaImage(Bitmap bitmap) : System.Windows.Media.ImageSource
{
    public Bitmap Bitmap { get; } = bitmap;
}

// Lets a view show a view model's picture: {Binding IconPreview, Converter={x:Static img:AppImages.ToBitmap}}.
public sealed class ImageSourceToBitmapConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value as AvaloniaImage)?.Bitmap;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

// How the shared (UI-free) icon helpers get their pictures drawn: icon files become bitmaps, and the
// profile-icon gallery's emoji are drawn and coloured the way the Windows app draws them.
public static class AppImages
{
    public static readonly IValueConverter ToBitmap = new ImageSourceToBitmapConverter();

    // Colour emoji fonts in the order each system has them (the Windows app uses Segoe UI Emoji).
    private static readonly FontFamily EmojiFont = new("Apple Color Emoji, Noto Color Emoji, Segoe UI Emoji, Twemoji, EmojiOne Color");

    public static void Install()
    {
        IconHelper.ImageFactory = FromFile;
        GalleryIconHelper.GlyphRenderer = RenderGlyph;
    }

    // An .ico (the largest picture in it) or any other picture file's bytes.
    public static AvaloniaImage FromFile(byte[] data)
    {
        using var stream = new MemoryStream(data);
        return new AvaloniaImage(new Bitmap(stream));
    }

    // The emoji centred on a transparent size×size square at 72% of its height (as on Windows),
    // then, for Black/White, every visible pixel in that colour with its transparency kept.
    public static byte[] RenderGlyph(string emoji, IconColor color, int size)
    {
        using var target = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
        using (var context = target.CreateDrawingContext())
        {
            var text = new FormattedText(emoji, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(EmojiFont), size * 0.72, Brushes.Black);
            context.DrawText(text, new Point(Math.Max(0, (size - text.Width) / 2), Math.Max(0, (size - text.Height) / 2)));
        }

        using var bitmap = new WriteableBitmap(new PixelSize(size, size), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var frame = bitmap.Lock())
        {
            target.CopyPixels(frame, AlphaFormat.Premul);
            if (color != IconColor.Auto)
            {
                byte value = color == IconColor.White ? (byte)255 : (byte)0;
                var row = new byte[size * 4];
                for (int y = 0; y < size; y++)
                {
                    var line = frame.Address + y * frame.RowBytes;
                    Marshal.Copy(line, row, 0, row.Length);
                    for (int i = 0; i < row.Length; i += 4)
                    {
                        byte alpha = row[i + 3];
                        if (alpha == 0) continue;
                        // Premultiplied B G R A: the new colour scaled by the pixel's alpha.
                        row[i] = row[i + 1] = row[i + 2] = (byte)(value * alpha / 255);
                    }
                    Marshal.Copy(row, 0, line, row.Length);
                }
            }
        }

        using var png = new MemoryStream();
        bitmap.Save(png);
        return png.ToArray();
    }
}
