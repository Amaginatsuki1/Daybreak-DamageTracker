using DaybreakDamageTracker.Client.UI;
using DaybreakDamageTracker.Common.Bosses;
using DaybreakDamageTracker.Common.Data;
using DaybreakDamageTracker.Common.Networking;
using DaybreakDamageTracker.Common.Systems;

namespace DaybreakDamageTracker.Client;

internal static class ClientRuntime
{
    private static readonly Dictionary<PrivateSourceBucketKey, MutableSourceTree> CurrentSources = [];
    private static readonly Dictionary<TargetSourceKey, MutableSourceTree> SuspendedSources = [];
    private static readonly List<BossPresentation> ActiveBosses = [];
    private static bool _encounterPrepared;
    private static bool _acceptingPrivateDamage;
    private static int _historyBatchId;
    private static ResultDelivery?[] _pendingHistory = [];
    private static DamageResultHudSystem? _hud;
    private static bool _autoShowResults = true;
    private static int _historyCapacity = 4;
    private static int _autoHideSeconds = 12;
    private static float _panelBackgroundOpacity = 0.94f;

    public static PresentationSettings Presentation { get; private set; } = new();
    public static long ActiveEncounterId { get; private set; }
    public static bool AutoShowResults => _autoShowResults;
    public static int HistoryCapacity => _historyCapacity;
    public static int AutoHideSeconds => _autoHideSeconds;
    public static float PanelBackgroundOpacity => _panelBackgroundOpacity;

    public static void ResetForWorld()
    {
        Presentation = new PresentationSettings();
        ActiveEncounterId = 0;
        _encounterPrepared = false;
        _acceptingPrivateDamage = false;
        _historyBatchId = 0;
        _pendingHistory = [];
        CurrentSources.Clear();
        SuspendedSources.Clear();
        ActiveBosses.Clear();
        ClientResultHistory.Reset();
        _hud?.Hide();
    }

    public static void ApplyPresentation(PresentationSettings presentation)
    {
        Presentation = presentation.SanitizedClone();
        ClientResultHistory.Trim(HistoryCapacity);
        _hud?.RefreshPresentation();
    }

    public static void ApplyLocalPreferences(ClientConfig config)
    {
        int previousCapacity = _historyCapacity;
        _autoShowResults = config.AutoShowResults;
        _historyCapacity = Math.Clamp(config.HistoryCount, 1, 10);
        _autoHideSeconds = Math.Clamp(config.AutoHideSeconds, 1, 120);
        _panelBackgroundOpacity = Math.Clamp(config.PanelBackgroundOpacity, 0f, 1f);

        ClientResultHistory.Trim(HistoryCapacity);
        _hud?.ApplyLocalPreferences();

        // ConfigManager invokes OnChanged while the config is still being registered.
        // Only a fully initialized client HUD may request additional server history.
        if (_hud is not null &&
            Main.netMode == NetmodeID.MultiplayerClient &&
            _historyCapacity > previousCapacity)
        {
            DamageNetwork.RequestServerState();
        }
    }

    public static void RegisterHud(DamageResultHudSystem hud)
    {
        _hud = hud;
        ClientResultHistory.Trim(HistoryCapacity);
        hud.ApplyLocalPreferences();
    }

    public static void UnregisterHud(DamageResultHudSystem hud)
    {
        if (ReferenceEquals(_hud, hud))
            _hud = null;
    }

    public static void OnEncounterArmed(long encounterId, IReadOnlyList<BossPresentation> bosses)
    {
        if (!_encounterPrepared || (ActiveEncounterId != 0 && ActiveEncounterId != encounterId))
            CurrentSources.Clear();

        ActiveEncounterId = encounterId;
        _encounterPrepared = true;
        _acceptingPrivateDamage = true;
        ActiveBosses.Clear();
        ActiveBosses.AddRange(bosses);
        MergeSuspendedSources();
        ResolvePendingSources();
    }

    public static void OnEncounterStarted(long encounterId)
    {
        if (!_encounterPrepared || (ActiveEncounterId != 0 && ActiveEncounterId != encounterId))
            CurrentSources.Clear();

        ActiveEncounterId = encounterId;
        _encounterPrepared = true;
        _acceptingPrivateDamage = true;
        MergeSuspendedSources();
        ResolvePendingSources();
    }

    public static void OnEncounterSuspended(long encounterId)
    {
        if (ActiveEncounterId != encounterId && ActiveEncounterId != 0)
            return;

        ActiveEncounterId = encounterId;
        _encounterPrepared = true;
        _acceptingPrivateDamage = false;
        SuspendedSources.Clear();
    }

    public static void OnEncounterCancelled(long encounterId)
    {
        if (ActiveEncounterId != encounterId && ActiveEncounterId != 0)
            return;

        bool carryToNextEncounter = BossCatalog.HasActiveBossClientSide() &&
            (CurrentSources.Count > 0 || SuspendedSources.Count > 0);
        if (carryToNextEncounter)
            MergeCurrentSourcesIntoSuspended();
        else
            SuspendedSources.Clear();

        ActiveEncounterId = 0;
        _encounterPrepared = false;
        _acceptingPrivateDamage = false;
        CurrentSources.Clear();
        ActiveBosses.Clear();
    }

