using System.Runtime.InteropServices;
using VibeSwitcher.Helpers;
using VibeSwitcher.NativeMethods;

namespace VibeSwitcher.Services;

// Receives IMMNotificationClient callbacks from Windows Audio (MTA thread).
// Debounces rapid bursts (plug-in fires several events in milliseconds) into one DevicesChanged event.
[ClassInterface(ClassInterfaceType.None)]
internal sealed class DeviceNotificationClient : IMMNotificationClient, IDisposable
{
    private readonly object _lock = new();
    private readonly TimeSpan _debounceInterval;
    private CancellationTokenSource? _debounce;
    // Per-device debounce so simultaneous property changes on different devices aren't collapsed
    // into one event (which would drop an earlier device's power-on trigger).
    private readonly Dictionary<string, CancellationTokenSource> _propDebounce = new();
    private bool _disposed;

    public event Action? DevicesChanged;
    public event Action<string>? DevicePropertyChanged;

    public DeviceNotificationClient(TimeSpan? debounceInterval = null)
    {
        _debounceInterval = debounceInterval ?? TimeSpan.FromMilliseconds(500);
    }

    public void OnDeviceStateChanged(string deviceId, AudioDeviceState newState) => Schedule();
    public void OnDeviceAdded(string deviceId) => Schedule();
    public void OnDeviceRemoved(string deviceId) => Schedule();
    public void OnDefaultDeviceChanged(EDataFlow flow, ERole role, string? defaultDeviceId) { }

    public void OnPropertyValueChanged(string deviceId, PROPERTYKEY key)
    {
        CancellationTokenSource cts;
        lock (_lock)
        {
            if (_disposed) return;
            if (_propDebounce.TryGetValue(deviceId, out var existing))
            {
                existing.Cancel();
                existing.Dispose();
            }
            _propDebounce[deviceId] = cts = new CancellationTokenSource();
        }
        _ = Task.Delay(_debounceInterval, cts.Token).ContinueWith(t =>
        {
            lock (_lock)
            {
                // Only clear our own entry — a newer change may have replaced it.
                if (_propDebounce.TryGetValue(deviceId, out var cur) && ReferenceEquals(cur, cts))
                    _propDebounce.Remove(deviceId);
            }
            cts.Dispose();
            if (t.IsCanceled) return;
            try { DevicePropertyChanged?.Invoke(deviceId); }
            catch (Exception ex) { AppLog.Warning("DeviceNotificationClient", ex.Message); }
        }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    private void Schedule()
    {
        CancellationTokenSource cts;
        lock (_lock)
        {
            if (_disposed) return;
            _debounce?.Cancel();
            _debounce?.Dispose();
            _debounce = cts = new CancellationTokenSource();
        }
        _ = Task.Delay(_debounceInterval, cts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            try { DevicesChanged?.Invoke(); }
            catch (Exception ex) { AppLog.Warning("DeviceNotificationClient", ex.Message); }
        }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _debounce?.Cancel();
            _debounce?.Dispose();
            _debounce = null;
            foreach (var c in _propDebounce.Values) { c.Cancel(); c.Dispose(); }
            _propDebounce.Clear();
        }
    }
}
