using DaybreakDamageTracker.Common.Data;
using DaybreakDamageTracker.Common.Config;

namespace DaybreakDamageTracker.Common.Systems;

internal static class ServerResultHistory
{
    private static readonly List<ServerResultHistoryEntry> Results = [];
    private static readonly object Sync = new();

    // State requests can be handled off the world-update thread. Return a
    // materialized snapshot so settlement/trim cannot invalidate enumeration.
    public static IReadOnlyList<ServerResultHistoryEntry> All
    {
        get
        {
            lock (Sync)
                return Results.ToArray();
        }
    }

    public static void Add(PublicResultSnapshot result, int capacity)
    {
        ResultRecipients frozenRecipients = ResultRecipientPolicy.FailClosed(
            ServerConfigService.Current.Recipients);
        lock (Sync)
        {
            Results.Insert(0, new ServerResultHistoryEntry(result, frozenRecipients));
            TrimUnsafe(capacity);
        }
    }

    public static void Trim(int capacity)
    {
        lock (Sync)
            TrimUnsafe(capacity);
    }

    public static void Clear()
    {
        lock (Sync)
            Results.Clear();
    }

    private static void TrimUnsafe(int capacity)
    {
        capacity = Math.Clamp(capacity, 1, PresentationSettings.MaximumHistoryCount);
        if (Results.Count > capacity)
            Results.RemoveRange(capacity, Results.Count - capacity);
    }
}

internal readonly record struct ServerResultHistoryEntry(
    PublicResultSnapshot Result,
    ResultRecipients FrozenRecipients);
