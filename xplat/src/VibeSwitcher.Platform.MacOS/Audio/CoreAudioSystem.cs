using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using VibeSwitcher.Platform.MacOS.Interop;
using static VibeSwitcher.Platform.MacOS.Interop.CoreAudio;

namespace VibeSwitcher.Platform.MacOS.Audio;

// The real Core Audio (macOS only — every call here P/Invokes into CoreAudio.framework).
internal sealed class CoreAudioSystem : ICoreAudioSystem
{
    private static int _halThreadSet;

    // Volumes saved when a device without a mute control was "muted" by setting its volume to 0.
    private readonly ConcurrentDictionary<(uint Id, bool Input), float> _savedVolumes = new();

    public CoreAudioSystem() => UseHalThread();

    // By default the HAL delivers property notifications on the process's main run loop, which a
    // process that never runs one (the test host; any non-Cocoa thread) never services — so
    // listeners would never fire. Setting the HAL's run loop to NULL makes it use its own thread.
    // Must happen before the first listener is added; doing it once per process is enough.
    private static void UseHalThread()
    {
        if (Interlocked.Exchange(ref _halThreadSet, 1) == 1) return;
        var address = new AudioObjectPropertyAddress(HardwareRunLoop, ScopeGlobal, ElementMain);
        var data = Marshal.AllocHGlobal(IntPtr.Size);
        try
        {
            Marshal.WriteIntPtr(data, IntPtr.Zero);
            AudioObjectSetPropertyData(SystemObject, ref address, 0, IntPtr.Zero, (uint)IntPtr.Size, data);
        }
        finally { Marshal.FreeHGlobal(data); }
    }

    public IReadOnlyList<MacDevice> GetDevices()
    {
        var list = new List<MacDevice>();
        foreach (var id in GetUIntArray(SystemObject, new(HardwareDevices, ScopeGlobal)))
        {
            if (GetUInt(id, DeviceIsAlive, ScopeGlobal) == 0) continue;
            var uid = GetString(id, DeviceUID, ScopeGlobal);
            if (string.IsNullOrEmpty(uid)) continue;

            bool hasOut = StreamCount(id, ScopeOutput) > 0;
            bool hasIn = StreamCount(id, ScopeInput) > 0;
            bool canOut = hasOut && (GetUInt(id, CanBeDefaultDevice, ScopeOutput) ?? 1) != 0;
            list.Add(new MacDevice(
                ObjectId: id,
                Uid: uid,
                Name: GetString(id, ObjectName, ScopeGlobal) is { Length: > 0 } n ? n : uid,
                HasOutput: hasOut,
                HasInput: hasIn,
                // Devices that can't be the default (e.g. some aggregate sub-devices) aren't offered, as on Windows.
                CanBeDefaultOutput: canOut,
                CanBeDefaultInput: hasIn && (GetUInt(id, CanBeDefaultDevice, ScopeInput) ?? 1) != 0,
                CanBeSystemOutput: canOut && (GetUInt(id, CanBeDefaultSystemDevice, ScopeOutput) ?? 1) != 0,
                InputChannels: hasIn ? ChannelCount(id, ScopeInput) : 0,
                TransportType: GetUInt(id, TransportType, ScopeGlobal) ?? 0,
                ModelUid: GetString(id, ModelUID, ScopeGlobal),
                JackConnected: JackState(id),
                Hidden: (GetUInt(id, IsHidden, ScopeGlobal) ?? 0) != 0));
        }
        return list;
    }

    // Intel Macs report headphone-jack sensing on the built-in device's output (and sometimes input)
    // scope. (Apple Silicon Macs show plugged-in headphones as a separate device instead.)
    private static bool? JackState(uint id)
    {
        var o = GetUInt(id, JackIsConnected, ScopeOutput);
        var i = GetUInt(id, JackIsConnected, ScopeInput);
        return o == null && i == null ? null : (o ?? 0) != 0 || (i ?? 0) != 0;
    }

    public uint? GetDefault(DefaultRole role) => GetUInt(SystemObject, Selector(role), ScopeGlobal) is uint v && v != 0 ? v : null;

    public int SetDefault(DefaultRole role, uint objectId)
    {
        var address = new AudioObjectPropertyAddress(Selector(role), ScopeGlobal, ElementMain);
        var data = Marshal.AllocHGlobal(4);
        try
        {
            Marshal.WriteInt32(data, unchecked((int)objectId));
            return AudioObjectSetPropertyData(SystemObject, ref address, 0, IntPtr.Zero, 4, data);
        }
        finally { Marshal.FreeHGlobal(data); }
    }

