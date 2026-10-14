using System.Text.Json;
using VibeSwitcher.Models;

namespace VibeSwitcher.Platform;

public enum AppCommandKind
{
    Open,       // show the Settings window (what launching the app again does on Windows)
    Switch,     // switch to a profile by name or id
    Mute,       // toggle mute: mic, speakers or both
    MiniMode,   // show or hide Mini Mode (what the Mini Mode shortcut does)
}

// One instruction for the running app, from the command line or a second launch.
public sealed record AppCommand(AppCommandKind Kind, string? Argument = null)
{
    public static readonly AppCommand Open = new(AppCommandKind.Open);

    internal string Serialize() => JsonSerializer.Serialize(new Wire(Kind.ToString(), Argument));

    internal static AppCommand? Deserialize(string line)
    {
        try
        {
            var w = JsonSerializer.Deserialize<Wire>(line);
            if (w == null || !Enum.TryParse<AppCommandKind>(w.Kind, ignoreCase: false, out var k) || !Enum.IsDefined(k)) return null;
            // Same rules as the command line, so every handler gets a well-formed command.
            return k switch
            {
                AppCommandKind.Switch when string.IsNullOrWhiteSpace(w.Argument) => null,
                AppCommandKind.Mute when ParseMuteScope(w.Argument) == null => null,
                _ => new AppCommand(k, w.Argument),
            };
        }
        catch (JsonException) { return null; }
    }

    private sealed record Wire(string Kind, string? Argument);

    public static MuteScope? ParseMuteScope(string? s) => s?.Trim().ToLowerInvariant() switch
    {
        "mic" or "microphone" => MuteScope.Mic,
        "speakers" or "speaker" or "output" => MuteScope.Speakers,
        "both" or "all" => MuteScope.Both,
        _ => null,
    };
}

// The command line of the macOS/Linux app:
//   vibeswitcher                    start, or bring the running app's Settings forward
//   vibeswitcher --switch <profile> switch the running app to a profile (name or id)
//   vibeswitcher --mute mic|speakers|both
//   vibeswitcher --open             open Settings
//   vibeswitcher --mini             show or hide Mini Mode
//   vibeswitcher --help | --version
//   vibeswitcher --selftest | --diagnostics [file]   (run on their own, beside a running app)
// (Linux desktop shortcuts on Wayland call these, since apps can't grab global keys there.)
public enum LocalTool { None, SelfTest, Diagnostics }

public sealed record CommandLine(AppCommand? Command, bool ShowHelp, bool ShowVersion, string? Error,
    LocalTool Tool = LocalTool.None, string? ToolPath = null)
{
    public const string Usage =
        """
        Usage: vibeswitcher [option]

          (no option)            Start VibeSwitcher, or open Settings if it's already running
          --switch <profile>     Switch the running app to a profile (by name or id)
          --mute <mic|speakers|both>
                                 Toggle mute, like the mute shortcuts
          --open                 Open Settings
          --mini                 Show or hide Mini Mode
          --selftest             Check what VibeSwitcher needs on this computer, and print a report
          --diagnostics [file]   Save a zip with that report, the logs, the settings and pictures
                                 of the windows (to send when asking for help)
          --version              Print the version
          --help                 Show this help
        """;

    public static CommandLine Parse(IReadOnlyList<string> args)
    {
        // macOS adds its own arguments in some launches: "-psn_0_12345" from older Finder versions,
        // "-NSDocumentRevisionsDebugMode YES" (and other -NS…/-Apple… pairs) when run from Xcode.
        var list = new List<string>();
        for (int i = 0; i < args.Count; i++)
        {
            // (Never filter a value: "--switch -NSFoo" means a profile with that name.)
            bool isValue = list.Count > 0 && list[^1] is "--switch" or "--mute";
            if (isValue) { list.Add(args[i]); continue; }
            if (args[i].StartsWith("-psn_", StringComparison.Ordinal)) continue;
            if ((args[i].StartsWith("-NS", StringComparison.Ordinal) || args[i].StartsWith("-Apple", StringComparison.Ordinal)) && !args[i].StartsWith("--", StringComparison.Ordinal))
            {
                i++; // and its value
                continue;
            }
            // --switch=Gaming is the same as --switch Gaming.
            int eq = args[i].IndexOf('=');
            if (args[i].StartsWith("--", StringComparison.Ordinal) && eq > 2)
            {
                list.Add(args[i][..eq]);
                list.Add(args[i][(eq + 1)..]);
            }
            else list.Add(args[i]);
        }
        args = list;

        if (args.Count == 0) return new(AppCommand.Open, false, false, null);
        var a = args[0];
        switch (a)
        {
            case "--help" or "-h" or "-?":
                return new(null, true, false, null);
            case "--version":
                return new(null, false, true, null);
            case "--open":
                return Only(args, AppCommand.Open);
            case "--mini":
                return Only(args, new AppCommand(AppCommandKind.MiniMode));
            case "--selftest":
                return args.Count > 1 ? Fail($"Unexpected '{args[1]}'.") : new(null, false, false, null, LocalTool.SelfTest);
            case "--diagnostics":
                if (args.Count > 2) return Fail($"Unexpected '{args[2]}'.");
                return new(null, false, false, null, LocalTool.Diagnostics, args.Count > 1 ? args[1] : null);
            case "--switch":
                if (args.Count < 2 || string.IsNullOrWhiteSpace(args[1])) return Fail("--switch needs a profile name or id.");
                return Only(args, new AppCommand(AppCommandKind.Switch, args[1]), 2);
            case "--mute":
                if (args.Count < 2 || AppCommand.ParseMuteScope(args[1]) is not { } scope)
                    return Fail("--mute needs one of: mic, speakers, both.");
                return Only(args, new AppCommand(AppCommandKind.Mute, scope.ToString()), 2);
            default:
                return Fail($"Unknown option '{a}'.");
        }
    }

    private static CommandLine Only(IReadOnlyList<string> args, AppCommand cmd, int used = 1) =>
        args.Count > used ? Fail($"Unexpected '{args[used]}'.") : new(cmd, false, false, null);

    private static CommandLine Fail(string error) => new(null, false, false, error);
}
