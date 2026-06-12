using System.IO;
using Microsoft.Win32;
using VibeSwitcher.Helpers;

namespace VibeSwitcher.Services;

// Registers the app's AppUserModelID with a display name + icon so Windows shows "VibeSwitcher"
// and our icon in toast/notification attribution — for the installed app AND dev builds (which have
// no Start Menu shortcut). Without this, an explicit AUMID with no registration makes Windows show
// the raw AUMID string ("RaphaelMansour.VibeSwitcher") and no icon.
internal static class NotificationIdentity
{
    public const string AppId = "RaphaelMansour.VibeSwitcher";

    public static void Register(IAppLogger logger)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VibeSwitcher");
            Directory.CreateDirectory(dir);
            var iconPath = Path.Combine(dir, "app-icon.png");

            // Pre-redesign builds wrote an "app.ico" (old icon design) here for balloon
            // notifications; nothing references it anymore — clean it up on upgrade.
            try { File.Delete(Path.Combine(dir, "app.ico")); } catch { /* best-effort */ }

            // Refresh the on-disk icon from the embedded resource each launch so it always matches
            // the current build (and overwrites any stale icon a previous build wrote).
            try
            {
                var uri = new Uri("pack://application:,,,/Resources/Icons/vs-icon-256.png", UriKind.Absolute);
                var info = System.Windows.Application.GetResourceStream(uri);
                if (info != null)
                {
                    using var src = info.Stream;
                    using var dst = File.Create(iconPath);
                    src.CopyTo(dst);
                }
            }
            catch (Exception ex) { logger.Warning("NotificationIdentity.Icon", ex.Message); }

            using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{AppId}");
            if (key == null) return;
            key.SetValue("DisplayName", "VibeSwitcher", RegistryValueKind.String);
            if (File.Exists(iconPath))
                key.SetValue("IconUri", iconPath, RegistryValueKind.String);
            key.SetValue("IconBackgroundColor", "FF13131E", RegistryValueKind.String);
        }
        catch (Exception ex)
        {
            logger.Error("NotificationIdentity.Register", ex);
        }
    }
}