    private static uint Selector(DefaultRole role) => role switch
    {
        DefaultRole.Output => DefaultOutputDevice,
        DefaultRole.Input => DefaultInputDevice,
        _ => DefaultSystemOutputDevice,
    };

    // ── Mute ────────────────────────────────────────────────────────────────────────────────────
    // Mute lives on the main element for most devices, per channel for some, and nowhere at all
    // for others (many built-in and USB microphones only have a volume). Windows can mute any
    // device in software; here a device without a mute control is muted by setting its volume to 0
    // and unmuted by restoring the volume it had.

    public bool? GetMute(uint objectId, bool input)
    {
        uint scope = input ? ScopeInput : ScopeOutput;
        foreach (var element in new uint[] { ElementMain, 1 })
            if (GetUInt(objectId, Mute, scope, element) is uint v) return v != 0;
        return GetVolume(objectId, scope) is float vol ? vol <= 0f : null;
    }

    public bool SetMute(uint objectId, bool input, bool mute)
    {
        uint scope = input ? ScopeInput : ScopeOutput;
        if (TrySetUInt(objectId, Mute, scope, ElementMain, mute ? 1u : 0u)) return true;
        bool any = false;
        foreach (var ch in Channels(objectId, Mute, scope))
            any |= TrySetUInt(objectId, Mute, scope, ch, mute ? 1u : 0u);
        if (any) return true;

        // No mute control: use the volume.
        var key = (objectId, input);
        if (mute)
        {
            if (GetVolume(objectId, scope) is not float current) return false;
            if (current > 0f) _savedVolumes[key] = current;
            return SetVolume(objectId, scope, 0f);
        }
        float restore = _savedVolumes.TryRemove(key, out var saved) ? saved : 0.5f;
        return GetVolume(objectId, scope) is float now && now > 0f ? true : SetVolume(objectId, scope, restore);
    }

    private static float? GetVolume(uint id, uint scope)
    {
        if (GetFloat(id, VolumeScalar, scope, ElementMain) is float main) return main;
        float? max = null;
        foreach (var ch in Channels(id, VolumeScalar, scope))
            if (GetFloat(id, VolumeScalar, scope, ch) is float v) max = Math.Max(max ?? 0f, v);
        return max;
    }

    private static bool SetVolume(uint id, uint scope, float value)
    {
        if (TrySetFloat(id, VolumeScalar, scope, ElementMain, value)) return true;
        bool any = false;
        foreach (var ch in Channels(id, VolumeScalar, scope)) any |= TrySetFloat(id, VolumeScalar, scope, ch, value);
        return any;
    }

    // The per-channel elements (1…n) that have the property.
    private static IEnumerable<uint> Channels(uint id, uint selector, uint scope)
    {
        for (uint ch = 1; ch <= 32; ch++)
        {
            var a = new AudioObjectPropertyAddress(selector, scope, ch);
            if (AudioObjectHasProperty(id, ref a) == 0) yield break;
            yield return ch;
        }
    }

    // ── Listening ───────────────────────────────────────────────────────────────────────────────

    public IDisposable Listen(Action onChange) => new Listener(onChange);

    // Core Audio calls one static function for every listener; clientData is a plain number naming
    // the listener in a table — never a pointer to managed memory — so a callback that arrives late
    // (after Dispose, or racing with removal) just finds nothing and returns.
    private sealed class Listener : IDisposable
    {
        private static readonly PropertyListenerProc Proc = OnProperty; // static: never collected
        private static readonly IntPtr ProcPtr = Marshal.GetFunctionPointerForDelegate(Proc);
        private static readonly ConcurrentDictionary<nint, Listener> Live = new();
        private static long _nextId;

        private static readonly uint[] SystemSelectors = [HardwareDevices, DefaultOutputDevice, DefaultInputDevice, DefaultSystemOutputDevice];
        private readonly Action _onChange;
        private readonly nint _id;
        private readonly object _gate = new();
        private readonly List<(uint Id, AudioObjectPropertyAddress Address)> _registered = [];
        private bool _disposed;

