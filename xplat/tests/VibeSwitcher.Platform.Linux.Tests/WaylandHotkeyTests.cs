using System.Diagnostics;
using VibeSwitcher.Models;
using VibeSwitcher.Platform;
using VibeSwitcher.Platform.Linux.Hotkeys;
using VibeSwitcher.Platform.Linux.Processes;

namespace VibeSwitcher.Platform.Linux.Tests;

public class ShortcutTriggerTests
{
    private const int Ctrl = HotkeyDefinition.MOD_CTRL, Alt = HotkeyDefinition.MOD_ALT,
        Shift = HotkeyDefinition.MOD_SHIFT, Win = HotkeyDefinition.MOD_WIN;

    [Theory]
    [InlineData(Ctrl | Alt, 0x47, "CTRL+ALT+g", "<Control><Alt>g")]
    [InlineData(Win | Shift, 0x31, "SHIFT+LOGO+1", "<Shift><Super>1")]
    [InlineData(Ctrl | Alt | Shift | Win, 0x7B, "CTRL+ALT+SHIFT+LOGO+F12", "<Control><Alt><Shift><Super>F12")]
    [InlineData(Ctrl, 0x87, "CTRL+F24", "<Control>F24")]
    [InlineData(Alt, 0x65, "ALT+KP_5", "<Alt>KP_5")]
    [InlineData(Ctrl, 0xBA, "CTRL+semicolon", "<Control>semicolon")]
    [InlineData(Ctrl, 0x21, "CTRL+Page_Up", "<Control>Page_Up")]
    [InlineData(0, 0xB3, "XF86AudioPlay", "XF86AudioPlay")]
    public void PortalAndGnomeForms(int mods, int vk, string portal, string gnome)
    {
        Assert.Equal(portal, ShortcutTriggers.Portal(mods, vk));
        Assert.Equal(gnome, ShortcutTriggers.Gnome(mods, vk));
    }

    [Fact]
    public void EveryKeyX11CanGrab_HasAName_SoWaylandSupportsTheSameKeys()
    {
        var missing = Enumerable.Range(1, 254)
            .Where(vk => X11Keysyms.FromVirtualKey(vk) != null && ShortcutTriggers.KeyName(vk) == null)
            .Select(vk => $"0x{vk:X2}").ToList();
        Assert.True(missing.Count == 0, "No keysym name for: " + string.Join(", ", missing));
        Assert.Null(ShortcutTriggers.Portal(Ctrl, 0x10)); // a modifier on its own isn't a key
    }
}

// An in-memory stand-in for gsettings (only the commands the bridge uses).
internal sealed class FakeGsettings : IProcessRunner
{
    public readonly Dictionary<(string Schema, string Key), string> Values = [];
    public readonly List<string> Writes = [];
    public bool Fail;

    public string List => Values.GetValueOrDefault((GnomeShortcutBridge.MediaKeysSchema, GnomeShortcutBridge.ListKey), "@as []");
    // The value as text (gsettings stores it in GVariant form).
    public string? Entry(string path, string key) =>
        Values.TryGetValue((GnomeShortcutBridge.EntrySchema + ":" + path, key), out var v) ? GnomeShortcutBridge.ParseGVariantStringArray(v).Single() : null;

    public ProcessResult Run(string file, IReadOnlyList<string> args, TimeSpan timeout)
    {
        Assert.Equal("gsettings", file);
        lock (Values)
        {
            if (Fail) return new ProcessResult(1, "", "dconf is not running");
            switch (args[0])
            {
                case "list-keys":
                    return new ProcessResult(0, "media-keys\ncustom-keybindings\nscreensaver\n", "");
                case "get":
                    return new ProcessResult(0, Values.GetValueOrDefault((args[1], args[2]), args[2] == GnomeShortcutBridge.ListKey ? "@as []" : "''") + "\n", "");
                case "set":
                    Writes.Add($"{args[1]} {args[2]}");
                    Values[(args[1], args[2])] = args[3];
                    return new ProcessResult(0, "", "");
                case "reset-recursively":
                    Writes.Add($"reset {args[1]}");
                    foreach (var k in Values.Keys.Where(k => k.Schema == args[1]).ToList()) Values.Remove(k);
                    return new ProcessResult(0, "", "");
            }
        }
        return new ProcessResult(1, "", "unknown command");
    }

