using Tmds.DBus.Protocol;
using VibeSwitcher.Helpers;
using VibeSwitcher.Platform.Linux.OS;

namespace VibeSwitcher.Platform.Linux.Hotkeys;

public enum PortalShortcutState
{
    NotBound,        // no shortcuts to give the desktop
    WaitingForUser,  // the desktop is asking the user to approve them
    Bound,           // approved; presses arrive
    Declined,        // the user said no (asked again when the shortcuts change)
    Failed,          // the desktop reported an error (see the session log)
}

// Global hotkeys on a Wayland session through the desktop's "Global Shortcuts" service
// (org.freedesktop.portal.GlobalShortcuts — KDE Plasma 5.27+, GNOME 48+, Hyprland…). On Wayland an
// app can't grab keys itself: it hands the desktop a list of named shortcuts with a preferred key, the
// desktop asks the user to approve them (and lets them pick a different key), and then reports each
// press by name. So, unlike X11 and macOS:
//   - there's no "taken by another app" test: Register only checks the key has a name the desktop
//     understands, and that the same shortcut isn't used twice inside VibeSwitcher (as on Windows);
//   - registrations are collected and handed over together, shortly after the last change, and only
//     when the set of shortcuts actually changed — so saving Settings, or TestHotkey releasing and
//     re-taking everything, doesn't ask the user again.
// Shortcut ids are the stable ShortcutNames, which is how the desktop recognises shortcuts the user
// already approved. Pressed is raised on the thread pool. Nothing here throws to callers.
public sealed class PortalKeyGrabber : IGlobalKeyGrabber
{
    internal const string PortalBus = "org.freedesktop.portal.Desktop";
    internal const string PortalPath = "/org/freedesktop/portal/desktop";
    internal const string ShortcutsInterface = "org.freedesktop.portal.GlobalShortcuts";
    internal const string RequestInterface = "org.freedesktop.portal.Request";
    internal const string SessionInterface = "org.freedesktop.portal.Session";
    // How the desktop knows which app is asking (matches the installed .desktop file's name).
    public const string AppId = OS.LinuxAppIdentity.AppId;

    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(10);
    // Binding shows the user a dialog; give them time to read it.
    private static readonly TimeSpan DefaultApprovalTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(300);
    // A failure before the user was asked (e.g. the desktop's shortcut service still starting at
    // login) is retried after these pauses, then given up until the shortcuts change.
    private static readonly TimeSpan[] DefaultRetryDelays = [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2)];

    private sealed record Shortcut(string Name, string Description, string Trigger);

    private readonly Connection _connection;
    private readonly IAppLogger _logger;
    private readonly TimeSpan _debounce;
    private readonly TimeSpan _approvalTimeout;
    private readonly TimeSpan[] _retryDelays;
    private readonly Timer _timer;
    private readonly CancellationTokenSource _disposing = new();
    private readonly SemaphoreSlim _rebindGate = new(1, 1);
    private readonly object _lock = new();
    // Under _lock: what each id stands for (kept across Unregister: TestHotkey re-registers the same
    // ids without describing them again), and what's registered now.
    private readonly Dictionary<ushort, (string Name, string Description)> _described = new();
    private readonly Dictionary<ushort, Shortcut> _registered = new();
    private string? _session;
    private IReadOnlyDictionary<string, string> _assigned = new Dictionary<string, string>();
    private PortalShortcutState _state;
    // Rebind only (under _rebindGate): the set whose outcome is settled (approved, declined, or failed
    // for good) — nothing to do while it's still what's wanted — and the failures in a row for a set.
    private IReadOnlyList<Shortcut>? _settled = [];
    private IReadOnlyList<Shortcut>? _attempted;
    private int _failures;
    // Under _lock: the dialog being waited on, and for which set (cancelled when a different set is due).
    private CancellationTokenSource? _approval;
    private IReadOnlyList<Shortcut>? _approvalSet;
    private DateTime _dueAt = DateTime.MaxValue; // under _lock: when the timer fires next
    private readonly List<IDisposable> _watches = [];
    // Portal requests waiting for their Response, by the Request object's path.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, TaskCompletionSource<(uint, Dictionary<string, VariantValue>)>> _requests = new();
    private int _tokens;
    private int _disposed;

    public event Action<ushort>? Pressed;
    // Raised (on the thread pool) when State or AssignedTriggers change.
    public event Action? StateChanged;

    public PortalShortcutState State { get { lock (_lock) return _state; } }

    // Shortcut name → the key the desktop actually uses, in the desktop's own words ("Ctrl+Alt+G").
    // The user may have picked a different key than VibeSwitcher's.
    public IReadOnlyDictionary<string, string> AssignedTriggers { get { lock (_lock) return _assigned; } }

    // The same with what each one does, for Settings: ("Switch to Gaming", "Ctrl+Alt+G"), by description.
    public IReadOnlyList<(string Description, string Trigger)> AssignedKeys
    {
        get
        {
            lock (_lock)
            {
                // (From the shortcuts registered now: a renamed profile shows its new name.)
                var descriptions = new Dictionary<string, string>();
                foreach (var s in _registered.Values) descriptions[s.Name] = s.Description;
                return _assigned.Where(kv => kv.Value.Length > 0 && kv.Key != ShortcutNames.Probe)
                    .Select(kv => (descriptions.TryGetValue(kv.Key, out var d) ? d : kv.Key, kv.Value))
                    .OrderBy(k => k.Item1, StringComparer.CurrentCulture)
                    .ToList();
            }
        }
    }

    private PortalKeyGrabber(Connection connection, IAppLogger logger, TimeSpan debounce, TimeSpan approvalTimeout, TimeSpan[] retryDelays)
    {
        _connection = connection;
        _logger = logger;
        _debounce = debounce;
        _approvalTimeout = approvalTimeout;
        _retryDelays = retryDelays;
        _timer = new Timer(_ => _ = Due());
    }

    // Null when the session has no Global Shortcuts service (then another way is needed).
    public static Task<PortalKeyGrabber?> TryCreateAsync(IAppLogger logger) =>
        TryCreateAsync(logger, LinuxNotifier.SessionBusAddress(), DefaultDebounce);

    internal static async Task<PortalKeyGrabber?> TryCreateAsync(IAppLogger logger, string? busAddress, TimeSpan debounce,
        TimeSpan? approvalTimeout = null, TimeSpan[]? retryDelays = null)
    {
        if (string.IsNullOrEmpty(busAddress)) return null;
        var connection = new Connection(busAddress);
        PortalKeyGrabber? grabber = null;
        try
        {
            await connection.ConnectAsync().AsTask().WaitAsync(CallTimeout).ConfigureAwait(false);
            // Must come before any other portal call on this connection.
            await RegisterAppIdAsync(connection, logger).ConfigureAwait(false);
            uint version = await connection.CallMethodAsync(GetVersion(connection),
                static (Message m, object? _) => Unwrap(m.GetBodyReader().ReadVariantValue()).GetUInt32(), null)
                .WaitAsync(CallTimeout).ConfigureAwait(false);
            grabber = new PortalKeyGrabber(connection, logger, debounce,
                approvalTimeout ?? DefaultApprovalTimeout, retryDelays ?? DefaultRetryDelays);
            await grabber.WatchSignalsAsync().ConfigureAwait(false);
            logger.Info("PortalKeyGrabber", $"Using the desktop's Global Shortcuts service (version {version}).");
            return grabber;
        }
        catch (Exception ex)
        {
            logger.Debug("PortalKeyGrabber", $"No Global Shortcuts service: {ex.Message}");
            if (grabber != null) grabber.Dispose();
            else connection.Dispose();
            return null;
        }
    }

    public void Describe(ushort id, string name, string description)
    {
        lock (_lock) _described[id] = (name, description);
    }

    public GrabResult Register(ushort id, int modifiers, int virtualKey, out string? error)
    {
        error = null;
        if (Volatile.Read(ref _disposed) != 0) { error = "the shortcut service was closed"; return GrabResult.Failed; }
        if (ShortcutTriggers.Portal(modifiers, virtualKey) is not { } trigger)
        {
            error = $"key code {virtualKey} has no name the desktop understands";
            return GrabResult.Unsupported;
        }
        lock (_lock)
        {
            var (name, description) = _described.TryGetValue(id, out var d) ? d : ($"shortcut-{id}", "VibeSwitcher shortcut");
            // Nothing to test against: the desktop decides, with the user. Never handed over.
            if (name == ShortcutNames.Probe) { _described.Remove(id); return GrabResult.Ok; }
            _registered.Remove(id);
            // The same shortcut twice in this app is a conflict, as on Windows.
            if (_registered.Values.Any(s => s.Trigger == trigger)) return GrabResult.InUse;
            _registered[id] = new Shortcut(name, description, trigger);
        }
        Schedule();
        return GrabResult.Ok;
    }

    public void Unregister(ushort id)
    {
        bool removed;
        // (Its description is kept: the hotkey service registers an id again after testing a key,
        // without describing it again.)
        lock (_lock) removed = _registered.Remove(id);
        if (removed) Schedule();
    }

    // Without a delay: the short pause after a registration (each one restarts it). With one: a retry,
    // which never postpones a run that's already due sooner.
    private void Schedule(TimeSpan? retryDelay = null)
    {
        var delay = retryDelay ?? _debounce;
        var now = DateTime.UtcNow;
        var at = delay == Timeout.InfiniteTimeSpan ? DateTime.MaxValue : now + delay;
        lock (_lock)
        {
            if (retryDelay != null && _dueAt > now && _dueAt < at) return;
            _dueAt = at;
            try { _timer.Change(delay, Timeout.InfiniteTimeSpan); }
            catch (ObjectDisposedException) { }
        }
    }

    // Hands the current shortcuts over now (tests; the timer does it otherwise).
    internal Task FlushAsync()
    {
        try { _timer.Change(Timeout.Infinite, Timeout.Infinite); } catch (ObjectDisposedException) { }
        return Due();
    }

    // The registrations settled (or a retry is due). If the desktop is still showing a dialog for a
    // different set, that dialog is withdrawn so the user only answers for the current one.
    private Task Due()
    {
        CancellationTokenSource? stale = null;
        lock (_lock)
        {
            _dueAt = DateTime.MaxValue;
            if (_approval != null && !Wanted().SequenceEqual(_approvalSet!)) stale = _approval;
        }
        try { stale?.Cancel(); } catch (ObjectDisposedException) { }
        return RebindAsync();
    }

    // Under _lock.
    private List<Shortcut> Wanted() =>
        _registered.Values.DistinctBy(s => s.Name).OrderBy(s => s.Name, StringComparer.Ordinal).ToList();

    private async Task RebindAsync()
    {
        var ct = _disposing.Token;
        try { await _rebindGate.WaitAsync(ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }
        List<Shortcut> want = [];
        bool asked = false; // the desktop accepted the bind request: its dialog is (or was) up
        using var approval = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            lock (_lock) want = Wanted();
            if (_settled != null && want.SequenceEqual(_settled)) return;
            if (_attempted == null || !want.SequenceEqual(_attempted)) _failures = 0;
            _attempted = want;

            // A session's shortcuts are bound once, so a new set means a new session. The settled
            // one is going away, so nothing is settled until this attempt ends.
            _settled = null;
            await CloseSessionAsync().ConfigureAwait(false);
            if (want.Count == 0)
            {
                _settled = want;
                SetState(PortalShortcutState.NotBound, new Dictionary<string, string>());
                return;
            }

            var created = await RequestAsync(t => CreateSession(_connection, t), CallTimeout, ct).ConfigureAwait(false);
            if (created.Code != 0 || !created.Results.TryGetValue("session_handle", out var handle))
                throw new InvalidOperationException($"the desktop didn't start a shortcut session (response {created.Code})");
            var session = AsString(handle);
            bool current;
            lock (_lock)
            {
                _session = session;
                // Changed again while the session was being set up: don't ask about an outdated
                // set — the rebind already queued for the new one takes over.
                current = Wanted().SequenceEqual(want);
                if (current)
                {
                    _approval = approval;
                    _approvalSet = want;
                }
            }
            if (!current) return;
            SetState(PortalShortcutState.WaitingForUser, null);

            var bound = await RequestAsync(t => BindShortcuts(_connection, session, want, t), _approvalTimeout, approval.Token,
                onAsked: () => asked = true).ConfigureAwait(false);
            switch (bound.Code)
            {
                case 0:
                    _settled = want;
                    SetState(PortalShortcutState.Bound, bound.Results.TryGetValue("shortcuts", out var list) ? ReadShortcuts(list) : new Dictionary<string, string>());
                    _logger.Info("PortalKeyGrabber", $"The desktop accepted {want.Count} shortcut(s).");
                    break;
                case 1:
                    await CloseSessionAsync().ConfigureAwait(false);
                    _settled = want;
                    SetState(PortalShortcutState.Declined, new Dictionary<string, string>());
                    _logger.Warning("PortalKeyGrabber", "The shortcuts were not approved in the desktop's dialog, so they won't work until they're allowed.");
                    break;
                default:
                    await CloseSessionAsync().ConfigureAwait(false);
                    Failed(want, $"the desktop couldn't set up the shortcuts (response {bound.Code})", retry: false);
                    break;
            }
        }
        catch (Exception) when (ct.IsCancellationRequested || Volatile.Read(ref _disposed) != 0) { }
        catch (OperationCanceledException) when (approval.IsCancellationRequested)
        {
            // Replaced by a newer set: the rebind that follows starts a new session and asks for that.
            _logger.Info("PortalKeyGrabber", "The shortcuts changed while the desktop was asking; asking again for the new set.");
        }
        catch (TimeoutException) when (asked)
        {
            // Nobody answered: withdraw it rather than leave a half-set-up session behind.
            await CloseSessionAsync().ConfigureAwait(false);
            _settled = want;
            SetState(PortalShortcutState.Declined, new Dictionary<string, string>());
            _logger.Warning("PortalKeyGrabber", "The desktop's shortcut dialog wasn't answered, so the shortcuts are off until they change or VibeSwitcher restarts.");
        }
        catch (Exception ex)
        {
            await CloseSessionAsync().ConfigureAwait(false);
            Failed(want, ex.Message, retry: !asked);
        }
        finally
        {
            lock (_lock)
            {
                if (_approval == approval) { _approval = null; _approvalSet = null; }
            }
            _rebindGate.Release();
        }
    }

    // A failure: one before the user was asked is tried again after a pause, a few times; otherwise
    // (or once those run out) it's left until the shortcuts change, so a dialog never keeps popping up.
    private void Failed(IReadOnlyList<Shortcut> want, string why, bool retry)
    {
        SetState(PortalShortcutState.Failed, new Dictionary<string, string>());
        if (retry && _failures < _retryDelays.Length)
        {
            var delay = _retryDelays[_failures++];
            _settled = null;
            _logger.Warning("PortalKeyGrabber", $"Setting up the shortcuts failed ({why}); trying again in {delay.TotalSeconds:0.#} s.");
            Schedule(delay);
        }
        else
        {
            _settled = want;
            _logger.Warning("PortalKeyGrabber", $"Setting up the shortcuts failed ({why}); giving up until they change.");
        }
    }

    private void SetState(PortalShortcutState state, Dictionary<string, string>? assigned)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        lock (_lock)
        {
            _state = state;
            if (assigned != null) _assigned = assigned;
        }
        RaiseStateChanged();
    }

    private void RaiseStateChanged()
    {
        if (StateChanged is { } handler) ThreadPool.QueueUserWorkItem(_ => handler());
    }

    private async Task CloseSessionAsync()
    {
        string? session;
        lock (_lock) { session = _session; _session = null; }
        if (session != null) await CloseObjectAsync(session, SessionInterface).ConfigureAwait(false);
    }

    // Session.Close / Request.Close: ends a session, or withdraws a dialog that's still showing.
    private async Task CloseObjectAsync(string path, string @interface)
    {
        try
        {
            await _connection.CallMethodAsync(CloseObject(_connection, path, @interface)).WaitAsync(CallTimeout).ConfigureAwait(false);
        }
        catch (Exception ex) { _logger.Debug("PortalKeyGrabber", $"Closing {path}: {ex.Message}"); }
    }

    // A portal call answers with a Request object, then later sends that object's Response signal.
    // The object's path is predictable from the token, so the answer is expected before the call is
    // made — otherwise a fast answer could be missed. One standing watch receives every Response and
    // hands it to the request waiting on that path, so no subscription is set up per request.
    private async Task<(uint Code, Dictionary<string, VariantValue> Results)> RequestAsync(
        Func<string, MessageBuffer> build, TimeSpan timeout, CancellationToken ct, Action? onAsked = null)
    {
        string token = $"vibeswitcher{Interlocked.Increment(ref _tokens)}";
        string expected = $"{RequestPrefix()}/{token}";
        var response = new TaskCompletionSource<(uint, Dictionary<string, VariantValue>)>(TaskCreationOptions.RunContinuationsAsynchronously);
        _requests[expected] = response;
        string? path = null;
        try
        {
            // The call itself answers at once and isn't cancelled, so the Request object (and any
            // dialog it shows) is always known and can be withdrawn.
            path = await _connection.CallMethodAsync(build(token),
                static (Message m, object? _) => m.GetBodyReader().ReadObjectPathAsString(), null)
                .WaitAsync(CallTimeout).ConfigureAwait(false);
            // Portals before 0.9 ignored the token (and predate Global Shortcuts), but just in case.
            if (path != expected) _requests[path] = response;
            onAsked?.Invoke();
            return await response.Task.WaitAsync(timeout, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (path != null && ex is OperationCanceledException or TimeoutException && Volatile.Read(ref _disposed) == 0)
        {
            // Withdraw a request nobody will wait for any more (e.g. close its dialog).
            await CloseObjectAsync(path, RequestInterface).ConfigureAwait(false);
            throw;
        }
        finally
        {
            _requests.TryRemove(expected, out _);
            if (path != null) _requests.TryRemove(path, out _);
        }
    }

    // Where the portal puts this connection's Request objects: …/request/<unique name, ':' dropped, '.' → '_'>.
    private string RequestPrefix() =>
        $"{PortalPath}/request/{(_connection.UniqueName ?? "").TrimStart(':').Replace('.', '_')}";

    private async Task WatchSignalsAsync()
    {
        // Response(u response, a{sv} results) on the portal's Request objects; ours are picked by path.
        // (Not narrowed with path_namespace: this library version spells that key wrongly.)
        _watches.Add(await _connection.AddMatchAsync(
            new MatchRule { Type = MessageType.Signal, Sender = PortalBus, Interface = RequestInterface, Member = "Response" },
            static (Message m, object? _) =>
            {
                var r = m.GetBodyReader();
                return (m.PathAsString ?? "", r.ReadUInt32(), r.ReadDictionaryOfStringToVariantValue());
            },
            static (Exception? e, (string Path, uint Code, Dictionary<string, VariantValue> Results) value, object? _, object? state) =>
            {
                if (e == null && ((PortalKeyGrabber)state!)._requests.TryGetValue(value.Path, out var tcs))
                    tcs.TrySetResult((value.Code, value.Results));
            },
            ObserverFlags.None, null, this, false).ConfigureAwait(false));

        // Activated(o session_handle, s shortcut_id, t timestamp, a{sv} options)
        _watches.Add(await _connection.AddMatchAsync(
            new MatchRule { Type = MessageType.Signal, Sender = PortalBus, Interface = ShortcutsInterface, Member = "Activated", Path = PortalPath },
            static (Message m, object? _) =>
            {
                var r = m.GetBodyReader();
                return (r.ReadObjectPathAsString(), r.ReadString());
            },
            static (Exception? e, (string Session, string Name) value, object? _, object? state) =>
            {
                if (e == null) ((PortalKeyGrabber)state!).OnActivated(value.Session, value.Name);
            },
            ObserverFlags.None, null, this, false).ConfigureAwait(false));

        // ShortcutsChanged(o session_handle, a(sa{sv}) shortcuts): the user changed a key in the desktop's settings.
        _watches.Add(await _connection.AddMatchAsync(
            new MatchRule { Type = MessageType.Signal, Sender = PortalBus, Interface = ShortcutsInterface, Member = "ShortcutsChanged", Path = PortalPath },
            static (Message m, object? _) =>
            {
                var r = m.GetBodyReader();
                var session = r.ReadObjectPathAsString();
                var assigned = new Dictionary<string, string>();
                var end = r.ReadArrayStart(DBusType.Struct);
                while (r.HasNext(end))
                {
                    r.AlignStruct();
                    var name = r.ReadString();
                    var props = r.ReadDictionaryOfStringToVariantValue();
                    if (props.TryGetValue("trigger_description", out var t)) assigned[name] = AsString(t);
                }
                return (session, assigned);
            },
            static (Exception? e, (string Session, Dictionary<string, string> Assigned) value, object? _, object? state) =>
            {
                if (e == null) ((PortalKeyGrabber)state!).OnShortcutsChanged(value.Session, value.Assigned);
            },
            ObserverFlags.None, null, this, false).ConfigureAwait(false));
    }

    private void OnActivated(string session, string name)
    {
        ushort? id = null;
        lock (_lock)
        {
            if (session != _session) return; // a press for an earlier set of shortcuts
            foreach (var (key, s) in _registered)
                if (s.Name == name) { id = key; break; }
        }
        if (id is ushort pressed && Pressed is { } handler)
            ThreadPool.QueueUserWorkItem(_ => handler(pressed));
    }

    private void OnShortcutsChanged(string session, Dictionary<string, string> assigned)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        lock (_lock)
        {
            if (session != _session) return;
            _assigned = assigned;
        }
        RaiseStateChanged();
    }

    // results["shortcuts"]: a(sa{sv}) with "trigger_description" in each entry's properties.
    private static Dictionary<string, string> ReadShortcuts(VariantValue list)
    {
        var assigned = new Dictionary<string, string>();
        list = Unwrap(list);
        if (list.Type != VariantValueType.Array) return assigned;
        for (int i = 0; i < list.Count; i++)
        {
            var entry = Unwrap(list.GetItem(i));
            if (entry.Type != VariantValueType.Struct || entry.Count < 2) continue;
            var name = AsString(entry.GetItem(0));
            var props = Unwrap(entry.GetItem(1));
            if (props.Type != VariantValueType.Dictionary) continue;
            for (int j = 0; j < props.Count; j++)
            {
                var kv = props.GetDictionaryEntry(j);
                if (AsString(kv.Key) == "trigger_description") assigned[name] = AsString(kv.Value);
            }
        }
        return assigned;
    }

    private static VariantValue Unwrap(VariantValue v)
    {
        while (v.Type == VariantValueType.Variant) v = v.GetVariantValue();
        return v;
    }

    private static string AsString(VariantValue v)
    {
        v = Unwrap(v);
        return v.Type switch
        {
            VariantValueType.String => v.GetString(),
            VariantValueType.ObjectPath => v.GetObjectPathAsString(),
            _ => "",
        };
    }

    // org.freedesktop.host.portal.Registry.Register(s app_id, a{sv} options): tells the portal which
    // app this is when it isn't a Flatpak/Snap. Older portals don't have it; then the desktop works it
    // out from the process (or names it after the executable).
    private static async Task RegisterAppIdAsync(Connection connection, IAppLogger logger)
    {
        try
        {
            await connection.CallMethodAsync(RegisterAppId(connection)).WaitAsync(CallTimeout).ConfigureAwait(false);
        }
        // (Expected on portals before 1.19; on newer ones it means no "<AppId>.desktop" file is installed.)
        catch (Exception ex) { logger.Info("PortalKeyGrabber", $"Couldn't register the app id with the portal: {ex.Message}"); }
    }

    // (Messages are built in plain methods: the writer can't live inside an async method.)
    private static MessageBuffer RegisterAppId(Connection connection)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(PortalBus, PortalPath, "org.freedesktop.host.portal.Registry", "Register", "sa{sv}", MessageFlags.None);
        writer.WriteString(AppId);
        writer.WriteDictionary(new Dictionary<string, VariantValue>());
        return writer.CreateMessage();
    }

    private static MessageBuffer CloseObject(Connection connection, string path, string @interface)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(PortalBus, path, @interface, "Close", null, MessageFlags.None);
        return writer.CreateMessage();
    }

    private static MessageBuffer GetVersion(Connection connection)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(PortalBus, PortalPath, "org.freedesktop.DBus.Properties", "Get", "ss", MessageFlags.None);
        writer.WriteString(ShortcutsInterface);
        writer.WriteString("version");
        return writer.CreateMessage();
    }

    // CreateSession(a{sv} options) → o request
    private static MessageBuffer CreateSession(Connection connection, string token)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(PortalBus, PortalPath, ShortcutsInterface, "CreateSession", "a{sv}", MessageFlags.None);
        writer.WriteDictionary(new Dictionary<string, VariantValue>
        {
            ["handle_token"] = VariantValue.String(token),
            ["session_handle_token"] = VariantValue.String(token),
        });
        return writer.CreateMessage();
    }

    // BindShortcuts(o session_handle, a(sa{sv}) shortcuts, s parent_window, a{sv} options) → o request
    private static MessageBuffer BindShortcuts(Connection connection, string session, IReadOnlyList<Shortcut> shortcuts, string token)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(PortalBus, PortalPath, ShortcutsInterface, "BindShortcuts", "oa(sa{sv})sa{sv}", MessageFlags.None);
        writer.WriteObjectPath(session);
        var array = writer.WriteArrayStart(DBusType.Struct);
        foreach (var s in shortcuts)
        {
            writer.WriteStructureStart();
            writer.WriteString(s.Name);
            writer.WriteDictionary(new Dictionary<string, VariantValue>
            {
                ["description"] = VariantValue.String(s.Description),
                ["preferred_trigger"] = VariantValue.String(s.Trigger),
            });
        }
        writer.WriteArrayEnd(array);
        writer.WriteString(""); // no parent window: the desktop centres its dialog
        writer.WriteDictionary(new Dictionary<string, VariantValue> { ["handle_token"] = VariantValue.String(token) });
        return writer.CreateMessage();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _disposing.Cancel();
        _timer.Dispose();
        foreach (var w in _watches) w.Dispose();
        // The desktop ends this app's shortcut session when its connection closes.
        _connection.Dispose();
    }
}
