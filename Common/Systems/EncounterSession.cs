using DaybreakDamageTracker.Common.Bosses;
using DaybreakDamageTracker.Common.Data;
using DaybreakDamageTracker.Common.NPCs;

namespace DaybreakDamageTracker.Common.Systems;

internal sealed class EncounterSession
{
    private readonly Dictionary<string, SessionBoss> _bosses = new(StringComparer.Ordinal);
    private readonly Dictionary<EncounterPlayerKey, MutableConnectionLedger> _connections = [];
    private readonly List<MutableLogicalPlayer> _logicalPlayers = [];
    private readonly HashSet<string> _encounterGroupKeys = new(StringComparer.Ordinal);
    private readonly HashSet<int> _keepAliveNpcTypes = [];
    private readonly HashSet<EncounterPlayerKey> _encounterPlayers = [];
    private bool _continueAfterPartyWipe;

    public EncounterSession(long id, IEnumerable<BossDescriptor> descriptors)
    {
        EncounterId = id;
        State = EncounterState.Armed;
        ArmedTick = (long)Main.GameUpdateCount;
        foreach (BossDescriptor descriptor in descriptors)
            Merge(descriptor);
    }

    public long EncounterId { get; }
    public EncounterState State { get; set; }
    public long ArmedTick { get; }
    public long StartTick { get; set; }
    public long TransitionStartTick { get; set; }
    public long TeamDamage { get; private set; }
    public long BossBodyDamage { get; private set; }
    public long UnattributedDamage { get; private set; }
    public bool PartyWipeObserved { get; private set; }
    public bool LastHadLivingEngagedPlayer { get; private set; }
    public bool ContinueAfterPartyWipe => _continueAfterPartyWipe;
    public bool AllBossesResolved => _bosses.Count > 0 && _bosses.Values.All(value => value.Outcome != EncounterOutcome.Unknown);
    public bool HasResolvedBoss => _bosses.Values.Any(value => value.Outcome != EncounterOutcome.Unknown);
    public int PublicPlayerCount => _logicalPlayers.Count;
    public IReadOnlyCollection<string> BossKeys => _bosses.Keys;
    public IReadOnlyCollection<string> EncounterGroupKeys => _encounterGroupKeys;

    public bool Merge(BossDescriptor descriptor)
    {
        if (_bosses.TryGetValue(descriptor.Key, out SessionBoss? existing))
        {
            // Keep the opening portrait stable, but accept lifecycle roles revealed by a
            // later phase using the same Boss Checklist/override key.
            existing.Descriptor.BodyNpcTypes.UnionWith(descriptor.BodyNpcTypes);
            existing.Descriptor.KeepAliveNpcTypes.UnionWith(descriptor.KeepAliveNpcTypes);
            existing.Descriptor.FinalNpcTypes.UnionWith(descriptor.FinalNpcTypes);
            existing.Descriptor.ExcludedBodyNpcTypes.UnionWith(descriptor.ExcludedBodyNpcTypes);
            existing.InvalidateAssociatedTypes();
            _keepAliveNpcTypes.UnionWith(descriptor.KeepAliveNpcTypes);
            _encounterGroupKeys.Add(GroupKey(descriptor));
            _continueAfterPartyWipe |= descriptor.ContinueAfterPartyWipe;
            if (ShouldUseAutoFinal(descriptor, existing.DownedAtArm))
                existing.AutoFinalNpcTypes.UnionWith(descriptor.BodyNpcTypes);
            return false;
        }

        BossDescriptor copy = descriptor.Clone();
        bool downedAtArm = SafeDowned(copy.Downed);
        _bosses[copy.Key] = new SessionBoss(copy, downedAtArm);
        _encounterGroupKeys.Add(GroupKey(copy));
        _continueAfterPartyWipe |= copy.ContinueAfterPartyWipe;
        _keepAliveNpcTypes.UnionWith(copy.KeepAliveNpcTypes);
        // On a first clear, Boss Checklist's downed transition is a stronger final-phase signal
        // than a temporary NPC-free gap. Repeated clears cannot observe false->true again, so a
        // known single-body/generic fallback (or an explicit server override) is used there.
        if (ShouldUseAutoFinal(copy, downedAtArm))
            _bosses[copy.Key].AutoFinalNpcTypes.UnionWith(copy.BodyNpcTypes);
        return true;
    }