    public static void OnEncounterCompleted(long encounterId)
    {
        if (ActiveEncounterId != 0 && ActiveEncounterId != encounterId)
            return;

        bool carryToNextEncounter = BossCatalog.HasActiveBossClientSide() &&
            (CurrentSources.Count > 0 || SuspendedSources.Count > 0);
        if (carryToNextEncounter)
            MergeCurrentSourcesIntoSuspended();
        else
        {
            CurrentSources.Clear();
            SuspendedSources.Clear();
        }
        ActiveEncounterId = 0;
        _encounterPrepared = false;
        _acceptingPrivateDamage = false;
        ActiveBosses.Clear();
    }

    public static void RecordPrivateDamage(NPC target, DamageRootKey root, DamageLeafKey leaf, int damage)
        => RecordPrivateDamage(
            new TargetSnapshot(
                TargetClassifier.IsEligibleHostile(target),
                target.type,
                target.realLife >= 0 && target.realLife < Main.maxNPCs
                    ? Main.npc[target.realLife].type
                    : -1),
            root,
            leaf,
            damage);

    public static void RecordPrivateDamage(
        TargetSnapshot target,
        DamageRootKey root,
        DamageLeafKey leaf,
        int damage)
    {
        if (!target.Eligible || damage <= 0)
            return;

        if (!_encounterPrepared)
        {
            if (!BossCatalog.HasActiveBossClientSide())
                return;

            ActiveEncounterId = 0;
            _encounterPrepared = true;
            _acceptingPrivateDamage = true;
            CurrentSources.Clear();
        }

        if (!_acceptingPrivateDamage)
        {
            // During an NPC-free transition, the first hit can occur before the server's
            // resume/new-encounter packet reaches this client. Keep that source separate
            // until packet order tells us which encounter owns it.
            if (BossCatalog.HasActiveBossClientSide())
                SourceBucket(SuspendedSources, TargetKey(target)).Add(root, leaf, damage);
            return;
        }

        TargetSourceKey targetKey = TargetKey(target);
        PrivateSourceMatch match = PrivateSourceAttributionPolicy.Resolve(MatchingBossKeys(targetKey));
        if (match.Kind == PrivateSourceMatchKind.Ambiguous)
        {
            // The server deliberately excludes ambiguous targets from every Boss.
            // Dropping the private atom here prevents it from becoming falsely
            // unique after one sibling result is removed from ActiveBosses.
            return;
        }

        string bossKey = match.Kind == PrivateSourceMatchKind.Unique ? match.BossKey : string.Empty;
        SourceBucket(CurrentSources, new PrivateSourceBucketKey(targetKey, bossKey))
            .Add(root, leaf, damage);
    }

    public static void OnPublicResult(PublicResultSnapshot result, long? localTotal)
    {
        bool matchesCurrent = _encounterPrepared && (ActiveEncounterId == 0 || ActiveEncounterId == result.EncounterId);
        BossPresentation? boss = result.Bosses.FirstOrDefault();
        SourceTreeSnapshot privateTree = matchesCurrent && localTotal.HasValue && boss is not null
            ? ExtractSourcesForBoss(boss).Reconcile(localTotal.Value)
            : SourceTreeSnapshot.Unavailable(localTotal);

        ClientHistoryEntry entry = new()
        {
            Public = result,
            PrivateSources = privateTree
        };
        ClientResultHistory.Add(entry, HistoryCapacity);

        if (boss is not null)
            ActiveBosses.RemoveAll(value => value.Key.Equals(boss.Key, StringComparison.Ordinal));
        if (Presentation.ChatSummary)
            Main.NewText(FormatHistoryMap(), new Color(125, 220, 255));

        DamageResultHudSystem? hud = _hud;
        if (hud is null)
            return;

        if (hud.IsVisible)
            hud.Append(entry);
        else if (AutoShowResults)
            hud.Show(entry, automatic: true);
    }

    public static void OnHistorySyncBegin(int batchId, int count)
    {
        _historyBatchId = batchId;
        _pendingHistory = new ResultDelivery?[count];
        if (count == 0)
            ClientResultHistory.ReplacePublicHistory([], HistoryCapacity);
    }

    public static void OnHistorySyncEntry(int batchId, int index, ResultDelivery delivery)
    {
        if (batchId != _historyBatchId || index < 0 || index >= _pendingHistory.Length)
            return;

        _pendingHistory[index] = delivery;
        if (_pendingHistory.Any(value => !value.HasValue))
            return;

        ResultDelivery[] complete = _pendingHistory.Select(value => value!.Value).ToArray();
        _pendingHistory = [];
        ClientResultHistory.ReplacePublicHistory(complete, HistoryCapacity);
    }

    public static string FormatCompactSummary(PublicResultSnapshot result, int historyIndex = 0)
    {
        string boss = result.Bosses.Count == 0
            ? Text("UI.UnknownBoss", "Unknown boss")
            : ResolveBossName(result.Bosses[0]);
        string outcome = Text($"Outcome.{result.Outcome}", result.Outcome.ToString());
        string command = historyIndex == 0 ? "/dt" : $"/dt{historyIndex}";
        return Text("Chat.SavedResult", "{0} | {1} | {2}", boss, outcome, command);
    }

