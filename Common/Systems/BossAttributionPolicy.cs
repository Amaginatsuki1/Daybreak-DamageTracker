namespace DaybreakDamageTracker.Common.Systems;

internal static class BossAttributionPolicy
{
    public static T? FindUnique<T>(
        IEnumerable<T> candidates,
        int npcType,
        int rootNpcType,
        Func<T, IReadOnlySet<int>> associatedTypes,
        string exactOwnerKey = "",
        Func<T, string>? candidateKey = null)
        where T : class
    {
        if (!string.IsNullOrWhiteSpace(exactOwnerKey) && candidateKey is not null)
        {
            return candidates.FirstOrDefault(candidate =>
                candidateKey(candidate).Equals(exactOwnerKey, StringComparison.Ordinal));
        }

        T? match = null;
        foreach (T candidate in candidates)
        {
            if (!Matches(associatedTypes(candidate), npcType, rootNpcType))
                continue;
            if (match is not null)
                return null;
            match = candidate;
        }
        return match;
    }

    public static bool Matches(IReadOnlySet<int> types, int npcType, int rootNpcType)
        => types.Contains(npcType) || (rootNpcType >= 0 && types.Contains(rootNpcType));

    public static bool IsBody(
        IReadOnlySet<int> bodyTypes,
        IReadOnlySet<int> excludedTypes,
        int npcType,
        int rootNpcType,
        string exactSpawnOwnerKey = "")
        => string.IsNullOrWhiteSpace(exactSpawnOwnerKey) &&
           Matches(bodyTypes, npcType, rootNpcType) &&
           !Matches(excludedTypes, npcType, rootNpcType);
}
