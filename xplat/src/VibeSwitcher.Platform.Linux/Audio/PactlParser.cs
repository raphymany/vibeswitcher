using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VibeSwitcher.Platform.Linux.Audio;

// Parses `pactl` output. pactl 15.99+ can print JSON (`-f json`); older versions (e.g. Debian 11's 14.2)
// only print text, and some releases have printed broken JSON, so both are supported. Callers always
// run pactl with LC_ALL=C, so the text labels are the English ones matched here.
internal static partial class PactlParser
{
    // ── JSON (pactl 16+) ────────────────────────────────────────────────────────────────────────

    internal static IReadOnlyList<PulseDevice> ParseJsonDevices(string json, bool sinks)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            throw new FormatException("pactl JSON device list is not an array.");

        var list = new List<PulseDevice>();
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            var name = Str(e, "name");
            if (string.IsNullOrEmpty(name)) continue;

            var props = new Dictionary<string, string>(StringComparer.Ordinal);
            if (e.TryGetProperty("properties", out var pe) && pe.ValueKind == JsonValueKind.Object)
                foreach (var p in pe.EnumerateObject())
                    props[p.Name] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString()! : p.Value.GetRawText();

            var ports = new List<PulsePort>();
            if (e.TryGetProperty("ports", out var pa) && pa.ValueKind == JsonValueKind.Array)
                foreach (var p in pa.EnumerateArray())
                {
                    var pn = Str(p, "name");
                    if (string.IsNullOrEmpty(pn)) continue;
                    ports.Add(new PulsePort(pn, Str(p, "description") ?? pn, Availability(Str(p, "availability"))));
                }

            // pactl 16 writes a source's "monitor of sink" under the key "monitor_source" (a pactl
            // quirk); newer versions may use "monitor_of_sink". For sinks, "monitor_source" is the
            // sink's own monitor and is irrelevant here.
            string? monitorOf = null;
            if (!sinks) monitorOf = NullIfNa(Str(e, "monitor_of_sink") ?? Str(e, "monitor_source"));

            list.Add(new PulseDevice(
                Index: e.TryGetProperty("index", out var ix) && ix.TryGetUInt32(out var u) ? u : 0,
                Name: name,
                Description: Str(e, "description") ?? name,
                IsSink: sinks,
                Mute: e.TryGetProperty("mute", out var m) && m.ValueKind == JsonValueKind.True,
                MonitorOfSink: monitorOf,
                Ports: ports,
                ActivePort: NullIfNa(Str(e, "active_port")),
                Properties: props));
        }
        return list;
    }

    private static string? Str(JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    // ── Text (every pactl version) ──────────────────────────────────────────────────────────────

    internal static IReadOnlyList<PulseDevice> ParseTextDevices(string text, bool sinks)
    {
        string header = sinks ? "Sink #" : "Source #";
        var list = new List<PulseDevice>();
        Builder? cur = null;
        string section = "";

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith(header, StringComparison.Ordinal))
            {
                if (cur?.Build(sinks) is { } done) list.Add(done);
                cur = new Builder { Index = uint.TryParse(line[header.Length..].Trim(), out var i) ? i : 0 };
                section = "";
                continue;
            }
            if (cur == null || line.Length == 0) continue;

            int depth = 0;
            while (depth < line.Length && line[depth] == '\t') depth++;
            var body = line[depth..];

            if (depth == 1)
            {
                int colon = body.IndexOf(':');
                if (colon < 0) { section = ""; continue; }
                var key = body[..colon];
                var value = body[(colon + 1)..].Trim();
                section = value.Length == 0 ? key : "";
                switch (key)
                {
                    case "Name": cur.Name = value; break;
                    case "Description": cur.Description = value; break;
                    case "Mute": cur.Mute = value == "yes"; break;
                    case "Monitor of Sink": cur.MonitorOfSink = NullIfNa(value); break;
                    case "Active Port": cur.ActivePort = NullIfNa(value); break;
                }
            }
            else if (depth == 2 && section == "Properties")
            {
                var m = PropLine().Match(body);
                if (m.Success) cur.Props[m.Groups["k"].Value] = Unescape(m.Groups["v"].Value);
            }
            else if (depth == 2 && section == "Ports")
            {
                var m = PortLine().Match(body);
                if (m.Success)
                    cur.Ports.Add(new PulsePort(m.Groups["name"].Value, m.Groups["desc"].Value,
                        Availability(m.Groups["attrs"].Value.Split(',').Last().Trim())));
            }
            // Deeper lines (per-port properties in newer pactl) carry nothing needed here.
        }
        if (cur?.Build(sinks) is { } last) list.Add(last);
        return list;
    }

    internal static PulseServerInfo ParseInfo(string text)
    {
        string? sink = null, source = null, server = null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            int colon = line.IndexOf(':');
            if (colon < 0) continue;
            var value = line[(colon + 1)..].Trim();
            switch (line[..colon])
            {
                case "Default Sink": sink = NullIfNa(value); break;
                case "Default Source": source = NullIfNa(value); break;
                case "Server Name": server = value; break;
            }
        }
        return new PulseServerInfo(sink, source, server);
    }

    // `pactl subscribe` line → the facility it's about ("sink", "source", "card", "server", …) and
    // the event type ("new", "change", "remove"); null for anything else.
    internal static (string Type, string Facility)? ParseSubscribeEvent(string line)
    {
        var m = EventLine().Match(line.Trim());
        return m.Success ? (m.Groups["type"].Value, m.Groups["fac"].Value) : null;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────

    private static PortAvailability Availability(string? s) => s?.Trim().ToLowerInvariant() switch
    {
        "available" or "yes" => PortAvailability.Available,
        "not available" or "no" => PortAvailability.NotAvailable,
        _ => PortAvailability.Unknown,
    };

    private static string? NullIfNa(string? s) =>
        string.IsNullOrWhiteSpace(s) || s is "n/a" or "(null)" ? null : s;

    // pa_proplist_to_string escapes backslashes and double quotes inside values.
    private static string Unescape(string s)
    {
        if (s.IndexOf('\\') < 0) return s;
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length) i++;
            sb.Append(s[i]);
        }
        return sb.ToString();
    }

    [GeneratedRegex("^(?<k>[^=\\s]+) = \"(?<v>.*)\"$")]
    private static partial Regex PropLine();

    // "analog-output-speaker: Speakers (type: Speaker, priority: 10000, availability group: …, not available)"
    // UCM sound cards (most recent laptops) use names with spaces: "[Out] Speaker: Speaker (…)".
    // The attribute list is the last parenthesised group; the description itself may contain parentheses.
    [GeneratedRegex("^(?<name>.+?): (?<desc>.*) \\((?<attrs>[^()]*)\\)$")]
    private static partial Regex PortLine();

    [GeneratedRegex("^Event '(?<type>[a-z]+)' on (?<fac>[a-z-]+) #\\d+$")]
    private static partial Regex EventLine();

    private sealed class Builder
    {
        public uint Index;
        public string? Name, Description, MonitorOfSink, ActivePort;
        public bool Mute;
        public readonly Dictionary<string, string> Props = new(StringComparer.Ordinal);
        public readonly List<PulsePort> Ports = new();

        public PulseDevice? Build(bool sinks) => string.IsNullOrEmpty(Name) ? null : new PulseDevice(
            Index, Name, string.IsNullOrEmpty(Description) ? Name : Description, sinks, Mute,
            sinks ? null : MonitorOfSink, Ports, ActivePort, Props);
    }
}
