namespace VibeSwitcher.Helpers;

public enum ErrorCode
{
    PlaybackDeviceUnavailable  = 1,
    RecordingDeviceUnavailable = 2,
    ProfileSwitchFailed        = 3,
    HotkeyConflict             = 4,
    IconLoadFailed             = 5,
    IconCopyFailed             = 6,
}

public static class ErrorCodeExtensions
{
    public static string ToCode(this ErrorCode code) => $"VS-{(int)code:D3}";
}

public record SessionError(DateTime Timestamp, ErrorCode Code, string Title, string Message);

public static class SessionErrorTracker
{
    private static readonly object _lock = new();
    private static readonly List<SessionError> _errors = new();

    public static IReadOnlyList<SessionError> Errors
    {
        get { lock (_lock) { return _errors.ToList().AsReadOnly(); } }
    }

    public static bool HasErrors { get { lock (_lock) { return _errors.Count > 0; } } }

    public static int Count { get { lock (_lock) { return _errors.Count; } } }

    public static event EventHandler? ErrorAdded;

    public static void Record(ErrorCode code, string title, string message)
    {
        lock (_lock)
        {
            _errors.Add(new SessionError(DateTime.Now, code, title, message));
        }
        ErrorAdded?.Invoke(null, EventArgs.Empty);
    }
}
