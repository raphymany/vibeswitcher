namespace VibeSwitcher.Helpers;

public enum ErrorCode
{
    // Audio switching
    PlaybackDeviceUnavailable  = 1,
    RecordingDeviceUnavailable = 2,
    ProfileSwitchFailed        = 3,
    PolicySetDefaultFailed     = 12,  // SetDefaultEndpoint returned non-zero HRESULT
    PolicyConfigUnsupported    = 13,  // Windows version doesn't support the COM interface
    AudioDeviceInfoFailed      = 14,  // Per-device COM call failed during enumeration

    // Hotkeys
    HotkeyConflict             = 4,
    HotkeyRegistrationFailed   = 9,
    HotkeyAtomCreateFailed     = 18,  // GlobalAddAtom returned 0 (atom table full)

    // Icons
    IconLoadFailed             = 5,
    IconCopyFailed             = 6,
    GdiRenderFailed            = 20,  // GDI+ icon creation failed (e.g. handle exhaustion)
    IconRenderFailed           = 21,  // HICON → WPF ImageSource conversion failed
    IconDeleteFailed           = 22,  // Old icon file could not be deleted
    IconPreviewFailed          = 23,  // Profile card icon preview update failed

    // Config
    ConfigLoadFailed           = 7,
    ConfigSaveFailed           = 8,
    ConfigDirCreateFailed      = 15,  // AppData directory could not be created

    // Startup / registry
    StartupRegistryFailed      = 11,
    StartupRegistryReadFailed  = 16,  // IsStartupEnabled() threw — registry access denied
    StartupPathResolutionFailed = 17, // Executable path is empty — startup entry would be broken

    // Audio enumeration
    AudioEnumerationFailed     = 10,

    // Commands / UI
    CommandExecutionFailed     = 19,  // Unhandled exception in a RelayCommand action

    // Process / shell
    HyperlinkOpenFailed        = 24,  // Process.Start for a URL or file failed
    SoundSettingsOpenFailed    = 26,  // control.exe /name Microsoft.Sound could not launch

    // Tray
    TrayIconCreateFailed       = 25,  // Shell_NotifyIcon registration failed

    // Audio service / notifications
    AudioServiceUnavailable    = 27,  // HRESULT 0x80070424 — Windows Audio service not running
    DeviceNotificationFailed   = 28,  // RegisterEndpointNotificationCallback failed
}

public static class ErrorCodeExtensions
{
    public static string ToCode(this ErrorCode code) => $"VS-{(int)code:D3}";
}

public record SessionError(DateTime Timestamp, ErrorCode Code, string Title, string Message);

public interface ISessionErrorTracker
{
    void Record(ErrorCode code, string title, string message);
    IReadOnlyList<SessionError> Errors { get; }
    bool HasErrors { get; }
    int Count { get; }
    event EventHandler? ErrorAdded;
}

public class SessionErrorTracker : ISessionErrorTracker
{
    private readonly object _lock = new();
    private readonly List<SessionError> _errors = [];
    public IReadOnlyList<SessionError> Errors
    {
        get { lock (_lock) { return _errors.ToList().AsReadOnly(); } }
    }

    public bool HasErrors { get { lock (_lock) { return _errors.Count > 0; } } }

    public int Count { get { lock (_lock) { return _errors.Count; } } }

    public event EventHandler? ErrorAdded;

    public void Record(ErrorCode code, string title, string message)
    {
        lock (_lock)
        {
            _errors.Add(new SessionError(DateTime.Now, code, title, message));
        }
        ErrorAdded?.Invoke(this, EventArgs.Empty);
    }
}
