using System.Text.Json;
using VibeSwitcher.Platform.Linux.Processes;

namespace VibeSwitcher.Platform.Linux.Audio;

// The sound server can't be reached at all: pactl is missing, or neither PulseAudio nor PipeWire's
// pulse server is running. The Linux counterpart of "Windows Audio service is not running".
internal sealed class SoundServerUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);

// A pactl command ran but failed (e.g. the device vanished between listing and switching).
internal sealed class PactlCommandException(string message) : Exception(message);

// Thin wrapper over the `pactl` command-line tool.
//
// unavailableCooldown: after the server has been found unreachable, further calls fail at once for
// this long instead of each waiting out a timeout — the device list is read many times in a row
// (e.g. on every headset event), and a hung server must not freeze the UI for seconds per call.
internal sealed class PactlClient(IProcessRunner runner, bool forceText = false, TimeSpan? unavailableCooldown = null)
{
    internal const string Tool = "pactl";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private readonly TimeSpan _cooldown = unavailableCooldown ?? TimeSpan.FromSeconds(3);
    private readonly object _gate = new();
    private DateTime _downUntil = DateTime.MinValue;
    private SoundServerUnavailableException? _lastDown;

    // 0 = not probed yet, 1 = JSON works, 2 = text only (pactl before 15.99 has no `-f json`).
    private volatile int _jsonState = forceText ? 2 : 0;

    internal bool? UsesJson => _jsonState switch { 1 => true, 2 => false, _ => null };

    internal IReadOnlyList<PulseDevice> ListDevices(bool sinks)
    {
        string what = sinks ? "sinks" : "sources";
        if (_jsonState != 2)
        {
            var r = Exec(["-f", "json", "list", what]);
            if (r.ExitCode == 0 && r.StdOut.TrimStart().StartsWith('['))
            {
                try
                {
                    var devices = PactlParser.ParseJsonDevices(r.StdOut, sinks);
                    _jsonState = 1;
                    return devices;
                }
                // Some pactl releases have printed broken JSON; the text output is always usable.
                catch (Exception ex) when (ex is JsonException or FormatException) { }
            }
            // (A down server already threw inside Exec.)
            // An older pactl doesn't know -f (or its JSON is unusable): use text from now on.
            _jsonState = 2;
        }
        var t = Exec(["list", what]);
        ThrowIfFailed(t, $"list {what}");
        return PactlParser.ParseTextDevices(t.StdOut, sinks);
    }

    internal PulseServerInfo GetServerInfo()
    {
        var r = Exec(["info"]);
        ThrowIfFailed(r, "info");
        return PactlParser.ParseInfo(r.StdOut);
    }

    internal void SetDefault(bool sink, string name) =>
        ThrowIfFailed(Exec([sink ? "set-default-sink" : "set-default-source", CheckName(name)]), "set default");

    internal void SetMute(bool sink, string name, bool mute) =>
        ThrowIfFailed(Exec([sink ? "set-sink-mute" : "set-source-mute", CheckName(name), mute ? "1" : "0"]), "set mute");

    // A name starting with "-" would be read as a pactl option (pactl doesn't accept "--" before
    // its arguments). Real sink/source names never start with one; a profile could only hold such an
    // id if config.json was edited by hand.
    private static string CheckName(string name) => name.StartsWith('-')
        ? throw new PactlCommandException($"'{name}' is not a valid audio device name.")
        : name;

    private ProcessResult Exec(string[] args)
    {
        lock (_gate)
            if (_lastDown != null && DateTime.UtcNow < _downUntil)
                throw new SoundServerUnavailableException(_lastDown.Message, _lastDown);
        try
        {
            var r = runner.Run(Tool, args, Timeout);
            if (r.ExitCode != 0 && LooksLikeServerDown(r.StdErr))
                throw Down(new SoundServerUnavailableException(
                    "The sound server (PipeWire or PulseAudio) isn't running. Log out and back in, or start it, then try again. " +
                    $"({r.StdErr.Trim()})"));
            lock (_gate) _lastDown = null;
            return r;
        }
        catch (ToolNotFoundException ex)
        {
            throw Down(new SoundServerUnavailableException(
                "The 'pactl' tool isn't installed, so VibeSwitcher can't control audio devices. Install the " +
                "'pulseaudio-utils' package (Ubuntu, Debian, Mint, Fedora) and restart VibeSwitcher.", ex));
        }
        catch (TimeoutException ex)
        {
            throw Down(new SoundServerUnavailableException("The sound server isn't responding.", ex));
        }
        catch (InvalidOperationException ex)
        {
            throw Down(new SoundServerUnavailableException(ex.Message, ex));
        }
    }

    // Background refreshes call this first: they run because the server just said something changed
    // (or the event listener just reconnected), so a cooldown from before is stale by definition.
    internal void ClearCooldown()
    {
        lock (_gate) _lastDown = null;
    }

    private SoundServerUnavailableException Down(SoundServerUnavailableException ex)
    {
        lock (_gate)
        {
            _lastDown = ex;
            _downUntil = DateTime.UtcNow + _cooldown;
        }
        return ex;
    }

    private static void ThrowIfFailed(ProcessResult r, string what)
    {
        if (r.ExitCode == 0) return;
        throw new PactlCommandException($"pactl {what} failed (exit {r.ExitCode}): {r.StdErr.Trim()}");
    }

    internal static bool LooksLikeServerDown(string stderr) =>
        stderr.Contains("Connection failure", StringComparison.OrdinalIgnoreCase) ||
        stderr.Contains("Connection refused", StringComparison.OrdinalIgnoreCase) ||
        stderr.Contains("pa_context_connect", StringComparison.OrdinalIgnoreCase);
}
