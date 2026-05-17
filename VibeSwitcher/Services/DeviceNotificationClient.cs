using System.Runtime.InteropServices;
using VibeSwitcher.NativeMethods;

namespace VibeSwitcher.Services;

// Receives IMMNotificationClient callbacks from Windows Audio (MTA thread).
// Debounces rapid bursts (plug-in fires several events in milliseconds) into one DevicesChanged event.
[ClassInterface(ClassInterfaceType.None)]
internal sealed class DeviceNotificationClient : IMMNotificationClient
{
    private readonly object _lock = new();
    private CancellationTokenSource? _debounce;

    public event Action? DevicesChanged;

    public void OnDeviceStateChanged(string deviceId, AudioDeviceState newState) => Schedule();
    public void OnDeviceAdded(string deviceId) => Schedule();
    public void OnDeviceRemoved(string deviceId) => Schedule();
    public void OnDefaultDeviceChanged(EDataFlow flow, ERole role, string? defaultDeviceId) { }
    public void OnPropertyValueChanged(string deviceId, PROPERTYKEY key) { }

    private void Schedule()
    {
        CancellationTokenSource cts;
        lock (_lock)
        {
            _debounce?.Cancel();
            _debounce?.Dispose();
            _debounce = cts = new CancellationTokenSource();
        }
        _ = Task.Delay(500, cts.Token).ContinueWith(
            t => { if (!t.IsCanceled) DevicesChanged?.Invoke(); },
            TaskContinuationOptions.ExecuteSynchronously);
    }
}
