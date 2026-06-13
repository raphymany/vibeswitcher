using HidSharp;
using VibeSwitcher.Helpers;

namespace VibeSwitcher.Services;

/// <summary>
/// Monitors known wireless headsets via their HID vendor interface.
/// Fires WirelessConnected / WirelessDisconnected when the headset powers on or off.
/// Uses shared (non-exclusive) device access — works alongside G HUB and similar software.
///
/// Logitech and Corsair: event-driven (blocking read loop, unsolicited HID reports).
/// SteelSeries and HyperX: poll-based (write a status command, read the response).
/// All non-Logitech protocols are implemented from open-source reverse-engineering
/// and have not been tested on real hardware — they may not work on every model.
/// </summary>
public sealed class HidHeadsetService : IDisposable
{
    public event Action<HidHeadsetDescriptor>? WirelessConnected;
    public event Action<HidHeadsetDescriptor>? WirelessDisconnected;
    public event Action<HidHeadsetDescriptor>? DeviceMonitoringStarted;

    private readonly List<DeviceReader> _readers = [];
    private readonly IAppLogger _logger;

    public HidHeadsetService(IAppLogger logger)
    {
        _logger = logger;
    }

    public void Start()
    {
        foreach (var descriptor in KnownHidHeadsets.All)
            TryOpenDevice(descriptor);
    }

    private void TryOpenDevice(HidHeadsetDescriptor descriptor)
    {
        var candidates = DeviceList.Local
            .GetHidDevices(vendorID: descriptor.VendorId, productID: descriptor.ProductId)
            .ToList();

        if (candidates.Count == 0)
        {
            _logger.Info("HidHeadsetService",
                $"{descriptor.ModelName}: no HID devices found for " +
                $"VID={descriptor.VendorId:X4} PID={descriptor.ProductId:X4}.");
            return;
        }

        foreach (var c in candidates)
        {
            _logger.Info("HidHeadsetService",
                $"{descriptor.ModelName} candidate: {c.DevicePath} usages=[{GetUsageSummary(c)}]");
        }

        var hidDevice = SelectInterface(candidates, descriptor);

        if (hidDevice == null)
        {
            _logger.Info("HidHeadsetService",
                $"{descriptor.ModelName}: no matching HID interface found. " +
                $"Check the log for 'candidate' lines to identify the correct path.");
            return;
        }

        try
        {
            var reader = new DeviceReader(hidDevice, descriptor,
                d => WirelessConnected?.Invoke(d),
                d => WirelessDisconnected?.Invoke(d),
                _logger);
            _readers.Add(reader);
            reader.Start();
            DeviceMonitoringStarted?.Invoke(descriptor);

            _logger.Info("HidHeadsetService",
                $"Monitoring {descriptor.ModelName} ({descriptor.Protocol}) at {hidDevice.DevicePath}");
        }
        catch (Exception ex)
        {
            _logger.Warning("HidHeadsetService",
                $"Could not open {descriptor.ModelName}: {ex.Message}");
        }
    }

    // Picks the HID interface to open based on the descriptor's protocol and usage page.
    private static HidDevice? SelectInterface(IList<HidDevice> candidates, HidHeadsetDescriptor descriptor)
    {
        // Descriptor specifies an exact interface — use it.
        if (descriptor.UsagePage.HasValue)
            return candidates.FirstOrDefault(d => HasUsagePage(d, descriptor.UsagePage.Value, descriptor.UsageId));

        // Protocol-default heuristics.
        return descriptor.Protocol switch
        {
            HidProtocolType.LogitechHidPP =>
                candidates.FirstOrDefault(IsHidPpInterface)
                ?? candidates.FirstOrDefault(HasAnyVendorUsagePage),

            HidProtocolType.CorsairVoid =>
                candidates.FirstOrDefault(d => HasUsagePage(d, 0xFFC5, 0x0001)),

            _ => candidates.FirstOrDefault(HasAnyVendorUsagePage),
        };
    }

    private static string GetUsageSummary(HidDevice device)
    {
        try
        {
            return string.Join(", ", device.GetReportDescriptor().DeviceItems
                .SelectMany(item => item.Usages.GetAllValues())
                .Select(u => $"0x{u:X8}"));
        }
        catch (Exception ex) { return $"error:{ex.Message}"; }
    }

