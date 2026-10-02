using DaybreakDamageTracker.Common.Bosses;
using DaybreakDamageTracker.Common.Data;
using DaybreakDamageTracker.Common.NPCs;

namespace DaybreakDamageTracker.Common.Systems;

internal sealed class EncounterSession
{
    private readonly Dictionary<string, SessionBoss> _bosses = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _bossOccurrenceCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<EncounterPlayerKey, MutableConnectionLedger> _connections = [];
    private readonly List<MutableLogicalPlayer> _logicalPlayers = [];
    private readonly Dictionary<string, EncounterGroupState> _groupStates = new(StringComparer.Ordinal);
    private readonly HashSet<string> _encounterGroupKeys = new(StringComparer.Ordinal);
    private readonly HashSet<int> _keepAliveNpcTypes = [];
    private readonly Dictionary<NpcInstanceKey, SpawnOriginObservation> _spawnOrigins = [];
    private readonly Dictionary<NpcInstanceKey, SpawnedNpcOwnership> _spawnedNpcOwnership = [];
    private readonly HashSet<(string OwnerKey, string TargetKey)> _verifiedSpawnHandoffs = [];
    private bool _spawnedRelationshipsDirty;

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
    public bool AllBossesResolved => _bosses.Count > 0 && _bosses.Values.All(value => value.Outcome != EncounterOutcome.Unknown);
    public bool HasResolvedBoss => _bosses.Values.Any(value => value.Outcome != EncounterOutcome.Unknown);
    public int PublicPlayerCount => _logicalPlayers.Count;
    public IReadOnlyCollection<string> BossKeys => _bosses.Keys;
    public IReadOnlyCollection<string> EncounterGroupKeys => _encounterGroupKeys;

    public EncounterMergeResult Merge(BossDescriptor descriptor)
    {
        if (_bosses.TryGetValue(descriptor.Key, out SessionBoss? existing) &&
            existing.Outcome == EncounterOutcome.Unknown)
        {
            // Keep the opening portrait stable, but accept lifecycle roles revealed by a
            // later phase using the same Boss Checklist/override key.
            int bodyCount = existing.Descriptor.BodyNpcTypes.Count;
            int associatedCount = existing.Descriptor.BodyNpcTypes.Count + existing.Descriptor.ExcludedBodyNpcTypes.Count;
            int controllerCount = existing.Descriptor.ControllerNpcTypes.Count;
            int keepAliveCount = existing.Descriptor.KeepAliveNpcTypes.Count;
            int finalCount = existing.Descriptor.FinalNpcTypes.Count;
            bool forceAutomaticFinalization = existing.Descriptor.ForceAutomaticFinalization;
            bool presentationChanged = false;
            bool groupChanged = false;
            string previousGroupKey = GroupKey(existing.Descriptor);
            existing.Descriptor.BodyNpcTypes.UnionWith(descriptor.BodyNpcTypes);
            existing.Descriptor.ControllerNpcTypes.UnionWith(descriptor.ControllerNpcTypes);
            existing.Descriptor.KeepAliveNpcTypes.UnionWith(descriptor.KeepAliveNpcTypes);
            existing.Descriptor.FinalNpcTypes.UnionWith(descriptor.FinalNpcTypes);
            existing.Descriptor.ExcludedBodyNpcTypes.UnionWith(descriptor.ExcludedBodyNpcTypes);
            existing.Descriptor.BodyNpcTypes.ExceptWith(existing.Descriptor.ExcludedBodyNpcTypes);
            existing.Descriptor.FinishOnBodyDeathWhenNoParticipants |= descriptor.FinishOnBodyDeathWhenNoParticipants;
            existing.Descriptor.ContinueAfterPartyWipe |= descriptor.ContinueAfterPartyWipe;
            existing.Descriptor.ForceAutomaticFinalization |= descriptor.ForceAutomaticFinalization;
            existing.Descriptor.HasExplicitServerOverride |= descriptor.HasExplicitServerOverride;
            existing.Descriptor.AllowGroupedDisappearanceBoundary |= descriptor.AllowGroupedDisappearanceBoundary;
            if (!string.IsNullOrWhiteSpace(descriptor.EncounterGroupKey) &&
                !descriptor.EncounterGroupKey.Equals(existing.Descriptor.EncounterGroupKey, StringComparison.Ordinal))
            {
                existing.Descriptor.EncounterGroupKey = descriptor.EncounterGroupKey;
                presentationChanged = true;
                groupChanged = true;
            }
            presentationChanged |= bodyCount != existing.Descriptor.BodyNpcTypes.Count ||
                                   associatedCount != existing.Descriptor.BodyNpcTypes.Count +
                                       existing.Descriptor.ExcludedBodyNpcTypes.Count;
            if (presentationChanged)
                existing.InvalidateAssociatedTypes();
            if (groupChanged)
            {
                EncounterGroupState previousState = existing.GroupState;
                EncounterGroupState nextState = GetOrCreateGroupState(GroupKey(existing.Descriptor));
                nextState.MergeFrom(previousState);
                existing.GroupState = nextState;
                RemoveGroupStateIfUnused(previousGroupKey, previousState);
                RebuildDescriptorAggregates();
            }
            else
            {
                _keepAliveNpcTypes.UnionWith(descriptor.KeepAliveNpcTypes);
                _encounterGroupKeys.Add(GroupKey(descriptor));
            }
            if (ShouldUseAutoFinal(existing.Descriptor, existing.DownedAtArm))
                existing.AutoFinalControllerNpcTypes.UnionWith(existing.Descriptor.ControllerNpcTypes);

            bool lifecycleChanged = controllerCount != existing.Descriptor.ControllerNpcTypes.Count ||
                                    keepAliveCount != existing.Descriptor.KeepAliveNpcTypes.Count ||
                                    finalCount != existing.Descriptor.FinalNpcTypes.Count ||
                                    forceAutomaticFinalization != existing.Descriptor.ForceAutomaticFinalization;
            if (presentationChanged)
                return EncounterMergeResult.PresentationUpdated;
            return lifecycleChanged ? EncounterMergeResult.LifecycleUpdated : EncounterMergeResult.None;
        }

        BossDescriptor copy = descriptor.Clone();
        bool downedAtArm = SafeDowned(copy.Downed);
        _bossOccurrenceCounts.TryGetValue(copy.Key, out int previousOccurrence);
        int occurrence = previousOccurrence == int.MaxValue ? int.MaxValue : previousOccurrence + 1;
        _bossOccurrenceCounts[copy.Key] = occurrence;
        EncounterGroupState groupState = GetGroupStateForNewBoss(GroupKey(copy));
        _bosses[copy.Key] = new SessionBoss(copy, downedAtArm, occurrence, groupState);
        _encounterGroupKeys.Add(GroupKey(copy));
        _keepAliveNpcTypes.UnionWith(copy.KeepAliveNpcTypes);
        // On a first clear, Boss Checklist's downed transition is a stronger final-phase signal
        // than a temporary NPC-free gap. Repeated clears cannot observe false->true again, so a
        // known single-body/generic fallback (or an explicit server override) is used there.
        if (ShouldUseAutoFinal(copy, downedAtArm))
            _bosses[copy.Key].AutoFinalControllerNpcTypes.UnionWith(copy.ControllerNpcTypes);
        return EncounterMergeResult.BossAdded;
    }

