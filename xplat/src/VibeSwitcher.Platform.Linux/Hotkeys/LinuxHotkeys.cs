using VibeSwitcher.Helpers;

namespace VibeSwitcher.Platform.Linux.Hotkeys;

public enum LinuxHotkeyBackend
{
    X11,                // key grabs on the X server
    Portal,             // the desktop's Global Shortcuts service (Wayland: KDE, GNOME 48+, …)
    GnomeShortcuts,     // GNOME custom keyboard shortcuts (Wayland, GNOME before 48)
    GnomeNeedsConsent,  // as above, but the user hasn't agreed yet: shortcuts are off until they do
    None,               // nothing available: shortcuts are off (Explanation says why)
}

public sealed record LinuxHotkeySetup(LinuxHotkeyBackend Backend, IGlobalKeyGrabber Grabber, string? Explanation);

// Picks how global hotkeys work in this Linux session:
//   X11 session                 → X11 key grabs
//   Wayland + Global Shortcuts  → the portal (the desktop asks the user to approve the shortcuts)
//   Wayland + GNOME without it  → GNOME custom shortcuts, once the user agrees
//   anything else               → off, with an explanation (the command line still works, so the
//                                 user can bind keys to it in their desktop's settings)
// On GNOME, custom shortcuts VibeSwitcher added in an earlier session are removed whenever another
// way is used (an X11 login, GNOME upgraded to 48, consent withdrawn) — they'd hold the same keys.
// Call it off the UI thread's critical path: it may run gsettings.
public static class LinuxHotkeys
{
    // cleanUp: false for a look only (the self-test) — the running app's GNOME shortcuts are left be.
    public static Task<LinuxHotkeySetup> CreateAsync(IAppLogger logger, bool gnomeShortcutsAllowed, bool cleanUp = true) =>
        CreateAsync(gnomeShortcutsAllowed, Environment.GetEnvironmentVariable,
            () => X11KeyGrabber.TryCreate(),
            () => PortalKeyGrabber.TryCreateAsync(logger),
            () => GnomeShortcutBridge.IsAvailable(),
            () => GnomeShortcutBridge.DefaultLaunchPath() is { } path ? new GnomeShortcutBridge(logger, path) : null,
            cleanUp ? () => GnomeShortcutBridge.RemoveAllEntries(logger) : () => false);

    // gnomeBridge: null when GNOME couldn't keep running this copy (see GnomeShortcutBridge.DefaultLaunchPath).
    internal static async Task<LinuxHotkeySetup> CreateAsync(bool gnomeShortcutsAllowed,
        Func<string, string?> env,
        Func<IGlobalKeyGrabber?> x11,
        Func<Task<PortalKeyGrabber?>> portal,
        Func<bool> gnomeAvailable,
        Func<IGlobalKeyGrabber?> gnomeBridge,
        Func<bool> removeGnomeShortcuts)
    {
        var gnome = new Lazy<bool>(gnomeAvailable); // asked at most once
        var setup = await Choose(gnomeShortcutsAllowed, env, x11, portal, () => gnome.Value, gnomeBridge).ConfigureAwait(false);
        if (setup.Backend != LinuxHotkeyBackend.GnomeShortcuts && IsGnome(env))
            await Task.Run(() => { if (gnome.Value) removeGnomeShortcuts(); }).ConfigureAwait(false);
        return setup;
    }

    private static async Task<LinuxHotkeySetup> Choose(bool gnomeShortcutsAllowed,
        Func<string, string?> env,
        Func<IGlobalKeyGrabber?> x11,
        Func<Task<PortalKeyGrabber?>> portal,
        Func<bool> gnomeAvailable,
        Func<IGlobalKeyGrabber?> gnomeBridge)
    {
        if (!X11KeyGrabber.IsWaylandSession(env))
        {
            if (x11() is { } grabber) return new(LinuxHotkeyBackend.X11, grabber, null);
            return Off(LinuxHotkeyBackend.None, NoX11);
        }

        if (await portal().ConfigureAwait(false) is { } p) return new(LinuxHotkeyBackend.Portal, p, null);

        if (IsGnome(env) && await Task.Run(gnomeAvailable).ConfigureAwait(false))
        {
            if (!gnomeShortcutsAllowed) return Off(LinuxHotkeyBackend.GnomeNeedsConsent, GnomeConsent);
            return gnomeBridge() is { } bridge
                ? new(LinuxHotkeyBackend.GnomeShortcuts, bridge, null)
                : Off(LinuxHotkeyBackend.None, GnomeCantLaunch);
        }
        return Off(LinuxHotkeyBackend.None, NoWaylandShortcuts);
    }

    private static LinuxHotkeySetup Off(LinuxHotkeyBackend backend, string why) => new(backend, new NoKeyGrabber(why), why);

    // XDG_CURRENT_DESKTOP is a ':'-separated list, e.g. "ubuntu:GNOME" or "pop:GNOME".
    internal static bool IsGnome(Func<string, string?> env) =>
        (env("XDG_CURRENT_DESKTOP") ?? "").Split(':').Any(d => d.Equals("GNOME", StringComparison.OrdinalIgnoreCase));

    internal const string NoX11 =
        "VibeSwitcher couldn't connect to the display server, so global shortcuts are off.";
    internal const string GnomeConsent =
        "This version of GNOME doesn't let apps set global shortcuts directly. VibeSwitcher can add its shortcuts to " +
        "GNOME's keyboard settings (Settings › Keyboard › Custom Shortcuts) once you allow it.";
    internal const string GnomeCantLaunch =
        "VibeSwitcher can't add GNOME keyboard shortcuts for this copy, because GNOME couldn't start it from where it's " +
        "running. Move it to a permanent place (for an AppImage, start it from its own file) to turn shortcuts on.";
    internal const string NoWaylandShortcuts =
        "This desktop doesn't let apps set global shortcuts. You can add keyboard shortcuts in your desktop's settings " +
        "that run VibeSwitcher's commands instead, for example: vibeswitcher --switch \"Gaming\"";
}

// Shortcuts are off: every registration says why (shown in the session log).
public sealed class NoKeyGrabber(string reason) : IGlobalKeyGrabber
{
    public event Action<ushort>? Pressed { add { } remove { } }

    public GrabResult Register(ushort id, int modifiers, int virtualKey, out string? error)
    {
        error = reason;
        return GrabResult.Unsupported;
    }

    public void Unregister(ushort id) { }
    public void Dispose() { }
}
