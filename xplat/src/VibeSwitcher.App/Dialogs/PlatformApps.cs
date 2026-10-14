using VibeSwitcher.Platform;
using VibeSwitcher.Platform.Linux.Apps;
using VibeSwitcher.Platform.Linux.Hid;
using VibeSwitcher.Platform.MacOS.Apps;

namespace VibeSwitcher.App.Dialogs;

// This system's app catalog (the app-trigger wizard's lists) and, on Linux, the wireless-headset
// access check. Null where there's none (Windows, where tests run).
internal static class PlatformApps
{
    public static IAppCatalog? Catalog() =>
        OperatingSystem.IsMacOS() ? new MacAppCatalog(new MacProcesses())
        : OperatingSystem.IsLinux() ? new LinuxAppCatalog(new LinuxProcesses())
        : null;

    public static HeadsetAccessHooks? HeadsetAccess()
    {
        if (!OperatingSystem.IsLinux()) return null;
        var access = new LinuxHidAccess();
        return new HeadsetAccessHooks(access.Check, access.Install);
    }
}