    public bool IsRelated(BossDescriptor descriptor)
        => _bosses.ContainsKey(descriptor.Key) || _encounterGroupKeys.Contains(GroupKey(descriptor));

    public bool HasActiveParticipantNpc()
    {
        foreach (NPC npc in Main.ActiveNPCs)
        {
            if (_keepAliveNpcTypes.Contains(npc.type) || IsActiveNestedDependencyForAnyBoss(npc))
                return true;
        }
        return false;
    }

    public bool IsEncounterParticipant(NPC npc)
        => npc.active && (_keepAliveNpcTypes.Contains(npc.type) ||
            IsActiveNestedDependencyForAnyBoss(npc));

    public bool HasUniqueAssociatedBoss(TargetSnapshot target)
        => FindUniqueAssociatedBoss(target) is not null;

    public string GetUniqueAssociatedBossKey(TargetSnapshot target)
        => FindUniqueAssociatedBoss(target)?.Descriptor.Key ?? string.Empty;

    public TargetSnapshot CaptureTarget(bool eligible, NPC npc)
    {
        bool hasSharedRoot = npc.realLife >= 0 && npc.realLife < Main.maxNPCs;
        NPC root = hasSharedRoot ? Main.npc[npc.realLife] : npc;
        int rootNpcType = hasSharedRoot ? root.type : -1;
        NpcInstanceKey instance = InstanceOf(npc);
        string exactOwnerKey = _spawnedNpcOwnership.TryGetValue(instance, out SpawnedNpcOwnership ownership) &&
            IsOwnershipCurrent(ownership)
            ? ownership.OwnerKey
            : string.Empty;
        return new TargetSnapshot(
            eligible,
            npc.type,
            rootNpcType,
            instance,
            exactOwnerKey,
            InstanceOf(root));
    }

    public bool RefreshSpawnedNpcRelationships(IReadOnlyCollection<BossDescriptor> activeDescriptors)
    {
        bool changed = _spawnedRelationshipsDirty;
        _spawnedRelationshipsDirty = false;
        List<BossDescriptor> candidates = _bosses.Values
            .Select(value => value.Descriptor)
            .Concat(activeDescriptors)
            .ToList();

        foreach (NPC npc in Main.ActiveNPCs)
        {
            NpcInstanceKey targetInstance = InstanceOf(npc);
            foreach (SessionBoss boss in _bosses.Values)
            {
                if (boss.Outcome == EncounterOutcome.Unknown &&
                    boss.Descriptor.KeepAliveNpcTypes.Contains(npc.type))
                {
                    boss.ObservedParticipantInstances.Add(targetInstance);
                    boss.HasActiveParticipants = true;
                    boss.InactiveSinceTick = -1;
                }
            }

            if (!DamageTrackingGlobalNpc.TryGetSpawnOrigin(npc, out NpcInstanceKey originInstance, out int originNpcType))
                continue;

            _spawnOrigins[targetInstance] = new SpawnOriginObservation(
                targetInstance,
                npc.type,
                originInstance,
                originNpcType);
        }

        foreach (SpawnOriginObservation origin in _spawnOrigins.Values.ToArray())
        {
            List<string> originOwners = BossFallbackPolicy.FindOwnerKeys(
                candidates,
                origin.OriginNpcType,
                descriptor => descriptor.Key,
                BossCatalog.DescriptorManagesNpcType);
            List<string> targetOwners = BossFallbackPolicy.FindOwnerKeys(
                candidates,
                origin.TargetNpcType,
                descriptor => descriptor.Key,
                BossCatalog.DescriptorManagesNpcType);
            IEnumerable<string> explicitTargets = targetOwners.Where(key => candidates.Any(descriptor =>
                descriptor.Key.Equals(key, StringComparison.Ordinal) && descriptor.HasExplicitServerOverride));
            SpawnedNpcAttribution? attribution = SpawnedNpcAttributionPolicy.Resolve(
                originOwners,
                targetOwners,
                explicitTargets);
            if (attribution is null)
            {
                changed |= _spawnedNpcOwnership.Remove(origin.TargetInstance);
                continue;
            }

            string ownerKey = attribution.Value.OwnerKey;
            string? nestedKey = attribution.Value.NestedBossKey;
            if (!_bosses.TryGetValue(ownerKey, out SessionBoss? ownerBoss) ||
                ownerBoss.Outcome != EncounterOutcome.Unknown ||
                !ownerBoss.ObservedParticipantInstances.Contains(origin.OriginInstance))
            {
                // A type match alone cannot prove that this target came from the
                // current occurrence. In particular, an add left behind by a resolved
                // Boss must not be reattached when the same key is summoned again.
                changed |= _spawnedNpcOwnership.Remove(origin.TargetInstance);
                continue;
            }

            if (Main.zenithWorld &&
                ownerKey.Equals(CalamityCompatibilityPolicy.DesertScourgeKey, StringComparison.Ordinal) &&
                nestedKey?.Equals(CalamityCompatibilityPolicy.AquaticScourgeKey, StringComparison.Ordinal) == true)
            {
                changed |= _verifiedSpawnHandoffs.Add((ownerKey, nestedKey));
                changed |= _spawnedNpcOwnership.Remove(origin.TargetInstance);
                continue;
            }

            // Types already managed by the same descriptor (for example normal worm
            // segments) use the catalog's body/keep-alive roles. Only new dependents or
            // genuinely nested Boss instances require an exact ownership override.
            if (nestedKey is null && targetOwners.Contains(ownerKey, StringComparer.Ordinal))
            {
                changed |= _spawnedNpcOwnership.Remove(origin.TargetInstance);
                continue;
            }

            SpawnedNpcOwnership next = new(
                origin.TargetNpcType,
                ownerKey,
                nestedKey,
                ownerBoss.Occurrence,
                origin.OriginInstance);
            if (!_spawnedNpcOwnership.TryGetValue(origin.TargetInstance, out SpawnedNpcOwnership previous) ||
                previous != next)
            {
                _spawnedNpcOwnership[origin.TargetInstance] = next;
                changed = true;
            }
        }

        return changed;
    }

    public bool HasVerifiedSpawnHandoff(string ownerKey, string targetKey)
        => _verifiedSpawnHandoffs.Contains((ownerKey, targetKey));

