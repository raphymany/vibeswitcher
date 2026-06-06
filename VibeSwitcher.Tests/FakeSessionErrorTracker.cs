using VibeSwitcher.Helpers;

namespace VibeSwitcher.Tests;

public class FakeSessionErrorTracker : ISessionErrorTracker
{
    private readonly object _lock = new();
    private readonly List<SessionError> _errors = [];

    public IReadOnlyList<SessionError> Errors { get { lock (_lock) { return _errors.ToList().AsReadOnly(); } } }
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
