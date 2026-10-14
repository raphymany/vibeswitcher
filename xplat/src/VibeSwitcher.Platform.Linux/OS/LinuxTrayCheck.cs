using Tmds.DBus.Protocol;

namespace VibeSwitcher.Platform.Linux.OS;

// Whether this desktop shows tray icons: apps' icons (StatusNotifierItem) appear only where a tray
// host is running — KDE, Cinnamon, MATE, XFCE, Budgie and Ubuntu's GNOME have one; stock GNOME (e.g.
// Fedora) needs the "AppIndicator and KStatusNotifierItem Support" extension. The watcher service
// (org.kde.StatusNotifierWatcher on the session bus) says whether a host is showing the icons — the
// watcher alone isn't enough: KDE's background service can run it in a GNOME session, with no host.
public static class LinuxTrayCheck
{
    public const string WatcherName = "org.kde.StatusNotifierWatcher";
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(3);

    // True / false; null when it can't be told (no session bus).
    public static async Task<bool?> IsTrayAvailableAsync() => await IsTrayAvailableAsync(LinuxNotifier.SessionBusAddress()).ConfigureAwait(false);

    internal static async Task<bool?> IsTrayAvailableAsync(string? busAddress)
    {
        if (string.IsNullOrEmpty(busAddress)) return null;
        try
        {
            using var connection = new Connection(busAddress);
            await connection.ConnectAsync().AsTask().WaitAsync(CallTimeout).ConfigureAwait(false);
            var watcher = await connection.CallMethodAsync(NameHasOwner(connection, WatcherName),
                static (Message m, object? _) => m.GetBodyReader().ReadBool(), null)
                .WaitAsync(CallTimeout).ConfigureAwait(false);
            if (!watcher) return false;
            try
            {
                return await connection.CallMethodAsync(HostRegistered(connection),
                    static (Message m, object? _) => m.GetBodyReader().ReadVariantValue().GetBool(), null)
                    .WaitAsync(CallTimeout).ConfigureAwait(false);
            }
            catch (Exception) { return true; } // (a watcher that doesn't say: taken at its word)
        }
        catch (Exception) { return null; }
    }

    // org.freedesktop.DBus.Properties.Get("org.kde.StatusNotifierWatcher", "IsStatusNotifierHostRegistered") → v(b)
    private static MessageBuffer HostRegistered(Connection connection)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(destination: WatcherName, path: "/StatusNotifierWatcher",
            @interface: "org.freedesktop.DBus.Properties", member: "Get", signature: "ss", flags: MessageFlags.None);
        writer.WriteString(WatcherName);
        writer.WriteString("IsStatusNotifierHostRegistered");
        return writer.CreateMessage();
    }

    // org.freedesktop.DBus.NameHasOwner(name s) → b
    private static MessageBuffer NameHasOwner(Connection connection, string name)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(destination: "org.freedesktop.DBus", path: "/org/freedesktop/DBus",
            @interface: "org.freedesktop.DBus", member: "NameHasOwner", signature: "s", flags: MessageFlags.None);
        writer.WriteString(name);
        return writer.CreateMessage();
    }
}
