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

    public static EncounterState State => _session?.State ?? EncounterState.Idle;
    public static long ActiveEncounterId => _session?.EncounterId ?? 0;

    public override void PostAddRecipes() => BossCatalog.LoadBossChecklist(Mod);

    public override void OnWorldLoad()
    {
        _session = null;
        _nextEncounterId = 0;
        _ignoreBossesUntilClear = false;
        DamageTrackingGlobalNpc.ResetSpawnSerials();
        PlayerConnectionRegistry.Reset();
        ServerResultHistory.Clear();
        DamageNetwork.Reset();

        if (Main.netMode != NetmodeID.MultiplayerClient)
        {
            ServerConfigService.Reload(out string message);
            Mod.Logger.Info(message);
        }

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
        PlayerConnectionRegistry.Reset();
        ServerResultHistory.Clear();
        DamageNetwork.Reset();
        if (!Main.dedServ)
            ClientRuntime.ResetForWorld();
    }

    public override void Unload()
    {
        _session = null;
        BossCatalog.Clear();
    }

    public override void PostUpdateWorld()
    {
        if (Main.netMode == NetmodeID.MultiplayerClient)
            return;

        PlayerConnectionRegistry.Update();
        List<BossDescriptor> activeBosses = BossCatalog.ScanActiveBosses();

        if (_ignoreBossesUntilClear)
        {
            if (activeBosses.Count == 0)
                _ignoreBossesUntilClear = false;
            return;
        }

        ReconcileActiveBosses(activeBosses);
        if (_session is null)
            return;

        EngagementObservation engagement = _session.ObserveEngagedPlayers();

        bool participantActive = _session.RefreshParticipantWindow();
        PublishResolvedBosses();
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

        if (_session.ObservePartyWipe())
            LogLifecycle("party wipe observed; waiting for the encounter lifecycle to resolve");

        if (participantActive)
        {
            if (_session.TryRecoverPartyWipe(engagement.LivingPlayerEngaged))
                LogLifecycle("party wipe continuation recovered after a living player re-engaged");
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
            Finish(outcome.Value);
    }

    public static TargetSnapshot PrepareTarget(NPC npc)
    {
        bool eligible = TargetClassifier.IsEligibleHostile(npc);
        if (Main.netMode == NetmodeID.MultiplayerClient)
            return new TargetSnapshot(eligible, npc.type, -1);

        ReconcileActiveBosses(BossCatalog.ScanActiveBosses());
        if (_session?.State == EncounterState.Transitioning && _session.HasActiveParticipantNpc())
            ResumeFightIfNeeded();

        return _session?.CaptureTarget(eligible, npc) ?? new TargetSnapshot(eligible, npc.type, -1);
    }

    public static void RecordPlayerDamage(int playerIndex, TargetSnapshot target, int actualDamage)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient || !target.Eligible || actualDamage <= 0 || _session is null)
            return;

        if (playerIndex < 0 || playerIndex >= Main.maxPlayers || !Main.player[playerIndex].active)
        {
            RecordUnattributedDamage(target, actualDamage);
            return;
        }

        if (_session.State == EncounterState.Armed)
            BeginFight();

        if (_session.State != EncounterState.Fighting)
            return;

        if (_session.RecordPlayerDamage(playerIndex, target, actualDamage))
            LogLifecycle("merged a non-overlapping reconnect into an existing public ranking row");
    }

    public static void RecordUnattributedDamage(TargetSnapshot target, int actualDamage)
    {
        if (_session?.State == EncounterState.Fighting)
            _session.RecordUnattributedDamage(target, actualDamage);
    }

    public static void MarkNpcKilled(NPC npc)
    {
        if (Main.netMode != NetmodeID.MultiplayerClient)
            _session?.MarkNpcKilled(npc);
    }

    public static void MarkNpcSpawned(NPC npc)
    {
        if (Main.netMode != NetmodeID.MultiplayerClient)
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

    private static void ReconcileActiveBosses(List<BossDescriptor> activeBosses)
    {
        if (_session is null)
        {
            if (activeBosses.Count > 0)
                Arm(activeBosses);
            return;
        }

        if (activeBosses.Count == 0)
            return;

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
                _session.RefreshParticipantWindow();
                if (_session.ObservePartyWipe())
                    LogLifecycle("party wipe observed at an unrelated encounter boundary");
                EncounterOutcome outcome = ResolveInactiveBoundary(
                    foreignEncounterStarted: true,
                    genericFallbackExpired: false) ?? EncounterOutcome.Escaped;
                Finish(outcome);
            }

            Arm(activeBosses);
            return;
        }

        bool changed = false;
        foreach (BossDescriptor descriptor in activeBosses)
        {
            if (_session.Merge(descriptor))
            {
                changed = true;
                LogLifecycle($"added boss={descriptor.Key}, group={descriptor.EncounterGroupKey}");
            }
        }
        if (changed)
        {
            if (Main.netMode == NetmodeID.Server)
                DamageNetwork.SendEncounterArmed(_session.EncounterId, _session.BuildBossPresentation());
            else
                ClientRuntime.OnEncounterArmed(_session.EncounterId, _session.BuildBossPresentation());
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
        _session = new EncounterSession(++_nextEncounterId, descriptors);
        _session.ObserveEngagedPlayers();
        _session.RefreshParticipantWindow();
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

    private static void Finish(EncounterOutcome outcome)
    {
        if (_session is null)
            return;

        _session.CompleteUnresolved(outcome);
        PublishResolvedBosses();
        CloseResolvedSession();
    }

    private static void PublishResolvedBosses()
    {
        if (_session is null)
            return;

        foreach (PublicResultSnapshot result in _session.DrainResolvedResults((long)Main.GameUpdateCount))
        {
            BossPresentation boss = result.Bosses.FirstOrDefault() ?? new BossPresentation();
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