    public bool StageFinalBlowCandidate(
        TargetSnapshot target,
        NPC lethalRoot,
        string playerName,
        FinalBlowKind kind,
        long nowTick)
    {
        if (kind == FinalBlowKind.None || string.IsNullOrEmpty(playerName))
            return false;

        SessionBoss? boss = FindUniqueAssociatedBoss(target);
        if (boss is null || boss.Outcome != EncounterOutcome.Unknown || boss.ResultPublished)
            return false;

        bool struckRoot = target.Instance == target.RootInstance;
        bool struckManagedBody = boss.Descriptor.BodyNpcTypes.Contains(target.NpcType) ||
            boss.Descriptor.FinalNpcTypes.Contains(target.NpcType);
        if (boss.Descriptor.ExcludedBodyNpcTypes.Contains(target.NpcType) ||
            (!struckRoot && !struckManagedBody))
        {
            // An owned add, shield, or other dependent may share realLife with the
            // controller and make that root reach zero. It contributed damage, but it
            // is not reliable evidence that the player delivered the Boss's final blow.
            return false;
        }

        bool explicitFinal = boss.Descriptor.FinalNpcTypes.Contains(lethalRoot.type);
        bool lifecycleController = boss.Descriptor.KeepAliveNpcTypes.Contains(lethalRoot.type) &&
            AutomaticFinalizationPolicy.IsControllerNpc(
                boss.Descriptor.ControllerNpcTypes,
                lethalRoot.type,
                lethalRoot.boss,
                lethalRoot.whoAmI,
                lethalRoot.realLife);
        bool eligibleTerminal = FinalBlowPolicy.IsEligibleTerminal(
            boss.Descriptor.ExcludedBodyNpcTypes.Contains(lethalRoot.type),
            lifecycleController,
            explicitFinal);
        if (!eligibleTerminal)
            return false;

        boss.FinalBlowCandidates.Stage(
            InstanceOf(lethalRoot),
            new FinalBlowAttribution(playerName, kind),
            nowTick);
        return true;
    }

    public void RemoveFullySuppressedDescriptors(List<BossDescriptor> descriptors)
        => descriptors.RemoveAll(IsFullySuppressedNestedDescriptor);

    public bool PruneSyntheticNestedBosses()
    {
        string[] removable = _bosses.Values
            .Where(value => value.Outcome == EncounterOutcome.Unknown &&
                            value.TeamDamage == 0 &&
                            value.StartTick == 0 &&
                            IsFullySuppressedNestedDescriptor(value.Descriptor))
            .Select(value => value.Descriptor.Key)
            .ToArray();
        if (removable.Length == 0)
            return false;

        foreach (string key in removable)
        {
            if (!_bosses.Remove(key, out SessionBoss? removed))
                continue;
            if (_bossOccurrenceCounts.TryGetValue(key, out int count) && count == removed.Occurrence)
            {
                if (count <= 1)
                    _bossOccurrenceCounts.Remove(key);
                else
                    _bossOccurrenceCounts[key] = count - 1;
            }
        }
        RebuildDescriptorAggregates();
        return true;
    }

    public bool RefreshParticipantWindow(long nowTick, int automaticFinalConfirmationTicks)
    {
        bool anyActive = false;
        HashSet<string> victoryGroups = new(StringComparer.Ordinal);

        foreach (SessionBoss boss in _bosses.Values)
        {
            HashSet<NpcInstanceKey> current = [];
            HashSet<NpcInstanceKey> currentControllers = [];
            HashSet<NpcInstanceKey> currentLifecycleControllers = [];
            HashSet<NpcInstanceKey> currentLivingLifecycleControllers = [];
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                NPC npc = Main.npc[i];
                if (!npc.active)
                    continue;

                NpcInstanceKey instance = InstanceOf(npc);
                bool staticallyManaged = boss.Descriptor.KeepAliveNpcTypes.Contains(npc.type);
                bool nestedDependency = IsNestedDependencyForBoss(instance, boss.Descriptor.Key);
                bool suppressedForThisBoss = IsSuppressedNestedInstance(instance, boss.Descriptor.Key);
                if ((!staticallyManaged && !nestedDependency) || suppressedForThisBoss)
                    continue;

                current.Add(instance);
                if (staticallyManaged &&
                    AutomaticFinalizationPolicy.IsControllerNpc(
                        boss.Descriptor.ControllerNpcTypes,
                        npc.type,
                        npc.boss,
                        npc.whoAmI,
                        npc.realLife))
                {
                    currentLifecycleControllers.Add(instance);
                    if (npc.life > 0 && !boss.KilledSinceParticipantScan.Contains(instance))
                        currentLivingLifecycleControllers.Add(instance);
                    // Boss Checklist guarantees only a representative form. Learn other
                    // independently rooted forms (paired bosses and later root phases) from
                    // the authoritative post-AI scan, never from OnSpawn where realLife may
                    // not have been assigned to dependent segments yet.
                    boss.Descriptor.ControllerNpcTypes.Add(npc.type);
                    if (ShouldUseAutoFinal(boss.Descriptor, boss.DownedAtArm))
                        boss.AutoFinalControllerNpcTypes.Add(npc.type);
                }
                if (staticallyManaged && IsAutomaticFinalController(boss, npc))
                    currentControllers.Add(instance);
            }

            if (currentLifecycleControllers.Count >= 2)
                boss.ObservedMultipleLifecycleControllers = true;

            bool hasLiveController = currentLifecycleControllers.Any(instance =>
                !boss.KilledSinceParticipantScan.Contains(instance));
            if (hasLiveController)
            {
                // A live root confirms an active/replacement phase, so dependent segment
                // cleanup from the preceding phase is not an escape boundary. Root loss
                // itself is tracked conservatively inside AutomaticFinalizationTracker.
                boss.ObservedNonKillDisappearance = false;
            }
            else
            {
                boss.ObservedNonKillDisappearance |= boss.PreviousKeepAliveInstances.Any(previous =>
                    !current.Contains(previous) &&
                    !boss.KilledSinceParticipantScan.Contains(previous));
            }

            if (current.Count > 0)
            {
                anyActive = true;
                boss.ProvisionalLastParticipantKill = false;
                boss.InactiveSinceTick = -1;
                boss.ObservedParticipantInstances.UnionWith(current);
            }
            else if (boss.HasActiveParticipants && boss.InactiveSinceTick < 0)
            {
                boss.InactiveSinceTick = nowTick;
            }
            boss.HasActiveParticipants = current.Count > 0;

            AutomaticFinalizationDecision automaticFinal = boss.AutomaticFinalization.Refresh(
                current,
                currentControllers,
                boss.KilledSinceParticipantScan,
                boss.KilledAutomaticFinalControllersSinceParticipantScan,
                nowTick,
                automaticFinalConfirmationTicks,
                boss.SpawnedAutomaticFinalControllerInstancesSinceParticipantScan);
            LogAutomaticFinalizationState(
                boss,
                automaticFinal,
                currentControllers.Count,
                current.Count);
            boss.ProvisionalLastParticipantKill = automaticFinal == AutomaticFinalizationDecision.Victory;
            if (!hasLiveController && boss.AutomaticFinalization.HasUnresolvedControllerDisappearance)
                boss.ObservedNonKillDisappearance = true;

            bool downedTransition = EncounterPolicy.CanUseEntryDownedSignal(
                    boss.Descriptor.Key,
                    GroupKey(boss.Descriptor)) &&
                !boss.DownedAtArm &&
                SafeDowned(boss.Descriptor.Downed);
            bool shouldResolveVictory = boss.Outcome == EncounterOutcome.Unknown &&
                EncounterPolicy.ShouldResolveVictory(
                    current.Count > 0,
                    boss.ExplicitFinalKill,
                    boss.ProvisionalLastParticipantKill,
                    downedTransition);
            if (shouldResolveVictory)
            {
                IReadOnlySet<NpcInstanceKey> finalBlowEvidence = boss.ExplicitFinalKill
                    ? boss.KilledExplicitFinalInstancesSinceParticipantScan
                    : downedTransition
                        ? boss.KilledLifecycleControllersSinceParticipantScan
                        : boss.AutomaticFinalization.LastConfirmedControllerInstances;
                ConfirmFinalBlow(boss, finalBlowEvidence);
                victoryGroups.Add(GroupKey(boss.Descriptor));
            }
            else
            {
                // Any genuinely living root is a surviving sibling, revived controller,
                // or successor phase. A killed root which remains active at life <= 0 for
                // a death animation is deliberately absent from this set.
                boss.FinalBlowCandidates.CancelForLiveControllers(currentLivingLifecycleControllers);
            }

            // An instance that has already fired OnKill is not a future non-kill
            // disappearance merely because a mod leaves it active for a death animation.
            boss.PreviousKeepAliveInstances = current
                .Where(instance => !boss.KilledSinceParticipantScan.Contains(instance))
                .ToHashSet();
            boss.KilledSinceParticipantScan.Clear();
            boss.KilledAutomaticFinalControllersSinceParticipantScan.Clear();
            boss.KilledLifecycleControllersSinceParticipantScan.Clear();
            boss.KilledExplicitFinalInstancesSinceParticipantScan.Clear();
            boss.SpawnedAutomaticFinalControllerInstancesSinceParticipantScan.Clear();
        }

