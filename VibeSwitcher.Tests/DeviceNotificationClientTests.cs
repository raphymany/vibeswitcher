using VibeSwitcher.Services;

namespace VibeSwitcher.Tests;

public class DeviceNotificationClientTests
{
    private static DeviceNotificationClient Fast() =>
        new(TimeSpan.FromMilliseconds(50));

    [Fact]
    public async Task OnDeviceAdded_FiresDevicesChangedAfterDebounce()
    {
        var client = Fast();
        bool fired = false;
        client.DevicesChanged += () => fired = true;

        client.OnDeviceAdded("dev1");

        await Task.Delay(200);
        Assert.True(fired);
    }

    [Fact]
    public async Task OnDeviceRemoved_FiresDevicesChanged()
    {
        var client = Fast();
        bool fired = false;
        client.DevicesChanged += () => fired = true;

        client.OnDeviceRemoved("dev1");

        await Task.Delay(200);
        Assert.True(fired);
    }

    [Fact]
    public async Task OnDeviceStateChanged_FiresDevicesChanged()
    {
        var client = Fast();
        bool fired = false;
        client.DevicesChanged += () => fired = true;

        client.OnDeviceStateChanged("dev1", default);

        await Task.Delay(200);
        Assert.True(fired);
    }

    [Fact]
    public async Task RapidCalls_CoalesceToSingleFire()
    {
        var client = new DeviceNotificationClient(TimeSpan.FromMilliseconds(100));
        int count = 0;
        client.DevicesChanged += () => Interlocked.Increment(ref count);

        for (int i = 0; i < 5; i++)
            client.OnDeviceAdded($"dev{i}");

        await Task.Delay(400);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task SecondSchedule_CancelsPrior_OnlyOneFire()
    {
        var client = new DeviceNotificationClient(TimeSpan.FromMilliseconds(200));
        int count = 0;
        client.DevicesChanged += () => Interlocked.Increment(ref count);

        client.OnDeviceAdded("dev1");
        await Task.Delay(50); // well before 200ms debounce fires
        client.OnDeviceAdded("dev2"); // cancels first

        await Task.Delay(600); // wait long enough for second to fire
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task NoListeners_DoesNotThrow()
    {
        var client = Fast();
        // DevicesChanged has no subscribers
        var ex = await Record.ExceptionAsync(async () =>
        {
            client.OnDeviceAdded("dev1");
            await Task.Delay(200);
        });
        Assert.Null(ex);
    }

    [Fact]
    public void OnDefaultDeviceChanged_DoesNotTriggerEvent()
    {
        var client = Fast();
        bool fired = false;
        client.DevicesChanged += () => fired = true;

        client.OnDefaultDeviceChanged(default, default, null);

        // No delay needed — this method is intentionally a no-op
        Assert.False(fired);
    }
}
