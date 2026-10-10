using VibeSwitcher.Helpers;
using VibeSwitcher.Models;

namespace VibeSwitcher.Services;

public readonly record struct DeviceReconcileResult(bool ConfigChanged, bool Rebound);

// Keeps profiles bound to their devices when Windows re-issues an endpoint ID.
//
// Serial-less USB hardware (SPDIF adapters, webcams, many DACs and headset dongles) gets a brand-new
// endpoint ID whenever Windows re-detects it — a different USB port, a hub reset, a driver update.
// The old ID simply vanishes, so a profile that only stored the ID looked "disconnected" until the
// user re-picked the device. Each profile slot therefore also stores a fingerprint (friendly name +
// hardware key). When the stored ID is gone and exactly one connected device matches the fingerprint,
// the slot is rebound to it.
//
// Deliberately narrow, because a wrong rebind silently routes audio to the wrong device:
//  - USB hardware only. Non-USB keys are shared by unrelated endpoints (every HDMI/DisplayPort
//    monitor on a graphics card has the same HDAUDIO key, often the same name) and those endpoints
//    keep their IDs anyway — a monitor that's off would otherwise get rebound to its twin.
//  - Ambiguous matches are left alone.
//  - A device another profile is already bound to is never taken (identical twin adapters).
public sealed class DeviceRebindService
{
    private readonly IAudioService _audioService;
    private readonly IConfigService _configService;
    private readonly IAppLogger _logger;

    // Raised (on the calling thread) after at least one profile was rebound to a new device ID.
    public event Action? DevicesRebound;

    public DeviceRebindService(IAudioService audioService, IConfigService configService, IAppLogger logger)
    {
        _audioService  = audioService;
        _configService = configService;
        _logger        = logger;
    }

    // UI thread only — mutates the shared DeviceProfile instances the Settings cards also edit.
    // Returns true if any profile was rebound.
    public bool Reconcile()
    {
        DeviceReconcileResult result;
        try
        {
            result = Reconcile(_configService.Current,
                _audioService.GetPlaybackDevices(), _audioService.GetRecordingDevices(), _logger);
        }
        catch (Exception ex)
        {
            // Enumeration can fail (audio service restarting); next device change retries.
            _logger.Warning("DeviceRebindService.Reconcile", $"Could not check profile devices: {ex.Message}");
            return false;
        }

        if (result.ConfigChanged) _configService.SaveDeferred();
        if (result.Rebound) DevicesRebound?.Invoke();
        return result.Rebound;
    }

