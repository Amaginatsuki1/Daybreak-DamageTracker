namespace DaybreakDamageTracker.Common.Systems;

internal static class BossAttributionPolicy
{
    public static T? FindUnique<T>(
        IEnumerable<T> candidates,
        int npcType,
        int rootNpcType,
        Func<T, IReadOnlySet<int>> associatedTypes)
        where T : class
    {
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
        int rootNpcType)
        => Matches(bodyTypes, npcType, rootNpcType) &&
           !Matches(excludedTypes, npcType, rootNpcType);
}