    public IRunningProcess Start(string file, IReadOnlyList<string> args) => throw new NotSupportedException();
}

public class GnomeShortcutBridgeTests
{
    private const int CtrlAlt = HotkeyDefinition.MOD_CTRL | HotkeyDefinition.MOD_ALT;
    private const string Launch = "/home/sam/Apps/Vibe Switcher's.AppImage";
    private static readonly string OtherEntry = GnomeShortcutBridge.BasePath + "custom0/";

    private readonly FakeGsettings _gs = new();
    private readonly LinuxAudioServiceTests.RecordingLogger _log = new();
    private readonly GnomeShortcutBridge _bridge;

    public GnomeShortcutBridgeTests()
    {
        // The user already has a custom shortcut of their own.
        _gs.Values[(GnomeShortcutBridge.MediaKeysSchema, GnomeShortcutBridge.ListKey)] = $"['{OtherEntry}']";
        _gs.Values[(GnomeShortcutBridge.EntrySchema + ":" + OtherEntry, "binding")] = "'<Super>t'";
        _bridge = new GnomeShortcutBridge(_log, _gs, Launch, Timeout.InfiniteTimeSpan);
    }

    private GrabResult Add(ushort id, string name, int vk, string description = "Do it")
    {
        _bridge.Describe(id, name, description);
        return _bridge.Register(id, CtrlAlt, vk, out _);
    }

    [Fact]
    public void Shortcuts_BecomeGnomeCustomShortcuts_RunningTheCommandLine()
    {
        var profile = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
        Assert.Equal(GrabResult.Ok, Add(1, ShortcutNames.Profile(profile), 0x47, "Switch to Gaming"));
        Assert.Equal(GrabResult.Ok, Add(2, ShortcutNames.Mute(MuteScope.Mic), 0x4D));
        Assert.Equal(GrabResult.Ok, Add(3, ShortcutNames.MiniMode, 0x43));
        Assert.Equal(GrabResult.Ok, Add(4, ShortcutNames.Settings, 0x53));
        _bridge.Flush();

        var list = GnomeShortcutBridge.ParseGVariantStringArray(_gs.List);
        Assert.Equal(OtherEntry, list[0]); // the user's own shortcut keeps its place
        Assert.Equal(5, list.Count);
        var p = GnomeShortcutBridge.PathFor(ShortcutNames.Profile(profile));
        Assert.Equal("VibeSwitcher: Switch to Gaming", _gs.Entry(p, "name"));
        Assert.Equal("<Control><Alt>g", _gs.Entry(p, "binding"));
        // GNOME splits the command like a shell: the path's space and apostrophe must survive.
        Assert.Equal(@"'/home/sam/Apps/Vibe Switcher'\''s.AppImage' '--switch' '0f8fad5b-d9cb-469f-a165-70867728950e'",
            _gs.Entry(p, "command"));
        Assert.Equal(@"'/home/sam/Apps/Vibe Switcher'\''s.AppImage' '--mute' 'mic'", _gs.Entry(GnomeShortcutBridge.PathFor("mute-mic"), "command"));
        Assert.EndsWith("'--mini'", _gs.Entry(GnomeShortcutBridge.PathFor("mini-mode"), "command"));
        Assert.EndsWith("'--open'", _gs.Entry(GnomeShortcutBridge.PathFor("settings"), "command"));
    }

    [Fact]
    public void TheCommandLineGnomeRuns_ParsesBackToTheSameCommand()
    {
        var id = Guid.NewGuid();
        foreach (var name in new[] { ShortcutNames.Profile(id), ShortcutNames.Mute(MuteScope.Both), ShortcutNames.MiniMode, ShortcutNames.Settings })
        {
            var expected = ShortcutNames.ToCommand(name)!;
            var argv = ShellSplit(_bridge.CommandLineFor(expected));
            Assert.Equal(Launch, argv[0]);
            Assert.Equal(expected, CommandLine.Parse(argv[1..]).Command);
        }
    }

