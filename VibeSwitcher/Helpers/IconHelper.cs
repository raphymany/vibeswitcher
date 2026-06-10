using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VibeSwitcher.Helpers;

public static class IconHelper
{
    private static Icon? _defaultIcon;
    private static IntPtr _balloonIconHandle;
    private static ImageSource? _appIconImageSource;
    private static readonly object _syncRoot = new();

    static IconHelper()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            _defaultIcon?.Dispose();
            if (_balloonIconHandle != IntPtr.Zero)
                DestroyIcon(_balloonIconHandle);
        };
    }

    public static Icon LoadIcon(string? iconPath, string iconsDir)
    {
        if (!string.IsNullOrEmpty(iconPath))
        {
            try
            {
                // Canonicalize to resolve any ".." traversal before probing the filesystem.
                var canonical = Path.GetFullPath(iconPath);

                // Reject paths outside the managed icons directory — prevents path traversal.
                // Check for separator after the prefix so "Icons_sibling" dirs can't slip through.
                var iconsPrefix = iconsDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                          + Path.DirectorySeparatorChar;
                if (!canonical.StartsWith(iconsPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    AppLog.Warning("IconHelper.LoadIcon", $"Rejected icon path outside icons directory: '{canonical}'");
                    AppErrors.Record(ErrorCode.IconLoadFailed, "Icon Path Rejected",
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
                AppLog.Warning("IconHelper.LoadIcon", ex.Message);
                AppErrors.Record(ErrorCode.IconLoadFailed, "Icon Load Failed",
                    $"Could not load icon from '{iconPath}': {ex.Message}");
            }
        }

        try
        {
            return CopyIcon(GetDefaultIcon());
        }
        catch (Exception ex)
        {
            AppLog.Error("IconHelper.LoadIcon", ex.Message);
            AppErrors.Record(ErrorCode.GdiRenderFailed, "GDI Render Failed",
                $"Could not create default icon: {ex.Message}");
            throw;
        }
    }

    public static Icon GetDefaultIcon()
    {
        if (_defaultIcon != null) return _defaultIcon;
        lock (_syncRoot)
        {
            if (_defaultIcon != null) return _defaultIcon;
            try
            {
                var uri = new Uri("pack://application:,,,/Resources/Icons/VibeSwitcherIcon.ico", UriKind.Absolute);
                var info = System.Windows.Application.GetResourceStream(uri);
                if (info != null)
                {
                    using (info.Stream)
                        _defaultIcon = new Icon(info.Stream);
                    return _defaultIcon;
                }
            }
            catch (Exception ex) { AppLog.Warning("IconHelper.GetDefaultIcon", ex.Message); }
            _defaultIcon = CreateColorIcon(System.Drawing.Color.FromArgb(0, 120, 212));
            return _defaultIcon;
        }
    }

    // Returns a cached 32×32 HICON suitable for NIIF_LARGE_ICON balloon tips.
    // The HICON is owned here and destroyed at process exit via the static constructor hook.
    public static IntPtr GetBalloonIconHandle()
    {
        if (_balloonIconHandle != IntPtr.Zero) return _balloonIconHandle;
        lock (_syncRoot)
        {
            if (_balloonIconHandle != IntPtr.Zero) return _balloonIconHandle;
            try
            {
                var uri = new Uri("pack://application:,,,/Resources/Icons/VibeSwitcherIcon.ico", UriKind.Absolute);
                var info = System.Windows.Application.GetResourceStream(uri);
                if (info != null)
                {
                    using (info.Stream)
                    using (var icon = new Icon(info.Stream, new System.Drawing.Size(32, 32)))
                    using (var src = icon.ToBitmap())
                    using (var bmp32 = new Bitmap(32, 32))
                    using (var g = Graphics.FromImage(bmp32))
                    {
                        // ToBitmap() returns whatever frame the ICO contains (may be 256×256).
                        // DrawImage forces an exact 32×32 output that H.NotifyIcon requires.
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.DrawImage(src, 0, 0, 32, 32);
                        _balloonIconHandle = bmp32.GetHicon();
                    }
                    return _balloonIconHandle;
                }
            }
            catch (Exception ex) { AppLog.Warning("IconHelper.GetBalloonIconHandle", ex.Message); }
            // Fallback: solid-color 32×32 (exact size required by balloon API).
            using (var bmp = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(System.Drawing.Color.FromArgb(0, 120, 212));
                _balloonIconHandle = bmp.GetHicon();
            }
            return _balloonIconHandle;
        }
    }

    public static ImageSource GetAppIconImageSource()
    {
        if (_appIconImageSource != null) return _appIconImageSource;
        lock (_syncRoot)
        {
            if (_appIconImageSource != null) return _appIconImageSource;
            try
            {
                var uri = new Uri("pack://application:,,,/Resources/Icons/VibeSwitcherIcon.ico", UriKind.Absolute);
                var decoder = BitmapDecoder.Create(uri, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                var frame = decoder.Frames.OrderByDescending(f => f.PixelWidth).First();
                frame.Freeze();
                _appIconImageSource = frame;
                return _appIconImageSource;
            }
            catch (Exception ex)
            {
                AppLog.Warning("IconHelper.GetAppIconImageSource", ex.Message);
                _appIconImageSource = ToImageSource(GetDefaultIcon());
                return _appIconImageSource;
            }
        }
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
            AppLog.Error("IconHelper.ToImageSource", ex.Message);
            AppErrors.Record(ErrorCode.IconRenderFailed, "Icon Render Failed",
                $"Could not convert icon to image: {ex.Message}");
            throw;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
