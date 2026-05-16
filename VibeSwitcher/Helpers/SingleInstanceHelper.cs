namespace VibeSwitcher.Helpers;

public class SingleInstanceHelper : IDisposable
{
    private const string MutexName = "VibeSwitcher_SingleInstance_v1";
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
