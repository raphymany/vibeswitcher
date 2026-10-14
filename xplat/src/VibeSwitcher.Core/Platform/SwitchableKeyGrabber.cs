namespace VibeSwitcher.Platform;

// The app's key grabber, whose way of grabbing keys can change while it runs (Linux: the user allows
// or stops GNOME keyboard shortcuts in Settings). The hotkey service keeps this one; Swap puts the new
// way in place — unregister everything first and register it again after, through the service.
public sealed class SwitchableKeyGrabber : IGlobalKeyGrabber
{
    private readonly object _lock = new();
    private IGlobalKeyGrabber _inner;
    private bool _disposed;

    public SwitchableKeyGrabber(IGlobalKeyGrabber inner)
    {
        _inner = inner;
        _inner.Pressed += OnPressed;
    }

    public event Action<ushort>? Pressed;

    public IGlobalKeyGrabber Inner { get { lock (_lock) return _inner; } }

    // Returns the grabber it replaced, for the caller to dispose (that may block: off the UI thread).
    public IGlobalKeyGrabber Swap(IGlobalKeyGrabber next)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var old = _inner;
            old.Pressed -= OnPressed;
            _inner = next;
            next.Pressed += OnPressed;
            return old;
        }
    }

    public GrabResult Register(ushort id, int modifiers, int virtualKey, out string? error) =>
        Inner.Register(id, modifiers, virtualKey, out error);

    public void Unregister(ushort id) => Inner.Unregister(id);

    public void Describe(ushort id, string name, string description) => Inner.Describe(id, name, description);

    private void OnPressed(ushort id) => Pressed?.Invoke(id);

    public void Dispose()
    {
        IGlobalKeyGrabber inner;
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            inner = _inner;
            inner.Pressed -= OnPressed;
        }
        inner.Dispose();
    }
}