    [Fact]
    public void NothingChanged_NothingWritten_AsWhenTestHotkeyRetakesEverything()
    {
        Add(1, ShortcutNames.Settings, 0x53);
        Add(2, ShortcutNames.MiniMode, 0x43);
        _bridge.Flush();
        int writes = _gs.Writes.Count;

        _bridge.Unregister(1);
        _bridge.Unregister(2);
        Add(9, ShortcutNames.Probe, 0x48);
        _bridge.Unregister(9);
        Assert.Equal(GrabResult.Ok, _bridge.Register(1, CtrlAlt, 0x53, out _));
        Assert.Equal(GrabResult.Ok, _bridge.Register(2, CtrlAlt, 0x43, out _));
        _bridge.Flush();

        Assert.Equal(writes, _gs.Writes.Count);
    }

    [Fact]
    public void Removed_AreDeleted_OthersUntouched()
    {
        Add(1, ShortcutNames.Settings, 0x53);
        Add(2, ShortcutNames.MiniMode, 0x43);
        _bridge.Flush();
        _bridge.Unregister(2);
        _bridge.Flush();

        var list = GnomeShortcutBridge.ParseGVariantStringArray(_gs.List);
        Assert.Equal([OtherEntry, GnomeShortcutBridge.PathFor("settings")], list);
        Assert.Null(_gs.Entry(GnomeShortcutBridge.PathFor("mini-mode"), "binding"));
        Assert.Equal("'<Super>t'", _gs.Values[(GnomeShortcutBridge.EntrySchema + ":" + OtherEntry, "binding")]);
    }

    [Fact]
    public void RemoveAll_ClearsOurs_IncludingOnesFromEarlierRuns()
    {
        var stale = GnomeShortcutBridge.PathFor("profile-from-an-old-run");
        _gs.Values[(GnomeShortcutBridge.MediaKeysSchema, GnomeShortcutBridge.ListKey)] = $"['{OtherEntry}', '{stale}']";
        _gs.Values[(GnomeShortcutBridge.EntrySchema + ":" + stale, "binding")] = "'<Control>x'";
        Add(1, ShortcutNames.Settings, 0x53);
        _bridge.Flush();

        Assert.True(_bridge.RemoveAll());
        Assert.Equal([OtherEntry], GnomeShortcutBridge.ParseGVariantStringArray(_gs.List));
        Assert.Null(_gs.Entry(stale, "binding"));
    }

    [Fact]
    public void Probe_SameShortcutTwice_UnknownKey_AndUnnamed()
    {
        Assert.Equal(GrabResult.Ok, Add(1, ShortcutNames.Probe, 0x48));
        Assert.Equal(GrabResult.Ok, Add(2, ShortcutNames.Settings, 0x53));
        Assert.Equal(GrabResult.InUse, Add(3, ShortcutNames.MiniMode, 0x53)); // as on Windows
        Assert.Equal(GrabResult.Unsupported, Add(4, ShortcutNames.MiniMode, 0x10));
        Assert.Equal(GrabResult.Failed, _bridge.Register(5, CtrlAlt, 0x41, out var error));
        Assert.NotNull(error);
        _bridge.Flush();
        Assert.Equal(2, GnomeShortcutBridge.ParseGVariantStringArray(_gs.List).Count); // the user's + Settings
    }

    [Fact]
    public void GsettingsFailing_LoggedOnce_AndRetriedOnTheNextChange()
    {
        _gs.Fail = true;
        Add(1, ShortcutNames.Settings, 0x53);
        _bridge.Flush();
        Add(2, ShortcutNames.MiniMode, 0x43);
        _bridge.Flush();
        Assert.Single(_log.Warnings);

        _gs.Fail = false;
        _bridge.Unregister(2);
        _bridge.Flush();
        Assert.Contains(GnomeShortcutBridge.PathFor("settings"), _gs.List);
    }

    [Fact]
    public void Changes_AreWrittenShortlyAfterTheLastOne()
    {
        using var bridge = new GnomeShortcutBridge(_log, _gs, Launch, TimeSpan.FromMilliseconds(20));
        bridge.Describe(1, ShortcutNames.Settings, "Open settings");
        bridge.Register(1, CtrlAlt, 0x53, out _);
        var sw = Stopwatch.StartNew();
        while (!_gs.List.Contains("vibeswitcher-settings") && sw.Elapsed < TimeSpan.FromSeconds(5)) Thread.Sleep(10);
        Assert.Contains("vibeswitcher-settings", _gs.List);
    }

