using DaybreakDamageTracker.Common.Data;
using DaybreakDamageTracker.Common.Networking;

namespace DaybreakDamageTracker.Client;

internal sealed class ClientHistoryEntry
{
    public required PublicResultSnapshot Public { get; init; }
    public required SourceTreeSnapshot PrivateSources { get; init; }
}

internal static class ClientResultHistory
{
    private static readonly List<ClientHistoryEntry> EntriesInternal = [];

    public static IReadOnlyList<ClientHistoryEntry> Entries => EntriesInternal;
    public static ClientHistoryEntry? Latest => EntriesInternal.FirstOrDefault();

    public static void Reset() => EntriesInternal.Clear();

    public static void Add(ClientHistoryEntry entry, int capacity)
    {
        ResultKey key = KeyOf(entry.Public);
        EntriesInternal.RemoveAll(existing => KeyOf(existing.Public) == key);
        EntriesInternal.Insert(0, entry);
        Trim(capacity);
    }

    public static void ReplacePublicHistory(IEnumerable<ResultDelivery> deliveries, int capacity)
    {
        Dictionary<ResultKey, SourceTreeSnapshot> privateById = EntriesInternal.ToDictionary(
            entry => KeyOf(entry.Public),
            entry => entry.PrivateSources);

        EntriesInternal.Clear();
        foreach (ResultDelivery delivery in deliveries)
        {
            PublicResultSnapshot snapshot = delivery.Result;
            EntriesInternal.Add(new ClientHistoryEntry
            {
                Public = snapshot,
                PrivateSources = privateById.TryGetValue(KeyOf(snapshot), out SourceTreeSnapshot? sources)
                    ? sources
                    : SourceTreeSnapshot.Unavailable(delivery.OwnDamage)
            });
        }
        Trim(capacity);
    }

    public static ClientHistoryEntry? GetZeroBased(int index)
        => index >= 0 && index < EntriesInternal.Count ? EntriesInternal[index] : null;

    public static void Trim(int capacity)
    {
        capacity = Math.Clamp(capacity, 1, 10);
        if (EntriesInternal.Count > capacity)
            EntriesInternal.RemoveRange(capacity, EntriesInternal.Count - capacity);
    }

    private static ResultKey KeyOf(PublicResultSnapshot result)
        => new(
            result.EncounterId,
            result.BossOccurrence,
            result.Bosses.FirstOrDefault()?.Key ?? string.Empty);

    private readonly record struct ResultKey(long EncounterId, int BossOccurrence, string BossKey);
}