        foreach (string groupKey in victoryGroups)
            SetGroupOutcome(groupKey, EncounterOutcome.Victory);

        return anyActive;
    }

    public void ResolveInactiveSiblingGroups(
        bool anotherGroupActive,
        long nowTick,
        int transitionSyncGraceTicks)
    {
        bool encounterStarted = State != EncounterState.Armed;
        bool closesMixedEncounter = HasResolvedBoss;
        foreach (IGrouping<string, SessionBoss> group in UnresolvedGroups())
        {
            EncounterGroupState state = group.First().GroupState;
            bool noActivePlayers = HasNoActiveGroupPlayers(state);
            bool groupHasActiveParticipant = group.Any(value => value.HasActiveParticipants);
            bool inactiveGraceElapsed = group.All(value =>
                value.InactiveSinceTick >= 0 &&
                nowTick - value.InactiveSinceTick >= Math.Max(0, transitionSyncGraceTicks));
            bool continueAfterWipe = group.Any(value => value.Descriptor.ContinueAfterPartyWipe);
            bool allowDisappearanceBoundary = group.All(value =>
                value.Descriptor.FinishOnBodyDeathWhenNoParticipants &&
                EncounterPolicy.CanUseDisappearanceBoundary(
                    value.Descriptor.Key,
                    GroupKey(value.Descriptor),
                    value.Descriptor.AllowGroupedDisappearanceBoundary));
            bool observedNonKillDisappearance = group.Any(value => value.ObservedNonKillDisappearance);
            EncounterOutcome? outcome = EncounterPolicy.ResolveInactiveSiblingGroup(
                new InactiveSiblingGroupFacts(
                    encounterStarted,
                    anotherGroupActive,
                    groupHasActiveParticipant,
                    inactiveGraceElapsed,
                    group.Any(value => value.AutomaticFinalization.HasCandidate),
                    new UnresolvedBossFacts(
                        state.PartyWipeObserved,
                        continueAfterWipe,
                        noActivePlayers,
                        state.LastHadLivingEngagedPlayer,
                        observedNonKillDisappearance,
                        allowDisappearanceBoundary,
                        ForeignEncounterStarted: false,
                        GenericFallbackExpired: false,
                        closesMixedEncounter,
                        allowDisappearanceBoundary)));
            if (outcome.HasValue)
                SetGroupOutcome(group.Key, outcome.Value);
        }
    }

    public void ObserveEngagedPlayers()
    {
        float activeRangeX = Math.Max(1, NPC.sWidth) * 2.1f;
        float activeRangeY = Math.Max(1, NPC.sHeight) * 2.1f;
        foreach (EncounterGroupState state in _groupStates.Values)
        {
            state.SawParticipantNpcThisScan = false;
            state.LivingPlayerEngagedThisScan = false;
        }

        for (int npcIndex = 0; npcIndex < Main.maxNPCs; npcIndex++)
        {
            NPC npc = Main.npc[npcIndex];
            if (!npc.active ||
                (!_keepAliveNpcTypes.Contains(npc.type) && !IsActiveNestedDependencyForAnyBoss(npc)))
                continue;

            EncounterGroupState? group = FindUniqueParticipantGroup(npc);
            if (group is null)
                continue;

            group.SawParticipantNpcThisScan = true;

            if (npc.HasValidTarget && Main.player[npc.target].active)
            {
                Player target = Main.player[npc.target];
                EncounterPlayerKey targetKey = PlayerConnectionRegistry.GetCurrent(npc.target);
                bool targetNearby = Math.Abs(target.Center.X - npc.Center.X) <= activeRangeX &&
                                    Math.Abs(target.Center.Y - npc.Center.Y) <= activeRangeY;
                // A Boss can retarget a living player in another simultaneous fight
                // across the world after its own party dies. Only a previously known
                // group participant, or a target actually within this group's encounter
                // window, may affect this group's wipe state.
                if (EncounterPolicy.CanAssociateNpcTargetWithGroup(
                        group.Players.Contains(targetKey),
                        targetNearby))
                {
                    group.Players.Add(targetKey);
                    group.LivingPlayerEngagedThisScan |= !target.dead && !target.ghost;
                }
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

                group.Players.Add(PlayerConnectionRegistry.GetCurrent(playerIndex));
                group.LivingPlayerEngagedThisScan |= !player.dead && !player.ghost;
            }
        }

        foreach (EncounterGroupState state in _groupStates.Values)
        {
            if (state.SawParticipantNpcThisScan)
                state.LastHadLivingEngagedPlayer = state.LivingPlayerEngagedThisScan;
        }

    }

    public void MarkNpcKilled(NPC killed)
    {
        NpcInstanceKey instance = new(killed.whoAmI, DamageTrackingGlobalNpc.GetSpawnSerial(killed));
        string? explicitFinalGroup = ResolveUniqueExplicitFinalGroup(killed.type, instance);
        string? bossRushProgressionFinalGroup = ResolveUniqueAstrumDeusBossRushFinalGroup(killed, instance);
        foreach (SessionBoss boss in _bosses.Values)
        {
            if (IsSuppressedNestedInstance(instance, boss.Descriptor.Key))
                continue;

            bool staticallyManaged = boss.Descriptor.KeepAliveNpcTypes.Contains(killed.type);
            bool nestedDependency = IsNestedDependencyForBoss(instance, boss.Descriptor.Key);
            bool explicitConfiguredFinal = explicitFinalGroup is not null &&
                GroupKey(boss.Descriptor).Equals(explicitFinalGroup, StringComparison.Ordinal) &&
                boss.Descriptor.FinalNpcTypes.Contains(killed.type);
            bool astrumDeusBossRushFinal = bossRushProgressionFinalGroup is not null &&
                GroupKey(boss.Descriptor).Equals(bossRushProgressionFinalGroup, StringComparison.Ordinal);
            if (explicitConfiguredFinal || astrumDeusBossRushFinal)
            {
                boss.ExplicitFinalKill = true;
                boss.KilledExplicitFinalInstancesSinceParticipantScan.Add(instance);
            }
            if (staticallyManaged || nestedDependency)
                boss.KilledSinceParticipantScan.Add(instance);
            if (staticallyManaged &&
                AutomaticFinalizationPolicy.IsControllerNpc(
                    boss.Descriptor.ControllerNpcTypes,
                    killed.type,
                    killed.boss,
                    killed.whoAmI,
                    killed.realLife))
            {
                boss.KilledLifecycleControllersSinceParticipantScan.Add(instance);
                boss.Descriptor.ControllerNpcTypes.Add(killed.type);
                if (ShouldUseAutoFinal(boss.Descriptor, boss.DownedAtArm))
                    boss.AutoFinalControllerNpcTypes.Add(killed.type);
            }
            if (IsAutomaticFinalController(boss, killed))
                boss.KilledAutomaticFinalControllersSinceParticipantScan.Add(instance);
        }
    }

    public void MarkNpcSpawned(NPC spawned)
    {
        NpcInstanceKey instance = InstanceOf(spawned);
        foreach (NpcInstanceKey stale in _spawnOrigins.Keys
                     .Concat(_spawnedNpcOwnership.Keys)
                     .Where(value => value.Slot == instance.Slot && value != instance)
                     .Distinct()
                     .ToArray())
        {
            _spawnOrigins.Remove(stale);
            _spawnedRelationshipsDirty |= _spawnedNpcOwnership.Remove(stale);
        }

        if (DamageTrackingGlobalNpc.TryGetSpawnOrigin(
                spawned,
                out NpcInstanceKey originInstance,
                out int originNpcType))
        {
            _spawnOrigins[instance] = new SpawnOriginObservation(
                instance,
                spawned.type,
                originInstance,
                originNpcType);

            SessionBoss[] originOwners = _bosses.Values
                .Where(boss => boss.Outcome == EncounterOutcome.Unknown &&
                               BossCatalog.DescriptorManagesNpcType(boss.Descriptor, originNpcType))
                .Take(2)
                .ToArray();
            if (originOwners.Length == 1)
                originOwners[0].ObservedParticipantInstances.Add(originInstance);
        }

        foreach (SessionBoss boss in _bosses.Values)
        {
            if (IsSuppressedNestedInstance(instance, boss.Descriptor.Key))
                continue;
            if (boss.Descriptor.KeepAliveNpcTypes.Contains(spawned.type) ||
                IsNestedDependencyForBoss(instance, boss.Descriptor.Key))
            {
                boss.PreviousKeepAliveInstances.Add(instance);
                boss.ObservedParticipantInstances.Add(instance);
                boss.HasActiveParticipants = true;
                boss.InactiveSinceTick = -1;
            }
            if (AutomaticFinalizationPolicy.IsKnownControllerType(
                    boss.AutoFinalControllerNpcTypes,
                    spawned.type))
                boss.SpawnedAutomaticFinalControllerInstancesSinceParticipantScan.Add(instance);
        }
    }

    public EncounterOutcome? ResolveInactiveBoundary(
        bool foreignEncounterStarted,
        bool genericFallbackExpired)
    {
        if (AllBossesResolved)
            return AggregateOutcome();

        bool closesMixedEncounter = HasResolvedBoss;
        foreach (IGrouping<string, SessionBoss> group in UnresolvedGroups())
        {
            EncounterGroupState state = group.First().GroupState;
            bool noActivePlayers = HasNoActiveGroupPlayers(state);
            AutomaticFinalizationDecision boundary = AutomaticFinalizationPolicy.ResolveBoundary(
                group.Any(value => value.AutomaticFinalization.HasCandidate),
                foreignEncounterStarted);
            if (boundary != AutomaticFinalizationDecision.None)
            {
                // A different encounter is a sufficient boundary once a real controller
                // OnKill has already been observed. Otherwise keep the short per-boss
                // confirmation window from losing a victory race to wipe/despawn logic.
                if (boundary == AutomaticFinalizationDecision.Victory)
                {
                    foreach (SessionBoss boss in group.Where(value => value.AutomaticFinalization.HasCandidate))
                        ConfirmFinalBlow(boss, boss.AutomaticFinalization.CandidateControllerInstances);
                    SetGroupOutcome(group.Key, EncounterOutcome.Victory);
                }
                continue;
            }

            bool continueAfterWipe = group.Any(value => value.Descriptor.ContinueAfterPartyWipe);
            bool allowDisappearanceBoundary = group.All(value =>
                value.Descriptor.FinishOnBodyDeathWhenNoParticipants &&
                EncounterPolicy.CanUseDisappearanceBoundary(
                    value.Descriptor.Key,
                    GroupKey(value.Descriptor),
                    value.Descriptor.AllowGroupedDisappearanceBoundary));
            bool observedNonKillDisappearance = group.Any(value => value.ObservedNonKillDisappearance);
            EncounterOutcome? outcome = EncounterPolicy.ResolveUnresolvedBoss(new UnresolvedBossFacts(
                state.PartyWipeObserved,
                continueAfterWipe,
                noActivePlayers,
                state.LastHadLivingEngagedPlayer,
                observedNonKillDisappearance,
                allowDisappearanceBoundary,
                foreignEncounterStarted,
                genericFallbackExpired,
                closesMixedEncounter,
                allowDisappearanceBoundary));
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
        EncounterPlayerKey key = PlayerConnectionRegistry.GetCurrent(playerIndex);
        string name = playerIndex >= 0 && playerIndex < Main.maxPlayers
            ? Main.player[playerIndex].name
            : $"Player {playerIndex}";
        return RecordPlayerDamage(key, name, target, damage);
    }

    public bool RecordPlayerDamage(
        EncounterPlayerKey key,
        string playerName,
        TargetSnapshot target,
        int damage)
    {
        if (damage <= 0)
            return false;

        SessionBoss? attributedBoss = FindUniqueAssociatedBoss(target);
        if (attributedBoss is null)
            return false;

        attributedBoss.GroupState.Players.Add(key);
        bool reconnected = false;
        if (!_connections.TryGetValue(key, out MutableConnectionLedger? connection))
        {
            List<LogicalPlayerCandidate> reconnectCandidates = _logicalPlayers
                .Select(candidate => new LogicalPlayerCandidate(
                    candidate.Name,
                    candidate.Connections.Any(PlayerConnectionRegistry.IsCurrent)))
                .ToList();
            int reconnectIndex = PlayerIdentityPolicy.FindReconnectCandidateIndex(reconnectCandidates, playerName);
            MutableLogicalPlayer? logical = reconnectIndex >= 0 ? _logicalPlayers[reconnectIndex] : null;
            if (logical is null)
            {
                logical = new MutableLogicalPlayer(_logicalPlayers.Count + 1, playerName);
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
        return reconnected;
    }

    public void RecordUnattributedDamage(TargetSnapshot target, int damage)
    {
        if (damage <= 0)
            return;

        SessionBoss? attributedBoss = FindUniqueAssociatedBoss(target);
        if (attributedBoss is null)
            return;

        UnattributedDamage = SaturatingAdd(UnattributedDamage, damage);
        TeamDamage = SaturatingAdd(TeamDamage, damage);
        attributedBoss.StartTick = attributedBoss.StartTick == 0
            ? (long)Main.GameUpdateCount
            : attributedBoss.StartTick;
        attributedBoss.TeamDamage = SaturatingAdd(attributedBoss.TeamDamage, damage);
        attributedBoss.UnattributedDamage = SaturatingAdd(attributedBoss.UnattributedDamage, damage);
        if (IsBossBodyFor(attributedBoss, target))
        {
            attributedBoss.BodyDamage = SaturatingAdd(attributedBoss.BodyDamage, damage);
            BossBodyDamage = SaturatingAdd(BossBodyDamage, damage);
        }
    }

    public int ObservePartyWipes()
    {
        int observed = 0;
        foreach (IGrouping<string, SessionBoss> group in UnresolvedGroups())
        {
            EncounterGroupState state = group.First().GroupState;
            if (state.PartyWipeObserved || !AllGroupPlayersDead(state))
                continue;
            state.PartyWipeObserved = true;
            observed++;
        }
        return observed;
    }

    public int TryRecoverPartyWipes()
    {
        int recovered = 0;
        foreach (IGrouping<string, SessionBoss> group in UnresolvedGroups())
        {
            EncounterGroupState state = group.First().GroupState;
            bool continueAfterWipe = group.Any(value => value.Descriptor.ContinueAfterPartyWipe);
            if (!EncounterPolicy.ShouldRecoverPartyWipe(
                    state.PartyWipeObserved,
                    continueAfterWipe,
                    state.LivingPlayerEngagedThisScan))
            {
                continue;
            }

            state.PartyWipeObserved = false;
            recovered++;
        }
        return recovered;
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
                BossOccurrence = boss.Occurrence,
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
        List<SessionBoss> group = _bosses.Values
            .Where(value => string.Equals(GroupKey(value.Descriptor), groupKey, StringComparison.Ordinal))
            .ToList();
        foreach (SessionBoss boss in group)
        {
            boss.Outcome = outcome;
            boss.FinalBlowCandidates.Reset();
            if (outcome != EncounterOutcome.Victory)
            {
                boss.FinalBlowPlayerName = string.Empty;
                boss.FinalBlowKind = FinalBlowKind.None;
            }
            if (outcome == EncounterOutcome.Unknown)
            {
                boss.ResultPublished = false;
                boss.ExplicitFinalKill = false;
                boss.ProvisionalLastParticipantKill = false;
                boss.ObservedNonKillDisappearance = false;
                boss.KilledAutomaticFinalControllersSinceParticipantScan.Clear();
                boss.KilledLifecycleControllersSinceParticipantScan.Clear();
                boss.KilledExplicitFinalInstancesSinceParticipantScan.Clear();
                boss.SpawnedAutomaticFinalControllerInstancesSinceParticipantScan.Clear();
                boss.AutomaticFinalization.Reset();
                boss.LastLoggedAutomaticFinalCandidate = false;
                boss.LastLoggedControllerEpochContaminated = false;
            }
        }

        // A verified spawn handoff only proves one lifecycle handover. Once the owning
        // boss has actually resolved, keeping the marker would group a later,
        // independently summoned successor of the same key into an already finished
        // encounter. _session.BossKeys includes resolved bosses, so the marker must
        // not outlive its owner.
        if (outcome != EncounterOutcome.Unknown)
        {
            _verifiedSpawnHandoffs.RemoveWhere(handoff => group.Any(boss =>
                boss.Descriptor.Key.Equals(handoff.OwnerKey, StringComparison.Ordinal)));
        }
    }

    private static bool IsBossBodyFor(SessionBoss boss, TargetSnapshot target)
        => BossAttributionPolicy.IsBody(
            boss.Descriptor.BodyNpcTypes,
            boss.Descriptor.ExcludedBodyNpcTypes,
            target.NpcType,
            target.RootNpcType,
            target.ExactOwnerBossKey);

    private static void ConfirmFinalBlow(
        SessionBoss boss,
        IReadOnlySet<NpcInstanceKey> terminalEvidence)
    {
        FinalBlowAttribution? attribution = boss.FinalBlowCandidates.Confirm(terminalEvidence);
        if (!attribution.HasValue)
            return;

        boss.FinalBlowPlayerName = attribution.Value.PlayerName;
        boss.FinalBlowKind = attribution.Value.Kind;
    }

    private SessionBoss? FindUniqueAssociatedBoss(TargetSnapshot target)
        => BossAttributionPolicy.FindUnique(
            _bosses.Values.Where(boss => boss.Outcome == EncounterOutcome.Unknown),
            target.NpcType,
            target.RootNpcType,
            boss => boss.AssociatedNpcTypes,
            target.ExactOwnerBossKey,
            boss => boss.Descriptor.Key);

    private string? ResolveUniqueExplicitFinalGroup(int npcType, NpcInstanceKey instance)
    {
        if (_spawnedNpcOwnership.TryGetValue(instance, out SpawnedNpcOwnership ownership) &&
            IsOwnershipCurrent(ownership))
        {
            string exactKey = ownership.NestedBossKey ?? ownership.OwnerKey;
            if (_bosses.TryGetValue(exactKey, out SessionBoss? exactBoss) &&
                exactBoss.Outcome == EncounterOutcome.Unknown &&
                exactBoss.Descriptor.FinalNpcTypes.Contains(npcType))
            {
                return GroupKey(exactBoss.Descriptor);
            }
        }

        string[] groups = _bosses.Values
            .Where(boss => boss.Outcome == EncounterOutcome.Unknown &&
                           boss.Descriptor.FinalNpcTypes.Contains(npcType) &&
                           !IsSuppressedNestedInstance(instance, boss.Descriptor.Key))
            .Select(boss => GroupKey(boss.Descriptor))
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .ToArray();
        return groups.Length == 1 ? groups[0] : null;
    }

    private string? ResolveUniqueAstrumDeusBossRushFinalGroup(NPC killed, NpcInstanceKey instance)
    {
        if (!BossCatalog.IsCalamityBossRushActive())
            return null;

        string? killedModName = killed.ModNPC?.Mod.Name;
        string? killedNpcName = killed.ModNPC?.Name;
        string[] groups = _bosses.Values
            .Where(boss =>
            {
                if (boss.Outcome != EncounterOutcome.Unknown ||
                    IsSuppressedNestedInstance(instance, boss.Descriptor.Key) ||
                    !boss.Descriptor.KeepAliveNpcTypes.Contains(killed.type))
                {
                    return false;
                }

                bool killedLifecycleController = AutomaticFinalizationPolicy.IsControllerNpc(
                    boss.Descriptor.ControllerNpcTypes,
                    killed.type,
                    killed.boss,
                    killed.whoAmI,
                    killed.realLife);
                return CalamityCompatibilityPolicy.IsAstrumDeusBossRushProgressionFinal(
                    bossRushActive: true,
                    boss.Descriptor.Key,
                    killedModName,
                    killedNpcName,
                    boss.Descriptor.HasExplicitServerOverride,
                    boss.ObservedMultipleLifecycleControllers,
                    killedLifecycleController);
            })
            .Select(boss => GroupKey(boss.Descriptor))
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .ToArray();
        return groups.Length == 1 ? groups[0] : null;
    }

    private BossPresentation CreatePresentation(SessionBoss boss) => new()
    {
        Key = boss.Descriptor.Key,
        NameLocalizationKey = boss.Descriptor.DisplayNameLocalizationKey,
        NameNpcType = boss.Descriptor.DisplayNameNpcType,
        Name = boss.Descriptor.DisplayName,
        PortraitNpcType = boss.Descriptor.PortraitNpcType,
        Outcome = boss.Outcome,
        BodyDamage = boss.BodyDamage,
        FinalBlowPlayerName = boss.FinalBlowPlayerName,
        FinalBlowKind = boss.FinalBlowKind,
        BodyNpcTypes = boss.Descriptor.BodyNpcTypes.Order().ToList(),
        AssociatedNpcTypes = boss.Descriptor.BodyNpcTypes
            .Concat(boss.Descriptor.ExcludedBodyNpcTypes)
            .Distinct()
            .Order()
            .ToList(),
        AssociatedNpcInstances = _spawnedNpcOwnership
            .Where(pair => pair.Value.OwnerKey.Equals(boss.Descriptor.Key, StringComparison.Ordinal) &&
                           pair.Value.OwnerOccurrence == boss.Occurrence &&
                           SpawnedNpcInstancePolicy.IsOwnershipCurrent(
                               pair.Value.OwnerOccurrence,
                               boss.Occurrence,
                               pair.Value.OriginInstance,
                               boss.ObservedParticipantInstances))
            .Select(pair => new AssociatedNpcInstance(pair.Key.Slot, pair.Value.TargetNpcType, pair.Key.SpawnSerial))
            .Distinct()
            .OrderBy(value => value.NpcIndex)
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

    private static NpcInstanceKey InstanceOf(NPC npc)
        => new(npc.whoAmI, DamageTrackingGlobalNpc.GetSpawnSerial(npc));

    private bool IsNestedDependencyForBoss(NpcInstanceKey instance, string ownerKey)
        => _spawnedNpcOwnership.TryGetValue(instance, out SpawnedNpcOwnership ownership) &&
           IsOwnershipCurrent(ownership) &&
           ownership.NestedBossKey is not null &&
           ownership.OwnerKey.Equals(ownerKey, StringComparison.Ordinal);

    private bool IsSuppressedNestedInstance(NpcInstanceKey instance, string bossKey)
        => _spawnedNpcOwnership.TryGetValue(instance, out SpawnedNpcOwnership ownership) &&
           IsOwnershipCurrent(ownership) &&
           ownership.NestedBossKey?.Equals(bossKey, StringComparison.Ordinal) == true;

    private bool IsActiveNestedDependencyForAnyBoss(NPC npc)
    {
        NpcInstanceKey instance = InstanceOf(npc);
        return _spawnedNpcOwnership.TryGetValue(instance, out SpawnedNpcOwnership ownership) &&
               IsOwnershipCurrent(ownership) &&
               ownership.NestedBossKey is not null &&
               _bosses.TryGetValue(ownership.OwnerKey, out SessionBoss? owner) &&
               owner.Outcome == EncounterOutcome.Unknown;
    }

    private bool IsFullySuppressedNestedDescriptor(BossDescriptor descriptor)
    {
        List<NpcInstanceKey> activeManaged = [];
        foreach (NPC npc in Main.ActiveNPCs)
        {
            if (descriptor.KeepAliveNpcTypes.Contains(npc.type))
                activeManaged.Add(InstanceOf(npc));
        }
        HashSet<NpcInstanceKey> nested = _spawnedNpcOwnership
            .Where(pair => IsOwnershipCurrent(pair.Value) &&
                           pair.Value.NestedBossKey?.Equals(descriptor.Key, StringComparison.Ordinal) == true)
            .Select(pair => pair.Key)
            .ToHashSet();
        return SpawnedNpcInstancePolicy.ShouldSuppressNestedDescriptor(activeManaged, nested);
    }

    private bool IsOwnershipCurrent(SpawnedNpcOwnership ownership)
    {
        if (!_bosses.TryGetValue(ownership.OwnerKey, out SessionBoss? owner) ||
            owner.Outcome != EncounterOutcome.Unknown)
        {
            return false;
        }

        return SpawnedNpcInstancePolicy.IsOwnershipCurrent(
            ownership.OwnerOccurrence,
            owner.Occurrence,
            ownership.OriginInstance,
            owner.ObservedParticipantInstances);
    }

    private EncounterGroupState? FindUniqueParticipantGroup(NPC npc)
    {
        NpcInstanceKey instance = InstanceOf(npc);
        if (_spawnedNpcOwnership.TryGetValue(instance, out SpawnedNpcOwnership exactOwnership) &&
            IsOwnershipCurrent(exactOwnership) &&
            _bosses.TryGetValue(exactOwnership.OwnerKey, out SessionBoss? exactOwner) &&
            exactOwner.Outcome == EncounterOutcome.Unknown)
        {
            return exactOwner.GroupState;
        }

        EncounterGroupState[] groups = _bosses.Values
            .Where(boss => boss.Outcome == EncounterOutcome.Unknown &&
                           !IsSuppressedNestedInstance(instance, boss.Descriptor.Key) &&
                           (boss.Descriptor.KeepAliveNpcTypes.Contains(npc.type) ||
                            IsNestedDependencyForBoss(instance, boss.Descriptor.Key)))
            .Select(boss => boss.GroupState)
            .Distinct()
            .Take(2)
            .ToArray();
        return groups.Length == 1 ? groups[0] : null;
    }

    private List<IGrouping<string, SessionBoss>> UnresolvedGroups()
        => _bosses.Values
            .Where(value => value.Outcome == EncounterOutcome.Unknown)
            .GroupBy(value => GroupKey(value.Descriptor), StringComparer.Ordinal)
            .ToList();

    private static bool AllGroupPlayersDead(EncounterGroupState state)
        => EncounterPolicy.AllCurrentParticipantsDead(
            state.Players,
            PlayerConnectionRegistry.IsCurrent,
            key => key.PlayerIndex >= 0 &&
                   key.PlayerIndex < Main.maxPlayers &&
                   (Main.player[key.PlayerIndex].dead || Main.player[key.PlayerIndex].ghost));

    private static bool HasNoActiveGroupPlayers(EncounterGroupState state)
        => EncounterPolicy.HasNoCurrentParticipants(
            state.Players,
            PlayerConnectionRegistry.IsCurrent);

    private EncounterGroupState GetGroupStateForNewBoss(string groupKey)
    {
        bool unresolvedGroupExists = _bosses.Values.Any(boss =>
            boss.Outcome == EncounterOutcome.Unknown &&
            GroupKey(boss.Descriptor).Equals(groupKey, StringComparison.Ordinal));
        if (unresolvedGroupExists)
            return GetOrCreateGroupState(groupKey);

        EncounterGroupState fresh = new();
        _groupStates[groupKey] = fresh;
        return fresh;
    }

    private EncounterGroupState GetOrCreateGroupState(string groupKey)
    {
        if (!_groupStates.TryGetValue(groupKey, out EncounterGroupState? state))
            _groupStates[groupKey] = state = new EncounterGroupState();
        return state;
    }

    private void RemoveGroupStateIfUnused(string groupKey, EncounterGroupState state)
    {
        if (_groupStates.TryGetValue(groupKey, out EncounterGroupState? current) &&
            ReferenceEquals(current, state) &&
            !_bosses.Values.Any(boss => ReferenceEquals(boss.GroupState, state)))
        {
            _groupStates.Remove(groupKey);
        }
    }

    private void RebuildDescriptorAggregates()
    {
        _encounterGroupKeys.Clear();
        _keepAliveNpcTypes.Clear();
        HashSet<string> usedGroups = new(StringComparer.Ordinal);
        foreach (SessionBoss boss in _bosses.Values)
        {
            string groupKey = GroupKey(boss.Descriptor);
            _encounterGroupKeys.Add(groupKey);
            usedGroups.Add(groupKey);
            _groupStates[groupKey] = boss.GroupState;
            _keepAliveNpcTypes.UnionWith(boss.Descriptor.KeepAliveNpcTypes);
        }
        foreach (string staleGroup in _groupStates.Keys.Where(key => !usedGroups.Contains(key)).ToArray())
            _groupStates.Remove(staleGroup);
    }

    private static bool ShouldUseAutoFinal(BossDescriptor descriptor, bool downedAtArm)
        => EncounterPolicy.CanUseAutomaticBodyFinal(
            descriptor.Key,
            GroupKey(descriptor),
            descriptor.FinishOnBodyDeathWhenNoParticipants,
            descriptor.ContinueAfterPartyWipe,
            descriptor.Downed is not null,
            downedAtArm,
            descriptor.ForceAutomaticFinalization);

    private static bool IsAutomaticFinalController(SessionBoss boss, NPC npc)
        => AutomaticFinalizationPolicy.IsControllerNpc(
            boss.AutoFinalControllerNpcTypes,
            npc.type,
            npc.boss,
            npc.whoAmI,
            npc.realLife);

    private void LogAutomaticFinalizationState(
        SessionBoss boss,
        AutomaticFinalizationDecision decision,
        int activeControllerCount,
        int activeParticipantCount)
    {
        bool hasCandidate = boss.AutomaticFinalization.HasCandidate;
        bool contaminated = boss.AutomaticFinalization.HasUnresolvedControllerDisappearance;

        if (boss.AutomaticFinalization.LastRefreshAcceptedOverlapHandoff)
        {
            DaybreakDamageTrackerMod.Instance.Logger.Info(
                $"Encounter {EncounterId}: accepted overlapping controller phase handoff " +
                $"boss={boss.Descriptor.Key}, activeControllers={activeControllerCount}");
        }

        if (hasCandidate != boss.LastLoggedAutomaticFinalCandidate)
        {
            string state = hasCandidate
                ? "armed"
                : decision == AutomaticFinalizationDecision.Victory ? "confirmed" : "cancelled";
            DaybreakDamageTrackerMod.Instance.Logger.Info(
                $"Encounter {EncounterId}: automatic controller-final candidate {state} " +
                $"boss={boss.Descriptor.Key}, activeControllers={activeControllerCount}, " +
                $"activeParticipants={activeParticipantCount}");
        }

        if (contaminated != boss.LastLoggedControllerEpochContaminated)
        {
            string state = contaminated ? "incomplete" : "replaced by a clean controller phase";
            DaybreakDamageTrackerMod.Instance.Logger.Info(
                $"Encounter {EncounterId}: controller epoch {state} boss={boss.Descriptor.Key}");
        }

        boss.LastLoggedAutomaticFinalCandidate = hasCandidate;
        boss.LastLoggedControllerEpochContaminated = contaminated;
    }

    private sealed class SessionBoss(
        BossDescriptor descriptor,
        bool downedAtArm,
        int occurrence,
        EncounterGroupState groupState)
    {
        public BossDescriptor Descriptor { get; } = descriptor;
        public bool DownedAtArm { get; } = downedAtArm;
        public int Occurrence { get; } = occurrence;
        public EncounterGroupState GroupState { get; set; } = groupState;
        public HashSet<int> AutoFinalControllerNpcTypes { get; } = [];
        public HashSet<NpcInstanceKey> PreviousKeepAliveInstances { get; set; } = [];
        public HashSet<NpcInstanceKey> KilledSinceParticipantScan { get; } = [];
        public HashSet<NpcInstanceKey> KilledAutomaticFinalControllersSinceParticipantScan { get; } = [];
        public HashSet<NpcInstanceKey> KilledLifecycleControllersSinceParticipantScan { get; } = [];
        public HashSet<NpcInstanceKey> KilledExplicitFinalInstancesSinceParticipantScan { get; } = [];
        public HashSet<NpcInstanceKey> SpawnedAutomaticFinalControllerInstancesSinceParticipantScan { get; } = [];
        public AutomaticFinalizationTracker<NpcInstanceKey> AutomaticFinalization { get; } = new();
        public FinalBlowCandidateTracker<NpcInstanceKey> FinalBlowCandidates { get; } = new();
        public EncounterOutcome Outcome { get; set; }
        public long StartTick { get; set; }
        public long TeamDamage { get; set; }
        public long BodyDamage { get; set; }
        public long UnattributedDamage { get; set; }
        public string FinalBlowPlayerName { get; set; } = string.Empty;
        public FinalBlowKind FinalBlowKind { get; set; }
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
        public bool ExplicitFinalKill { get; set; }
        public bool ProvisionalLastParticipantKill { get; set; }
        public bool ObservedNonKillDisappearance { get; set; }
        public bool HasActiveParticipants { get; set; }
        public long InactiveSinceTick { get; set; } = -1;
        public HashSet<NpcInstanceKey> ObservedParticipantInstances { get; } = [];
        public bool ObservedMultipleLifecycleControllers { get; set; }
        public bool LastLoggedAutomaticFinalCandidate { get; set; }
        public bool LastLoggedControllerEpochContaminated { get; set; }
        private HashSet<int>? _associatedNpcTypes;

        public void InvalidateAssociatedTypes() => _associatedNpcTypes = null;
    }

    private sealed class EncounterGroupState
    {
        public HashSet<EncounterPlayerKey> Players { get; } = [];
        public bool PartyWipeObserved { get; set; }
        public bool LastHadLivingEngagedPlayer { get; set; }
        public bool SawParticipantNpcThisScan { get; set; }
        public bool LivingPlayerEngagedThisScan { get; set; }

        public void MergeFrom(EncounterGroupState other)
        {
            if (ReferenceEquals(this, other))
                return;

            Players.UnionWith(other.Players);
            PartyWipeObserved |= other.PartyWipeObserved;
            LastHadLivingEngagedPlayer |= other.LastHadLivingEngagedPlayer;
            SawParticipantNpcThisScan |= other.SawParticipantNpcThisScan;
            LivingPlayerEngagedThisScan |= other.LivingPlayerEngagedThisScan;
        }
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

    private readonly record struct SpawnOriginObservation(
        NpcInstanceKey TargetInstance,
        int TargetNpcType,
        NpcInstanceKey OriginInstance,
        int OriginNpcType);

    private readonly record struct SpawnedNpcOwnership(
        int TargetNpcType,
        string OwnerKey,
        string? NestedBossKey,
        int OwnerOccurrence,
        NpcInstanceKey OriginInstance);
}

internal enum EncounterMergeResult
{
    None,
    LifecycleUpdated,
    PresentationUpdated,
    BossAdded
}