    [Fact]
    public void RemoveAllEntries_WithoutABridge_AndOnlyWhenThereAreAny()
    {
        var log = new LinuxAudioServiceTests.RecordingLogger();
        Assert.True(GnomeShortcutBridge.RemoveAllEntries(log, _gs));
        Assert.Empty(_gs.Writes); // none of ours: nothing written

        var stale = GnomeShortcutBridge.PathFor("settings");
        _gs.Values[(GnomeShortcutBridge.MediaKeysSchema, GnomeShortcutBridge.ListKey)] = $"['{stale}', '{OtherEntry}']";
        _gs.Values[(GnomeShortcutBridge.EntrySchema + ":" + stale, "binding")] = "'<Control>s'";
        Assert.True(GnomeShortcutBridge.RemoveAllEntries(log, _gs));
        Assert.Equal([OtherEntry], GnomeShortcutBridge.ParseGVariantStringArray(_gs.List));
        Assert.Null(_gs.Entry(stale, "binding"));

        _gs.Fail = true;
        Assert.False(GnomeShortcutBridge.RemoveAllEntries(log, _gs));
        Assert.Single(log.Warnings);
    }

    [Theory]
    [InlineData("/opt/VibeSwitcher/vibeswitcher", true)]
    [InlineData("/home/sam/Apps/VibeSwitcher.AppImage", true)]
    [InlineData("/tmp/.mount_VibeSwAbC123/usr/bin/vibeswitcher", false)] // an AppImage's temporary mount
    [InlineData("/home/sam/100%/vibeswitcher", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyLaunchPathsGnomeCanKeepRunning(string? path, bool usable) =>
        Assert.Equal(usable ? path : null, GnomeShortcutBridge.UsableLaunchPath(path));

    [Fact]
    public void Availability_AndDispose()
    {
        Assert.True(GnomeShortcutBridge.IsAvailable(_gs));
        _bridge.Dispose();
        Assert.Equal(GrabResult.Failed, Add(1, ShortcutNames.Settings, 0x53));
        _bridge.Unregister(1);
        _bridge.Dispose();
    }

    [Theory]
    [InlineData("@as []")]
    [InlineData("[]")]
    public void GVariant_EmptyArray(string text) => Assert.Empty(GnomeShortcutBridge.ParseGVariantStringArray(text));

    [Fact]
    public void GVariant_RoundTrip()
    {
        string[] items = ["/a/b/", "it's", @"back\slash", "\"quoted\""];
        Assert.Equal(items, GnomeShortcutBridge.ParseGVariantStringArray(GnomeShortcutBridge.GVariantStringArray(items)));
        Assert.Equal("@as []", GnomeShortcutBridge.GVariantStringArray([]));
        Assert.Equal(["x", "y"], GnomeShortcutBridge.ParseGVariantStringArray("[\"x\", 'y']\n"));
    }

    // g_shell_parse_argv's rules for what CommandLineFor produces: single-quoted words, '\'' for a quote.
    private static string[] ShellSplit(string s)
    {
        var args = new List<string>();
        var cur = new System.Text.StringBuilder();
        bool inQuote = false, any = false;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (inQuote) { if (c == '\'') inQuote = false; else cur.Append(c); }
            else if (c == '\'') { inQuote = true; any = true; }
            else if (c == '\\' && i + 1 < s.Length) { cur.Append(s[++i]); any = true; }
            else if (c == ' ') { if (any) { args.Add(cur.ToString()); cur.Clear(); any = false; } }
            else { cur.Append(c); any = true; }
        }
        if (any) args.Add(cur.ToString());
        return [.. args];
    }
}

public class LinuxHotkeySelectionTests
{
    private sealed class StubGrabber : IGlobalKeyGrabber
    {
        public event Action<ushort>? Pressed { add { } remove { } }
        public GrabResult Register(ushort id, int modifiers, int virtualKey, out string? error) { error = null; return GrabResult.Ok; }
        public void Unregister(ushort id) { }
        public void Dispose() { }
    }

