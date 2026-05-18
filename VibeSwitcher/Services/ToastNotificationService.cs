using System.IO;
using System.Security;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using VibeSwitcher.Helpers;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace VibeSwitcher.Services;

// Registers a per-user AUMID so Windows shows the app icon in the notification
// attribution area, then sends toast notifications via the raw WinRT API.
// Falls back gracefully if any step fails — callers receive false from TryShow().
internal static class ToastNotificationService
{
    private const string Aumid = "VibeSwitcher.App";
    private static volatile bool _enabled;

    // Must be called on the UI thread — WriteIconPng uses WPF imaging (BitmapEncoder)
    // which requires STA context.
    public static void Initialize()
    {
        try
        {
            var appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "VibeSwitcher");
            Directory.CreateDirectory(appDataDir);

            var iconPath = WriteIconPng(appDataDir);
            RegisterAumid(iconPath);

            _enabled = true;
            AppLogger.Info("ToastNotificationService.Initialize", "Toast notifications registered.");
        }
        catch (Exception ex)
        {
            AppLogger.Warning("ToastNotificationService.Initialize",
                $"Toast notifications unavailable: {ex.Message}");
        }
    }

    private static string WriteIconPng(string appDataDir)
    {
        var path = Path.Combine(appDataDir, "icon.png");
        var imageSource = IconHelper.GetAppIconImageSource();
        // Avoid double-wrapping: if GetAppIconImageSource already returned a BitmapFrame
        // (the normal path), use it directly rather than passing it to BitmapFrame.Create().
        var frame = imageSource as BitmapFrame
            ?? BitmapFrame.Create((BitmapSource)imageSource);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(frame);
        using var stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }

    private static void RegisterAumid(string iconPath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(
            @"SOFTWARE\Classes\AppUserModelId\" + Aumid);
        key.SetValue("DisplayName", "VibeSwitcher");
        key.SetValue("IconUri", new Uri(iconPath).AbsoluteUri);
    }

    // Returns true if the toast was sent; false means the caller should use a balloon tip.
    public static bool TryShow(string title, string message)
    {
        if (!_enabled) return false;
        try
        {
            var xml = new XmlDocument();
            xml.LoadXml(
                "<toast>" +
                "<visual><binding template=\"ToastGeneric\">" +
                $"<text>{SecurityElement.Escape(title)}</text>" +
                $"<text>{SecurityElement.Escape(message)}</text>" +
                "</binding></visual>" +
                "</toast>");

            var notifier = ToastNotificationManager.CreateToastNotifier(Aumid);
            notifier.Show(new ToastNotification(xml));
            return true;
        }
        catch (Exception ex)
        {
            // Permanently disable toasts for this session — WinRT errors here are
            // typically fatal (bad AUMID, notification platform down); retrying would
            // just produce more failures. Subsequent calls fall back to balloon tips.
            AppLogger.Warning("ToastNotificationService.TryShow", ex.Message);
            _enabled = false;
            return false;
        }
    }

    public static void Unregister()
    {
        // AUMID registry key is intentionally left in place so Windows can still
        // resolve the icon on any notifications that were queued before exit.
        _enabled = false;
    }
}