    // Logitech LIGHTSPEED vendor interface.
    private static bool IsHidPpInterface(HidDevice device)
    {
        try
        {
            return device.GetReportDescriptor().DeviceItems.Any(item =>
                item.Usages.GetAllValues().Any(u =>
                    (u >> 16) == KnownHidHeadsets.LogitechVendorUsagePage));
        }
        catch { return false; }
    }

    // Any vendor-defined usage page (0xFF00+).
    private static bool HasAnyVendorUsagePage(HidDevice device)
    {
        try
        {
            return device.GetReportDescriptor().DeviceItems.Any(item =>
                item.Usages.GetAllValues().Any(u => (u >> 16) >= 0xFF00));
        }
        catch { return false; }
    }

    private static bool HasUsagePage(HidDevice device, ushort usagePage, ushort? usageId = null)
    {
        try
        {
            return device.GetReportDescriptor().DeviceItems.Any(item =>
                item.Usages.GetAllValues().Any(u =>
                    (u >> 16) == usagePage && (usageId == null || (u & 0xFFFF) == usageId)));
        }
        catch { return false; }
    }

    public void Dispose()
    {
        foreach (var r in _readers)
            r.Dispose();
        _readers.Clear();
    }

    // ── DeviceReader ─────────────────────────────────────────────────────────

    private sealed class DeviceReader : IDisposable
    {
        private readonly HidDevice _device;
        private readonly HidHeadsetDescriptor _descriptor;
        private readonly Action<HidHeadsetDescriptor> _onConnected;
        private readonly Action<HidHeadsetDescriptor> _onDisconnected;
        private readonly CancellationTokenSource _cts = new();
        private readonly IAppLogger _logger;
        private HidStream? _stream;
        private Task? _loopTask;

        private bool? _lastConnected;

        public DeviceReader(
            HidDevice device,
            HidHeadsetDescriptor descriptor,
            Action<HidHeadsetDescriptor> onConnected,
            Action<HidHeadsetDescriptor> onDisconnected,
            IAppLogger logger)
        {
            _device         = device;
            _descriptor     = descriptor;
            _onConnected    = onConnected;
            _onDisconnected = onDisconnected;
            _logger         = logger;
        }

        public void Start()
        {
            var isEventDriven = _descriptor.Protocol is
                HidProtocolType.LogitechHidPP or
                HidProtocolType.CorsairVoid;

            _loopTask = Task.Run(isEventDriven ? ReadLoop : PollLoop);
        }

        // ── Event-driven loop (Logitech, Corsair) ─────────────────────────────

        private async Task ReadLoop()
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    var openConfig = new OpenConfiguration();
                    openConfig.SetOption(OpenOption.Exclusive, false);
                    _stream = _device.Open(openConfig);
                    _stream.ReadTimeout = Timeout.Infinite;

                    // Corsair requires one output report to seed the initial state;
                    // without it the device won't send unsolicited reports immediately.
                    if (_descriptor.Protocol == HidProtocolType.CorsairVoid)
                    {
                        try
                        {
                            var seed = new byte[] { 0xC9, 0x64 };
                            await _stream.WriteAsync(seed, 0, seed.Length, _cts.Token);
                        }
                        catch (Exception ex)
                        {
                            _logger.Warning("HidHeadsetService.ReadLoop",
                                $"{_descriptor.ModelName} seed query failed: {ex.Message}");
                        }
                    }

                    int reportLen = _device.GetMaxInputReportLength();
                    if (reportLen <= 0)
                    {
                        _logger.Warning("HidHeadsetService.ReadLoop",
                            $"{_descriptor.ModelName} reports a zero-length input report — not readable.");
                        return; // avoid a tight zero-byte read spin
                    }
                    var buffer = new byte[reportLen];

                    while (!_cts.Token.IsCancellationRequested)
                    {
                        int bytesRead;
                        try
                        {
                            bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length, _cts.Token);
                        }
                        catch (OperationCanceledException) { return; }
                        catch (Exception ex)
                        {
                            _logger.Warning("HidHeadsetService.ReadLoop",
                                $"{_descriptor.ModelName} read error: {ex.Message}");
                            break;
                        }

