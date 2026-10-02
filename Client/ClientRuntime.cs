using DaybreakDamageTracker.Client.UI;
using DaybreakDamageTracker.Common.Bosses;
using DaybreakDamageTracker.Common.Data;
using DaybreakDamageTracker.Common.Networking;
using DaybreakDamageTracker.Common.NPCs;
using DaybreakDamageTracker.Common.Systems;

namespace DaybreakDamageTracker.Client;

internal static class ClientRuntime
{
    private static readonly Dictionary<PrivateSourceBucketKey, MutableSourceTree> CurrentSources = [];
    private static readonly Dictionary<TargetSourceKey, MutableSourceTree> SuspendedSources = [];
    private static readonly Dictionary<(NpcInstanceKey Instance, int NpcType), HashSet<string>> ExactAssociationKeys = [];
    private static readonly HashSet<(NpcInstanceKey Instance, int NpcType, string BossKey)> ResolvedExactAssociations = [];
    private static readonly Dictionary<int, long> LatestNpcSpawnSerials = [];
    private static readonly List<BossPresentation> ActiveBosses = [];
    private static readonly ChatSummaryBatcher ChatSummary = new();
    private static bool _encounterPrepared;
    private static bool _acceptingPrivateDamage;
    private static int _historyBatchId;
    private static ResultDelivery?[] _pendingHistory = [];
    private static DamageResultHudSystem? _hud;
    private static bool _autoShowResults = true;
    private static int _historyCapacity = 4;
    private static int _autoHideSeconds = 12;
    private static float _panelBackgroundOpacity = ClientConfig.DefaultPanelBackgroundOpacity;
    private static IReadOnlyList<ResultPanelSection> _panelSectionOrder = ResultPanelSectionOrderPolicy.CreateDefault();
    private static PresentationSettings _effectivePresentation = new();

    public static PresentationSettings Presentation { get; private set; } = new();
    // The server exclusively owns which result sections and row counts are shown.
    // Clients only control automatic behavior, local history, and background opacity.
    public static PresentationSettings EffectivePresentation => _effectivePresentation;
    public static long ActiveEncounterId { get; private set; }
    public static bool AutoShowResults => _autoShowResults;
    public static int HistoryCapacity => _historyCapacity;
    public static int AutoHideSeconds => _autoHideSeconds;
    public static float PanelBackgroundOpacity => _panelBackgroundOpacity;
    public static IReadOnlyList<ResultPanelSection> PanelSectionOrder => _panelSectionOrder;

    public static void ResetForWorld()
    {
        Presentation = new PresentationSettings();
        RecomputeEffectivePresentation();
        ActiveEncounterId = 0;
        _encounterPrepared = false;
        _acceptingPrivateDamage = false;
        _historyBatchId = 0;
        _pendingHistory = [];
        ChatSummary.Reset();
        CurrentSources.Clear();
        SuspendedSources.Clear();
        ExactAssociationKeys.Clear();
        ResolvedExactAssociations.Clear();
        LatestNpcSpawnSerials.Clear();
        ActiveBosses.Clear();
        ClientResultHistory.Reset();
        _hud?.Hide();
    }

