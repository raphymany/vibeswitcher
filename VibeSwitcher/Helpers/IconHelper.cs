using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VibeSwitcher.Helpers;

public static class IconHelper
{
    private static Icon? _defaultIcon;

    public static Icon LoadIcon(string? iconPath)
    {
        if (!string.IsNullOrEmpty(iconPath))
        {
            try
            {
                // Canonicalize to resolve any ".." traversal before probing the filesystem.
                var canonical = Path.GetFullPath(iconPath);
                if (File.Exists(canonical))
                {
                    // Load at 32×32 (not 16×16) so Windows tray has a higher-res source to
                    // downsample from at high DPI, making the icon look sharper.
                    using var fileIcon = new Icon(canonical, new System.Drawing.Size(32, 32));
                    return CopyIcon(fileIcon);
                }
            }
            catch
            {
                // Fall through to default
            }
        }

        return CopyIcon(GetDefaultIcon());
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
        using var bitmap = new Bitmap(16, 16);
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
        var source = Imaging.CreateBitmapSourceFromHIcon(
            icon.Handle,
            Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions());
        source.Freeze();
        return source;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
