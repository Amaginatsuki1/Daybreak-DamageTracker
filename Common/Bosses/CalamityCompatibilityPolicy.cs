namespace DaybreakDamageTracker.Common.Bosses;

internal static class CalamityCompatibilityPolicy
{
    public const string DesertScourgeKey = "CalamityMod DesertScourge";
    public const string AquaticScourgeKey = "CalamityMod AquaticScourge";
    public const string AstrumDeusKey = "CalamityMod AstrumDeus";

    public static long BossRushQueryRetryDelayTicks(int consecutiveFailures)
    {
        int retryShift = Math.Min(3, Math.Max(0, consecutiveFailures - 1));
        return Math.Min(600L, 60L << retryShift);
    }

    public static bool ShouldGroupZenithScourgeTransition(
        bool zenithWorld,
        IEnumerable<string> currentEncounterBossKeys,
        string activeBossKey,
        bool hasExplicitServerOverride,
        bool hasVerifiedParentSpawn)
        => zenithWorld &&
           !hasExplicitServerOverride &&
           hasVerifiedParentSpawn &&
           activeBossKey.Equals(AquaticScourgeKey, StringComparison.Ordinal) &&
           currentEncounterBossKeys.Contains(DesertScourgeKey, StringComparer.Ordinal);

    // Calamity Boss Rush advances as soon as any split Astrum Deus head dies. The
    // surviving head is then removed without a matching OnKill, which is deliberately
    // incompatible with the ordinary conservative multi-controller boundary. Accept
    // this upstream progression rule only after the split roots were actually observed.
    public static bool IsAstrumDeusBossRushProgressionFinal(
        bool bossRushActive,
        string bossKey,
        string? killedModName,
        string? killedNpcName,
        bool hasExplicitServerOverride,
        bool observedMultipleControllers,
        bool killedLifecycleController)
    {
        bool isAstrumDeusHead = string.Equals(killedModName, "CalamityMod", StringComparison.Ordinal) &&
            string.Equals(killedNpcName, "AstrumDeusHead", StringComparison.Ordinal);
        bool isAstrumDeus = bossKey.Equals(AstrumDeusKey, StringComparison.Ordinal) || isAstrumDeusHead;
        return bossRushActive &&
            isAstrumDeus &&
            isAstrumDeusHead &&
            !hasExplicitServerOverride &&
            observedMultipleControllers &&
            killedLifecycleController;
    }
}
