using System.IO;
using System.Text.Json.Nodes;

namespace VibeSwitcher.App.Hosting;

// Choices only the macOS / Linux app has, kept in xplat.json beside config.json (which stays exactly
// the Windows app's, so the two can share a settings export). Missing or unreadable: the defaults.
internal sealed record XPlatSettings(bool GnomeShortcutsAllowed = false)
{
    public const string FileName = "xplat.json";

    public static XPlatSettings Load(string configDir)
    {
        try
        {
            var path = Path.Combine(configDir, FileName);
            if (!File.Exists(path)) return new();
            var json = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            return new(GnomeShortcutsAllowed: json?["gnomeShortcutsAllowed"]?.GetValueKind() == System.Text.Json.JsonValueKind.True);
        }
        catch (Exception) { return new(); }
    }

    // Written to a new file that then replaces the old one (never half-written).
    public void Save(string configDir)
    {
        Directory.CreateDirectory(configDir);
        var path = Path.Combine(configDir, FileName);
        var json = new JsonObject { ["gnomeShortcutsAllowed"] = GnomeShortcutsAllowed };
        var temp = path + ".tmp";
        File.WriteAllText(temp, json.ToJsonString(new() { WriteIndented = true }));
        File.Move(temp, path, overwrite: true);
    }
}
