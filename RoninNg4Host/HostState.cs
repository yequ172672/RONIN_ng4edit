using YakumoLib.Assets;

namespace RoninNg4Host;

public sealed class HostState
{
    private readonly object _gate = new();
    private AssetLibrary? _library;
    private string? _assetsDirectory;
    private string _status = "idle";
    private string? _error;
    private DateTimeOffset _lastActivityUtc = DateTimeOffset.UtcNow;
    private int _activeRequests;

    public (AssetLibrary? Library, string? AssetsDirectory, string Status, string? Error) Snapshot()
    {
        lock (_gate) return (_library, _assetsDirectory, _status, _error);
    }

    public void Touch() { lock (_gate) _lastActivityUtc = DateTimeOffset.UtcNow; }

    public IDisposable BeginRequest()
    {
        lock (_gate)
        {
            _activeRequests++;
            _lastActivityUtc = DateTimeOffset.UtcNow;
        }
        return new RequestLease(this);
    }

    public bool IsIdle(TimeSpan timeout)
    {
        lock (_gate) return _activeRequests == 0 && DateTimeOffset.UtcNow - _lastActivityUtc >= timeout;
    }

    public void SetLoading(string assetsDirectory)
    {
        lock (_gate)
        {
            _assetsDirectory = assetsDirectory;
            _library = null;
            _status = "loading";
            _error = null;
        }
    }

    public void SetReady(string assetsDirectory, AssetLibrary library)
    {
        lock (_gate)
        {
            _assetsDirectory = assetsDirectory;
            _library = library;
            _status = "ready";
            _error = null;
        }
    }

    public void SetError(string message)
    {
        lock (_gate)
        {
            _library = null;
            _status = "error";
            _error = message;
        }
    }

    private void EndRequest()
    {
        lock (_gate)
        {
            _activeRequests = Math.Max(0, _activeRequests - 1);
            _lastActivityUtc = DateTimeOffset.UtcNow;
        }
    }

    private sealed class RequestLease(HostState owner) : IDisposable
    {
        private HostState? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.EndRequest();
    }
}
