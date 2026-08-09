namespace DaybreakDamageTracker.Common.Systems;

internal enum PrivateSourceMatchKind : byte
{
    Pending,
    Unique,
    Ambiguous
}

internal readonly record struct PrivateSourceMatch(PrivateSourceMatchKind Kind, string BossKey);

internal static class PrivateSourceAttributionPolicy
{
    public static PrivateSourceMatch Resolve(IEnumerable<string> matchingBossKeys)
    {
        string[] matches = matchingBossKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .ToArray();
        return matches.Length switch
        {
            0 => new PrivateSourceMatch(PrivateSourceMatchKind.Pending, string.Empty),
            1 => new PrivateSourceMatch(PrivateSourceMatchKind.Unique, matches[0]),
            _ => new PrivateSourceMatch(PrivateSourceMatchKind.Ambiguous, string.Empty)
        };
    }
}
