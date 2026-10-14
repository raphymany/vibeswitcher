using VibeSwitcher.Models;

namespace VibeSwitcher.Platform;

// Stable names for the app's shortcuts (see IGlobalKeyGrabber.Describe). They only use letters,
// digits and '-', so they're valid as portal shortcut ids and as GNOME settings path names, and they
// never change for the same shortcut — that's how the desktop recognises a shortcut it already knows.
public static class ShortcutNames
{
    public const string Settings = "settings";
    public const string MiniMode = "mini-mode";
    // TestHotkey's "is this shortcut taken?" check: a grabber that can't test must not bind it.
    public const string Probe = "probe";

    private const string ProfilePrefix = "profile-";
    private const string MutePrefix = "mute-";

    public static string Profile(Guid id) => ProfilePrefix + id.ToString("D");
    public static string Mute(MuteScope scope) => MutePrefix + scope.ToString().ToLowerInvariant();

    // What the app should do for a shortcut: the same command its command line accepts.
    public static AppCommand? ToCommand(string name)
    {
        if (name == Settings) return AppCommand.Open;
        if (name == MiniMode) return new AppCommand(AppCommandKind.MiniMode);
        if (name.StartsWith(ProfilePrefix, StringComparison.Ordinal) && Guid.TryParse(name[ProfilePrefix.Length..], out var id))
            return new AppCommand(AppCommandKind.Switch, id.ToString("D"));
        if (name.StartsWith(MutePrefix, StringComparison.Ordinal) && AppCommand.ParseMuteScope(name[MutePrefix.Length..]) is { } scope)
            return new AppCommand(AppCommandKind.Mute, scope.ToString());
        return null;
    }
}
