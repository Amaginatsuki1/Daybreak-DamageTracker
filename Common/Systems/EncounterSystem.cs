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

        if (_session is null)
        {
            if (activeBosses.Count > 0)
                Arm(activeBosses);
            return;
        }

        foreach (BossDescriptor descriptor in activeBosses)
            _session.Merge(descriptor);

        _session.ObserveEngagedPlayers();

        bool participantActive = _session.RefreshParticipantWindow();

        if (_session.State == EncounterState.Armed)
        {
            if (!participantActive && activeBosses.Count == 0)
                CancelArmedEncounter();
            return;
        }

        if (participantActive)
        {
            _session.State = EncounterState.Fighting;
            _session.TransitionStartTick = 0;
            return;
        }

        long now = (long)Main.GameUpdateCount;
        if (_session.State != EncounterState.Transitioning)
        {
            _session.State = EncounterState.Transitioning;
            _session.TransitionStartTick = now;
        }

        if (now - _session.TransitionStartTick < ServerConfigService.Current.TransitionSyncGraceTicks)
            return;

        if (_session.HasVictorySignal())
        {
            Finish(EncounterOutcome.Victory);
            return;
        }

        if (_session.AllEncounterPlayersDead())
        {
            Finish(EncounterOutcome.Defeat);
            return;
        }

        if (_session.HasNoActiveEncounterPlayers())
        {
            Finish(EncounterOutcome.Escaped);
            return;
        }

        int fallbackSeconds = ServerConfigService.Current.GenericDespawnFallbackSeconds;
        if (fallbackSeconds > 0 && now - _session.TransitionStartTick >= fallbackSeconds * 60L)
            Finish(EncounterOutcome.Escaped);
    }

    public static TargetSnapshot PrepareTarget(NPC npc)
    {
        bool eligible = TargetClassifier.IsEligibleHostile(npc);
        if (Main.netMode == NetmodeID.MultiplayerClient)
            return new TargetSnapshot(eligible, false, npc.type);

        if (_session is null)
        {
            List<BossDescriptor> activeBosses = BossCatalog.ScanActiveBosses();
            if (activeBosses.Count > 0)
                Arm(activeBosses);
        }

        return new TargetSnapshot(eligible, _session?.IsBossBody(npc) ?? false, npc.type);
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

        if (_session.State is not (EncounterState.Fighting or EncounterState.Transitioning))
            return;

        _session.RecordPlayerDamage(playerIndex, target, actualDamage);
    }

    public static void RecordUnattributedDamage(TargetSnapshot target, int actualDamage)
    {
        if (_session?.State is EncounterState.Fighting or EncounterState.Transitioning)
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
        if (_session.State is EncounterState.Fighting or EncounterState.Transitioning)
            DamageNetwork.SendEncounterStarted(_session.EncounterId, toClient);
    }

    private static void Arm(IEnumerable<BossDescriptor> descriptors)
    {
        _session = new EncounterSession(++_nextEncounterId, descriptors);
        _session.ObserveEngagedPlayers();
        _session.RefreshParticipantWindow();
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

        PublicResultSnapshot result = _session.BuildResult(outcome, (long)Main.GameUpdateCount);
        ServerResultHistory.Add(result, ServerConfigService.Current.Presentation.HistoryCount);

        if (Main.netMode == NetmodeID.Server)
            DamageNetwork.SendResultToConfiguredRecipients(result);
        else
        {
            EncounterPlayerKey local = PlayerConnectionRegistry.GetCurrent(Main.myPlayer);
            long ownDamage = result.Players.FirstOrDefault(row =>
                row.PlayerIndex == local.PlayerIndex && row.ConnectionGeneration == local.Generation)?.Damage ?? 0;
            ClientRuntime.OnPublicResult(result, ownDamage);
        }

        _session = null;
    }
}
