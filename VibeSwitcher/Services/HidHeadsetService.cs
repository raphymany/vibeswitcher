using HidSharp;
using VibeSwitcher.Helpers;

namespace VibeSwitcher.Services;

/// <summary>
/// Monitors known wireless headsets via their HID++ protocol interface.
/// Fires WirelessConnected / WirelessDisconnected when the headset powers on or off.
/// Uses shared (non-exclusive) device access — works alongside G HUB.
/// </summary>
public sealed class HidHeadsetService : IDisposable
{
    public event Action<HidHeadsetDescriptor>? WirelessConnected;
    public event Action<HidHeadsetDescriptor>? WirelessDisconnected;

    private readonly List<DeviceReader> _readers = [];

    public void Start()
    {
        foreach (var descriptor in KnownHidHeadsets.All)
            TryOpenDevice(descriptor);
    }

    private void TryOpenDevice(HidHeadsetDescriptor descriptor)
    {
        try
        {
            // Find the HID++ vendor interface (usage page 0xFF43) for this device.
            // The audio interface (MI_00) and HID++ interface (MI_03) share the same
            // VID/PID but report different usage pages via their HID descriptors.
            var hidDevice = DeviceList.Local
                .GetHidDevices(vendorID: descriptor.VendorId, productID: descriptor.ProductId)
                .Where(IsHidPpInterface)
                .FirstOrDefault();

            if (hidDevice == null)
            {
                AppLogger.Info("HidHeadsetService",
                    $"{descriptor.ModelName} HID++ interface not found — skipping.");
                return;
            }

            var reader = new DeviceReader(hidDevice, descriptor,
                d => WirelessConnected?.Invoke(d),
                d => WirelessDisconnected?.Invoke(d));
            _readers.Add(reader);
            reader.Start();

            AppLogger.Info("HidHeadsetService",
                $"Monitoring {descriptor.ModelName} at {hidDevice.DevicePath}");
        }
        catch (Exception ex)
        {
            AppLogger.Warning("HidHeadsetService",
                $"Could not open {descriptor.ModelName}: {ex.Message}");
        }
    }

    private static bool IsHidPpInterface(HidDevice device)
    {
        try
        {
            var descriptor = device.GetReportDescriptor();
            // DeviceItems contains one entry per top-level HID collection.
            // Each DeviceItem.Usages stores 32-bit values: (usagePage << 16) | usage.
            // We look for any collection whose usage page is the Logitech vendor page.
            return descriptor.DeviceItems.Any(item =>
                item.Usages.GetAllValues().Any(u =>
                    (u >> 16) == KnownHidHeadsets.LogitechVendorUsagePage));
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        foreach (var r in _readers)
            r.Dispose();
        _readers.Clear();
    }

    // ── inner reader ──────────────────────────────────────────────────────────

    private sealed class DeviceReader : IDisposable
    {
        private readonly HidDevice _device;
        private readonly HidHeadsetDescriptor _descriptor;
        private readonly Action<HidHeadsetDescriptor> _onConnected;
        private readonly Action<HidHeadsetDescriptor> _onDisconnected;
        private readonly CancellationTokenSource _cts = new();
        private HidStream? _stream;

        // Tracks the last known wireless state so we only fire on changes.
        private bool? _lastConnected;

        public DeviceReader(
            HidDevice device,
            HidHeadsetDescriptor descriptor,
            Action<HidHeadsetDescriptor> onConnected,
            Action<HidHeadsetDescriptor> onDisconnected)
        {
            _device       = device;
            _descriptor   = descriptor;
            _onConnected  = onConnected;
            _onDisconnected = onDisconnected;
        }

        public void Start() => Task.Run(ReadLoop);

        private async Task ReadLoop()
        {
            try
            {
                var openConfig = new OpenConfiguration();
                openConfig.SetOption(OpenOption.Exclusive, false);
                _stream = _device.Open(openConfig);
                _stream.ReadTimeout = Timeout.Infinite;

                var buffer = new byte[_device.GetMaxInputReportLength()];

                while (!_cts.Token.IsCancellationRequested)
                {
                    int bytesRead;
                    try
                    {
                        bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length, _cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warning("HidHeadsetService.ReadLoop",
                            $"{_descriptor.ModelName} read error: {ex.Message}");
                        // Short pause before attempting to reopen
                        await Task.Delay(2000, _cts.Token).ConfigureAwait(false);
                        break;
                    }

                    if (bytesRead > 0)
                        ParseReport(buffer, bytesRead);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warning("HidHeadsetService.ReadLoop",
                    $"{_descriptor.ModelName} could not be opened: {ex.Message}");
            }
        }

        private void ParseReport(byte[] data, int length)
        {
            LogDebugReport(data, length);

            if (!TryParseWirelessState(data, length, out var connected))
                return;

            if (_lastConnected == connected) return; // no change
            _lastConnected = connected;

            if (connected)
                _onConnected(_descriptor);
            else
                _onDisconnected(_descriptor);
        }

        // Parses Logitech HID++ wireless state change reports.
        //
        // HID++ 1.0 short (7 bytes), Sub-ID 0x41 "Device Change":
        //   [10] [dev_idx] [41] [status] [00] [00] [00]
        //   status 0x04 = link established, 0x03 = link lost
        //
        // HID++ 2.0 long (20 bytes), Wireless Device Status feature (0x1D4B):
        //   [11] [dev_idx] [feat_idx] [evt] [status] ...
        //   status byte 0x01 = connected, 0x00 = disconnected
        //
        // If neither pattern matches, returns false and the report is ignored.
        private static bool TryParseWirelessState(byte[] data, int length, out bool connected)
        {
            connected = false;

            // HID++ 1.0 short report — Sub-ID 0x41 (Device Change)
            if (length >= 4 && data[0] == 0x10 && data[2] == 0x41)
            {
                connected = data[3] == 0x04; // 0x04 = link established, 0x03 = link lost
                return data[3] is 0x03 or 0x04;
            }

            // HID++ 2.0 long report — device index 0x01, check byte 4 for status
            if (length >= 5 && data[0] == 0x11 && data[1] == 0x01)
            {
                // Byte 4: wireless state (0x01 = connected, 0x00 = disconnected)
                // Only treat it as a wireless state report if byte 4 is 0 or 1
                // and the rest of the state bytes look like a status notification.
                if (data[4] is 0x00 or 0x01 && data[3] == 0x00)
                {
                    connected = data[4] == 0x01;
                    return true;
                }
            }

            return false;
        }

        private void LogDebugReport(byte[] data, int length)
        {
            try
            {
                var hex = string.Join(" ", data.Take(length).Select(b => b.ToString("X2")));
                AppLogger.Info("HidHeadsetService.Report",
                    $"{_descriptor.ModelName} [{length}]: {hex}");
            }
            catch { /* logging must never throw */ }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _stream?.Close();
            _cts.Dispose();
        }
    }
}
