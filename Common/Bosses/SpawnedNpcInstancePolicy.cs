namespace DaybreakDamageTracker.Common.Bosses;

internal static class SpawnedNpcInstancePolicy
{
    public static bool IsOwnershipCurrent<T>(
        int ownerOccurrence,
        int currentOccurrence,
        T originInstance,
        IReadOnlySet<T> observedOriginInstances)
        where T : notnull
        => ownerOccurrence == currentOccurrence &&
           observedOriginInstances.Contains(originInstance);

    public static bool ShouldSuppressNestedDescriptor<T>(
        IEnumerable<T> activeManagedInstances,
        IReadOnlySet<T> nestedInstances)
        where T : notnull
    {
        T[] active = activeManagedInstances.Distinct().ToArray();
        return active.Length > 0 && active.All(nestedInstances.Contains);
    }
}
