using System.IO;
using System.Windows.Media;

namespace VibeSwitcher.Helpers;

// A profile icon file's contents (.ico, as the Windows app stores them). Disposable so the linked
// view models' `using var icon = IconHelper.LoadIcon(...)` compiles unchanged; it owns nothing.
public sealed class IconImage : IDisposable
{
    internal IconImage(byte[] data) => Data = data;

    public byte[] Data { get; }

    public void Dispose() { }
}

// Fork of the Windows IconHelper for what the linked view models use: loading a profile's icon (the
// same managed-folder check and fallbacks) and turning it into an image. The pictures themselves are
// made by the app (ImageFactory), since this library has no UI toolkit. The Windows tray/balloon
// icon handles have no counterpart here.
public static class IconHelper
{
    private static volatile byte[]? _defaultIcon;
    private static readonly object _syncRoot = new();
    // (Real icons are kilobytes; this only stops a stray huge file being read into memory.)
    internal const long MaxIconBytes = 16 * 1024 * 1024;

    // Turns icon/picture file bytes into an image the views can show. Set by the app at start-up;
    // without it (unit tests) ToImageSource returns null, which the views show as no picture.
    public static Func<byte[], ImageSource>? ImageFactory { get; set; }

    public static IconImage LoadIcon(string? iconPath, string iconsDir)
    {
        if (!string.IsNullOrEmpty(iconPath))
        {
            try
            {
                // Resolve and confirm the path is inside the managed icons directory (handles ".."
                // traversal). Anything outside is rejected and falls back to the default icon.
                if (!PathSafety.TryResolveInside(iconPath, iconsDir, out var canonical))
                {
                    AppLog.Warning("IconHelper.LoadIcon", $"Rejected icon path outside icons directory: '{iconPath}'");
                    AppErrors.Record(ErrorCode.IconLoadFailed, "Icon Path Rejected",
                        $"Icon path '{iconPath}' is outside the expected directory and was not loaded.");
                    return GetDefaultIcon();
                }

                if (File.Exists(canonical))
                {
                    if (new FileInfo(canonical).Length > MaxIconBytes) throw new InvalidDataException("The icon file is too large.");
                    var data = File.ReadAllBytes(canonical);
                    // (Windows' Icon class refuses a file that isn't an icon; so does this.)
                    if (!IsIcoFile(data)) throw new InvalidDataException("The file is not a valid icon.");
                    return new IconImage(data);
                }
            }
            catch (Exception ex)
            {
                AppLog.Warning("IconHelper.LoadIcon", ex.Message);
                AppErrors.Record(ErrorCode.IconLoadFailed, "Icon Load Failed",
                    $"Could not load icon from '{iconPath}': {ex.Message}");
            }
        }

        try
        {
            return GetDefaultIcon();
        }
        catch (Exception ex)
        {
            AppLog.Error("IconHelper.LoadIcon", ex.Message);
            AppErrors.Record(ErrorCode.GdiRenderFailed, "GDI Render Failed",
                $"Could not create default icon: {ex.Message}");
            throw;
        }
    }

    // The app icon (the Windows app's own vs-icon.ico, embedded in this library).
    public static IconImage GetDefaultIcon()
    {
        if (_defaultIcon == null)
        {
            lock (_syncRoot)
            {
                if (_defaultIcon == null)
                {
                    using var stream = typeof(IconHelper).Assembly.GetManifestResourceStream("VibeSwitcher.Icons.vs-icon.ico")
                        ?? throw new InvalidOperationException("The app icon resource is missing.");
                    using var copy = new MemoryStream();
                    stream.CopyTo(copy);
                    _defaultIcon = copy.ToArray();
                }
            }
        }
        return new IconImage((byte[])_defaultIcon.Clone()); // (each caller gets its own copy)
    }

    // An .ico file: the 6-byte header (0, type 1, at least one image), then one 16-byte entry per
    // image, each pointing at data inside the file.
    internal static bool IsIcoFile(byte[] data)
    {
        if (data.Length < 6) return false;
        int reserved = BitConverter.ToUInt16(data, 0), type = BitConverter.ToUInt16(data, 2), count = BitConverter.ToUInt16(data, 4);
        if (reserved != 0 || type != 1 || count == 0 || data.Length < 6 + 16 * count) return false;
        for (int i = 0; i < count; i++)
        {
            int entry = 6 + 16 * i;
            long size = BitConverter.ToUInt32(data, entry + 8), offset = BitConverter.ToUInt32(data, entry + 12);
            if (size == 0 || offset < 6 + 16 * count || offset + size > data.Length) return false;
        }
        return true;
    }

    public static ImageSource? ToImageSource(IconImage icon)
    {
        var factory = ImageFactory;
        if (factory == null) return null;
        try
        {
            return factory(icon.Data);
        }
        catch (Exception ex)
        {
            AppLog.Error("IconHelper.ToImageSource", ex.Message);
            AppErrors.Record(ErrorCode.IconRenderFailed, "Icon Render Failed",
                $"Could not convert icon to image: {ex.Message}");
            throw;
        }
    }
}
