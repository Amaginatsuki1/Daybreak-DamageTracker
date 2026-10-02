namespace DaybreakDamageTracker.Common.Bosses;

internal readonly record struct SpawnedNpcAttribution(string OwnerKey, string? NestedBossKey);

internal static class SpawnedNpcAttributionPolicy
{
    public static SpawnedNpcAttribution? Resolve(
        IEnumerable<string> originOwnerKeys,
        IEnumerable<string> targetOwnerKeys,
        IEnumerable<string> explicitlyConfiguredTargetKeys)
    {
        List<string> origins = originOwnerKeys
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .ToList();
        if (origins.Count != 1)
            return null;

        string ownerKey = origins[0];
        List<string> nested = targetOwnerKeys
            .Where(value => !string.IsNullOrWhiteSpace(value) &&
                            !value.Equals(ownerKey, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .ToList();
        if (nested.Count > 1)
            return null;

        if (nested.Count == 1 && explicitlyConfiguredTargetKeys.Contains(nested[0], StringComparer.Ordinal))
            return null;

        return new SpawnedNpcAttribution(ownerKey, nested.Count == 1 ? nested[0] : null);
    }
}
