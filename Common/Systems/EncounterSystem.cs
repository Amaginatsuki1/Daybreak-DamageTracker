using DaybreakDamageTracker.Client;
using DaybreakDamageTracker.Common.Bosses;
using DaybreakDamageTracker.Common.Config;
using DaybreakDamageTracker.Common.Data;
using DaybreakDamageTracker.Common.Networking;
using DaybreakDamageTracker.Common.NPCs;

namespace DaybreakDamageTracker.Common.Systems;

internal sealed class EncounterSystem : ModSystem
{
    private static EncounterSession? _session;
    private static long _nextEncounterId;
    private static bool _ignoreBossesUntilClear;
    private static long _lastDamagePathReconcileTick = -1;
    private static long _npcSpawnGeneration;
    private static long _lastDamagePathReconcileSpawnGeneration = -1;
    private static readonly HashSet<string> ResolvedBossKeysUntilAbsent = new(StringComparer.Ordinal);

    public static EncounterState State => _session?.State ?? EncounterState.Idle;
    public static long ActiveEncounterId => _session?.EncounterId ?? 0;

    public override void PostAddRecipes()
    {
        BossCatalog.LoadBossChecklist(Mod);
        ServerConfigService.PersistPendingMigration();
    }

    public override void OnWorldLoad()
    {
        _session = null;
        _nextEncounterId = 0;
        _ignoreBossesUntilClear = false;
        _lastDamagePathReconcileTick = -1;
        _npcSpawnGeneration = 0;
        _lastDamagePathReconcileSpawnGeneration = -1;
        ResolvedBossKeysUntilAbsent.Clear();
        BossCatalog.ResetWorldState();
        DamageTrackingGlobalNpc.ResetSpawnSerials();
        PlayerConnectionRegistry.Reset();
        ServerResultHistory.Clear();
        DamageNetwork.Reset();

        if (Main.netMode != NetmodeID.MultiplayerClient)
            ServerConfigService.ActivateForWorld();

        if (!Main.dedServ)
        {
            ClientRuntime.ResetForWorld();
            if (Main.netMode == NetmodeID.SinglePlayer)
                ClientRuntime.ApplyPresentation(ServerConfigService.Current.Presentation.SanitizedClone());
        }
    }

    public override void OnWorldUnload()
    {
        _session = null;
        _lastDamagePathReconcileTick = -1;
        _npcSpawnGeneration = 0;
        _lastDamagePathReconcileSpawnGeneration = -1;
        ResolvedBossKeysUntilAbsent.Clear();
        BossCatalog.ResetWorldState();
        PlayerConnectionRegistry.Reset();
        ServerResultHistory.Clear();
        DamageNetwork.Reset();
        ServerConfigService.Deactivate();
        if (!Main.dedServ)
            ClientRuntime.ResetForWorld();
    }

    public override void Unload()
    {
        _session = null;
        ResolvedBossKeysUntilAbsent.Clear();
        ServerConfigService.Deactivate();
        BossCatalog.Clear();
    }