    private static Func<string, string?> Env(params (string Key, string Value)[] vars) =>
        k => vars.FirstOrDefault(v => v.Key == k).Value;

    private int _cleanups;

    private Task<LinuxHotkeySetup> Pick(Func<string, string?> env, bool consent = false, bool x11 = true, bool gnome = true, bool launchable = true) =>
        LinuxHotkeys.CreateAsync(consent, env,
            () => x11 ? new StubGrabber() : null,
            () => Task.FromResult<PortalKeyGrabber?>(null),
            () => gnome,
            () => launchable ? new StubGrabber() : null,
            () => { Interlocked.Increment(ref _cleanups); return true; });

    [Fact]
    public async Task GnomeCantStartThisCopy_Off_WithAReason_AndOldEntriesRemoved()
    {
        var s = await Pick(Env(("XDG_SESSION_TYPE", "wayland"), ("XDG_CURRENT_DESKTOP", "GNOME")), consent: true, launchable: false);
        Assert.Equal(LinuxHotkeyBackend.None, s.Backend);
        Assert.Equal(LinuxHotkeys.GnomeCantLaunch, s.Explanation);
        Assert.Equal(1, _cleanups);
    }

    [Fact]
    public async Task LeftoverGnomeShortcuts_AreRemoved_WheneverGnomeUsesAnotherWay()
    {
        // GNOME on Xorg: X11 grabs, and the keys must not stay held by old GNOME entries.
        await Pick(Env(("XDG_SESSION_TYPE", "x11"), ("XDG_CURRENT_DESKTOP", "ubuntu:GNOME")));
        Assert.Equal(1, _cleanups);
        // Consent withdrawn.
        await Pick(Env(("XDG_SESSION_TYPE", "wayland"), ("XDG_CURRENT_DESKTOP", "GNOME")));
        Assert.Equal(2, _cleanups);
        // In use: kept. Not GNOME, or GNOME's settings not there: nothing to do.
        await Pick(Env(("XDG_SESSION_TYPE", "wayland"), ("XDG_CURRENT_DESKTOP", "GNOME")), consent: true);
        await Pick(Env(("XDG_SESSION_TYPE", "x11"), ("XDG_CURRENT_DESKTOP", "KDE")));
        await Pick(Env(("XDG_SESSION_TYPE", "x11"), ("XDG_CURRENT_DESKTOP", "GNOME")), gnome: false);
        Assert.Equal(2, _cleanups);
    }

    [Fact]
    public async Task X11Session_UsesKeyGrabs()
    {
        var s = await Pick(Env(("XDG_SESSION_TYPE", "x11"), ("XDG_CURRENT_DESKTOP", "GNOME")));
        Assert.Equal(LinuxHotkeyBackend.X11, s.Backend);
        Assert.IsType<StubGrabber>(s.Grabber);
    }

    [Fact]
    public async Task X11Session_NoDisplay_Off_WithAReason()
    {
        var s = await Pick(Env(("XDG_SESSION_TYPE", "x11")), x11: false);
        Assert.Equal(LinuxHotkeyBackend.None, s.Backend);
        Assert.Equal(GrabResult.Unsupported, s.Grabber.Register(1, 0, 0x41, out var why));
        Assert.Equal(s.Explanation, why);
    }

    [Fact]
    public async Task OldGnomeOnWayland_UsesGnomeShortcuts_OnlyWithConsent()
    {
        var env = Env(("XDG_SESSION_TYPE", "wayland"), ("XDG_CURRENT_DESKTOP", "ubuntu:GNOME"));
        var without = await Pick(env);
        Assert.Equal(LinuxHotkeyBackend.GnomeNeedsConsent, without.Backend);
        Assert.IsType<NoKeyGrabber>(without.Grabber);
        Assert.Equal(LinuxHotkeyBackend.GnomeShortcuts, (await Pick(env, consent: true)).Backend);
    }