                        if (bytesRead > 0)
                            ParseReport(buffer, bytesRead);
                    }
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    _logger.Warning("HidHeadsetService.ReadLoop",
                        $"{_descriptor.ModelName} could not be opened: {ex.Message}");
                }
                finally
                {
                    _stream?.Close();
                    _stream = null;
                }

                try { await Task.Delay(2000, _cts.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }

        private void ParseReport(byte[] data, int length)
        {
            LogDebugReport(data, length);

            if (!TryParseEventReport(data, length, out var connected))
                return;

            if (_lastConnected == connected) return;
            _lastConnected = connected;

            if (connected) _onConnected(_descriptor);
            else           _onDisconnected(_descriptor);
        }

        private bool TryParseEventReport(byte[] data, int length, out bool connected)
        {
            connected = false;
            return _descriptor.Protocol switch
            {
                HidProtocolType.LogitechHidPP => TryParseLogitechHidPP(data, length, ref connected),
                HidProtocolType.CorsairVoid   => TryParseCorsairVoid(data, length, ref connected),
                _                             => false,
            };
        }

        // HID++ 1.0 short (7 bytes), Sub-ID 0x41 "Device Change":
        //   [10] [dev_idx] [41] [status] [00] [00] [00]
        //   status 0x04 = link established, 0x03 = link lost
        //
        // HID++ 2.0 long (20 bytes), LIGHTSPEED receiver broadcast (device index 0xFF):
        //   [11] [FF] [06] [00] [status] ...
        //   byte[4] = 0x00 → powered off; non-zero → powered on
        private static bool TryParseLogitechHidPP(byte[] data, int length, ref bool connected)
        {
            if (length >= 4 && data[0] == 0x10 && data[2] == 0x41)
            {
                connected = data[3] == 0x04;
                return data[3] is 0x03 or 0x04;
            }

            if (length >= 5 && data[0] == 0x11 && data[1] == 0xFF &&
                data[2] == 0x06 && data[3] == 0x00)
            {
                connected = data[4] != 0x00;
                return true;
            }

            return false;
        }

        // Corsair VOID/Pro/Elite: Report ID 0x64, 5-byte report.
        //   data[3] == 177 (0xB1) = link established; data[4] != 0 = active battery.
        private static bool TryParseCorsairVoid(byte[] data, int length, ref bool connected)
        {
            if (length < 5 || data[0] != 0x64) return false;
            connected = data[3] == 177 && data[4] != 0;
            return true;
        }

        // ── Poll-based loop (SteelSeries, HyperX) ────────────────────────────

        private async Task PollLoop()
        {
            try
            {
                var openConfig = new OpenConfiguration();
                openConfig.SetOption(OpenOption.Exclusive, false);
                _stream = _device.Open(openConfig);
                _stream.ReadTimeout = _descriptor.ReadTimeoutMs;

                var responseBuffer = new byte[Math.Max(256, _device.GetMaxInputReportLength())];

                while (!_cts.Token.IsCancellationRequested)
                {
                    Array.Clear(responseBuffer, 0, responseBuffer.Length);
                    bool? connected = null;
                    try
                    {
                        connected = await QueryConnectionStateAsync(responseBuffer);
                    }
                    catch (OperationCanceledException) { break; }
                    catch (TimeoutException)
                    {
                        // Headset is off or out of range — normal during polling, not an error.
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning("HidHeadsetService.PollLoop",
                            $"{_descriptor.ModelName} query error: {ex.Message}");
                    }

                    if (connected.HasValue)
                    {
                        LogDebugReport(responseBuffer, Math.Min(20, responseBuffer.Length));
                        if (_lastConnected != connected.Value)
                        {
                            _lastConnected = connected.Value;
                            if (connected.Value) _onConnected(_descriptor);
                            else                 _onDisconnected(_descriptor);
                        }
                    }

                    try
                    {
                        await Task.Delay(_descriptor.PollIntervalMs, _cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { break; }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning("HidHeadsetService.PollLoop",
                    $"{_descriptor.ModelName} could not be opened: {ex.Message}");
            }
            finally
            {
                // Close the stream when the poll loop exits (cancellation or error), mirroring ReadLoop.
                _stream?.Close();
                _stream = null;
            }
        }

        private Task<bool?> QueryConnectionStateAsync(byte[] responseBuffer) =>
            _descriptor.Protocol switch
            {
                HidProtocolType.SteelSeriesLegacy => QuerySteelSeriesLegacyAsync(responseBuffer),
                HidProtocolType.SteelSeriesNova   => QuerySteelSeriesNovaAsync(responseBuffer),
                HidProtocolType.HyperXAlpha       => QueryHyperXAlphaAsync(responseBuffer),
                HidProtocolType.HyperXCloudII     => QueryHyperXCloudIIAsync(responseBuffer),
                _                                 => Task.FromResult<bool?>(null),
            };

        // SteelSeries Arctis 1 / 7X / 7P: 31-byte query, 8-byte response.
        // response[2] == 0x01 → offline; any other value → online.
        private async Task<bool?> QuerySteelSeriesLegacyAsync(byte[] buf)
        {
            var prefix = _descriptor.LegacyQueryPrefix ?? [0x06, 0x12];
            var query  = new byte[31];
            Array.Copy(prefix, query, Math.Min(prefix.Length, query.Length));

            await _stream!.WriteAsync(query, 0, query.Length, _cts.Token);
            int read = await _stream.ReadAsync(buf, 0, buf.Length, _cts.Token);
            if (read < 3) return null;
            return buf[2] != 0x01;
        }

        // SteelSeries Arctis Nova 7 / 7X / 7P / 7+ / Nova 5 family: 64-byte query.
        // response[3] == 0x00 → offline; 0x01/0x02 = charging (still on); other = online.
        private async Task<bool?> QuerySteelSeriesNovaAsync(byte[] buf)
        {
            var query = new byte[64];
            query[0] = 0x00;
            query[1] = 0xB0;

            await _stream!.WriteAsync(query, 0, query.Length, _cts.Token);
            int read = await _stream.ReadAsync(buf, 0, buf.Length, _cts.Token);
            if (read < 4) return null;
            return buf[3] != 0x00;
        }

        // HyperX Cloud Alpha Wireless: 31-byte query, 31-byte response.
        // response[3] == 0x01 → disconnected; 0x00 → connected.
        private async Task<bool?> QueryHyperXAlphaAsync(byte[] buf)
        {
            var query = new byte[31];
            query[0] = 0x21;
            query[1] = 0xBB;
            query[2] = 0x03;

            await _stream!.WriteAsync(query, 0, query.Length, _cts.Token);
            int read = await _stream.ReadAsync(buf, 0, buf.Length, _cts.Token);
            if (read < 4) return null;
            return buf[3] != 0x01;
        }

        // HyperX Cloud II Wireless (HP): 52-byte wrapped command, 20-byte response.
        // A valid response header (response[0]==0x06, [1]==0xFF, [2]==0xBB) = connected.
        // Timeout or malformed response = state unknown (no event fired).
        private async Task<bool?> QueryHyperXCloudIIAsync(byte[] buf)
        {
            var query = new byte[52];
            query[0] = 0x06;
            query[1] = 0xFF;
            query[2] = 0xBB;
            query[3] = 0x02;

            await _stream!.WriteAsync(query, 0, query.Length, _cts.Token);
            await Task.Delay(100, _cts.Token).ConfigureAwait(false);

            int read = await _stream.ReadAsync(buf, 0, buf.Length, _cts.Token);
            if (read < 3) return null;
            return buf[0] == 0x06 && buf[1] == 0xFF && buf[2] == 0xBB;
        }

        private void LogDebugReport(byte[] data, int length)
        {
            try
            {
                var hex = string.Join(" ", data.Take(length).Select(b => b.ToString("X2")));
                _logger.Debug("HidHeadsetService.Report",
                    $"{_descriptor.ModelName} [{length}]: {hex}");
            }
            catch { /* logging must never throw */ }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _stream?.Close();
            // Wait (briefly) for the read/poll loop to observe cancellation before disposing the
            // CTS, so a still-running iteration can't touch a disposed token. The loop runs on a
            // pool thread with no captured context, so this Wait can't deadlock.
            try { _loopTask?.Wait(TimeSpan.FromSeconds(1)); }
            catch { /* loop exceptions are already logged */ }
            _cts.Dispose();
        }
    }
}
