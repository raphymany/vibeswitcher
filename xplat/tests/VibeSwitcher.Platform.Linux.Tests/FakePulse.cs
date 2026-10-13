using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using VibeSwitcher.Platform.Linux.Processes;

namespace VibeSwitcher.Platform.Linux.Tests;

internal sealed class FakeDevice
{
    public required string Name { get; set; }
    public string Description { get; set; } = "";
    public bool Mute { get; set; }
    public string? MonitorOf { get; set; }
    public List<(string Name, string Availability)> Ports { get; } = [];
    public string? ActivePort { get; set; }
    public Dictionary<string, string> Props { get; } = [];
}

// A tiny in-memory sound server answering pactl/pacat/parec the way the real tools do (JSON, or
// text when JsonSupported = false, like a pactl older than 15.99).
internal sealed class FakePulse : IProcessRunner
{
    private readonly object _gate = new();
    // Tests take this while changing devices, so a listing never sees a half-made change.
    public object Gate => _gate;
    private readonly List<SubscribeProcess> _subscribers = [];

    public List<FakeDevice> Sinks { get; } = [];
    public List<FakeDevice> Sources { get; } = [];
    public string? DefaultSink { get; set; }
    public string? DefaultSource { get; set; }
    public bool JsonSupported { get; set; } = true;
    public bool BrokenJson { get; set; }
    public bool ServerDown { get; set; }
    public bool PactlMissing { get; set; }
    public HashSet<string> RefuseDefault { get; } = [];
    public List<string> Calls { get; } = [];

    // pacat: what was played, with its arguments. parec: the float samples to hand out.
    public List<(IReadOnlyList<string> Args, byte[] Data)> Played { get; } = [];
    public float[] RecordSamples { get; set; } = [];
    public int PacatExitCode { get; set; }
    // pacat never finishes (a stalled server) until it is killed.
    public bool PacatStalls { get; set; }
    public int PacatKills;

    public int SubscriberCount { get { lock (_gate) return _subscribers.Count(s => !s.Killed); } }

    public ProcessResult Run(string file, IReadOnlyList<string> args, TimeSpan timeout)
    {
        lock (_gate) Calls.Add($"{file} {string.Join(' ', args)}");
        if (file != "pactl") throw new InvalidOperationException($"unexpected tool {file}");
        if (PactlMissing) throw new ToolNotFoundException("pactl", new Exception("ENOENT"));
        if (ServerDown) return new ProcessResult(1, "", "Connection failure: Connection refused\n");

        var a = args.ToList();
        bool json = false;
        if (a.Count >= 2 && a[0] == "-f" && a[1] == "json")
        {
            if (!JsonSupported) return new ProcessResult(1, "", "pactl: unrecognized option '-f'\n");
            if (BrokenJson) return Ok("[{\"index\":0,\"name\":");
            json = true;
            a = a.Skip(2).ToList();
        }

        lock (_gate)
        {
            switch (a[0])
            {
                case "list" when a[1] is "sinks" or "sources":
                    var devs = a[1] == "sinks" ? Sinks : Sources;
                    return Ok(json ? Json(devs, a[1] == "sinks") : Text(devs, a[1] == "sinks"));
                case "info":
                    return Ok($"Server Name: fake\nDefault Sink: {DefaultSink ?? "(null)"}\nDefault Source: {DefaultSource ?? "(null)"}\n");
                case "set-default-sink" or "set-default-source":
                    bool sink = a[0] == "set-default-sink";
                    if (RefuseDefault.Contains(a[1]) || !(sink ? Sinks : Sources).Any(d => d.Name == a[1]))
                        return new ProcessResult(1, "", "Failure: No such entity\n");
                    if (sink) DefaultSink = a[1]; else DefaultSource = a[1];
                    return Ok("");
                case "set-sink-mute" or "set-source-mute":
                    bool isSink = a[0] == "set-sink-mute";
                    var target = a[1] == "@DEFAULT_SINK@" ? DefaultSink : a[1] == "@DEFAULT_SOURCE@" ? DefaultSource : a[1];
                    var d = (isSink ? Sinks : Sources).FirstOrDefault(x => x.Name == target);
                    if (d == null) return new ProcessResult(1, "", "Failure: No such entity\n");
                    d.Mute = a[2] == "1";
                    return Ok("");
                default:
                    return new ProcessResult(1, "", $"unknown command {a[0]}\n");
            }
        }
    }

    public IRunningProcess Start(string file, IReadOnlyList<string> args)
    {
        lock (_gate) Calls.Add($"{file} {string.Join(' ', args)}");
        if (PactlMissing) throw new ToolNotFoundException(file, new Exception("ENOENT"));
        switch (file)
        {
            case "pactl" when args.Count == 1 && args[0] == "subscribe":
                var sub = new SubscribeProcess(ServerDown);
                lock (_gate) _subscribers.Add(sub);
                return sub;
            case "pacat":
                return new PlaybackProcess(this, args);
            case "parec":
                var bytes = new byte[RecordSamples.Length * 4];
                Buffer.BlockCopy(RecordSamples, 0, bytes, 0, bytes.Length);
                return new RecordProcess(bytes);
            default:
                throw new InvalidOperationException($"unexpected start {file}");
        }
    }

    // Sends a `pactl subscribe` line to every live subscriber.
    public void Emit(string line)
    {
        List<SubscribeProcess> subs;
        lock (_gate) subs = _subscribers.Where(s => !s.Killed).ToList();
        foreach (var s in subs) s.Write(line);
    }