    [Fact]
    public async Task WaylandWithoutAnyWay_Off_AndSuggestsTheCommandLine()
    {
        var kde = await Pick(Env(("WAYLAND_DISPLAY", "wayland-0"), ("XDG_CURRENT_DESKTOP", "KDE")), consent: true);
        Assert.Equal(LinuxHotkeyBackend.None, kde.Backend);
        Assert.Contains("--switch", kde.Explanation);
        // GNOME whose keyboard settings can't be found: also off.
        Assert.Equal(LinuxHotkeyBackend.None,
            (await Pick(Env(("XDG_SESSION_TYPE", "wayland"), ("XDG_CURRENT_DESKTOP", "GNOME")), consent: true, gnome: false)).Backend);
    }
}

// GNOME's real gsettings tool and schemas, with a throwaway settings file (CI installs the schemas).
public sealed class GsettingsFactAttribute : FactAttribute
{
    public GsettingsFactAttribute()
    {
        if (!OperatingSystem.IsLinux() || Environment.GetEnvironmentVariable("VS_GSETTINGS_IT") != "1")
            Skip = "Needs gsettings + GNOME schemas: VS_GSETTINGS_IT=1 (Linux; CI sets it up).";
    }
}

[Trait("Category", "Gnome")]
public sealed class GnomeShortcutBridgeIntegrationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vs-gsettings-" + Guid.NewGuid().ToString("N"));
    private readonly string? _oldBackend = Environment.GetEnvironmentVariable("GSETTINGS_BACKEND");
    private readonly string? _oldConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");

    public GnomeShortcutBridgeIntegrationTests()
    {
        // The keyfile backend writes to $XDG_CONFIG_HOME/glib-2.0/settings/keyfile: never the real settings.
        Directory.CreateDirectory(_dir);
        Environment.SetEnvironmentVariable("GSETTINGS_BACKEND", "keyfile");
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _dir);
    }

    private static string Gs(params string[] args)
    {
        var r = ProcessRunner.Instance.Run("gsettings", args, TimeSpan.FromSeconds(10));
        Assert.True(r.ExitCode == 0, r.StdErr);
        return r.StdOut.Trim();
    }

    [GsettingsFact]
    public void WritesRealGnomeShortcuts_AndRemovesThemAgain()
    {
        Assert.True(GnomeShortcutBridge.IsAvailable(ProcessRunner.Instance));
        var other = GnomeShortcutBridge.BasePath + "custom0/";
        Gs("set", GnomeShortcutBridge.MediaKeysSchema, GnomeShortcutBridge.ListKey, $"['{other}']");
        Gs("set", GnomeShortcutBridge.EntrySchema + ":" + other, "binding", "'<Super>t'");

        using var bridge = new GnomeShortcutBridge(new LinuxAudioServiceTests.RecordingLogger(), ProcessRunner.Instance, "/opt/vibe switcher/vibeswitcher", Timeout.InfiniteTimeSpan);
        bridge.Describe(1, ShortcutNames.Mute(MuteScope.Speakers), "Mute or unmute the speakers");
        Assert.Equal(GrabResult.Ok, bridge.Register(1, HotkeyDefinition.MOD_CTRL | HotkeyDefinition.MOD_SHIFT, 0x4D, out _));
        bridge.Flush();

        var path = GnomeShortcutBridge.PathFor("mute-speakers");
        Assert.Equal([other, path], GnomeShortcutBridge.ParseGVariantStringArray(Gs("get", GnomeShortcutBridge.MediaKeysSchema, GnomeShortcutBridge.ListKey)));
        var schema = GnomeShortcutBridge.EntrySchema + ":" + path;
        Assert.Equal("'<Control><Shift>m'", Gs("get", schema, "binding"));
        Assert.Equal("'VibeSwitcher: Mute or unmute the speakers'", Gs("get", schema, "name"));
        Assert.Equal(@"""'/opt/vibe switcher/vibeswitcher' '--mute' 'speakers'""", Gs("get", schema, "command"));

        Assert.True(bridge.RemoveAll());
        Assert.Equal([other], GnomeShortcutBridge.ParseGVariantStringArray(Gs("get", GnomeShortcutBridge.MediaKeysSchema, GnomeShortcutBridge.ListKey)));
        Assert.Equal("''", Gs("get", schema, "binding"));
        Assert.Equal("'<Super>t'", Gs("get", GnomeShortcutBridge.EntrySchema + ":" + other, "binding"));
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("GSETTINGS_BACKEND", _oldBackend);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _oldConfig);
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }
}