    public override void PostUpdateWorld()
    {
        if (Main.netMode == NetmodeID.MultiplayerClient)
            return;

        ServerConfigService.ApplyPendingIfIdle();
        DamageNetwork.FlushProjectileProvenance();
        PlayerConnectionRegistry.Update();
        List<BossDescriptor> activeBosses = BossCatalog.ScanActiveBosses();
        HashSet<string> activeBossKeysThisScan = activeBosses
            .Select(descriptor => descriptor.Key)
            .ToHashSet(StringComparer.Ordinal);

        if (_ignoreBossesUntilClear)
        {
            if (activeBosses.Count == 0)
                _ignoreBossesUntilClear = false;
            return;
        }

        ReconcileActiveBosses(activeBosses, activeBossKeysThisScan);
        if (_session is null)
            return;

        _session.ObserveEngagedPlayers();

        bool participantActive = RefreshParticipantWindow();
        if (_session.State != EncounterState.Armed)
        {
            int observedWipes = _session.ObservePartyWipes();
            if (observedWipes > 0)
                LogLifecycle($"party wipe observed for groups={observedWipes}; waiting for each group lifecycle to resolve");
        }
        _session.ResolveInactiveSiblingGroups(
            participantActive,
            (long)Main.GameUpdateCount,
            ServerConfigService.Current.TransitionSyncGraceTicks);
        PublishResolvedBosses(activeBossKeysThisScan);
        if (_session.AllBossesResolved)
        {
            CloseResolvedSession();
            return;
        }

        if (_session.State == EncounterState.Armed)
        {
            if (!participantActive && activeBosses.Count == 0)
                CancelArmedEncounter();
            return;
        }

        if (participantActive)
        {
            int recoveredWipes = _session.TryRecoverPartyWipes();
            if (recoveredWipes > 0)
                LogLifecycle($"party wipe continuation recovered for groups={recoveredWipes} after living players re-engaged");
            ResumeFightIfNeeded();
            return;
        }

        long now = (long)Main.GameUpdateCount;
        if (_session.State != EncounterState.Transitioning)
        {
            _session.State = EncounterState.Transitioning;
            _session.TransitionStartTick = now;
            NotifyEncounterSuspended(_session.EncounterId);
            LogLifecycle("entered an NPC-free transition");
        }

        if (now - _session.TransitionStartTick < ServerConfigService.Current.TransitionSyncGraceTicks)
            return;

        int fallbackSeconds = ServerConfigService.Current.GenericDespawnFallbackSeconds;
        bool fallbackExpired = fallbackSeconds > 0 &&
                               now - _session.TransitionStartTick >= fallbackSeconds * 60L;
        EncounterOutcome? outcome = ResolveInactiveBoundary(
            foreignEncounterStarted: false,
            fallbackExpired);
        if (outcome.HasValue)
            Finish(outcome.Value, activeBossKeysThisScan);
    }

    public static TargetSnapshot PrepareTarget(NPC npc)
    {
        bool eligible = TargetClassifier.IsEligibleHostile(npc);
        if (Main.netMode == NetmodeID.MultiplayerClient)
            return new TargetSnapshot(eligible, npc.type, -1);

        // A manual finish deliberately closes the current ledger while its NPCs may
        // still be alive. Do not let the next hit bypass PostUpdateWorld's hold and
        // immediately arm a duplicate encounter for those same residual NPCs.
        if (_ignoreBossesUntilClear)
            return new TargetSnapshot(eligible, npc.type, -1);

        // Damage and DoT hooks call this many times per tick (once per NPC for DoT
        // ticks alone). Reconcile once per tick, plus once after any later NPC spawn.
        // The spawn generation closes the gap where an earlier unrelated hit already
        // consumed this tick's scan before a Boss spawned and was immediately struck.
        long now = (long)Main.GameUpdateCount;
        long observedSpawnGeneration = _npcSpawnGeneration;
        if (EncounterPolicy.ShouldReconcileDamagePath(
                _lastDamagePathReconcileTick,
                now,
                _lastDamagePathReconcileSpawnGeneration,
                observedSpawnGeneration))
        {
            _lastDamagePathReconcileTick = now;
            _lastDamagePathReconcileSpawnGeneration = observedSpawnGeneration;
            List<BossDescriptor> activeBosses = BossCatalog.ScanActiveBosses();
            HashSet<string> activeBossKeysThisScan = activeBosses
                .Select(descriptor => descriptor.Key)
                .ToHashSet(StringComparer.Ordinal);
            ReconcileActiveBosses(activeBosses, activeBossKeysThisScan);
            if (_session?.State == EncounterState.Transitioning && _session.HasActiveParticipantNpc())
                ResumeFightIfNeeded();
        }

        return _session?.CaptureTarget(eligible, npc) ?? new TargetSnapshot(eligible, npc.type, -1);
    }

