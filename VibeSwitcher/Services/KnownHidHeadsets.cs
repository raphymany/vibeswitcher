namespace VibeSwitcher.Services;

public enum HidProtocolType
{
    // Event-driven: blocking ReadAsync loop; fires HID++ 1.0/2.0 wireless-status packets.
    LogitechHidPP,
    // Event-driven: blocking ReadAsync loop; fires Report ID 0x64 on power state change.
    CorsairVoid,
    // Poll-based: write 31-byte command, read 8-byte response; response[2] == 0x01 = offline.
    SteelSeriesLegacy,
    // Poll-based: write 64-byte command, read response; response[3] == 0x00 = offline.
    SteelSeriesNova,
    // Poll-based: 3-step 31-byte query; step-1 response[3] == 0x01 = disconnected.
    HyperXAlpha,
    // Poll-based: 52-byte wrapped command, 20-byte response; valid header = connected.
    HyperXCloudII,
}

/// <summary>
/// Describes a wireless headset supported by HID-based power-on/off detection.
/// </summary>
public sealed record HidHeadsetDescriptor(
    ushort VendorId,
    ushort ProductId,
    string ModelName,
    HidProtocolType Protocol = HidProtocolType.LogitechHidPP)
{
    // HID interface selection. Null = use the protocol's default heuristic.
    public ushort? UsagePage { get; init; }
    public ushort? UsageId  { get; init; }
    // Poll-based only.
    public int PollIntervalMs   { get; init; } = 10_000;
    public int ReadTimeoutMs    { get; init; } = 2_000;
    // SteelSeries Legacy only: first 2 bytes of the 31-byte status query.
    public byte[]? LegacyQueryPrefix { get; init; }
}

public static class KnownHidHeadsets
{
    // Note: SteelSeries Legacy also uses 0xFF43 (vendor pages are not globally unique).
    // VID+PID filtering in TryOpenDevice ensures the two brands never collide at runtime.
    internal const int LogitechVendorUsagePage = 0xFF43;