    // allowRebind: false = only keep fingerprints current (used by the Settings window, whose cached
    // device list can briefly lag behind Windows — rebinding from it could undo a fresh rebind).
    internal static DeviceReconcileResult Reconcile(
        AppConfig config,
        IReadOnlyList<AudioDeviceInfo> playback,
        IReadOnlyList<AudioDeviceInfo> recording,
        IAppLogger logger,
        bool allowRebind = true)
    {
        // Snapshot taken before any rebind: devices some profile was already bound to. Several
        // profiles sharing one (re-issued) device still all follow it, because the new ID isn't in here.
        var boundPlayback  = BoundIds(config, p => p.PlaybackDeviceId);
        var boundRecording = BoundIds(config, p => p.RecordingDeviceId);

        // Pass 1 — decide every slot without mutating anything.
        var slots = new List<(DeviceProfile Profile, bool IsPlayback, string? OldId, SlotResult Result)>();
        foreach (var profile in config.Profiles)
        {
            slots.Add((profile, true, profile.PlaybackDeviceId, ReconcileSlot(profile.PlaybackDeviceId,
                profile.PlaybackDeviceName, profile.PlaybackDeviceHardwareKey, playback, boundPlayback, allowRebind)));
            slots.Add((profile, false, profile.RecordingDeviceId, ReconcileSlot(profile.RecordingDeviceId,
                profile.RecordingDeviceName, profile.RecordingDeviceHardwareKey, recording, boundRecording, allowRebind)));
        }

        // A new ID claimed by slots coming from *different* old IDs means two distinct devices (identical
        // twins, both gone) look like this one — ambiguous, so none of them move. (Playback and recording
        // IDs never collide, so grouping across both flows is safe.)
        var contested = slots
            .Where(s => s.Result.Changed && !IdEquals(s.Result.Id, s.OldId))
            .GroupBy(s => s.Result.Id!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(s => s.OldId).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Pass 2 — apply.
        bool changed = false;
        var moves = new List<(string OldId, string NewId)>();
        foreach (var (profile, isPlayback, oldId, r) in slots)
        {
            if (!r.Changed) continue;
            bool isRebind = !IdEquals(r.Id, oldId);
            if (isRebind && contested.Contains(r.Id!)) continue;

            changed = true;
            if (isRebind)
            {
                moves.Add((oldId!, r.Id!));
                LogRebound(profile, isPlayback ? "playback" : "recording", r.Name, logger);
            }
            if (isPlayback)
                (profile.PlaybackDeviceId, profile.PlaybackDeviceName, profile.PlaybackDeviceHardwareKey) = (r.Id, r.Name, r.Key);
            else
                (profile.RecordingDeviceId, profile.RecordingDeviceName, profile.RecordingDeviceHardwareKey) = (r.Id, r.Name, r.Key);
        }

        MoveAliases(config, moves);
        return new DeviceReconcileResult(changed, moves.Count > 0);
    }

    private static bool IdEquals(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static HashSet<string> BoundIds(AppConfig config, Func<DeviceProfile, string?> slot) =>
        new(config.Profiles.Select(slot).Where(id => !string.IsNullOrEmpty(id)).Select(id => id!),
            StringComparer.OrdinalIgnoreCase);

    private readonly record struct SlotResult(bool Changed, string? Id, string? Name, string? Key);

    private static SlotResult ReconcileSlot(
        string? id, string? name, string? key, IReadOnlyList<AudioDeviceInfo> devices,
        HashSet<string> boundIds, bool allowRebind)
    {
        var unchanged = new SlotResult(false, id, name, key);
        if (string.IsNullOrEmpty(id)) return unchanged;

        // Stored device still enumerated (active, disabled or jack-unplugged) — it's the same device,
        // never rebind. Just keep the fingerprint current (backfills older configs, follows Windows
        // renames). A soft-failed key read must not wipe a key we already know.
        // (A removed USB device is "not present", which the enumerator omits — that's the rebind path.)
        var present = devices.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
        if (present != null)
        {
            var newKey = present.HardwareKey ?? key;
            if (present.FriendlyName == name && newKey == key) return unchanged;
            return new SlotResult(true, id, present.FriendlyName, newKey);
        }

        if (!allowRebind || !IsRebindableKey(key) || string.IsNullOrEmpty(name)) return unchanged;

        var matches = devices
            .Where(d => d.IsConnected
                && !boundIds.Contains(d.Id)
                && string.Equals(d.FriendlyName, name, StringComparison.OrdinalIgnoreCase)
                && string.Equals(d.HardwareKey, key, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToList();

        if (matches.Count != 1) return unchanged; // not reconnected yet, or ambiguous
        var match = matches[0];
        return new SlotResult(true, match.Id, match.FriendlyName, match.HardwareKey);
    }

    private static bool IsRebindableKey(string? key) =>
        key != null && key.StartsWith(@"USB\", StringComparison.OrdinalIgnoreCase);

    // Carry the user's alias over so the device keeps its custom name in the dropdowns. Done once,
    // after every slot has moved, so a device shared by several profiles migrates cleanly.
    private static void MoveAliases(AppConfig config, List<(string OldId, string NewId)> moves)
    {
        var aliases = config.DeviceAliases;
        foreach (var (oldId, newId) in moves.Distinct())
        {
            if (!aliases.TryGetValue(oldId, out var alias)) continue;
            if (!aliases.ContainsKey(newId)) aliases[newId] = alias;

            // Drop the dead entry unless a slot that couldn't follow (no fingerprint) still points at it.
            bool stillReferenced = config.Profiles.Any(p =>
                string.Equals(p.PlaybackDeviceId, oldId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(p.RecordingDeviceId, oldId, StringComparison.OrdinalIgnoreCase));
            if (!stillReferenced) aliases.Remove(oldId);
        }
    }

    private static void LogRebound(DeviceProfile profile, string kind, string? deviceName, IAppLogger logger) =>
        logger.Info("DeviceRebindService",
            $"'{profile.Name}': {kind} device '{deviceName}' reappeared under a new Windows ID — rebound automatically.");
}
