using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VibeSwitcher.Services;

namespace VibeSwitcher.Helpers;

public static class IconHelper
{
    private static Icon? _defaultIcon;

    static IconHelper()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => _defaultIcon?.Dispose();
    }

    public static Icon LoadIcon(string? iconPath)
    {
        if (!string.IsNullOrEmpty(iconPath))
        {
            try
            {
                // Canonicalize to resolve any ".." traversal before probing the filesystem.
                var canonical = Path.GetFullPath(iconPath);

                // Reject paths outside the managed icons directory — prevents path traversal.
                if (!canonical.StartsWith(ConfigService.IconsDir, StringComparison.OrdinalIgnoreCase))
                {
                    AppLogger.Warning("IconHelper.LoadIcon", $"Rejected icon path outside icons directory: '{canonical}'");
                    SessionErrorTracker.Record(ErrorCode.IconLoadFailed, "Icon Path Rejected",
                        $"Icon path '{iconPath}' is outside the expected directory and was not loaded.");
                    return CopyIcon(GetDefaultIcon());
                }

                if (File.Exists(canonical))
                {
                    // Load at 64×64 so Windows has a large source frame to downsample from
                    // at high DPI (e.g. 200% scaling), keeping the tray icon sharp.
                    using var fileIcon = new Icon(canonical, new System.Drawing.Size(64, 64));
                    return CopyIcon(fileIcon);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warning("IconHelper.LoadIcon", ex.Message);
                SessionErrorTracker.Record(ErrorCode.IconLoadFailed, "Icon Load Failed",
                    $"Could not load icon from '{iconPath}': {ex.Message}");
            }
        }

        try
        {
            return CopyIcon(GetDefaultIcon());
        }
        catch (Exception ex)
        {
            AppLogger.Error("IconHelper.LoadIcon", ex.Message);
            SessionErrorTracker.Record(ErrorCode.GdiRenderFailed, "GDI Render Failed",
                $"Could not create default icon: {ex.Message}");
            throw;
        }
    }

    public static Icon GetDefaultIcon()
    {
        if (_defaultIcon != null) return _defaultIcon;
        _defaultIcon = CreateColorIcon(System.Drawing.Color.FromArgb(0, 120, 212));
        return _defaultIcon;
    }

    private static Icon CopyIcon(Icon source)
    {
        using var ms = new MemoryStream();
        source.Save(ms);
        ms.Seek(0, SeekOrigin.Begin);
        return new Icon(ms);
    }

    private static Icon CreateColorIcon(System.Drawing.Color color)
    {
        using var bitmap = new Bitmap(64, 64);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(color);

        // GetHicon returns an HICON that we own and must destroy
        var hIcon = bitmap.GetHicon();
        try
        {
            // Icon.FromHandle does NOT own the HICON; save to stream for a fully independent copy
            using var tempIcon = Icon.FromHandle(hIcon);
            using var ms = new MemoryStream();
            tempIcon.Save(ms);
            ms.Seek(0, SeekOrigin.Begin);
            return new Icon(ms); // This copy owns its own internal resources
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    public static ImageSource ToImageSource(Icon icon)
    {
        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch (Exception ex)
        {
            AppLogger.Error("IconHelper.ToImageSource", ex.Message);
            SessionErrorTracker.Record(ErrorCode.IconRenderFailed, "Icon Render Failed",
                $"Could not convert icon to image: {ex.Message}");
            throw;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
