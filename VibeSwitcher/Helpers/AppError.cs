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
    private static readonly List<SessionError> _errors = new();

    public static IReadOnlyList<SessionError> Errors => _errors.AsReadOnly();

    public static bool HasErrors => _errors.Count > 0;

    public static event EventHandler? ErrorAdded;

    public static void Record(ErrorCode code, string title, string message)
    {
        _errors.Add(new SessionError(DateTime.Now, code, title, message));
        ErrorAdded?.Invoke(null, EventArgs.Empty);
    }
}
