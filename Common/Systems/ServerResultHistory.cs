using DaybreakDamageTracker.Common.Data;

namespace DaybreakDamageTracker.Common.Systems;

internal static class ServerResultHistory
{
    private static readonly List<PublicResultSnapshot> Results = [];

    public static IReadOnlyList<PublicResultSnapshot> All => Results;

    public static void Add(PublicResultSnapshot result, int capacity)
    {
        Results.Insert(0, result);
        Trim(capacity);
    }

    public static void Trim(int capacity)
    {
        capacity = Math.Clamp(capacity, 1, PresentationSettings.MaximumHistoryCount);
        if (Results.Count > capacity)
            Results.RemoveRange(capacity, Results.Count - capacity);
    }

    public static void Clear() => Results.Clear();
}
