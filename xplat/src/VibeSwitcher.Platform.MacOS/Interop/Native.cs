using System.Runtime.InteropServices;

namespace VibeSwitcher.Platform.MacOS.Interop;

// Hand-written bindings for the few Apple C APIs the macOS backend uses. Everything here is
// blittable (ints, pointers, plain structs), so plain DllImport marshals it with no surprises.
// Layouts and constants follow the macOS SDK headers (CoreAudio/AudioHardware*.h,
// AudioToolbox/AudioQueue.h, CoreAudioTypes.h, CoreFoundation, IOKit). 64-bit only.

internal static class FourCC
{
    // 'dev#' → 0x64657623, as the C four-character constants are defined.
    public static uint Of(string s) =>
        s.Length != 4 ? throw new ArgumentException("FourCC needs 4 characters", nameof(s))
            : ((uint)s[0] << 24) | ((uint)s[1] << 16) | ((uint)s[2] << 8) | s[3];

    public static string ToText(uint v) =>
        new([(char)((v >> 24) & 0xFF), (char)((v >> 16) & 0xFF), (char)((v >> 8) & 0xFF), (char)(v & 0xFF)]);
}

[StructLayout(LayoutKind.Sequential)]
internal struct AudioObjectPropertyAddress(uint selector, uint scope, uint element = 0)
{
    public uint Selector = selector;
    public uint Scope = scope;
    public uint Element = element;
}

[StructLayout(LayoutKind.Sequential)]
internal struct AudioStreamBasicDescription
{
    public double SampleRate;
    public uint FormatID;
    public uint FormatFlags;
    public uint BytesPerPacket;
    public uint FramesPerPacket;
    public uint BytesPerFrame;
    public uint ChannelsPerFrame;
    public uint BitsPerChannel;
    public uint Reserved;
}

// AudioQueueBuffer (AudioQueue.h). Natural alignment gives the same layout as C on 64-bit.
[StructLayout(LayoutKind.Sequential)]
internal struct AudioQueueBuffer
{
    public uint AudioDataBytesCapacity;
    public IntPtr AudioData;
    public uint AudioDataByteSize;
    public IntPtr UserData;
    public uint PacketDescriptionCapacity;
    public IntPtr PacketDescriptions;
    public uint PacketDescriptionCount;
}

[StructLayout(LayoutKind.Sequential)]
internal struct CFRange(nint location, nint length)
{
    public nint Location = location;
    public nint Length = length;
}

internal static class CoreAudio
{
    private const string Lib = "/System/Library/Frameworks/CoreAudio.framework/CoreAudio";

    public const uint SystemObject = 1;
    public static readonly uint ScopeGlobal = FourCC.Of("glob");
    public static readonly uint ScopeInput = FourCC.Of("inpt");
    public static readonly uint ScopeOutput = FourCC.Of("outp");
    public const uint ElementMain = 0;

    // Hardware (system object)
    public static readonly uint HardwareDevices = FourCC.Of("dev#");
    public static readonly uint DefaultInputDevice = FourCC.Of("dIn ");
    public static readonly uint DefaultOutputDevice = FourCC.Of("dOut");
    public static readonly uint DefaultSystemOutputDevice = FourCC.Of("sOut");
    // Which run loop the HAL delivers notifications on; NULL = its own thread.
    public static readonly uint HardwareRunLoop = FourCC.Of("rnlp");

    // Device
    public static readonly uint ObjectName = FourCC.Of("lnam");
    public static readonly uint DeviceUID = FourCC.Of("uid ");
    public static readonly uint ModelUID = FourCC.Of("muid");
    public static readonly uint TransportType = FourCC.Of("tran");
    public static readonly uint Streams = FourCC.Of("stm#");
    public static readonly uint DeviceIsAlive = FourCC.Of("livn");
    public static readonly uint IsHidden = FourCC.Of("hidn");
    public static readonly uint CanBeDefaultDevice = FourCC.Of("dflt");
    public static readonly uint CanBeDefaultSystemDevice = FourCC.Of("sflt");
    public static readonly uint StreamConfiguration = FourCC.Of("slay");
    public static readonly uint VolumeScalar = FourCC.Of("volm");
    public static readonly uint Mute = FourCC.Of("mute");
    public static readonly uint JackIsConnected = FourCC.Of("jack");

    // Transport types
    public static readonly uint TransportUSB = FourCC.Of("usb ");
    public static readonly uint TransportBluetooth = FourCC.Of("blue");
    public static readonly uint TransportBluetoothLE = FourCC.Of("blea");
    public static readonly uint TransportBuiltIn = FourCC.Of("bltn");
    public static readonly uint TransportVirtual = FourCC.Of("virt");
    public static readonly uint TransportAggregate = FourCC.Of("grup");

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int PropertyListenerProc(uint objectId, uint numberAddresses, IntPtr addresses, IntPtr clientData);

    [DllImport(Lib)]
    public static extern int AudioObjectGetPropertyDataSize(uint objectId, ref AudioObjectPropertyAddress address,
        uint qualifierDataSize, IntPtr qualifierData, out uint dataSize);

    [DllImport(Lib)]
    public static extern int AudioObjectGetPropertyData(uint objectId, ref AudioObjectPropertyAddress address,
        uint qualifierDataSize, IntPtr qualifierData, ref uint ioDataSize, IntPtr outData);

    [DllImport(Lib)]
    public static extern int AudioObjectSetPropertyData(uint objectId, ref AudioObjectPropertyAddress address,
        uint qualifierDataSize, IntPtr qualifierData, uint dataSize, IntPtr data);

    [DllImport(Lib)]
    public static extern byte AudioObjectHasProperty(uint objectId, ref AudioObjectPropertyAddress address);

