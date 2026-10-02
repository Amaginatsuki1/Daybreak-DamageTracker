using System.Threading;

namespace DaybreakDamageTracker.Common.Systems;

internal readonly record struct EncounterPlayerKey(int PlayerIndex, int Generation);

internal static class PlayerConnectionRegistry
{
    private static bool[] _wasActive = new bool[Main.maxPlayers];
    private static int[] _generation = new int[Main.maxPlayers];
    private static int[] _pendingDisconnectGeneration = CreatePendingMarkers();
    private static object[] _stateLocks = CreateStateLocks();
    private static readonly PendingDisconnectQueue PendingDisconnects = new();

    public static void Reset()
    {
        _wasActive = new bool[Main.maxPlayers];
        _generation = new int[Main.maxPlayers];
        _pendingDisconnectGeneration = CreatePendingMarkers();
        _stateLocks = CreateStateLocks();
        PendingDisconnects.Clear();
    }

    public static void Update()
    {
        for (int i = 0; i < Main.maxPlayers; i++)
        {
            lock (_stateLocks[i])
            {
                bool active = Main.player[i].active;
                if (active)
                    EnsureCurrentGeneration(i);
                _wasActive[i] = active;
            }
        }
    }

    public static EncounterPlayerKey GetCurrent(int playerIndex)
    {
        if (playerIndex < 0 || playerIndex >= Main.maxPlayers)
            return new EncounterPlayerKey(playerIndex, 0);

        lock (_stateLocks[playerIndex])
        {
            if (Main.player[playerIndex].active)
            {
                EnsureCurrentGeneration(playerIndex);
                _wasActive[playerIndex] = true;
            }

            return new EncounterPlayerKey(playerIndex, Volatile.Read(ref _generation[playerIndex]));
        }
    }

    public static bool IsCurrent(EncounterPlayerKey key)
        => key.PlayerIndex >= 0 &&
           key.PlayerIndex < Main.maxPlayers &&
           Main.player[key.PlayerIndex].active &&
           GetCurrent(key.PlayerIndex).Generation == key.Generation;

    // PlayerDisconnect may be raised by tML's network thread. Only enqueue plain
    // connection data here; every shared gameplay collection is cleaned on the
    // main world-update thread before private send queues drain.
    public static void QueueDisconnected(int playerIndex)
    {
        if (playerIndex < 0 || playerIndex >= Main.maxPlayers)
            return;

        lock (_stateLocks[playerIndex])
        {
            // Capture the connection generation under the same slot lock used by
            // GetCurrent. A replacement connection can request state and disconnect
            // again before PostUpdateWorld drains the previous notification. If the
            // slot is active, recognizing that pending-generation boundary here keeps
            // the disconnect callback from observing generation N just before a
            // concurrent request advances the slot to N+1.
            if (Main.player[playerIndex].active)
            {
                EnsureCurrentGeneration(playerIndex);
                _wasActive[playerIndex] = true;
            }

            int generation = Volatile.Read(ref _generation[playerIndex]);
            int pendingGeneration = Volatile.Read(ref _pendingDisconnectGeneration[playerIndex]);
            if (!ConnectionGenerationPolicy.ShouldQueueDisconnect(pendingGeneration, generation))
                return;

            Volatile.Write(ref _pendingDisconnectGeneration[playerIndex], generation);

            // A slot can disconnect, be reused, and disconnect again before the next
            // world tick. Keep one entry per connection generation rather than one
            // entry per slot so the newer connection's grants and private queues are
            // not left behind when the older notification is drained first.
            PendingDisconnects.Enqueue(playerIndex, generation);
        }
    }

    public static void FlushPendingDisconnects(Action<EncounterPlayerKey> cleanup)
    {
        ArgumentNullException.ThrowIfNull(cleanup);
        foreach (PendingConnectionDisconnect pending in PendingDisconnects.Drain())
        {
            if (pending.PlayerIndex < 0 || pending.PlayerIndex >= Main.maxPlayers)
                continue;

            EncounterPlayerKey disconnected = new(pending.PlayerIndex, pending.Generation);
            cleanup(disconnected);

            // Do not let a stale duplicate disconnect mark a later connection as
            // absent. If the slot was already reused, its generation has advanced.
            lock (_stateLocks[pending.PlayerIndex])
            {
                if (Volatile.Read(ref _generation[pending.PlayerIndex]) == pending.Generation)
                    _wasActive[pending.PlayerIndex] = false;

                Interlocked.CompareExchange(
                    ref _pendingDisconnectGeneration[pending.PlayerIndex],
                    -1,
                    pending.Generation);
            }
        }
    }

    private static void EnsureCurrentGeneration(int playerIndex)
    {
        int generation = Volatile.Read(ref _generation[playerIndex]);
        int pendingDisconnect = Volatile.Read(ref _pendingDisconnectGeneration[playerIndex]);
        if (ConnectionGenerationPolicy.ShouldAdvance(
                _wasActive[playerIndex],
                Main.player[playerIndex].active,
                generation,
                pendingDisconnect))
        {
            AdvanceGeneration(playerIndex);
        }
    }

    private static void AdvanceGeneration(int playerIndex)
    {
        int current = Volatile.Read(ref _generation[playerIndex]);
        Volatile.Write(ref _generation[playerIndex], current == int.MaxValue ? 1 : current + 1);
    }

    private static int[] CreatePendingMarkers()
    {
        int[] markers = new int[Main.maxPlayers];
        Array.Fill(markers, -1);
        return markers;
    }

    private static object[] CreateStateLocks()
        => Enumerable.Range(0, Main.maxPlayers).Select(_ => new object()).ToArray();
}
