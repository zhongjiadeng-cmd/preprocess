namespace GrayscaleLayersMac;

public sealed class TexturePreviewRequestGate
{
    private readonly object _sync = new();
    private long _latestRequestId;
    private bool _isClosed;

    public long BeginRequest()
    {
        lock (_sync)
            return ++_latestRequestId;
    }

    public bool RunIfCurrent(long requestId, Action action)
    {
        lock (_sync)
        {
            if (_isClosed || requestId != _latestRequestId)
                return false;

            action();
            return true;
        }
    }

    public void Close()
    {
        lock (_sync)
        {
            _isClosed = true;
            _latestRequestId++;
        }
    }
}
