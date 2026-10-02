using DaybreakDamageTracker.Common.Systems;

namespace DaybreakDamageTracker.Common.Networking;

internal readonly record struct PendingStateRequest(
    EncounterPlayerKey Connection,
    int RequestId);

internal sealed class PendingStateRequestQueue
{
    private readonly Queue<PendingStateRequest> _entries = new();
    private readonly object _sync = new();
    private readonly int _capacity;

    public PendingStateRequestQueue(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _capacity = capacity;
    }

    public bool TryEnqueue(PendingStateRequest request)
    {
        lock (_sync)
        {
            if (_entries.Count >= _capacity)
                return false;

            _entries.Enqueue(request);
            return true;
        }
    }

    public List<PendingStateRequest> Drain()
    {
        lock (_sync)
        {
            List<PendingStateRequest> drained = [.. _entries];
            _entries.Clear();
            return drained;
        }
    }

    public void Clear()
    {
        lock (_sync)
            _entries.Clear();
    }
}