        public Listener(Action onChange)
        {
            _onChange = onChange;
            _id = (nint)Interlocked.Increment(ref _nextId);
            Live[_id] = this;
            try
            {
                lock (_gate)
                {
                    foreach (var sel in SystemSelectors) Add(SystemObject, new(sel, ScopeGlobal, ElementMain));
                    AddDeviceListeners();
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private static int OnProperty(uint objectId, uint count, IntPtr addresses, IntPtr clientData)
        {
            try
            {
                if (!Live.TryGetValue(clientData, out var self)) return 0;
                bool listChanged = false;
                int size = Marshal.SizeOf<AudioObjectPropertyAddress>();
                for (int i = 0; i < count; i++)
                {
                    var a = Marshal.PtrToStructure<AudioObjectPropertyAddress>(addresses + i * size);
                    if (objectId == SystemObject && a.Selector == HardwareDevices) listChanged = true;
                }
                // (Re)attach per-device listeners off this Core Audio thread: changing listeners
                // from inside a listener callback risks deadlocking on the HAL's own locks.
                if (listChanged) Task.Run(self.Refresh);
                self._onChange();
            }
            catch (Exception) { /* never let an exception reach native code */ }
            return 0;
        }

        private void Refresh()
        {
            lock (_gate)
            {
                if (_disposed) return;
                foreach (var r in _registered.Where(r => r.Id != SystemObject).ToList()) Remove(r);
                AddDeviceListeners();
            }
        }

        // Per device: alive, streams (a Bluetooth headset gains a microphone when it switches to
        // its call profile), name, and jack sensing.
        private void AddDeviceListeners()
        {
            foreach (var id in GetUIntArray(SystemObject, new(HardwareDevices, ScopeGlobal)))
            {
                Add(id, new(DeviceIsAlive, ScopeGlobal, ElementMain));
                Add(id, new(ObjectName, ScopeGlobal, ElementMain));
                foreach (var scope in new[] { ScopeOutput, ScopeInput })
                {
                    Add(id, new(Streams, scope, ElementMain));
                    var jack = new AudioObjectPropertyAddress(JackIsConnected, scope, ElementMain);
                    if (AudioObjectHasProperty(id, ref jack) != 0) Add(id, jack);
                }
            }
        }

        private void Add(uint id, AudioObjectPropertyAddress address)
        {
            if (AudioObjectHasProperty(id, ref address) == 0) return;
            if (AudioObjectAddPropertyListener(id, ref address, ProcPtr, _id) == 0)
                _registered.Add((id, address));
        }

        private void Remove((uint Id, AudioObjectPropertyAddress Address) r)
        {
            var a = r.Address;
            AudioObjectRemovePropertyListener(r.Id, ref a, ProcPtr, _id);
            _registered.Remove(r);
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                Live.TryRemove(_id, out _);
                foreach (var r in _registered.ToList()) Remove(r);
            }
        }
    }

    // ── IO registry (USB ids) ───────────────────────────────────────────────────────────────────

    public (ushort Vendor, ushort Product)? UsbIdsForLocation(uint locationId)
    {
        var keyLocation = CoreFoundation.Create("locationID");
        var keyVendor = CoreFoundation.Create("idVendor");
        var keyProduct = CoreFoundation.Create("idProduct");
        try
        {
            foreach (var cls in new[] { "IOUSBHostDevice", "IOUSBDevice" })
            {
                var matching = IOKit.IOServiceMatching(cls);
                if (matching == IntPtr.Zero) continue;
                // (Consumes `matching` whether or not it succeeds.)
                if (IOKit.IOServiceGetMatchingServices(IOKit.MainPortDefault, matching, out var iterator) != 0) continue;
                try
                {
                    uint entry;
                    while ((entry = IOKit.IOIteratorNext(iterator)) != 0)
                    {
                        try
                        {
                            // locationID is usually stored as a signed 32-bit number, so high locations
                            // (0xFA…, 0xFD… on Intel Macs) read back negative: compare the low 32 bits.
                            if (Number(entry, keyLocation) is not long loc || unchecked((uint)loc) != locationId) continue;
                            if (Number(entry, keyVendor) is long v && Number(entry, keyProduct) is long p)
                                return (unchecked((ushort)v), unchecked((ushort)p));
                        }
                        finally { IOKit.IOObjectRelease(entry); }
                    }
                }
                finally { IOKit.IOObjectRelease(iterator); }
            }
            return null;
        }
        finally
        {
            CoreFoundation.CFRelease(keyLocation);
            CoreFoundation.CFRelease(keyVendor);
            CoreFoundation.CFRelease(keyProduct);
        }
    }

    private static long? Number(uint entry, IntPtr key)
    {
        var value = IOKit.IORegistryEntryCreateCFProperty(entry, key, IntPtr.Zero, 0);
        if (value == IntPtr.Zero) return null;
        try
        {
            if (CoreFoundation.CFGetTypeID(value) != CoreFoundation.CFNumberGetTypeID()) return null;
            return CoreFoundation.CFNumberGetValue(value, CoreFoundation.NumberSInt64Type, out var n) != 0 ? n : null;
        }
        finally { CoreFoundation.CFRelease(value); }
    }

    // ── Property helpers ────────────────────────────────────────────────────────────────────────

    private static uint? GetUInt(uint id, uint selector, uint scope, uint element = ElementMain)
    {
        var a = new AudioObjectPropertyAddress(selector, scope, element);
        if (AudioObjectHasProperty(id, ref a) == 0) return null;
        var buf = Marshal.AllocHGlobal(4);
        try
        {
            uint size = 4;
            return AudioObjectGetPropertyData(id, ref a, 0, IntPtr.Zero, ref size, buf) == 0 && size == 4
                ? unchecked((uint)Marshal.ReadInt32(buf))
                : null;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private static float? GetFloat(uint id, uint selector, uint scope, uint element) =>
        GetUInt(id, selector, scope, element) is uint bits ? BitConverter.UInt32BitsToSingle(bits) : null;

    private static bool TrySetUInt(uint id, uint selector, uint scope, uint element, uint value)
    {
        var a = new AudioObjectPropertyAddress(selector, scope, element);
        if (AudioObjectHasProperty(id, ref a) == 0) return false;
        if (AudioObjectIsPropertySettable(id, ref a, out var settable) != 0 || settable == 0) return false;
        var buf = Marshal.AllocHGlobal(4);
        try
        {
            Marshal.WriteInt32(buf, unchecked((int)value));
            return AudioObjectSetPropertyData(id, ref a, 0, IntPtr.Zero, 4, buf) == 0;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private static bool TrySetFloat(uint id, uint selector, uint scope, uint element, float value) =>
        TrySetUInt(id, selector, scope, element, BitConverter.SingleToUInt32Bits(value));

    // Copy-rule CFString properties (name, UIDs) come back retained; release after copying.
    private static string? GetString(uint id, uint selector, uint scope)
    {
        var a = new AudioObjectPropertyAddress(selector, scope, ElementMain);
        if (AudioObjectHasProperty(id, ref a) == 0) return null;
        var buf = Marshal.AllocHGlobal(IntPtr.Size);
        try
        {
            uint size = (uint)IntPtr.Size;
            if (AudioObjectGetPropertyData(id, ref a, 0, IntPtr.Zero, ref size, buf) != 0) return null;
            var cf = Marshal.ReadIntPtr(buf);
            if (cf == IntPtr.Zero) return null;
            try { return CoreFoundation.ToManaged(cf); }
            finally { CoreFoundation.CFRelease(cf); }
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private static int StreamCount(uint id, uint scope)
    {
        var a = new AudioObjectPropertyAddress(Streams, scope, ElementMain);
        return AudioObjectGetPropertyDataSize(id, ref a, 0, IntPtr.Zero, out var size) == 0 ? (int)(size / 4) : 0;
    }

    // Total channels across the scope's streams, from its AudioBufferList
    // ({ UInt32 mNumberBuffers; AudioBuffer mBuffers[] } with AudioBuffer = { UInt32 channels; UInt32 bytes; void* data }).
    private static int ChannelCount(uint id, uint scope)
    {
        var a = new AudioObjectPropertyAddress(StreamConfiguration, scope, ElementMain);
        if (AudioObjectGetPropertyDataSize(id, ref a, 0, IntPtr.Zero, out var size) != 0 || size < 4) return 1;
        var buf = Marshal.AllocHGlobal((int)size);
        try
        {
            if (AudioObjectGetPropertyData(id, ref a, 0, IntPtr.Zero, ref size, buf) != 0) return 1;
            int buffers = Marshal.ReadInt32(buf);
            int stride = 8 + IntPtr.Size, first = IntPtr.Size; // mBuffers is pointer-aligned
            int channels = 0;
            for (int i = 0; i < buffers && first + (i + 1) * stride <= size; i++)
                channels += Marshal.ReadInt32(buf, first + i * stride);
            return Math.Max(channels, 1);
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private static uint[] GetUIntArray(uint id, AudioObjectPropertyAddress a)
    {
        if (AudioObjectGetPropertyDataSize(id, ref a, 0, IntPtr.Zero, out var size) != 0 || size == 0) return [];
        var buf = Marshal.AllocHGlobal((int)size);
        try
        {
            if (AudioObjectGetPropertyData(id, ref a, 0, IntPtr.Zero, ref size, buf) != 0) return [];
            var result = new uint[size / 4];
            for (int i = 0; i < result.Length; i++) result[i] = unchecked((uint)Marshal.ReadInt32(buf, i * 4));
            return result;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }
}
