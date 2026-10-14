using Tmds.DBus.Protocol;
using VibeSwitcher.Helpers;
using VibeSwitcher.Platform;

namespace VibeSwitcher.Platform.Linux.OS;

// Notifications through org.freedesktop.Notifications on the session bus — the standard service
// GNOME, KDE, Cinnamon, XFCE and others all provide. Each new banner replaces the previous one, as
// the Windows tray balloon does, so a burst of profile switches never stacks up a pile of them.
public sealed class LinuxNotifier : IDesktopNotifier, IDisposable
{
    private const string Service = "org.freedesktop.Notifications";
    private const string ObjectPath = "/org/freedesktop/Notifications";
    private const int TimeoutMs = 5000;
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(5);

    private readonly IAppLogger _logger;
    private readonly string? _address;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Connection? _connection;
    private bool? _bodyMarkup; // whether the service renders markup in bodies (asked once per connection)
    private uint _lastId;
    private bool _failureLogged;

    public LinuxNotifier(IAppLogger logger) : this(logger, SessionBusAddress()) { }

    internal LinuxNotifier(IAppLogger logger, string? busAddress)
    {
        _logger = logger;
        _address = busAddress;
    }

    // $DBUS_SESSION_BUS_ADDRESS, or — as libdbus and GDBus also do — the per-user bus socket in
    // $XDG_RUNTIME_DIR, which some systemd-user and compositor setups rely on.
    internal static string? SessionBusAddress()
    {
        if (Address.Session is { Length: > 0 } a) return a;
        var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        return !string.IsNullOrEmpty(runtime) && File.Exists(Path.Combine(runtime, "bus"))
            ? "unix:path=" + Path.Combine(runtime, "bus")
            : null;
    }

    public async Task<bool> ShowAsync(string title, string message)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var connection = await ConnectAsync().ConfigureAwait(false);
            if (connection == null) return false;
            // Asked once per connection; if the question itself fails, escape this time and ask again next time.
            bool markup = _bodyMarkup ?? (_bodyMarkup = await SupportsBodyMarkup(connection).ConfigureAwait(false)) ?? true;
            var body = markup ? Escape(message) : message;
            _lastId = await connection.CallMethodAsync(Notify(connection, title, body, _lastId),
                static (Message m, object? _) => m.GetBodyReader().ReadUInt32(), null)
                .WaitAsync(CallTimeout).ConfigureAwait(false);
            _failureLogged = false;
            return true;
        }
        catch (Exception ex)
        {
            if (!_failureLogged)
            {
                _failureLogged = true;
                _logger.Warning("LinuxNotifier", $"Could not show a notification: {ex.Message}");
            }
            // A broken (or hung) connection is rebuilt next time.
            Drop();
            return false;
        }
        finally { _gate.Release(); }
    }

    private async Task<Connection?> ConnectAsync()
    {
        if (_connection != null) return _connection;
        if (string.IsNullOrEmpty(_address))
        {
            if (!_failureLogged)
            {
                _failureLogged = true;
                _logger.Warning("LinuxNotifier", "No desktop session bus, so notifications can't be shown.");
            }
            return null;
        }
        var connection = new Connection(_address);
        try
        {
            await connection.ConnectAsync().AsTask().WaitAsync(CallTimeout).ConfigureAwait(false);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
        _bodyMarkup = null;
        return _connection = connection;
    }

    // GetCapabilities() → as. Servers without "body-markup" show text literally, so escaping there
    // would put "&amp;" on screen.
    private static async Task<bool?> SupportsBodyMarkup(Connection connection)
    {
        try
        {
            var caps = await connection.CallMethodAsync(GetCapabilities(connection),
                static (Message m, object? _) => m.GetBodyReader().ReadArrayOfString(), null)
                .WaitAsync(CallTimeout).ConfigureAwait(false);
            return caps.Contains("body-markup");
        }
        catch (Exception) { return null; } // can't tell (yet)
    }

    private static MessageBuffer GetCapabilities(Connection connection)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(destination: Service, path: ObjectPath, @interface: Service,
            member: "GetCapabilities", signature: null, flags: MessageFlags.None);
        return writer.CreateMessage();
    }

    // Notify(app_name s, replaces_id u, app_icon s, summary s, body s, actions as, hints a{sv}, expire_timeout i) → id u
    private static MessageBuffer Notify(Connection connection, string title, string body, uint replaces)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(destination: Service, path: ObjectPath, @interface: Service,
            member: "Notify", signature: "susssasa{sv}i", flags: MessageFlags.None);
        writer.WriteString("VibeSwitcher");
        writer.WriteUInt32(replaces);
        writer.WriteString(LinuxAppIdentity.AppId); // the app icon the packages install (falls back to none)
        writer.WriteString(title);
        writer.WriteString(body);
        writer.WriteArray(Array.Empty<string>());
        writer.WriteDictionary(new Dictionary<string, VariantValue>
        {
            ["desktop-entry"] = VariantValue.String(LinuxAppIdentity.AppId), // the packages' com.raphymany.vibeswitcher.desktop
            ["transient"] = VariantValue.Bool(true),
        });
        writer.WriteInt32(TimeoutMs);
        return writer.CreateMessage();
    }

    // Markup-rendering servers would otherwise turn a profile name like "R&B <3" into broken markup.
    internal static string Escape(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private void Drop()
    {
        _connection?.Dispose();
        _connection = null;
    }

    public void Dispose() => Drop();
}