    public static void RecordPlayerDamage(int playerIndex, TargetSnapshot target, int actualDamage)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient || _session is null)
            return;

        // An eligible hostile is not automatically part of the encounter. Start the
        // timer and create ranking identities only after exactly one unresolved Boss
        // ledger accepts the target; ambiguous/shared types deliberately fail closed.
        if (!EncounterPolicy.CanRecordDamage(
                target.Eligible,
                actualDamage,
                _session.HasUniqueAssociatedBoss(target)))
            return;

        if (_session.State == EncounterState.Armed)
            BeginFight();

        if (_session.State != EncounterState.Fighting)
            return;

        if (playerIndex < 0 || playerIndex >= Main.maxPlayers || !Main.player[playerIndex].active)
        {
            _session.RecordUnattributedDamage(target, actualDamage);
            return;
        }

        if (_session.RecordPlayerDamage(playerIndex, target, actualDamage))
            LogLifecycle("merged a non-overlapping reconnect into an existing public ranking row");
        RecordDirectFinalBlow(target, playerIndex);
    }

    internal static string GetPrivateTargetOwnerKey(TargetSnapshot target)
        => _session?.GetUniqueAssociatedBossKey(target) ?? string.Empty;

    public static void RecordUnattributedDamage(TargetSnapshot target, int actualDamage)
    {
        if (_session?.State == EncounterState.Fighting && _session.HasUniqueAssociatedBoss(target))
            _session.RecordUnattributedDamage(target, actualDamage);
    }

    internal static DotDamageOwner? CaptureDotOwner(int playerIndex)
    {
        if (playerIndex < 0 || playerIndex >= Main.maxPlayers || !Main.player[playerIndex].active)
            return null;

        return new DotDamageOwner(
            PlayerConnectionRegistry.GetCurrent(playerIndex),
            Main.player[playerIndex].name);
    }

    internal static void RecordDirectFinalBlow(TargetSnapshot target, int playerIndex)
    {
        if (_session is null ||
            playerIndex < 0 ||
            playerIndex >= Main.maxPlayers ||
            !Main.player[playerIndex].active ||
            !TryResolveLethalRoot(target, out NPC lethalRoot))
            return;

        bool staged = _session.StageFinalBlowCandidate(
            target,
            lethalRoot,
            Main.player[playerIndex].name,
            FinalBlowKind.DirectHit,
            (long)Main.GameUpdateCount);
        if (staged)
            LogLifecycle($"recorded a direct final blow by player={playerIndex}");
    }

    internal static void RecordDotFinalBlow(TargetSnapshot target, FinalBlowOwner? owner)
    {
        if (_session is null ||
            !owner.HasValue ||
            string.IsNullOrEmpty(owner.Value.PlayerName) ||
            !TryResolveLethalRoot(target, out NPC lethalRoot))
            return;

        bool staged = _session.StageFinalBlowCandidate(
            target,
            lethalRoot,
            owner.Value.PlayerName,
            FinalBlowKind.DamageOverTime,
            (long)Main.GameUpdateCount);
        if (staged)
            LogLifecycle("recorded a damage-over-time final blow");
    }

    // The snapshot is captured before the hit lands, so its slot still names the struck
    // NPC. A blow is lethal when the shared root's life reached zero (or the root is
    // already gone); later hits on a dead NPC record zero damage and never reach here.
    private static bool TryResolveLethalRoot(TargetSnapshot target, out NPC lethalRoot)
    {
        lethalRoot = null!;
        if (!target.RootInstance.IsValid ||
            target.RootInstance.Slot < 0 ||
            target.RootInstance.Slot >= Main.maxNPCs)
        {
            return false;
        }

        NPC root = Main.npc[target.RootInstance.Slot];
        if (DamageTrackingGlobalNpc.GetSpawnSerial(root) != target.RootInstance.SpawnSerial ||
            (root.active && root.life > 0))
        {
            return false;
        }

        lethalRoot = root;
        return true;
    }

    internal static bool RecordDotDamage(DotDamageOwner? owner, TargetSnapshot target, int actualDamage)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient || _session is null)
            return false;

        if (!EncounterPolicy.CanRecordDamage(
                target.Eligible,
                actualDamage,
                _session.HasUniqueAssociatedBoss(target)))
            return false;

        if (_session.State == EncounterState.Armed)
            BeginFight();
        if (_session.State != EncounterState.Fighting)
            return false;

        if (owner.HasValue)
        {
            if (_session.RecordPlayerDamage(owner.Value.Key, owner.Value.PlayerName, target, actualDamage))
                LogLifecycle("merged a reconnect while applying owned damage over time");
        }
        else
        {
            _session.RecordUnattributedDamage(target, actualDamage);
        }

        return true;
    }

    public static void MarkNpcKilled(NPC npc)
    {
        if (Main.netMode != NetmodeID.MultiplayerClient)
            _session?.MarkNpcKilled(npc);
    }

    public static void MarkNpcSpawned(NPC npc)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient)
            return;

        if (_npcSpawnGeneration == long.MaxValue)
        {
            _npcSpawnGeneration = 1;
            _lastDamagePathReconcileSpawnGeneration = -1;
        }
        else
        {
            _npcSpawnGeneration++;
        }
        _session?.MarkNpcSpawned(npc);
    }

    public static bool ForceFinish(EncounterOutcome outcome)
    {
        if (_session?.State is not (EncounterState.Fighting or EncounterState.Transitioning))
            return false;

        Finish(outcome);
        _ignoreBossesUntilClear = true;
        return true;
    }

    public static void SendCurrentStateToClient(int toClient)
    {
        if (_session is null)
            return;

        DamageNetwork.SendEncounterArmed(_session.EncounterId, _session.BuildBossPresentation(), toClient);
        if (_session.State == EncounterState.Fighting)
            DamageNetwork.SendEncounterStarted(_session.EncounterId, toClient);
        else if (_session.State == EncounterState.Transitioning)
            DamageNetwork.SendEncounterSuspended(_session.EncounterId, toClient);
    }

    private static void ReconcileActiveBosses(
        List<BossDescriptor> activeBosses,
        IReadOnlySet<string> activeBossKeysThisScan)
    {
        // A killed Boss can leave a controller or dying body visible to Boss Checklist
        // for another scan. Do not turn that residue into a second result. Once the key
        // has been completely absent for an authoritative scan, the same Boss may be
        // summoned again even while a simultaneous sibling is still fighting.
        EncounterPolicy.ReleaseAbsentResolvedBossKeys(
            ResolvedBossKeysUntilAbsent,
            activeBosses.Select(descriptor => descriptor.Key));
        activeBosses.RemoveAll(descriptor => ResolvedBossKeysUntilAbsent.Contains(descriptor.Key));

        if (_session is null)
        {
            if (activeBosses.Count > 0)
                Arm(activeBosses);
            return;
        }

        if (activeBosses.Count == 0)
            return;

        bool relationshipChanged = _session.RefreshSpawnedNpcRelationships(activeBosses);
        ApplyDynamicEncounterRelationships(activeBosses);
        _session.RemoveFullySuppressedDescriptors(activeBosses);
        relationshipChanged |= _session.PruneSyntheticNestedBosses();

        bool currentParticipantActive = _session.HasActiveParticipantNpc();
        bool relatedBossActive = activeBosses.Any(_session.IsRelated);
        if (EncounterPolicy.ShouldStartNewEncounter(
                hasAnyActiveBoss: true,
                currentParticipantActive,
                relatedBossActive))
        {
            if (_session.State == EncounterState.Armed)
            {
                CancelArmedEncounter();
            }
            else
            {
                _session.ObserveEngagedPlayers();
                RefreshParticipantWindow();
                int observedWipes = _session.ObservePartyWipes();
                if (observedWipes > 0)
                    LogLifecycle($"party wipe observed for groups={observedWipes} at an unrelated encounter boundary");
                EncounterOutcome outcome = ResolveInactiveBoundary(
                    foreignEncounterStarted: true,
                    genericFallbackExpired: false) ?? EncounterOutcome.Escaped;
                Finish(outcome, activeBossKeysThisScan);
            }

            Arm(activeBosses);
            return;
        }

        bool presentationChanged = relationshipChanged;
        foreach (BossDescriptor descriptor in activeBosses)
        {
            EncounterMergeResult mergeResult = _session.Merge(descriptor);
            if (mergeResult == EncounterMergeResult.BossAdded)
            {
                presentationChanged = true;
                LogLifecycle($"added boss={descriptor.Key}, group={descriptor.EncounterGroupKey}");
            }
            else if (mergeResult == EncounterMergeResult.PresentationUpdated)
            {
                presentationChanged = true;
                LogLifecycle($"updated associated targets for boss={descriptor.Key}");
            }
        }
        if (presentationChanged)
        {
            if (Main.netMode == NetmodeID.Server)
                DamageNetwork.SendEncounterArmed(_session.EncounterId, _session.BuildBossPresentation());
            else
                ClientRuntime.OnEncounterArmed(_session.EncounterId, _session.BuildBossPresentation());
        }
    }

    private static void ApplyDynamicEncounterRelationships(List<BossDescriptor> activeBosses)
    {
        if (_session is null)
            return;

        foreach (BossDescriptor descriptor in activeBosses)
        {
            if (!CalamityCompatibilityPolicy.ShouldGroupZenithScourgeTransition(
                    Main.zenithWorld,
                    _session.BossKeys,
                    descriptor.Key,
                    descriptor.HasExplicitServerOverride,
                    _session.HasVerifiedSpawnHandoff(
                        CalamityCompatibilityPolicy.DesertScourgeKey,
                        CalamityCompatibilityPolicy.AquaticScourgeKey)))
            {
                continue;
            }

            descriptor.EncounterGroupKey = CalamityCompatibilityPolicy.DesertScourgeKey;
            descriptor.AllowGroupedDisappearanceBoundary = true;
            int finalNpcType = descriptor.DisplayNameNpcType >= 0
                ? descriptor.DisplayNameNpcType
                : descriptor.PortraitNpcType;
            if (finalNpcType >= 0)
                descriptor.FinalNpcTypes.Add(finalNpcType);
        }
    }

    private static EncounterOutcome? ResolveInactiveBoundary(
        bool foreignEncounterStarted,
        bool genericFallbackExpired)
    {
        if (_session is null)
            return null;

        return _session.ResolveInactiveBoundary(foreignEncounterStarted, genericFallbackExpired);
    }

    private static void Arm(IEnumerable<BossDescriptor> descriptors)
    {
        List<BossDescriptor> active = descriptors.ToList();
        _session = new EncounterSession(++_nextEncounterId, active);
        _session.RefreshSpawnedNpcRelationships(active);
        ApplyDynamicEncounterRelationships(active);
        foreach (BossDescriptor descriptor in active)
            _session.Merge(descriptor);
        _session.PruneSyntheticNestedBosses();
        _session.ObserveEngagedPlayers();
        RefreshParticipantWindow();
        LogLifecycle($"armed bosses=[{string.Join(",", _session.BossKeys)}], groups=[{string.Join(",", _session.EncounterGroupKeys)}]");
        if (Main.netMode == NetmodeID.Server)
            DamageNetwork.SendEncounterArmed(_session.EncounterId, _session.BuildBossPresentation());
        else
            ClientRuntime.OnEncounterArmed(_session.EncounterId, _session.BuildBossPresentation());
    }

    private static void BeginFight()
    {
        if (_session is null)
            return;

        _session.State = EncounterState.Fighting;
        _session.StartTick = (long)Main.GameUpdateCount;
        if (Main.netMode == NetmodeID.Server)
            DamageNetwork.SendEncounterStarted(_session.EncounterId);
        else
            ClientRuntime.OnEncounterStarted(_session.EncounterId);
        LogLifecycle("fight timer started");
    }

    private static bool RefreshParticipantWindow()
        => _session?.RefreshParticipantWindow(
            (long)Main.GameUpdateCount,
            ServerConfigService.Current.TransitionSyncGraceTicks) ?? false;

    private static void ResumeFightIfNeeded()
    {
        if (_session?.State != EncounterState.Transitioning)
            return;

        _session.State = EncounterState.Fighting;
        _session.TransitionStartTick = 0;
        if (Main.netMode == NetmodeID.Server)
            DamageNetwork.SendEncounterStarted(_session.EncounterId);
        else
            ClientRuntime.OnEncounterStarted(_session.EncounterId);
        LogLifecycle("resumed from transition");
    }

    private static void NotifyEncounterSuspended(long encounterId)
    {
        if (Main.netMode == NetmodeID.Server)
            DamageNetwork.SendEncounterSuspended(encounterId);
        else
            ClientRuntime.OnEncounterSuspended(encounterId);
    }

    private static void CancelArmedEncounter()
    {
        if (_session is null)
            return;

        long id = _session.EncounterId;
        _session = null;
        if (Main.netMode == NetmodeID.Server)
            DamageNetwork.SendEncounterCancelled(id);
        else
            ClientRuntime.OnEncounterCancelled(id);
    }

    private static void Finish(
        EncounterOutcome outcome,
        IReadOnlySet<string>? activeBossKeysThisScan = null)
    {
        if (_session is null)
            return;

        _session.CompleteUnresolved(outcome);
        PublishResolvedBosses(activeBossKeysThisScan);
        CloseResolvedSession();
    }

    private static void PublishResolvedBosses(IReadOnlySet<string>? activeBossKeysThisScan = null)
    {
        if (_session is null)
            return;

        foreach (PublicResultSnapshot result in _session.DrainResolvedResults((long)Main.GameUpdateCount))
        {
            BossPresentation boss = result.Bosses.FirstOrDefault() ?? new BossPresentation();
            if (!string.IsNullOrEmpty(boss.Key))
                ResolvedBossKeysUntilAbsent.Add(boss.Key);
            LogLifecycle($"published boss={boss.Key}, outcome={result.Outcome}, teamDamage={result.TeamDamage}, bodyDamage={result.BossBodyDamage}, publicPlayers={result.Players.Count}, durationTicks={result.DurationTicks}, encounterComplete={result.EncounterComplete}");
            ServerResultHistory.Add(result, PresentationSettings.MaximumHistoryCount);

            if (Main.netMode == NetmodeID.Server)
            {
                DamageNetwork.SendResultToConfiguredRecipients(result);
            }
            else
            {
                EncounterPlayerKey local = PlayerConnectionRegistry.GetCurrent(Main.myPlayer);
                long ownDamage = result.RecipientDamage.TryGetValue(local, out long damage) ? damage : 0;
                ClientRuntime.OnPublicResult(result, ownDamage);
            }
        }

        // A result may be published by the same authoritative scan which already
        // proved that Boss key absent. Do not force an immediately re-summoned copy
        // to disappear a second time merely because the hold was added after the
        // ordinary start-of-scan absence release. Residual/death-animation keys stay
        // held because they are still present in this unfiltered scan.
        if (activeBossKeysThisScan is not null)
        {
            EncounterPolicy.ReleaseAbsentResolvedBossKeys(
                ResolvedBossKeysUntilAbsent,
                activeBossKeysThisScan);
        }
    }

    private static void CloseResolvedSession()
    {
        if (_session is null)
            return;

        long encounterId = _session.EncounterId;
        _session = null;
        if (Main.netMode == NetmodeID.Server)
            DamageNetwork.SendEncounterCompleted(encounterId);
        else
            ClientRuntime.OnEncounterCompleted(encounterId);
    }

    private static void LogLifecycle(string message)
    {
        if (_session is not null)
            DaybreakDamageTrackerMod.Instance.Logger.Info($"Encounter {_session.EncounterId}: {message}");
    }
}
