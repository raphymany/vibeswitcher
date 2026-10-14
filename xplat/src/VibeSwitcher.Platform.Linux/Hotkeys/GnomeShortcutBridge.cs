using System.Text;
using VibeSwitcher.Helpers;
using VibeSwitcher.Platform.Linux.OS;
using VibeSwitcher.Platform.Linux.Processes;

namespace VibeSwitcher.Platform.Linux.Hotkeys;

// Global hotkeys on GNOME before version 48 under Wayland, which has no Global Shortcuts service: each
// VibeSwitcher shortcut becomes a GNOME custom keyboard shortcut (Settings › Keyboard › Custom
// Shortcuts) that runs VibeSwitcher's command line — "vibeswitcher --switch <profile id>",
// "--mute mic", "--open", "--mini". The running app receives those through its single-instance socket,
// so Pressed is never raised here.
//
// Only used once the user has agreed (it changes the desktop's own settings). It only ever touches
// its own entries ("vibeswitcher-<name>" under the custom-keybindings path) and keeps every other
// custom shortcut and their order. Changes are written shortly after the last registration and only
// when something changed. The entries stay when the app quits (pressing one then starts the app);
// RemoveAll clears them, e.g. when the user turns this off or uninstalls from inside the app, and
// LinuxHotkeys clears them whenever another way is used. There's no conflict test with other apps,
// as with the portal.
public sealed class GnomeShortcutBridge : IGlobalKeyGrabber
{
    internal const string MediaKeysSchema = "org.gnome.settings-daemon.plugins.media-keys";
    internal const string EntrySchema = MediaKeysSchema + ".custom-keybinding";
    internal const string ListKey = "custom-keybindings";
    internal const string BasePath = "/org/gnome/settings-daemon/plugins/media-keys/custom-keybindings/";
    internal const string OwnPrefix = "vibeswitcher-";
    private static readonly TimeSpan ToolTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(300);

    private sealed record Entry(string Label, string Command, string Binding);

    private readonly IAppLogger _logger;
    private readonly IProcessRunner _runner;
    private readonly string _launchPath;
    private readonly TimeSpan _debounce;
    private readonly Timer _timer;
    private readonly object _lock = new();
    private readonly object _applyLock = new();
    private readonly Dictionary<ushort, (string Name, string Description)> _described = new(); // under _lock
    private readonly Dictionary<ushort, (string Name, Entry Entry)> _registered = new();        // under _lock
    private Dictionary<string, Entry>? _applied;                                                // under _applyLock
    private bool _failureLogged;
    private int _disposed;

    // Never raised: GNOME runs the command line instead.
    public event Action<ushort>? Pressed { add { } remove { } }

    public GnomeShortcutBridge(IAppLogger logger, string launchPath)
        : this(logger, ProcessRunner.Instance, launchPath, DefaultDebounce) { }

    internal GnomeShortcutBridge(IAppLogger logger, IProcessRunner runner, string launchPath, TimeSpan debounce)
    {
        _logger = logger;
        _runner = runner;
        _launchPath = launchPath;
        _debounce = debounce;
        _timer = new Timer(_ => Apply());
    }

    // The app's own launch command (the AppImage file for an AppImage), when GNOME can keep running it:
    // not an AppImage's temporary mount folder (gone once the app exits), and no '%' (GNOME may read
    // those as desktop-file field codes).
    public static string? DefaultLaunchPath() => UsableLaunchPath(LinuxStartupService.DefaultLaunchPath());

    internal static string? UsableLaunchPath(string? path) =>
        string.IsNullOrEmpty(path) || path.Contains('%') || path.Contains("/.mount_", StringComparison.Ordinal) ? null : path;

    // True when GNOME's keyboard-shortcut settings are there to write to.
    public static bool IsAvailable() => IsAvailable(ProcessRunner.Instance);

    internal static bool IsAvailable(IProcessRunner runner)
    {
        try
        {
            var r = runner.Run("gsettings", ["list-keys", MediaKeysSchema], ToolTimeout);
            return r.ExitCode == 0 && r.StdOut.Split('\n').Any(l => l.Trim() == ListKey);
        }
        catch (Exception) { return false; }
    }