    public static readonly IReadOnlyList<HidHeadsetDescriptor> All =
    [
        // ── Logitech (HID++ 2.0, event-driven) ───────────────────────────────
        // All use VID 0x046D and the 0xFF43 vendor interface.
        // PIDs sourced from HeadsetControl (logitech_g633_g933_935.hpp,
        // logitech_g535.hpp, logitech_gpro.hpp).
        // Note: usage page 0xFF43 is shared with Logitech mice/keyboards — matching
        // by PID is required to avoid opening the wrong device.
        new(0x046D, 0x0A5C, "Logitech G633"),
        new(0x046D, 0x0A89, "Logitech G635"),
        new(0x046D, 0x0A5B, "Logitech G933"),
        new(0x046D, 0x0A87, "Logitech G935"),
        new(0x046D, 0x0AB5, "Logitech G733"),
        new(0x046D, 0x0AFE, "Logitech G733"),
        new(0x046D, 0x0B1F, "Logitech G733"),
        new(0x046D, 0x0AC4, "Logitech G535"),
        new(0x046D, 0x0AA7, "Logitech G Pro"),
        new(0x046D, 0x0AAA, "Logitech G Pro X Wireless"),
        new(0x046D, 0x0ABA, "Logitech PRO X Wireless"),
        new(0x046D, 0x0AFB, "Logitech G Pro X 2"),
        new(0x046D, 0x0AFC, "Logitech G Pro X 2"),

        // ── Corsair VOID / Elite Wireless (event-driven, Report ID 0x64) ─────
        // VID 0x1B1C. Interface: usage page 0xFFC5, usage ID 0x0001.
        // On open: send [0xC9, 0x64] to seed initial state. Then read in a loop.
        // Packet: data[3] == 177 (0xB1) AND data[4] != 0 → connected.
        // PIDs sourced from stuarthayhurst/corsair-void-driver (Linux kernel driver)
        // and HeadsetControl (corsair_void_rich.hpp).
        // UNTESTED — protocol sourced from open-source documentation only.
        new(0x1B1C, 0x0A0C, "Corsair Void Wireless",       HidProtocolType.CorsairVoid),
        new(0x1B1C, 0x1B27, "Corsair Void Wireless",       HidProtocolType.CorsairVoid),
        new(0x1B1C, 0x1B23, "Corsair Void Wireless",       HidProtocolType.CorsairVoid),
        new(0x1B1C, 0x0A2B, "Corsair Void Wireless",       HidProtocolType.CorsairVoid),
        new(0x1B1C, 0x0A14, "Corsair Void Pro Wireless",   HidProtocolType.CorsairVoid),
        new(0x1B1C, 0x0A16, "Corsair Void Pro Wireless",   HidProtocolType.CorsairVoid),
        new(0x1B1C, 0x0A1A, "Corsair Void Pro Wireless",   HidProtocolType.CorsairVoid),
        new(0x1B1C, 0x0A51, "Corsair Void Elite Wireless", HidProtocolType.CorsairVoid),
        new(0x1B1C, 0x0A55, "Corsair Void Elite Wireless", HidProtocolType.CorsairVoid),
        new(0x1B1C, 0x0A75, "Corsair Void Elite Wireless", HidProtocolType.CorsairVoid),
        new(0x1B1C, 0x0A38, "Corsair HS Wireless",         HidProtocolType.CorsairVoid),
        new(0x1B1C, 0x0A4F, "Corsair HS Wireless",         HidProtocolType.CorsairVoid),

        // ── SteelSeries Legacy (poll-based, 31-byte, [0x06,0x12] command) ────
        // VID 0x1038. Interface: usage page 0xFF43, usage ID 0x0202.
        // Query: LegacyQueryPrefix padded to 31 bytes. Response: 8 bytes.
        // response[2] == 0x01 → offline; any other value → online.
        // PIDs sourced from HeadsetControl (steelseries_arctis_1.hpp).
        // UNTESTED — protocol sourced from open-source documentation only.
        new(0x1038, 0x12B3, "SteelSeries Arctis 1 Wireless",
            HidProtocolType.SteelSeriesLegacy)
            { UsagePage = 0xFF43, UsageId = 0x0202, LegacyQueryPrefix = [0x06, 0x12] },
        new(0x1038, 0x12B6, "SteelSeries Arctis 1 Wireless Xbox",
            HidProtocolType.SteelSeriesLegacy)
            { UsagePage = 0xFF43, UsageId = 0x0202, LegacyQueryPrefix = [0x06, 0x12] },
        new(0x1038, 0x12D7, "SteelSeries Arctis 7X Wireless",
            HidProtocolType.SteelSeriesLegacy)
            { UsagePage = 0xFF43, UsageId = 0x0202, LegacyQueryPrefix = [0x06, 0x12] },
        new(0x1038, 0x12D5, "SteelSeries Arctis 7P Wireless",
            HidProtocolType.SteelSeriesLegacy)
            { UsagePage = 0xFF43, UsageId = 0x0202, LegacyQueryPrefix = [0x06, 0x12] },

        // ── SteelSeries Nova (poll-based, 64-byte, [0x00,0xB0] command) ──────
        // VID 0x1038. Interface: usage page 0xFFC0, usage ID 0x0001.
        // Query: [0x00, 0xB0] padded to 64 bytes. Response: up to 128 bytes.
        // response[3] == 0x00 → offline; 0x01/0x02 = charging (on); other = online.
        // PIDs sourced from HeadsetControl (steelseries_arctis_nova_7.hpp, etc.).
        // UNTESTED — protocol sourced from open-source documentation only.
        new(0x1038, 0x2202, "SteelSeries Arctis Nova 7",
            HidProtocolType.SteelSeriesNova) { UsagePage = 0xFFC0, UsageId = 0x0001 },
        new(0x1038, 0x22A1, "SteelSeries Arctis Nova 7",
            HidProtocolType.SteelSeriesNova) { UsagePage = 0xFFC0, UsageId = 0x0001 },
        new(0x1038, 0x227E, "SteelSeries Arctis Nova 7",
            HidProtocolType.SteelSeriesNova) { UsagePage = 0xFFC0, UsageId = 0x0001 },
        new(0x1038, 0x2206, "SteelSeries Arctis Nova 7X",
            HidProtocolType.SteelSeriesNova) { UsagePage = 0xFFC0, UsageId = 0x0001 },
        new(0x1038, 0x2258, "SteelSeries Arctis Nova 7X",
            HidProtocolType.SteelSeriesNova) { UsagePage = 0xFFC0, UsageId = 0x0001 },
        new(0x1038, 0x229E, "SteelSeries Arctis Nova 7X",
            HidProtocolType.SteelSeriesNova) { UsagePage = 0xFFC0, UsageId = 0x0001 },
        new(0x1038, 0x22AD, "SteelSeries Arctis Nova 7X",
            HidProtocolType.SteelSeriesNova) { UsagePage = 0xFFC0, UsageId = 0x0001 },
        new(0x1038, 0x220A, "SteelSeries Arctis Nova 7P",
            HidProtocolType.SteelSeriesNova) { UsagePage = 0xFFC0, UsageId = 0x0001 },
        new(0x1038, 0x22A7, "SteelSeries Arctis Nova 7P",
            HidProtocolType.SteelSeriesNova) { UsagePage = 0xFFC0, UsageId = 0x0001 },
        new(0x1038, 0x223A, "SteelSeries Arctis Nova 7 Diablo IV",
            HidProtocolType.SteelSeriesNova) { UsagePage = 0xFFC0, UsageId = 0x0001 },
        new(0x1038, 0x22A9, "SteelSeries Arctis Nova 7 Diablo IV",
            HidProtocolType.SteelSeriesNova) { UsagePage = 0xFFC0, UsageId = 0x0001 },
        new(0x1038, 0x227A, "SteelSeries Arctis Nova 7 WoW",
            HidProtocolType.SteelSeriesNova) { UsagePage = 0xFFC0, UsageId = 0x0001 },
        new(0x1038, 0x22A4, "SteelSeries Arctis Nova 7X",
            HidProtocolType.SteelSeriesNova) { UsagePage = 0xFFC0, UsageId = 0x0001 },
        new(0x1038, 0x22A5, "SteelSeries Arctis Nova 7X",
            HidProtocolType.SteelSeriesNova) { UsagePage = 0xFFC0, UsageId = 0x0001 },
        new(0x1038, 0x220E, "SteelSeries Arctis 7+",
            HidProtocolType.SteelSeriesNova) { UsagePage = 0xFFC0, UsageId = 0x0001 },
        new(0x1038, 0x2212, "SteelSeries Arctis 7+ PS5",
            HidProtocolType.SteelSeriesNova) { UsagePage = 0xFFC0, UsageId = 0x0001 },
        new(0x1038, 0x2216, "SteelSeries Arctis 7+ Xbox",
            HidProtocolType.SteelSeriesNova) { UsagePage = 0xFFC0, UsageId = 0x0001 },
        new(0x1038, 0x2236, "SteelSeries Arctis 7+ Destiny",
            HidProtocolType.SteelSeriesNova) { UsagePage = 0xFFC0, UsageId = 0x0001 },
        new(0x1038, 0x2232, "SteelSeries Arctis Nova 5",
            HidProtocolType.SteelSeriesNova) { UsagePage = 0xFFC0, UsageId = 0x0001 },
        new(0x1038, 0x2253, "SteelSeries Arctis Nova 5X",
            HidProtocolType.SteelSeriesNova) { UsagePage = 0xFFC0, UsageId = 0x0001 },
        new(0x1038, 0x2269, "SteelSeries Arctis Nova 3P Wireless",
            HidProtocolType.SteelSeriesNova) { UsagePage = 0xFFC0, UsageId = 0x0001 },
        new(0x1038, 0x226D, "SteelSeries Arctis Nova 3X Wireless",
            HidProtocolType.SteelSeriesNova) { UsagePage = 0xFFC0, UsageId = 0x0001 },

        // ── HyperX Cloud Alpha Wireless (poll-based, 3-step 31-byte query) ────
        // VID 0x03F0 (HP Inc.). No specific usage page — opens first vendor interface.
        // Query: [0x21, 0xBB, 0x03, 0x00...] (31 bytes), read 31 bytes.
        // response[3] == 0x01 → disconnected; 0x00 → connected.
        // PID sourced from HeadsetControl (hyperx_cloud_alpha_wireless.hpp).
        // UNTESTED — protocol sourced from open-source documentation only.
        new(0x03F0, 0x098D, "HyperX Cloud Alpha Wireless",
            HidProtocolType.HyperXAlpha) { PollIntervalMs = 30_000 },

        // ── HyperX Cloud II Wireless HP (poll-based, 52-byte wrapped command) ─
        // VID 0x03F0 (HP Inc.). Interface: usage page 0xFF90, usage ID 0x0303.
        // Query: [0x06, 0xFF, 0xBB, 0x02, 0x00...] (52 bytes), wait 100ms, read 20 bytes.
        // Valid response header (response[0]==0x06, [1]==0xFF, [2]==0xBB) → connected.
        // PID sourced from HeadsetControl (hyperx_cloud_2_wireless.hpp).
        // UNTESTED — protocol sourced from open-source documentation only.
        new(0x03F0, 0x0696, "HyperX Cloud II Wireless",
            HidProtocolType.HyperXCloudII)
            { UsagePage = 0xFF90, UsageId = 0x0303, PollIntervalMs = 10_000, ReadTimeoutMs = 1_000 },
    ];
}
