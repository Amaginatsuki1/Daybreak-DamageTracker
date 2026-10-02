namespace DaybreakDamageTracker.Client;

internal sealed class ChatSummaryBatcher
{
    private const long QuietTicks = 6;
    private bool _pending;
    private long _lastQueuedTick;

    public bool HasPending => _pending;

    public void Queue(long currentTick)
    {
        _pending = true;
        _lastQueuedTick = currentTick;
    }

    public bool TryConsume(long currentTick)
    {
        if (!_pending || currentTick - _lastQueuedTick < QuietTicks)
            return false;

        _pending = false;
        return true;
    }

    public void Reset()
    {
        _pending = false;
        _lastQueuedTick = 0;
    }
}