    public void Describe(ushort id, string name, string description)
    {
        lock (_lock) _described[id] = (name, description);
    }

    public GrabResult Register(ushort id, int modifiers, int virtualKey, out string? error)
    {
        error = null;
        if (Volatile.Read(ref _disposed) != 0) { error = "the shortcut bridge was closed"; return GrabResult.Failed; }
        if (ShortcutTriggers.Gnome(modifiers, virtualKey) is not { } binding)
        {
            error = $"key code {virtualKey} has no name GNOME understands";
            return GrabResult.Unsupported;
        }
        lock (_lock)
        {
            if (!_described.TryGetValue(id, out var d)) { error = "unnamed shortcut"; return GrabResult.Failed; }
            if (d.Name == ShortcutNames.Probe) { _described.Remove(id); return GrabResult.Ok; } // nothing to test against
            if (ShortcutNames.ToCommand(d.Name) is not { } command) { error = $"no command for '{d.Name}'"; return GrabResult.Failed; }
            _registered.Remove(id);
            // The same shortcut twice in this app is a conflict, as on Windows.
            if (_registered.Values.Any(r => r.Entry.Binding == binding)) return GrabResult.InUse;
            _registered[id] = (d.Name, new Entry("VibeSwitcher: " + d.Description, CommandLineFor(command), binding));
        }
        Schedule();
        return GrabResult.Ok;
    }

    public void Unregister(ushort id)
    {
        bool removed;
        lock (_lock) removed = _registered.Remove(id);
        if (removed) Schedule();
    }

    private void Schedule()
    {
        try { _timer.Change(_debounce, Timeout.InfiniteTimeSpan); }
        catch (ObjectDisposedException) { }
    }

    // Writes the current shortcuts now (tests; the timer does it otherwise).
    internal void Flush()
    {
        try { _timer.Change(Timeout.Infinite, Timeout.Infinite); } catch (ObjectDisposedException) { }
        Apply();
    }