    public bool IsRelated(BossDescriptor descriptor)
        => _bosses.ContainsKey(descriptor.Key) || _encounterGroupKeys.Contains(GroupKey(descriptor));

    public bool HasActiveParticipantNpc()
    {
        foreach (NPC npc in Main.ActiveNPCs)
        {
            if (_keepAliveNpcTypes.Contains(npc.type))
                return true;
        }
        return false;
    }

    public bool IsEncounterParticipant(NPC npc)
        => npc.active && _keepAliveNpcTypes.Contains(npc.type);

    public TargetSnapshot CaptureTarget(bool eligible, NPC npc)
    {
        int rootNpcType = npc.realLife >= 0 && npc.realLife < Main.maxNPCs
            ? Main.npc[npc.realLife].type
            : -1;
        return new TargetSnapshot(eligible, npc.type, rootNpcType);
    }

    public bool RefreshParticipantWindow()
    {
        bool anyActive = false;
        HashSet<string> victoryGroups = new(StringComparer.Ordinal);
        HashSet<string> reopenedGroups = new(StringComparer.Ordinal);

        foreach (SessionBoss boss in _bosses.Values)
        {
            HashSet<NpcInstanceKey> current = [];
            bool currentHasAutoFinal = false;
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                NPC npc = Main.npc[i];
                if (!npc.active || !boss.Descriptor.KeepAliveNpcTypes.Contains(npc.type))
                    continue;

                current.Add(new NpcInstanceKey(i, DamageTrackingGlobalNpc.GetSpawnSerial(npc)));
                currentHasAutoFinal |= boss.AutoFinalNpcTypes.Contains(npc.type);
            }

            if (current.Count > 0)
            {
                anyActive = true;
                boss.ProvisionalLastParticipantKill = false;
                if (currentHasAutoFinal)
                    boss.PendingAutoFinalKill = false;
                if (boss.Outcome != EncounterOutcome.Unknown)
                    reopenedGroups.Add(GroupKey(boss.Descriptor));
            }
            else if (boss.PreviousKeepAliveInstances.Count > 0)
            {
                // A generic body death counts as victory only when every participant in
                // this boss's own final window was killed. A sibling boss despawning no
                // longer invalidates the killed boss's result.
                boss.ProvisionalLastParticipantKill = boss.PendingAutoFinalKill &&
                    boss.PreviousKeepAliveInstances.All(previous => boss.KilledSinceParticipantScan.Contains(previous));
                boss.PendingAutoFinalKill = false;
            }
            else
            {
                boss.PendingAutoFinalKill = false;
            }

            bool downedTransition = EncounterPolicy.CanUseEntryDownedSignal(
                    boss.Descriptor.Key,
                    GroupKey(boss.Descriptor)) &&
                !boss.DownedAtArm &&
                SafeDowned(boss.Descriptor.Downed);
            if (current.Count == 0 && boss.Outcome == EncounterOutcome.Unknown &&
                (boss.ExplicitFinalKill || boss.ProvisionalLastParticipantKill || downedTransition))
            {
                victoryGroups.Add(GroupKey(boss.Descriptor));
            }

            boss.PreviousKeepAliveInstances = current;
            boss.KilledSinceParticipantScan.Clear();
        }

        foreach (string groupKey in reopenedGroups)
            SetGroupOutcome(groupKey, EncounterOutcome.Unknown);
        foreach (string groupKey in victoryGroups)
            SetGroupOutcome(groupKey, EncounterOutcome.Victory);

