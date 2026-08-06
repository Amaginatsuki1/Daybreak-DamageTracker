using DaybreakDamageTracker.Common.Systems;

namespace DaybreakDamageTracker.Common.Data;

public enum EncounterOutcome : byte
{
    Unknown,
    Victory,
    Defeat,
    Escaped,
    Manual,
    Mixed
}

public enum EncounterState : byte
{
    Idle,
    Armed,
    Fighting,
    Transitioning
}

public sealed class BossPresentation
{
    public string Key { get; set; } = string.Empty;
    // The localization key is resolved on the receiving client. Name remains a
    // server-provided fallback for mods that expose only a literal display name.
    public string NameLocalizationKey { get; set; } = string.Empty;
    public int NameNpcType { get; set; } = -1;
    public string Name { get; set; } = string.Empty;
    public int PortraitNpcType { get; set; }
    public EncounterOutcome Outcome { get; set; }
    public long BodyDamage { get; set; }
    // These bounded type sets let the owning client keep its private source tree
    // separated per boss without revealing any other player's detailed sources.
    public List<int> BodyNpcTypes { get; set; } = [];
    public List<int> AssociatedNpcTypes { get; set; } = [];
}

public sealed class PlayerResultRow
{
    public string PlayerName { get; set; } = string.Empty;
    public long Damage { get; set; }
}

public sealed class PublicResultSnapshot
{
    public long EncounterId { get; set; }
    public bool EncounterComplete { get; set; }
    public EncounterOutcome Outcome { get; set; }
    public long DurationTicks { get; set; }
    public long TeamDamage { get; set; }
    public long BossBodyDamage { get; set; }
    public long UnattributedDamage { get; set; }
    public List<BossPresentation> Bosses { get; set; } = [];
    public List<PlayerResultRow> Players { get; set; } = [];
    // Exact connection totals never leave the server. They let a reconnect contribute
    // to one public logical-player row without reconciling the new client's private
    // source tree against damage recorded by an earlier connection.
    internal Dictionary<EncounterPlayerKey, long> RecipientDamage { get; set; } = [];
}

public sealed class PresentationSettings
{
    public const int MaximumHistoryCount = 10;

    public bool AutoShow { get; set; } = true;
    public float AutoShowSeconds { get; set; } = 12f;
    public float FadeSeconds { get; set; } = 1.5f;
    public bool ShowBossHeader { get; set; } = true;
    public bool ShowTeamTotal { get; set; } = true;
    public bool ShowBossBodyDamage { get; set; } = true;
    public bool ShowDuration { get; set; } = true;
    public bool ShowRanking { get; set; } = true;
    public bool ShowPersonalBreakdown { get; set; } = true;
    public bool ShowUnattributedDamage { get; set; } = true;
    public int RankingRows { get; set; } = 10;
    public int SourceRootRows { get; set; } = 6;
    public int SourceLeafRows { get; set; } = 3;
    public int HistoryCount { get; set; } = MaximumHistoryCount;
    public bool ChatSummary { get; set; } = true;

    public PresentationSettings SanitizedClone() => new()
    {
        AutoShow = AutoShow,
        AutoShowSeconds = Math.Clamp(AutoShowSeconds, 1f, 120f),
        FadeSeconds = Math.Min(Math.Clamp(FadeSeconds, 0f, 10f), Math.Clamp(AutoShowSeconds, 1f, 120f)),
        ShowBossHeader = ShowBossHeader,
        ShowTeamTotal = ShowTeamTotal,
        ShowBossBodyDamage = ShowBossBodyDamage,
        ShowDuration = ShowDuration,
        ShowRanking = ShowRanking,
        ShowPersonalBreakdown = ShowPersonalBreakdown,
        ShowUnattributedDamage = ShowUnattributedDamage,
        RankingRows = Math.Clamp(RankingRows, 1, 64),
        SourceRootRows = Math.Clamp(SourceRootRows, 1, 32),
        SourceLeafRows = Math.Clamp(SourceLeafRows, 1, 16),
        // The server retains the maximum public window. Each client independently
        // chooses how many of those results to keep and expose through /dt commands.
        HistoryCount = MaximumHistoryCount,
        ChatSummary = ChatSummary
    };
}
