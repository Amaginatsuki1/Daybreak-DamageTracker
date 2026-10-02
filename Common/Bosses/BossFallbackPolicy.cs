namespace DaybreakDamageTracker.Common.Bosses;

internal static class BossFallbackPolicy
{
    public static List<string> FindOwnerKeys<T>(
        IEnumerable<T> descriptors,
        int controllerNpcType,
        Func<T, string> keySelector,
        Func<T, int, bool> managesNpcType)
    {
        List<string> ownerKeys = [];
        foreach (T descriptor in descriptors)
        {
            if (!managesNpcType(descriptor, controllerNpcType))
                continue;

            string key = keySelector(descriptor);
            if (!ownerKeys.Contains(key, StringComparer.Ordinal))
                ownerKeys.Add(key);
        }

        return ownerKeys;
    }

    public static BossFallbackRole InheritDependentRoles(
        bool controllerIsBody,
        bool controllerIsKeepAlive,
        bool controllerIsExcluded)
    {
        BossFallbackRole roles = BossFallbackRole.None;
        if (controllerIsExcluded)
            roles |= BossFallbackRole.ExcludedBody;
        else if (controllerIsBody)
            roles |= BossFallbackRole.Body;
        if (controllerIsKeepAlive)
            roles |= BossFallbackRole.KeepAlive;
        return roles;
    }

    public static string FallbackKey(int controllerNpcType)
        => $"npc:{controllerNpcType}";

    public static bool HasRealLifeDependency(int npcSlot, int realLifeSlot, int maxNpcs)
        => realLifeSlot >= 0 && realLifeSlot < maxNpcs && realLifeSlot != npcSlot;

    public static bool CanUseRealLifeController(
        bool controllerActive,
        long controllerSpawnSerial,
        long dependentSpawnSerial)
        // A real controller necessarily predates the dependent. If the referenced
        // slot now has a newer serial, Terraria has reused it for another NPC and the
        // stale realLife index must not turn that replacement into a Boss root.
        => controllerActive &&
           controllerSpawnSerial > 0 &&
           dependentSpawnSerial > 0 &&
           controllerSpawnSerial < dependentSpawnSerial;
}

[Flags]
internal enum BossFallbackRole
{
    None = 0,
    Body = 1,
    KeepAlive = 2,
    ExcludedBody = 4
}