    private void Apply()
    {
        lock (_applyLock)
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            Dictionary<string, Entry> want;
            lock (_lock) want = _registered.Values.DistinctBy(r => r.Name).ToDictionary(r => r.Name, r => r.Entry);
            if (_applied != null && want.Count == _applied.Count && want.All(kv => _applied.TryGetValue(kv.Key, out var e) && e == kv.Value))
                return;
            try
            {
                var list = ReadList();
                foreach (var (name, entry) in want)
                {
                    if (_applied != null && _applied.TryGetValue(name, out var old) && old == entry && list.Contains(PathFor(name))) continue;
                    var schema = EntrySchema + ":" + PathFor(name);
                    Set(schema, "name", GVariantString(entry.Label));
                    Set(schema, "command", GVariantString(entry.Command));
                    Set(schema, "binding", GVariantString(entry.Binding));
                }
                var wanted = want.Keys.Select(PathFor).ToHashSet();
                foreach (var path in list.Where(p => IsOwn(p) && !wanted.Contains(p)))
                    Gsettings("reset-recursively", EntrySchema + ":" + path);
                // Other shortcuts keep their place; ours go at the end.
                var newList = list.Where(p => !IsOwn(p)).Concat(wanted.Order(StringComparer.Ordinal)).ToList();
                if (!newList.SequenceEqual(list)) Set(MediaKeysSchema, ListKey, GVariantStringArray(newList));
                _applied = want;
                _failureLogged = false;
            }
            catch (Exception ex)
            {
                _applied = null; // try everything again next time
                if (!_failureLogged)
                {
                    _failureLogged = true;
                    _logger.Warning("GnomeShortcutBridge", $"Couldn't update GNOME's keyboard shortcuts: {ex.Message}");
                }
            }
        }
    }

    // Removes every VibeSwitcher shortcut from GNOME's settings (including ones from earlier runs), e.g.
    // when the user turns this off or uninstalls from inside the app. Runs gsettings: keep it off the UI thread.
    public bool RemoveAll()
    {
        lock (_applyLock)
        {
            _applied = null;
            return RemoveAllEntries(_logger, _runner);
        }
    }

    // The same without a bridge: when VibeSwitcher uses another way this time (an X11 session, the
    // portal after a GNOME upgrade, or the user withdrew consent), leftover entries would otherwise
    // hold the same keys. Does nothing (one gsettings call) when there are none.
    public static bool RemoveAllEntries(IAppLogger logger) => RemoveAllEntries(logger, ProcessRunner.Instance);

    internal static bool RemoveAllEntries(IAppLogger logger, IProcessRunner runner)
    {
        try
        {
            var list = ReadList(runner);
            if (!list.Any(IsOwn)) return true;
            foreach (var path in list.Where(IsOwn))
                Gsettings(runner, "reset-recursively", EntrySchema + ":" + path);
            Gsettings(runner, "set", MediaKeysSchema, ListKey, GVariantStringArray(list.Where(p => !IsOwn(p)).ToList()));
            logger.Info("GnomeShortcutBridge", "Removed VibeSwitcher's GNOME keyboard shortcuts.");
            return true;
        }
        catch (Exception ex)
        {
            logger.Warning("GnomeShortcutBridge", $"Couldn't remove VibeSwitcher's GNOME keyboard shortcuts: {ex.Message}");
            return false;
        }
    }

    private List<string> ReadList() => ReadList(_runner);

    private static List<string> ReadList(IProcessRunner runner) =>
        ParseGVariantStringArray(Gsettings(runner, "get", MediaKeysSchema, ListKey));

    private void Set(string schema, string key, string value) => Gsettings("set", schema, key, value);

    private string Gsettings(params string[] args) => Gsettings(_runner, args);

    private static string Gsettings(IProcessRunner runner, params string[] args)
    {
        var r = runner.Run("gsettings", args, ToolTimeout);
        if (r.ExitCode != 0)
            throw new InvalidOperationException($"gsettings {args[0]} failed: {r.StdErr.Trim()}");
        return r.StdOut;
    }

    internal static string PathFor(string name) => BasePath + OwnPrefix + name + "/";
    private static bool IsOwn(string path) => path.StartsWith(BasePath + OwnPrefix, StringComparison.Ordinal);

    // GNOME splits the command like a shell does (g_shell_parse_argv), so every part is single-quoted.
    internal string CommandLineFor(AppCommand command)
    {
        var args = new List<string> { _launchPath };
        switch (command.Kind)
        {
            case AppCommandKind.Open: args.Add("--open"); break;
            case AppCommandKind.MiniMode: args.Add("--mini"); break;
            case AppCommandKind.Switch: args.Add("--switch"); args.Add(command.Argument!); break;
            case AppCommandKind.Mute: args.Add("--mute"); args.Add(command.Argument!.ToLowerInvariant()); break;
        }
        return string.Join(' ', args.Select(a => "'" + a.Replace("'", "'\\''") + "'"));
    }

    // GVariant text format, as gsettings prints and reads it.
    internal static string GVariantString(string s) =>
        "'" + s.Replace("\\", "\\\\").Replace("'", "\\'") + "'";

    internal static string GVariantStringArray(IReadOnlyList<string> items) =>
        items.Count == 0 ? "@as []" : "[" + string.Join(", ", items.Select(GVariantString)) + "]";

    // Parses "['a', 'b']", "@as []" or "[\"a\"]" into its strings.
    internal static List<string> ParseGVariantStringArray(string text)
    {
        var items = new List<string>();
        int i = 0;
        while (i < text.Length)
        {
            char q = text[i++];
            if (q != '\'' && q != '"') continue;
            var sb = new StringBuilder();
            while (i < text.Length && text[i] != q)
            {
                if (text[i] == '\\' && i + 1 < text.Length) i++;
                sb.Append(text[i++]);
            }
            i++; // closing quote
            items.Add(sb.ToString());
        }
        return items;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _timer.Dispose();
    }
}
