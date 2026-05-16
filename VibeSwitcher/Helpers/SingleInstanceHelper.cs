namespace VibeSwitcher.Helpers;

public class SingleInstanceHelper : IDisposable
{
    // Local\ prefix scopes the mutex to the current session so Fast User Switching
    // and Remote Desktop users each get their own independent instance.
    private static readonly string MutexName =
        $"Local\\VibeSwitcher_SingleInstance_v1_{Environment.UserName}";
    private Mutex? _mutex;
    private bool _owned;

    public bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: true, name: MutexName, out _owned);
        return _owned;
    }

    public void Dispose()
    {
        if (_owned && _mutex != null)
        {
            try { _mutex.ReleaseMutex(); } catch { }
        }
        _mutex?.Dispose();
        _mutex = null;
    }
}
