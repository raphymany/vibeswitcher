using System.Reflection;
using VibeSwitcher.Platform;

namespace VibeSwitcher.App;

// The command line, before any window: --version and --help answer and exit, a bad option is
// explained and exits; anything else starts the app (sending --switch/--mute/--open/--mini to an
// already-running VibeSwitcher comes with the app wiring).
internal static class Cli
{
    public static string Version =>
        typeof(Cli).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(Cli).Assembly.GetName().Version?.ToString() ?? "?";

    // An exit code when the command line has been fully handled, or null to start the app.
    public static int? Handle(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        var cl = CommandLine.Parse(args);
        if (cl.Error != null)
        {
            error.WriteLine(cl.Error);
            error.WriteLine();
            error.WriteLine(CommandLine.Usage);
            return 2;
        }
        if (cl.ShowVersion)
        {
            output.WriteLine($"VibeSwitcher {Version}");
            return 0;
        }
        if (cl.ShowHelp)
        {
            output.WriteLine(CommandLine.Usage);
            return 0;
        }
        return null;
    }
}
