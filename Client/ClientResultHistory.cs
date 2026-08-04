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
        EntriesInternal.RemoveAll(existing => existing.Public.EncounterId == entry.Public.EncounterId);
        EntriesInternal.Insert(0, entry);
        Trim(capacity);
    }

    public static void ReplacePublicHistory(IEnumerable<ResultDelivery> deliveries, int capacity)
    {
        Dictionary<long, SourceTreeSnapshot> privateById = EntriesInternal.ToDictionary(
            entry => entry.Public.EncounterId,
            entry => entry.PrivateSources);

        EntriesInternal.Clear();
        foreach (ResultDelivery delivery in deliveries)
        {
            PublicResultSnapshot snapshot = delivery.Result;
            EntriesInternal.Add(new ClientHistoryEntry
            {
                Public = snapshot,
                PrivateSources = privateById.TryGetValue(snapshot.EncounterId, out SourceTreeSnapshot? sources)
                    ? sources
                    : SourceTreeSnapshot.Unavailable(delivery.OwnDamage)
            });
        }
        Trim(capacity);
    }

    public static ClientHistoryEntry? GetOneBased(int index)
        => index >= 1 && index <= EntriesInternal.Count ? EntriesInternal[index - 1] : null;

    public static void Trim(int capacity)
    {
        capacity = Math.Clamp(capacity, 1, 10);
        if (EntriesInternal.Count > capacity)
            EntriesInternal.RemoveRange(capacity, EntriesInternal.Count - capacity);
    }
}