    public static void ApplyPresentation(PresentationSettings presentation)
    {
        Presentation = presentation.SanitizedClone();
        RecomputeEffectivePresentation();
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
        _panelSectionOrder = ResultPanelSectionOrderPolicy.Sanitize(config.PanelSectionOrder);
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

    private static void RecomputeEffectivePresentation()
        => _effectivePresentation = Presentation.SanitizedClone();

    public static void UnregisterHud(DamageResultHudSystem hud)
    {
        if (ReferenceEquals(_hud, hud))
            _hud = null;
    }

    public static void OnEncounterArmed(long encounterId, IReadOnlyList<BossPresentation> bosses)
    {
        bool newEncounter = !_encounterPrepared || (ActiveEncounterId != 0 && ActiveEncounterId != encounterId);
        if (newEncounter)
        {
            CurrentSources.Clear();
            ResolvedExactAssociations.Clear();
        }

        ActiveEncounterId = encounterId;
        _encounterPrepared = true;
        _acceptingPrivateDamage = true;
        ActiveBosses.Clear();
        ActiveBosses.AddRange(bosses);
        RebuildExactAssociations();
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
        ExactAssociationKeys.Clear();
        ResolvedExactAssociations.Clear();
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
        ExactAssociationKeys.Clear();
        ResolvedExactAssociations.Clear();
    }

    public static void RecordPrivateDamage(NPC target, DamageRootKey root, DamageLeafKey leaf, int damage)
        => RecordPrivateDamage(
            new TargetSnapshot(
                TargetClassifier.IsEligibleHostile(target),
                target.type,
                target.realLife >= 0 && target.realLife < Main.maxNPCs
                    ? Main.npc[target.realLife].type
                    : -1,
                new NpcInstanceKey(target.whoAmI, DamageTrackingGlobalNpc.GetSpawnSerial(target))),
            root,
            leaf,
            damage);

    public static void RecordPrivateDamage(
        TargetSnapshot target,
        DamageRootKey root,
        DamageLeafKey leaf,
        int damage)
    {
        if (!target.Eligible || damage <= 0 || !target.Instance.IsValid)
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
        PrivateSourceMatch match;
        if (!string.IsNullOrEmpty(target.ExactOwnerBossKey))
        {
            // Server-confirmed DoT carries its owner from capture time. Read neither
            // today's slot occupant nor today's type association to reassign it.
            if (!ActiveBosses.Any(value => value.Key.Equals(target.ExactOwnerBossKey, StringComparison.Ordinal)))
                return;
            match = new PrivateSourceMatch(PrivateSourceMatchKind.Unique, target.ExactOwnerBossKey);
        }
        else
        {
            if (IsObsolete(target.Instance))
                return;
            match = PrivateSourceAttributionPolicy.Resolve(MatchingBossKeys(targetKey));
        }
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
        {
            foreach (AssociatedNpcInstance instance in boss.AssociatedNpcInstances)
                ResolvedExactAssociations.Add((new NpcInstanceKey(instance.NpcIndex, instance.SpawnSerial), instance.NpcType, boss.Key));
            ActiveBosses.RemoveAll(value => value.Key.Equals(boss.Key, StringComparison.Ordinal));
            RebuildExactAssociations();
        }
        if (_effectivePresentation.ChatSummary)
            ChatSummary.Queue((long)Main.GameUpdateCount);

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

    public static void FlushPendingChatSummary()
    {
        if (!ChatSummary.TryConsume((long)Main.GameUpdateCount))
            return;
        if (_effectivePresentation.ChatSummary && ClientResultHistory.Entries.Count > 0)
            Main.NewText(FormatHistoryMap(), new Color(125, 220, 255));
    }

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
        return new TargetSourceKey(
            new NpcInstanceKey(target.whoAmI, DamageTrackingGlobalNpc.GetSpawnSerial(target)),
            target.type,
            rootNpcType,
            string.Empty);
    }

    private static TargetSourceKey TargetKey(TargetSnapshot target)
        => new(target.Instance, target.NpcType, target.RootNpcType, target.ExactOwnerBossKey);

    private static List<string> MatchingBossKeys(TargetSourceKey target)
    {
        if (!string.IsNullOrEmpty(target.ExactOwnerBossKey))
        {
            return ActiveBosses
                .Where(value => value.Key.Equals(target.ExactOwnerBossKey, StringComparison.Ordinal))
                .Select(value => value.Key)
                .ToList();
        }
        if (!target.Instance.IsValid || IsObsolete(target.Instance))
            return [];

        if (ExactAssociationKeys.TryGetValue((target.Instance, target.NpcType), out HashSet<string>? exact))
        {
            return exact.ToList();
        }

        // A type-only match is safe only while the captured instance is still the
        // same slot occupant. Late unresolved atoms must never acquire a replacement
        // NPC's body/root membership merely because its type happens to match.
        if (target.Instance.Slot >= Main.maxNPCs ||
            DamageTrackingGlobalNpc.GetSpawnSerial(Main.npc[target.Instance.Slot]) != target.Instance.SpawnSerial)
            return [];

        return ActiveBosses
            .Where(candidate => Matches(candidate, target))
            .Select(candidate => candidate.Key)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    public static void OnNpcInstanceReset(int npcIndex)
    {
        if (npcIndex < 0)
            return;
        RemoveUnresolvedSourcesForSlot(npcIndex, keepSpawnSerial: 0);
        // Do not clear Armed metadata here: it may have arrived before SyncNPC.
        // ReceiveExtraAI will identify the replacement and retain its exact mapping.
    }

    public static void OnNpcInstanceAvailable(NpcInstanceKey instance)
    {
        if (!instance.IsValid)
            return;
        LatestNpcSpawnSerials[instance.Slot] = instance.SpawnSerial;
        RemoveUnresolvedSourcesForSlot(instance.Slot, instance.SpawnSerial);
        foreach ((NpcInstanceKey Instance, int NpcType) key in ExactAssociationKeys.Keys
                     .Where(key => key.Instance.Slot == instance.Slot && key.Instance.SpawnSerial < instance.SpawnSerial)
                     .ToArray())
        {
            ExactAssociationKeys.Remove(key);
        }
        ResolvedExactAssociations.RemoveWhere(value =>
            value.Instance.Slot == instance.Slot && value.Instance.SpawnSerial < instance.SpawnSerial);
    }

    private static void RemoveUnresolvedSourcesForSlot(int npcIndex, long keepSpawnSerial)
    {
        // Already attributed damage remains attached to the original Boss until its
        // result is finalized. Only observations still awaiting ownership are stale.
        foreach (PrivateSourceBucketKey key in CurrentSources.Keys.Where(key =>
                     string.IsNullOrEmpty(key.BossKey) &&
                     string.IsNullOrEmpty(key.Target.ExactOwnerBossKey) &&
                     key.Target.Instance.Slot == npcIndex &&
                     key.Target.Instance.SpawnSerial != keepSpawnSerial).ToArray())
            CurrentSources.Remove(key);
        foreach (TargetSourceKey key in SuspendedSources.Keys.Where(key =>
                     string.IsNullOrEmpty(key.ExactOwnerBossKey) &&
                     key.Instance.Slot == npcIndex && key.Instance.SpawnSerial != keepSpawnSerial).ToArray())
            SuspendedSources.Remove(key);
    }

    private static bool IsObsolete(NpcInstanceKey instance)
        => LatestNpcSpawnSerials.TryGetValue(instance.Slot, out long latest) && latest > instance.SpawnSerial;

    private static void RebuildExactAssociations()
    {
        ExactAssociationKeys.Clear();
        foreach (BossPresentation boss in ActiveBosses)
        {
            foreach (AssociatedNpcInstance instance in boss.AssociatedNpcInstances)
            {
                NpcInstanceKey identity = new(instance.NpcIndex, instance.SpawnSerial);
                if (!identity.IsValid || IsObsolete(identity))
                    continue;
                (NpcInstanceKey Instance, int NpcType) key = (identity, instance.NpcType);
                if (!ExactAssociationKeys.TryGetValue(key, out HashSet<string>? owners))
                    ExactAssociationKeys[key] = owners = new HashSet<string>(StringComparer.Ordinal);
                owners.Add(boss.Key);
            }
        }
        foreach ((NpcInstanceKey instance, int npcType, string bossKey) in ResolvedExactAssociations)
        {
            if (IsObsolete(instance))
                continue;
            (NpcInstanceKey Instance, int NpcType) key = (instance, npcType);
            if (!ExactAssociationKeys.TryGetValue(key, out HashSet<string>? owners))
                ExactAssociationKeys[key] = owners = new HashSet<string>(StringComparer.Ordinal);
            owners.Add(bossKey);
        }
    }

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

    private readonly record struct TargetSourceKey(
        NpcInstanceKey Instance,
        int NpcType,
        int RootNpcType,
        string ExactOwnerBossKey);
    private readonly record struct PrivateSourceBucketKey(TargetSourceKey Target, string BossKey);
}
