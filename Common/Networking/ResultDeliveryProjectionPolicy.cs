namespace DaybreakDamageTracker.Common.Networking;

internal readonly record struct ResultDeliveryProjection(
    bool IncludeDuration,
    bool IncludeTeamDamage,
    bool IncludeBossBodyDamage,
    bool IncludeUnattributedDamage,
    bool IncludeRanking,
    bool IncludeFinalBlow);

internal static class ResultDeliveryProjectionPolicy
{
    public static ResultDeliveryProjection Create(
        bool showTeamTotal,
        bool showBossBodyDamage,
        bool showDuration,
        bool showRanking,
        bool showUnattributedDamage,
        bool showFinalBlow)
        => new(
            IncludeDuration: showDuration,
            // Body and ranking percentages use team damage as their denominator.
            IncludeTeamDamage: showTeamTotal || showBossBodyDamage || showRanking,
            IncludeBossBodyDamage: showBossBodyDamage,
            IncludeUnattributedDamage: showUnattributedDamage,
            IncludeRanking: showRanking,
            IncludeFinalBlow: showFinalBlow);
}

internal static class RankingProjectionPolicy
{
    public static int GetVisiblePlayerCount(
        bool includeRanking,
        int configuredRows,
        int availableRows,
        int packetRowLimit)
    {
        if (!includeRanking)
            return 0;

        return Math.Min(
            Math.Clamp(configuredRows, 1, 64),
            Math.Min(Math.Max(0, availableRows), Math.Max(0, packetRowLimit)));
    }
}

internal static class ModPacketBudget
{
    // ModPacket owns two length bytes, one Terraria message byte, and one or two
    // mod-ID bytes. Reserve the two-byte form plus our packet-type byte.
    public const int MaximumPacketBytes = ushort.MaxValue;
    public const int WorstCaseFrameworkHeaderBytes = 5;
    public const int PacketTypeBytes = 1;
    public const int MaximumPayloadBytes =
        MaximumPacketBytes - WorstCaseFrameworkHeaderBytes - PacketTypeBytes;

    public static bool FitsCompletePacket(long byteCount)
        => byteCount >= 0 && byteCount <= MaximumPacketBytes;
}

internal static class StateRequestRetryPolicy
{
    public static int GetRetryAfterTicks(long nowTick, long lastAcceptedTick, int cooldownTicks)
    {
        if (cooldownTicks <= 0)
            return 0;

        long elapsed = nowTick - lastAcceptedTick;
        if (elapsed >= cooldownTicks)
            return 0;
        if (elapsed < 0)
            return cooldownTicks;
        return Math.Clamp((int)(cooldownTicks - elapsed), 1, cooldownTicks);
    }
}
