using System.Reflection;
using VibeSwitcher.Platform;

namespace VibeSwitcher.App;

// The command line, before any window: --version and --help answer and exit, a bad option is
// explained and exits; anything else starts the app — or, when VibeSwitcher is already running,
// hands the command (--switch / --mute / --open / --mini, or a plain launch: open Settings) to it.
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
        if (cl.Tool == LocalTool.SelfTest)
        {
            if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
            {
                error.WriteLine("The self-test is for VibeSwitcher on macOS and Linux.");
                return 2;
            }
            var report = Diagnostics.SelfTest.RunAsync(Hosting.IHostPlatform.Current(), AppPaths.Current()).GetAwaiter().GetResult();
            output.Write(report.Format());
            return report.ExitCode;
        }
        return null;
    }

    // Takes the single-instance lock (null: this is the running copy now, go on starting), or sends
    // the command to the copy that has it and returns the exit code: 0 done, 1 refused or no answer.
    internal static int? AcquireOrForward(SingleInstance instance, AppPaths paths, AppCommand command,
        Func<AppCommand, Task<string>> handle, TextWriter output, TextWriter error)
    {
        try
        {
            if (instance.TryAcquire(handle)) return null;
        }
        catch (InvalidOperationException ex)
        {
            error.WriteLine($"VibeSwitcher can't start: {ex.Message}");
            return 1;
        }
        var reply = SingleInstance.SendAsync(paths, command).GetAwaiter().GetResult();
        if (reply == null)
        {
            // (Its folder's path can be too long for the command channel: then no waiting will help.)
            error.WriteLine(paths.SocketPathFits
                ? "VibeSwitcher is already running but didn't answer. Try again in a moment."
                : "VibeSwitcher is already running, but it can't take commands from here: its settings folder's path is too long.");
            return 1;
        }
        if (reply.StartsWith("error:", StringComparison.Ordinal))
        {
            error.WriteLine(reply["error:".Length..].Trim());
            return 1;
        }
        if (reply != "ok") output.WriteLine(reply);
        return 0;
    }
}