using System.Collections.Concurrent;
using System.IO;
using System.Text.RegularExpressions;
using Avalonia.Media.Imaging;
using SkiaSharp;
using VibeSwitcher.Platform;
using VibeSwitcher.Platform.Linux.Apps;
using VibeSwitcher.Platform.MacOS.Apps;

namespace VibeSwitcher.App.Imaging;

// The app picker's icons (drawn at 18 px, as on Windows; decoded at twice that for sharp screens):
//   macOS  the app bundle's own icon
//   Linux  the desktop entry's icon (a theme icon or a file; PNG, or SVG drawn here), and a running
//          Flatpak app's icon, which Flatpak names after the app's id
// Null when there's none: the row then has no picture, as on Windows. Each icon is read once and kept
// (shared by every list that shows it; a few KB each). Slow-ish the first time: call off the UI thread.
// The files come from other apps (desktop entries, icon themes, bundles), so they're read with care:
// size limits, and SVGs that refer to anything outside themselves aren't drawn (the SVG library would
// fetch it).
internal static partial class AppIcons
{
    public const int Pixels = 36;
    private const long MaxPng = 4 << 20, MaxSvg = 1 << 20;

    private static readonly Lazy<LinuxIconLookup> Linux = new(() => new LinuxIconLookup());
    private static readonly ConcurrentDictionary<string, Bitmap?> Cache = new(StringComparer.Ordinal);

    public static Bitmap? Load(AppEntry app)
    {
        if (OperatingSystem.IsMacOS())
            return AppTriggerMatch.IsBundle(app.Trigger)
                ? Cache.GetOrAdd("mac:" + app.Trigger, _ => Safely(() => MacAppIcon.Png(app.Trigger, Pixels) is { } png ? Decode(png) : null))
                : null;
        if (!OperatingSystem.IsLinux() || LinuxIconName(app) is not { } name) return null;
        return Cache.GetOrAdd("linux:" + name, _ => Safely(() => Linux.Value.Find(name, Pixels) is { } file ? FromFile(file, Pixels) : null));
    }

    // The icon a Linux app goes by: its desktop entry's, or a Flatpak app's id.
    internal static string? LinuxIconName(AppEntry app) =>
        app.Icon is { Length: > 0 } icon ? icon
        : app.Trigger.StartsWith(AppTriggerMatch.FlatpakPrefix, StringComparison.Ordinal) ? app.Trigger[AppTriggerMatch.FlatpakPrefix.Length..]
        : null;

    private static Bitmap? Safely(Func<Bitmap?> load)
    {
        try { return load(); }
        catch (Exception) { return null; } // (an unreadable or broken picture: no icon)
    }

    // A PNG or SVG file as a bitmap about `pixels` wide; null when it's too big or can't be drawn.
    internal static Bitmap? FromFile(string file, int pixels)
    {
        if (file.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            return ReadCapped(file, MaxSvg) is { } text && SvgToPng(System.Text.Encoding.UTF8.GetString(text), pixels) is { } png ? Decode(png) : null;
        return ReadCapped(file, MaxPng) is { } bytes ? Decode(bytes) : null;
    }

    // The file's bytes, if it's no bigger than max and reads within a couple of seconds (a named pipe
    // posing as a picture would wait forever: that read is left behind).
    private static byte[]? ReadCapped(string file, long max)
    {
        var read = Task.Run(() =>
        {
            using var stream = File.OpenRead(file);
            if (!stream.CanSeek || stream.Length > max) return null;
            var bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
            return bytes;
        });
        return read.Wait(TimeSpan.FromSeconds(2)) ? read.Result : null;
    }

    private static Bitmap Decode(byte[] png)
    {
        using var stream = new MemoryStream(png);
        return Bitmap.DecodeToWidth(stream, Pixels, BitmapInterpolationMode.HighQuality);
    }

    // An SVG's text drawn centred on a transparent square, as PNG data (null when it can't be read,
    // or refers to something outside itself).
    internal static byte[]? SvgToPng(string svgText, int pixels)
    {
        if (!IsSelfContained(svgText)) return null;
        using var svg = new Svg.Skia.SKSvg();
        if (svg.FromSvg(DocType().Replace(svgText, "", 1)) is not { } picture) return null;
        var bounds = picture.CullRect;
        if (bounds.Width <= 0 || bounds.Height <= 0) return null;
        var scale = pixels / Math.Max(bounds.Width, bounds.Height);
        using var surface = SKSurface.Create(new SKImageInfo(pixels, pixels, SKColorType.Bgra8888, SKAlphaType.Premul));
        if (surface == null) return null;
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Translate((pixels - bounds.Width * scale) / 2, (pixels - bounds.Height * scale) / 2);
        canvas.Scale(scale);
        canvas.Translate(-bounds.Left, -bounds.Top);
        canvas.DrawPicture(picture);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data?.ToArray();
    }

    // Links only within the file (#id) or embedded (data:); no style sheets or entities from elsewhere;
    // a plain DOCTYPE line is dropped before drawing (it names a DTD on the web), one with its own
    // definitions isn't drawn.
    internal static bool IsSelfContained(string svgText) =>
        !ExternalLink().IsMatch(svgText) && !ExternalUrl().IsMatch(svgText)
        && !svgText.Contains("@import", StringComparison.OrdinalIgnoreCase)
        && !svgText.Contains("<!ENTITY", StringComparison.OrdinalIgnoreCase)
        && !svgText.Contains("xml-stylesheet", StringComparison.OrdinalIgnoreCase)
        && !DocTypeWithDefinitions().IsMatch(svgText);

    [GeneratedRegex("""(?:href|src)\s*=\s*["']\s*(?!#|data:)""", RegexOptions.IgnoreCase)]
    private static partial Regex ExternalLink();

    [GeneratedRegex("""url\(\s*["']?\s*(?!#|data:)""", RegexOptions.IgnoreCase)]
    private static partial Regex ExternalUrl();

    [GeneratedRegex("""<!DOCTYPE[^>\[]*>""", RegexOptions.IgnoreCase)]
    private static partial Regex DocType();

    [GeneratedRegex("""<!DOCTYPE[^>]*\[""", RegexOptions.IgnoreCase)]
    private static partial Regex DocTypeWithDefinitions();
}