    public static string FormatHistoryMap()
        => "[DT] " + string.Join("  ·  ", ClientResultHistory.Entries
            .Take(HistoryCapacity)
            .Select((entry, index) => FormatCompactSummary(entry.Public, index)));

    public static string FormatDuration(long ticks)
    {
        TimeSpan span = TimeSpan.FromSeconds(Math.Max(0, ticks) / 60d);
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{span.Minutes:00}:{span.Seconds:00}";
    }

    public static string ResolveBossName(BossPresentation boss)
    {
        if (!string.IsNullOrWhiteSpace(boss.NameLocalizationKey) && Language.Exists(boss.NameLocalizationKey))
        {
            string localized = Language.GetTextValue(boss.NameLocalizationKey);
            if (!string.IsNullOrWhiteSpace(localized))
                return localized;
        }

        if (boss.NameNpcType > NPCID.None && boss.NameNpcType < NPCLoader.NPCCount)
        {
            string localized = Lang.GetNPCNameValue(boss.NameNpcType);
            if (!string.IsNullOrWhiteSpace(localized))
                return localized;
        }

        return string.IsNullOrWhiteSpace(boss.Name)
            ? Text("UI.UnknownBoss", "Unknown boss")
            : boss.Name;
    }

    internal static string Text(string suffix, string fallback, params object[] args)
    {
        string key = $"Mods.DaybreakDamageTracker.{suffix}";
        string value = Language.Exists(key) ? Language.GetTextValue(key) : fallback;
        return args.Length == 0 ? value : string.Format(CultureInfo.CurrentCulture, value, args);
    }

    private static MutableSourceTree ExtractSourcesForBoss(BossPresentation resultBoss)
    {
        ResolvePendingSources();
        MutableSourceTree extracted = new();
        foreach ((PrivateSourceBucketKey bucket, MutableSourceTree tree) in CurrentSources.ToArray())
        {
            if (!bucket.BossKey.Equals(resultBoss.Key, StringComparison.Ordinal))
                continue;

            extracted.MergeFrom(tree);
            CurrentSources.Remove(bucket);
        }
        return extracted;
    }

    private static bool Matches(BossPresentation boss, TargetSourceKey target)
        => boss.AssociatedNpcTypes.Contains(target.NpcType) ||
           (target.RootNpcType >= 0 && boss.AssociatedNpcTypes.Contains(target.RootNpcType));

    private static TargetSourceKey TargetKey(NPC target)
    {
        int rootNpcType = target.realLife >= 0 && target.realLife < Main.maxNPCs
            ? Main.npc[target.realLife].type
            : -1;
        return new TargetSourceKey(target.type, rootNpcType);
    }

    private static TargetSourceKey TargetKey(TargetSnapshot target)
        => new(target.NpcType, target.RootNpcType);

    private static List<string> MatchingBossKeys(TargetSourceKey target)
        => ActiveBosses
            .Where(candidate => Matches(candidate, target))
            .Select(candidate => candidate.Key)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static void ResolvePendingSources()
    {
        foreach ((PrivateSourceBucketKey bucket, MutableSourceTree tree) in CurrentSources
                     .Where(pair => string.IsNullOrEmpty(pair.Key.BossKey))
                     .ToArray())
        {
            PrivateSourceMatch match = PrivateSourceAttributionPolicy.Resolve(MatchingBossKeys(bucket.Target));
            if (match.Kind == PrivateSourceMatchKind.Pending)
                continue;

            CurrentSources.Remove(bucket);
            if (match.Kind == PrivateSourceMatchKind.Unique)
            {
                SourceBucket(CurrentSources, new PrivateSourceBucketKey(bucket.Target, match.BossKey))
                    .MergeFrom(tree);
            }
        }
    }

    private static void MergeSuspendedSources()
    {
        foreach ((TargetSourceKey target, MutableSourceTree tree) in SuspendedSources.ToArray())
        {
            SourceBucket(CurrentSources, new PrivateSourceBucketKey(target, string.Empty))
                .MergeFrom(tree);
        }
        SuspendedSources.Clear();
    }

    private static void MergeCurrentSourcesIntoSuspended()
    {
        foreach ((PrivateSourceBucketKey bucket, MutableSourceTree tree) in CurrentSources.ToArray())
            SourceBucket(SuspendedSources, bucket.Target).MergeFrom(tree);
        CurrentSources.Clear();
    }

    private static MutableSourceTree SourceBucket<TKey>(
        Dictionary<TKey, MutableSourceTree> buckets,
        TKey key)
        where TKey : notnull
    {
        if (!buckets.TryGetValue(key, out MutableSourceTree? tree))
            buckets[key] = tree = new MutableSourceTree();
        return tree;
    }

    private readonly record struct TargetSourceKey(int NpcType, int RootNpcType);
    private readonly record struct PrivateSourceBucketKey(TargetSourceKey Target, string BossKey);
}
