namespace VibeSwitcher.Services;

/// <summary>
/// Describes a wireless headset that supports HID-based power-off detection.
/// To request support for a new model, open a GitHub issue with your device's
/// VID and PID (Device Manager → Human Interface Devices → right-click → Properties
/// → Details → Hardware IDs).
/// </summary>
public sealed record HidHeadsetDescriptor(
    ushort VendorId,
    ushort ProductId,
    string ModelName);

public static class KnownHidHeadsets
{
    // Logitech HID++ 2.0 uses usage page 0xFF43 for the vendor protocol interface.
    internal const int LogitechVendorUsagePage = 0xFF43;

    public static readonly IReadOnlyList<HidHeadsetDescriptor> All =
    [
        new(0x046D, 0x0ABA, "Logitech PRO X Wireless"),
    ];
}