        return anyActive;
    }

    public EngagementObservation ObserveEngagedPlayers()
    {
        float activeRangeX = Math.Max(1, NPC.sWidth) * 2.1f;
        float activeRangeY = Math.Max(1, NPC.sHeight) * 2.1f;
        bool sawParticipantNpc = false;
        bool livingPlayerEngaged = false;

        for (int npcIndex = 0; npcIndex < Main.maxNPCs; npcIndex++)
        {
            NPC npc = Main.npc[npcIndex];
            if (!npc.active || !_keepAliveNpcTypes.Contains(npc.type))
                continue;

            sawParticipantNpc = true;

            if (npc.HasValidTarget && Main.player[npc.target].active)
            {
                Player target = Main.player[npc.target];
                _encounterPlayers.Add(PlayerConnectionRegistry.GetCurrent(npc.target));
                livingPlayerEngaged |= !target.dead && !target.ghost;
            }

            for (int playerIndex = 0; playerIndex < Main.maxPlayers; playerIndex++)
            {
                Player player = Main.player[playerIndex];
                if (!player.active ||
                    Math.Abs(player.Center.X - npc.Center.X) > activeRangeX ||
                    Math.Abs(player.Center.Y - npc.Center.Y) > activeRangeY)
                {
                    continue;
                }

                _encounterPlayers.Add(PlayerConnectionRegistry.GetCurrent(playerIndex));
                livingPlayerEngaged |= !player.dead && !player.ghost;
            }
        }

        if (sawParticipantNpc)
            LastHadLivingEngagedPlayer = livingPlayerEngaged;
        return new EngagementObservation(sawParticipantNpc, livingPlayerEngaged);
    }

    public void MarkNpcKilled(NPC killed)
    {
        NpcInstanceKey instance = new(killed.whoAmI, DamageTrackingGlobalNpc.GetSpawnSerial(killed));
        foreach (SessionBoss boss in _bosses.Values)
        {
            if (boss.Descriptor.FinalNpcTypes.Contains(killed.type))
                boss.ExplicitFinalKill = true;
            if (boss.Descriptor.KeepAliveNpcTypes.Contains(killed.type))
                boss.KilledSinceParticipantScan.Add(instance);
            if (boss.AutoFinalNpcTypes.Contains(killed.type))
                boss.PendingAutoFinalKill = true;
        }
    }

    public void MarkNpcSpawned(NPC spawned)
    {
        NpcInstanceKey instance = new(spawned.whoAmI, DamageTrackingGlobalNpc.GetSpawnSerial(spawned));
        foreach (SessionBoss boss in _bosses.Values)
        {
            if (boss.Descriptor.KeepAliveNpcTypes.Contains(spawned.type))
                boss.PreviousKeepAliveInstances.Add(instance);
        }
    }

    public EncounterOutcome? ResolveInactiveBoundary(
        bool foreignEncounterStarted,
        bool genericFallbackExpired)
    {
        if (AllBossesResolved)
            return AggregateOutcome();

        bool noActivePlayers = HasNoActiveEncounterPlayers();
        bool closesMixedEncounter = HasResolvedBoss;
        foreach (IGrouping<string, SessionBoss> group in _bosses.Values
                     .Where(value => value.Outcome == EncounterOutcome.Unknown)
                     .GroupBy(value => GroupKey(value.Descriptor), StringComparer.Ordinal)
                     .ToList())
        {
            bool continueAfterWipe = group.Any(value => value.Descriptor.ContinueAfterPartyWipe);
            bool allowSiblingBoundary = group.All(value =>
                value.Descriptor.FinishOnBodyDeathWhenNoParticipants &&
                EncounterPolicy.CanUseEntryDownedSignal(
                    value.Descriptor.Key,
                    GroupKey(value.Descriptor)));
            EncounterOutcome? outcome = EncounterPolicy.ResolveUnresolvedBoss(new UnresolvedBossFacts(
                PartyWipeObserved,
                continueAfterWipe,
                noActivePlayers,
                LastHadLivingEngagedPlayer,
                foreignEncounterStarted,
                genericFallbackExpired,
                closesMixedEncounter,
                allowSiblingBoundary));
            if (outcome.HasValue)
                SetGroupOutcome(group.Key, outcome.Value);
        }

        return AllBossesResolved ? AggregateOutcome() : null;
    }

    public void CompleteUnresolved(EncounterOutcome outcome)
    {
        foreach (string groupKey in _bosses.Values
                     .Where(value => value.Outcome == EncounterOutcome.Unknown)
                     .Select(value => GroupKey(value.Descriptor))
                     .Distinct(StringComparer.Ordinal)
                     .ToList())
        {
            SetGroupOutcome(groupKey, outcome);
        }
    }

    public EncounterOutcome AggregateOutcome()
        => EncounterPolicy.AggregateOutcomes(_bosses.Values.Select(value => value.Outcome));

    public bool RecordPlayerDamage(int playerIndex, TargetSnapshot target, int damage)
    {
        if (damage <= 0)
            return false;

        EncounterPlayerKey key = PlayerConnectionRegistry.GetCurrent(playerIndex);
        _encounterPlayers.Add(key);
        bool reconnected = false;
        if (!_connections.TryGetValue(key, out MutableConnectionLedger? connection))
        {
            string name = playerIndex >= 0 && playerIndex < Main.maxPlayers
                ? Main.player[playerIndex].name
                : $"Player {playerIndex}";
            List<LogicalPlayerCandidate> reconnectCandidates = _logicalPlayers
                .Select(candidate => new LogicalPlayerCandidate(
                    candidate.Name,
                    candidate.Connections.Any(PlayerConnectionRegistry.IsCurrent)))
                .ToList();
            int reconnectIndex = PlayerIdentityPolicy.FindReconnectCandidateIndex(reconnectCandidates, name);
            MutableLogicalPlayer? logical = reconnectIndex >= 0 ? _logicalPlayers[reconnectIndex] : null;
            if (logical is null)
            {
                logical = new MutableLogicalPlayer(_logicalPlayers.Count + 1, name);
                _logicalPlayers.Add(logical);
            }
            else
            {
                reconnected = true;
            }

            logical.Connections.Add(key);
            connection = new MutableConnectionLedger(key, logical);
            _connections[key] = connection;
        }

        connection.Damage = SaturatingAdd(connection.Damage, damage);
        connection.Logical.Damage = SaturatingAdd(connection.Logical.Damage, damage);
        TeamDamage = SaturatingAdd(TeamDamage, damage);
        SessionBoss? attributedBoss = FindUniqueAssociatedBoss(target);
        if (attributedBoss is not null)
        {
            attributedBoss.StartTick = attributedBoss.StartTick == 0
                ? (long)Main.GameUpdateCount
                : attributedBoss.StartTick;
            attributedBoss.TeamDamage = SaturatingAdd(attributedBoss.TeamDamage, damage);
            attributedBoss.LogicalDamage.TryGetValue(connection.Logical.Id, out long logicalDamage);
            attributedBoss.LogicalDamage[connection.Logical.Id] = SaturatingAdd(logicalDamage, damage);
            attributedBoss.ConnectionDamage.TryGetValue(key, out long connectionDamage);
            attributedBoss.ConnectionDamage[key] = SaturatingAdd(connectionDamage, damage);
            if (IsBossBodyFor(attributedBoss, target))
            {
                attributedBoss.BodyDamage = SaturatingAdd(attributedBoss.BodyDamage, damage);
                BossBodyDamage = SaturatingAdd(BossBodyDamage, damage);
            }
        }
        return reconnected;
    }

    public void RecordUnattributedDamage(TargetSnapshot target, int damage)
    {
        if (damage <= 0)
            return;

        UnattributedDamage = SaturatingAdd(UnattributedDamage, damage);
        SessionBoss? attributedBoss = FindUniqueAssociatedBoss(target);
        if (attributedBoss is not null)
            attributedBoss.UnattributedDamage = SaturatingAdd(attributedBoss.UnattributedDamage, damage);
    }

    public bool AllEncounterPlayersDead()
    {
        bool anyActiveParticipant = false;
        foreach (EncounterPlayerKey key in _encounterPlayers)
        {
            if (!PlayerConnectionRegistry.IsCurrent(key))
                continue;

            anyActiveParticipant = true;
            Player player = Main.player[key.PlayerIndex];
            if (!player.dead && !player.ghost)
                return false;
        }
        return anyActiveParticipant;
    }

    public bool HasNoActiveEncounterPlayers()
        => _encounterPlayers.Count > 0 && !_encounterPlayers.Any(PlayerConnectionRegistry.IsCurrent);

    public bool ObservePartyWipe()
    {
        if (PartyWipeObserved || !AllEncounterPlayersDead())
            return false;
        PartyWipeObserved = true;
        return true;
    }

    public bool TryRecoverPartyWipe(bool livingPlayerEngaged)
    {
        if (!PartyWipeObserved || !_continueAfterPartyWipe || !livingPlayerEngaged)
            return false;
        PartyWipeObserved = false;
        return true;
    }

    public List<PublicResultSnapshot> DrainResolvedResults(long endTick)
    {
        List<SessionBoss> resolved = _bosses.Values
            .Where(value => value.Outcome != EncounterOutcome.Unknown && !value.ResultPublished)
            .ToList();
        bool encounterComplete = AllBossesResolved;
        List<PublicResultSnapshot> results = [];
        for (int index = 0; index < resolved.Count; index++)
        {
            SessionBoss boss = resolved[index];
            boss.ResultPublished = true;
            long startTick = boss.StartTick > 0
                ? boss.StartTick
                : StartTick > 0 ? StartTick : ArmedTick;
            results.Add(new PublicResultSnapshot
            {
                EncounterId = EncounterId,
                EncounterComplete = encounterComplete && index == resolved.Count - 1,
                Outcome = boss.Outcome,
                DurationTicks = Math.Max(0, endTick - startTick),
                TeamDamage = boss.TeamDamage,
                BossBodyDamage = boss.BodyDamage,
                UnattributedDamage = boss.UnattributedDamage,
                Bosses = [CreatePresentation(boss)],
                Players = _logicalPlayers
                    .Where(value => boss.LogicalDamage.ContainsKey(value.Id))
                    .OrderByDescending(value => boss.LogicalDamage[value.Id])
                    .Select(value => new PlayerResultRow
                    {
                        PlayerName = value.Name,
                        Damage = boss.LogicalDamage[value.Id]
                    }).ToList(),
                RecipientDamage = new Dictionary<EncounterPlayerKey, long>(boss.ConnectionDamage)
            });
        }
        return results;
    }

    public List<BossPresentation> BuildBossPresentation()
        => _bosses.Values
            .Where(value => value.Outcome == EncounterOutcome.Unknown)
            .Select(CreatePresentation)
            .ToList();

    private void SetGroupOutcome(string groupKey, EncounterOutcome outcome)
    {
        foreach (SessionBoss boss in _bosses.Values.Where(value =>
                     string.Equals(GroupKey(value.Descriptor), groupKey, StringComparison.Ordinal)))
        {
            boss.Outcome = outcome;
            if (outcome == EncounterOutcome.Unknown)
            {
                boss.ResultPublished = false;
                boss.ExplicitFinalKill = false;
                boss.ProvisionalLastParticipantKill = false;
                boss.PendingAutoFinalKill = false;
            }
        }
    }

    private static bool IsBossBodyFor(SessionBoss boss, TargetSnapshot target)
        => BossAttributionPolicy.IsBody(
            boss.Descriptor.BodyNpcTypes,
            boss.Descriptor.ExcludedBodyNpcTypes,
            target.NpcType,
            target.RootNpcType);

    private SessionBoss? FindUniqueAssociatedBoss(TargetSnapshot target)
        => BossAttributionPolicy.FindUnique(
            _bosses.Values.Where(boss => boss.Outcome == EncounterOutcome.Unknown),
            target.NpcType,
            target.RootNpcType,
            boss => boss.AssociatedNpcTypes);

    private static BossPresentation CreatePresentation(SessionBoss boss) => new()
    {
        Key = boss.Descriptor.Key,
        NameLocalizationKey = boss.Descriptor.DisplayNameLocalizationKey,
        NameNpcType = boss.Descriptor.DisplayNameNpcType,
        Name = boss.Descriptor.DisplayName,
        PortraitNpcType = boss.Descriptor.PortraitNpcType,
        Outcome = boss.Outcome,
        BodyDamage = boss.BodyDamage,
        BodyNpcTypes = boss.Descriptor.BodyNpcTypes.Order().ToList(),
        AssociatedNpcTypes = boss.Descriptor.BodyNpcTypes
            .Concat(boss.Descriptor.ExcludedBodyNpcTypes)
            .Distinct()
            .Order()
            .ToList()
    };

    private static bool SafeDowned(Func<bool>? downed)
    {
        try
        {
            return downed?.Invoke() ?? false;
        }
        catch
        {
            return false;
        }
    }

    private static long SaturatingAdd(long current, int addition)
        => current > long.MaxValue - addition ? long.MaxValue : current + addition;

    private static string GroupKey(BossDescriptor descriptor)
        => string.IsNullOrWhiteSpace(descriptor.EncounterGroupKey)
            ? descriptor.Key
            : descriptor.EncounterGroupKey;

    private static bool ShouldUseAutoFinal(BossDescriptor descriptor, bool downedAtArm)
        => EncounterPolicy.CanUseAutomaticBodyFinal(
            descriptor.Key,
            GroupKey(descriptor),
            descriptor.FinishOnBodyDeathWhenNoParticipants,
            descriptor.ContinueAfterPartyWipe,
            descriptor.Downed is not null,
            downedAtArm);

    private sealed class SessionBoss(BossDescriptor descriptor, bool downedAtArm)
    {
        public BossDescriptor Descriptor { get; } = descriptor;
        public bool DownedAtArm { get; } = downedAtArm;
        public HashSet<int> AutoFinalNpcTypes { get; } = [];
        public HashSet<NpcInstanceKey> PreviousKeepAliveInstances { get; set; } = [];
        public HashSet<NpcInstanceKey> KilledSinceParticipantScan { get; } = [];
        public EncounterOutcome Outcome { get; set; }
        public long StartTick { get; set; }
        public long TeamDamage { get; set; }
        public long BodyDamage { get; set; }
        public long UnattributedDamage { get; set; }
        public Dictionary<int, long> LogicalDamage { get; } = [];
        public Dictionary<EncounterPlayerKey, long> ConnectionDamage { get; } = [];
        public IReadOnlySet<int> AssociatedNpcTypes
        {
            get
            {
                if (_associatedNpcTypes is null)
                    _associatedNpcTypes = [.. Descriptor.BodyNpcTypes, .. Descriptor.ExcludedBodyNpcTypes];
                return _associatedNpcTypes;
            }
        }
        public bool ResultPublished { get; set; }
        public bool PendingAutoFinalKill { get; set; }
        public bool ExplicitFinalKill { get; set; }
        public bool ProvisionalLastParticipantKill { get; set; }
        private HashSet<int>? _associatedNpcTypes;

        public void InvalidateAssociatedTypes() => _associatedNpcTypes = null;
    }

    private sealed class MutableLogicalPlayer(int id, string name)
    {
        public int Id { get; } = id;
        public string Name { get; } = name;
        public HashSet<EncounterPlayerKey> Connections { get; } = [];
        public long Damage { get; set; }
    }

    private sealed class MutableConnectionLedger(EncounterPlayerKey key, MutableLogicalPlayer logical)
    {
        public EncounterPlayerKey Key { get; } = key;
        public MutableLogicalPlayer Logical { get; } = logical;
        public long Damage { get; set; }
    }

    private readonly record struct NpcInstanceKey(int Slot, long SpawnSerial);
}

internal readonly record struct EngagementObservation(bool SawParticipantNpc, bool LivingPlayerEngaged);
