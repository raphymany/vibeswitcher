namespace VibeSwitcher.Platform;

// Desktop notification banners — the counterpart of the Windows tray balloon
// (TrayService.ShowNotification). Linux uses the desktop's notification service; macOS shows the
// app's own styled banner (added with the UI).
public interface IDesktopNotifier
{
    // False when no notification could be shown (no notification service, etc.) — never throws.
    Task<bool> ShowAsync(string title, string message);
}