    [DllImport(Lib)]
    public static extern int AudioObjectIsPropertySettable(uint objectId, ref AudioObjectPropertyAddress address, out byte isSettable);

    [DllImport(Lib)]
    public static extern int AudioObjectAddPropertyListener(uint objectId, ref AudioObjectPropertyAddress address,
        IntPtr listener, IntPtr clientData);

    [DllImport(Lib)]
    public static extern int AudioObjectRemovePropertyListener(uint objectId, ref AudioObjectPropertyAddress address,
        IntPtr listener, IntPtr clientData);
}

internal static class AudioToolbox
{
    private const string Lib = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";

    public static readonly uint FormatLinearPCM = FourCC.Of("lpcm");
    public const uint FormatFlagIsFloat = 1;
    public const uint FormatFlagIsSignedInteger = 4;
    public const uint FormatFlagIsPacked = 8;

    public static readonly uint QueuePropertyCurrentDevice = FourCC.Of("aqcd");
    public static readonly uint QueuePropertyIsRunning = FourCC.Of("aqrn");

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OutputCallback(IntPtr userData, IntPtr queue, IntPtr buffer);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void InputCallback(IntPtr userData, IntPtr queue, IntPtr buffer, IntPtr startTime,
        uint numberPacketDescriptions, IntPtr packetDescs);

    [DllImport(Lib)]
    public static extern int AudioQueueNewOutput(ref AudioStreamBasicDescription format, IntPtr callback, IntPtr userData,
        IntPtr callbackRunLoop, IntPtr callbackRunLoopMode, uint flags, out IntPtr queue);

    [DllImport(Lib)]
    public static extern int AudioQueueNewInput(ref AudioStreamBasicDescription format, IntPtr callback, IntPtr userData,
        IntPtr callbackRunLoop, IntPtr callbackRunLoopMode, uint flags, out IntPtr queue);

    [DllImport(Lib)]
    public static extern int AudioQueueAllocateBuffer(IntPtr queue, uint bufferByteSize, out IntPtr buffer);

    [DllImport(Lib)]
    public static extern int AudioQueueEnqueueBuffer(IntPtr queue, IntPtr buffer, uint numPacketDescs, IntPtr packetDescs);

    [DllImport(Lib)]
    public static extern int AudioQueueStart(IntPtr queue, IntPtr startTime);

    [DllImport(Lib)]
    public static extern int AudioQueueStop(IntPtr queue, byte immediate);

    [DllImport(Lib)]
    public static extern int AudioQueueDispose(IntPtr queue, byte immediate);

    [DllImport(Lib)]
    public static extern int AudioQueueSetProperty(IntPtr queue, uint propertyId, IntPtr data, uint dataSize);

    [DllImport(Lib)]
    public static extern int AudioQueueGetProperty(IntPtr queue, uint propertyId, IntPtr data, ref uint dataSize);
}

internal static class CoreFoundation
{
    private const string Lib = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    public const nint NumberSInt64Type = 4; // CFNumberType is a CFIndex

    [DllImport(Lib)]
    public static extern nint CFStringGetLength(IntPtr str);

    [DllImport(Lib)]
    public static extern void CFStringGetCharacters(IntPtr str, CFRange range, IntPtr buffer);

    [DllImport(Lib)]
    public static extern IntPtr CFStringCreateWithCharacters(IntPtr allocator, IntPtr chars, nint numChars);

    [DllImport(Lib, EntryPoint = "CFRelease")]
    private static extern void CFReleaseNative(IntPtr obj);

    // CFRelease(NULL) crashes; this doesn't.
    public static void CFRelease(IntPtr obj)
    {
        if (obj != IntPtr.Zero) CFReleaseNative(obj);
    }

    [DllImport(Lib)]
    public static extern nuint CFGetTypeID(IntPtr obj);

    [DllImport(Lib)]
    public static extern nuint CFNumberGetTypeID();

    [DllImport(Lib)]
    public static extern nuint CFStringGetTypeID();

    [DllImport(Lib)]
    public static extern byte CFNumberGetValue(IntPtr number, nint type, out long value);

    // Copies a CFString into a .NET string (does not release it).
    public static string? ToManaged(IntPtr cfString)
    {
        if (cfString == IntPtr.Zero) return null;
        nint len = CFStringGetLength(cfString);
        if (len <= 0) return "";
        var buffer = Marshal.AllocHGlobal((int)len * 2);
        try
        {
            CFStringGetCharacters(cfString, new CFRange(0, len), buffer);
            return Marshal.PtrToStringUni(buffer, (int)len);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    // A new CFString; the caller must CFRelease it.
    public static IntPtr Create(string s)
    {
        var chars = Marshal.StringToHGlobalUni(s);
        try { return CFStringCreateWithCharacters(IntPtr.Zero, chars, s.Length); }
        finally { Marshal.FreeHGlobal(chars); }
    }
}

internal static class IOKit
{
    private const string Lib = "/System/Library/Frameworks/IOKit.framework/IOKit";
    public const uint MainPortDefault = 0;

    [DllImport(Lib)]
    public static extern IntPtr IOServiceMatching([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    // Consumes one reference to `matching`.
    [DllImport(Lib)]
    public static extern int IOServiceGetMatchingServices(uint mainPort, IntPtr matching, out uint iterator);

    [DllImport(Lib)]
    public static extern uint IOIteratorNext(uint iterator);

    [DllImport(Lib)]
    public static extern int IOObjectRelease(uint obj);

    [DllImport(Lib)]
    public static extern IntPtr IORegistryEntryCreateCFProperty(uint entry, IntPtr key, IntPtr allocator, uint options);
}
