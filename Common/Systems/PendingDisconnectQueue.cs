using System.Collections.Concurrent;

namespace DaybreakDamageTracker.Common.Systems;

internal readonly record struct PendingConnectionDisconnect(int PlayerIndex, int Generation);

internal sealed class PendingDisconnectQueue
{
    private readonly ConcurrentQueue<PendingConnectionDisconnect> _entries = new();

    public void Enqueue(int playerIndex, int generation)
        => _entries.Enqueue(new PendingConnectionDisconnect(playerIndex, generation));

    public List<PendingConnectionDisconnect> Drain()
    {
        List<PendingConnectionDisconnect> drained = [];
        while (_entries.TryDequeue(out PendingConnectionDisconnect entry))
            drained.Add(entry);
        return drained;
    }

    public void Clear()
    {
        while (_entries.TryDequeue(out _))
        {
        }
    }
}

internal static class ConnectionGenerationPolicy
{
    public static bool ShouldQueueDisconnect(
        int pendingDisconnectGeneration,
        int disconnectedGeneration)
        => pendingDisconnectGeneration != disconnectedGeneration;

    public static bool ShouldAdvance(
        bool wasActive,
        bool isActive,
        int currentGeneration,
        int pendingDisconnectGeneration)
        => isActive &&
           (!wasActive || pendingDisconnectGeneration == currentGeneration);
}