    // Ends every live subscriber, as if the sound server went away.
    public void DropSubscribers()
    {
        List<SubscribeProcess> subs;
        lock (_gate) subs = _subscribers.ToList();
        foreach (var s in subs) s.Kill();
    }

    private static ProcessResult Ok(string stdout) => new(0, stdout, "");

    private static string Json(List<FakeDevice> devs, bool sinks) => JsonSerializer.Serialize(devs.Select((d, i) => new Dictionary<string, object?>
    {
        ["index"] = i,
        ["name"] = d.Name,
        ["description"] = d.Description,
        ["mute"] = d.Mute,
        // pactl 16's quirk: a source's "monitor of sink" appears under "monitor_source".
        ["monitor_source"] = sinks ? d.Name + ".monitor" : d.MonitorOf ?? "n/a",
        ["properties"] = d.Props,
        ["ports"] = d.Ports.Select(p => new Dictionary<string, object> { ["name"] = p.Name, ["description"] = p.Name + " port", ["availability"] = p.Availability }).ToList(),
        ["active_port"] = d.ActivePort,
    }));

    private static string Text(List<FakeDevice> devs, bool sinks)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < devs.Count; i++)
        {
            var d = devs[i];
            sb.Append(sinks ? "Sink #" : "Source #").Append(i).Append('\n');
            sb.Append("\tState: SUSPENDED\n");
            sb.Append("\tName: ").Append(d.Name).Append('\n');
            sb.Append("\tDescription: ").Append(d.Description).Append('\n');
            sb.Append("\tMute: ").Append(d.Mute ? "yes" : "no").Append('\n');
            sb.Append("\tVolume: front-left: 65536 / 100% / 0.00 dB,   front-right: 65536 / 100% / 0.00 dB\n");
            sb.Append("\t        balance 0.00\n");
            if (!sinks) sb.Append("\tMonitor of Sink: ").Append(d.MonitorOf ?? "n/a").Append('\n');
            sb.Append("\tProperties:\n");
            foreach (var (k, v) in d.Props) sb.Append("\t\t").Append(k).Append(" = \"").Append(v.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append("\"\n");
            if (d.Ports.Count > 0)
            {
                sb.Append("\tPorts:\n");
                foreach (var (n, av) in d.Ports)
                    sb.Append("\t\t").Append(n).Append(": ").Append(n).Append(" port (type: Unknown, priority: 100, ").Append(av).Append(")\n");
                sb.Append("\tActive Port: ").Append(d.ActivePort ?? "n/a").Append('\n');
            }
            sb.Append("\tFormats:\n\t\tpcm\n\n");
        }
        return sb.ToString();
    }

    private sealed class SubscribeProcess : IRunningProcess
    {
        private readonly AnonymousPipeServerStream _server = new(PipeDirection.Out);
        private readonly AnonymousPipeClientStream _client;
        private readonly bool _fail;
        public volatile bool Killed;

        public SubscribeProcess(bool fail)
        {
            _client = new AnonymousPipeClientStream(PipeDirection.In, _server.ClientSafePipeHandle);
            _fail = fail;
            if (fail) Kill(); // a down server: pactl exits at once with an error
        }

        public Stream StandardInput => Stream.Null;
        public Stream StandardOutput => _client;
        public string StandardError => _fail ? "Connection failure: Connection refused" : "";
        public int ExitCode => Killed ? 1 : throw new InvalidOperationException("running");
        public bool WaitForExit(TimeSpan timeout) => Killed;

        public void Write(string line)
        {
            var b = Encoding.UTF8.GetBytes(line + "\n");
            try { _server.Write(b); _server.Flush(); } catch (IOException) { }
        }

        public void Kill()
        {
            Killed = true;
            try { _server.Dispose(); } catch (IOException) { }
        }

        public void Dispose() { Kill(); _client.Dispose(); }
    }

    // Signals when the caller closes it: pacat plays until EOF on stdin.
    private sealed class ClosingStream : MemoryStream
    {
        public readonly ManualResetEventSlim Closed = new();
        protected override void Dispose(bool disposing) { Closed.Set(); base.Dispose(disposing); }
    }

    private sealed class PlaybackProcess(FakePulse owner, IReadOnlyList<string> args) : IRunningProcess
    {
        private readonly ClosingStream _stdin = new();
        private bool _done;
        public Stream StandardInput => _stdin;
        public Stream StandardOutput => Stream.Null;
        public string StandardError => owner.PacatExitCode != 0 ? "Stream error: No such entity" : "";
        public int ExitCode => owner.PacatExitCode;

        public bool WaitForExit(TimeSpan timeout)
        {
            if (owner.PacatStalls) return _killed;
            if (!_stdin.Closed.Wait(timeout)) return false;
            if (!_done)
            {
                _done = true;
                lock (owner._gate) owner.Played.Add((args, _stdin.ToArray()));
            }
            return true;
        }

        private volatile bool _killed;
        public void Kill() { if (!_killed) { _killed = true; Interlocked.Increment(ref owner.PacatKills); } }
        public void Dispose() { }
    }

    private sealed class RecordProcess(byte[] data) : IRunningProcess
    {
        private readonly MemoryStream _out = new(data);
        public Stream StandardInput => Stream.Null;
        public Stream StandardOutput => _out;
        public string StandardError => "Stream ended";
        public int ExitCode => 0;
        public bool WaitForExit(TimeSpan timeout) => true;
        public void Kill() { }
        public void Dispose() { }
    }
}
