using DaybreakDamageTracker.Common.Bosses;
using DaybreakDamageTracker.Common.Data;
using DaybreakDamageTracker.Common.NPCs;

namespace DaybreakDamageTracker.Common.Systems;

internal sealed class EncounterSession
{
    private readonly Dictionary<string, SessionBoss> _bosses = new(StringComparer.Ordinal);
    private readonly Dictionary<EncounterPlayerKey, MutablePlayerLedger> _players = [];
    private readonly HashSet<int> _bodyNpcTypes = [];
    private readonly HashSet<int> _keepAliveNpcTypes = [];
    private readonly HashSet<int> _finalNpcTypes = [];
    private readonly HashSet<int> _autoFinalNpcTypes = [];
    private readonly HashSet<int> _excludedBodyNpcTypes = [];
    private readonly HashSet<EncounterPlayerKey> _encounterPlayers = [];
    private HashSet<NpcInstanceKey> _previousKeepAliveInstances = [];
    private readonly HashSet<NpcInstanceKey> _killedSinceParticipantScan = [];
    private bool _pendingAutoFinalKill;
    private bool _explicitFinalKill;
    private bool _provisionalLastParticipantKill;

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

    public void Merge(BossDescriptor descriptor)
    {
        if (_bosses.TryGetValue(descriptor.Key, out SessionBoss? existing))
        {
            // Keep the opening portrait stable, but accept lifecycle roles revealed by a
            // later phase using the same Boss Checklist/override key.
            existing.Descriptor.BodyNpcTypes.UnionWith(descriptor.BodyNpcTypes);
            existing.Descriptor.KeepAliveNpcTypes.UnionWith(descriptor.KeepAliveNpcTypes);
            existing.Descriptor.FinalNpcTypes.UnionWith(descriptor.FinalNpcTypes);
            existing.Descriptor.ExcludedBodyNpcTypes.UnionWith(descriptor.ExcludedBodyNpcTypes);
            _bodyNpcTypes.UnionWith(descriptor.BodyNpcTypes);
            _keepAliveNpcTypes.UnionWith(descriptor.KeepAliveNpcTypes);
            _finalNpcTypes.UnionWith(descriptor.FinalNpcTypes);
            _excludedBodyNpcTypes.UnionWith(descriptor.ExcludedBodyNpcTypes);
            if (descriptor.FinishOnBodyDeathWhenNoParticipants &&
                (descriptor.Downed is null || existing.DownedAtArm))
            {
                _autoFinalNpcTypes.UnionWith(descriptor.BodyNpcTypes);
            }
            return;
        }

        BossDescriptor copy = descriptor.Clone();
        bool downedAtArm = SafeDowned(copy.Downed);
        _bosses[copy.Key] = new SessionBoss(copy, downedAtArm);
        _bodyNpcTypes.UnionWith(copy.BodyNpcTypes);
        _keepAliveNpcTypes.UnionWith(copy.KeepAliveNpcTypes);
        _finalNpcTypes.UnionWith(copy.FinalNpcTypes);
        _excludedBodyNpcTypes.UnionWith(copy.ExcludedBodyNpcTypes);
        // On a first clear, Boss Checklist's downed transition is a stronger final-phase signal
        // than a temporary NPC-free gap. Repeated clears cannot observe false->true again, so a
        // known single-body/generic fallback (or an explicit server override) is used there.
        if (copy.FinishOnBodyDeathWhenNoParticipants && (copy.Downed is null || downedAtArm))
            _autoFinalNpcTypes.UnionWith(copy.BodyNpcTypes);
    }

    public bool IsBossBody(NPC npc)
    {
        if (_excludedBodyNpcTypes.Contains(npc.type))
            return false;
        if (_bodyNpcTypes.Contains(npc.type))
            return true;

        if (npc.realLife < 0 || npc.realLife >= Main.maxNPCs)
            return false;

        NPC root = Main.npc[npc.realLife];
        return root.active &&
               !_excludedBodyNpcTypes.Contains(root.type) &&
               _bodyNpcTypes.Contains(root.type);
    }

    public bool RefreshParticipantWindow()
    {
        HashSet<NpcInstanceKey> current = [];
        bool currentHasAutoFinal = false;
        for (int i = 0; i < Main.maxNPCs; i++)
        {
            NPC npc = Main.npc[i];
            if (npc.active && _keepAliveNpcTypes.Contains(npc.type))
            {
                current.Add(new NpcInstanceKey(i, DamageTrackingGlobalNpc.GetSpawnSerial(npc)));
                currentHasAutoFinal |= _autoFinalNpcTypes.Contains(npc.type);
            }
        }

        if (current.Count > 0)
        {
            // A living auto-final form is a replacement/remaining body and invalidates an
            // earlier generic final kill. A non-body companion may legitimately linger
            // after the body dies, so keep that pending signal until the companion closes.
            _provisionalLastParticipantKill = false;
            if (currentHasAutoFinal)
                _pendingAutoFinalKill = false;
        }
        else if (_previousKeepAliveInstances.Count > 0)
        {
            // A mixed "one part killed, another part despawned" window is an escape, not
            // a generic victory. Bosses that intentionally remove companion parts when a
            // final core dies should declare FinalNpcTypes (built-in Moon Lord does).
            _provisionalLastParticipantKill = _pendingAutoFinalKill &&
                _previousKeepAliveInstances.All(previous => _killedSinceParticipantScan.Contains(previous));
            _pendingAutoFinalKill = false;
        }
        else
        {
            // An orphan kill that was never observed as a lifecycle participant is not a
            // sufficient generic victory signal.
            _pendingAutoFinalKill = false;
        }

        _previousKeepAliveInstances = current;
        _killedSinceParticipantScan.Clear();
        return current.Count > 0;
    }

    public void ObserveEngagedPlayers()
    {
        float activeRangeX = Math.Max(1, NPC.sWidth) * 2.1f;
        float activeRangeY = Math.Max(1, NPC.sHeight) * 2.1f;

        for (int npcIndex = 0; npcIndex < Main.maxNPCs; npcIndex++)
        {
            NPC npc = Main.npc[npcIndex];
            if (!npc.active || !_keepAliveNpcTypes.Contains(npc.type))
                continue;

            if (npc.HasValidTarget && Main.player[npc.target].active)
                _encounterPlayers.Add(PlayerConnectionRegistry.GetCurrent(npc.target));

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
            }
        }
    }

    public void MarkNpcKilled(NPC killed)
    {
        if (_finalNpcTypes.Contains(killed.type))
            _explicitFinalKill = true;

        if (_keepAliveNpcTypes.Contains(killed.type))
            _killedSinceParticipantScan.Add(new NpcInstanceKey(
                killed.whoAmI,
                DamageTrackingGlobalNpc.GetSpawnSerial(killed)));

        if (_autoFinalNpcTypes.Contains(killed.type))
            _pendingAutoFinalKill = true;
    }

    public void MarkNpcSpawned(NPC spawned)
    {
        if (_keepAliveNpcTypes.Contains(spawned.type))
        {
            _previousKeepAliveInstances.Add(new NpcInstanceKey(
                spawned.whoAmI,
                DamageTrackingGlobalNpc.GetSpawnSerial(spawned)));
        }
    }

    public bool HasVictorySignal()
    {
        if (_explicitFinalKill || _provisionalLastParticipantKill)
            return true;

        foreach (SessionBoss boss in _bosses.Values)
        {
            if (!boss.DownedAtArm && SafeDowned(boss.Descriptor.Downed))
                return true;
        }
        return false;
    }

    public void RecordPlayerDamage(int playerIndex, TargetSnapshot target, int damage)
    {
        if (damage <= 0)
            return;

        EncounterPlayerKey key = PlayerConnectionRegistry.GetCurrent(playerIndex);
        _encounterPlayers.Add(key);
        if (!_players.TryGetValue(key, out MutablePlayerLedger? ledger))
        {
            string name = playerIndex >= 0 && playerIndex < Main.maxPlayers
                ? Main.player[playerIndex].name
                : $"Player {playerIndex}";
            ledger = new MutablePlayerLedger(key, name);
            _players[key] = ledger;
        }

        ledger.Damage = SaturatingAdd(ledger.Damage, damage);
        TeamDamage = SaturatingAdd(TeamDamage, damage);
        if (target.BossBody)
        {
            BossBodyDamage = SaturatingAdd(BossBodyDamage, damage);
        }
    }

    public void RecordUnattributedDamage(TargetSnapshot target, int damage)
    {
        if (damage > 0)
            UnattributedDamage = SaturatingAdd(UnattributedDamage, damage);
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

    public PublicResultSnapshot BuildResult(EncounterOutcome outcome, long endTick)
    {
        return new PublicResultSnapshot
        {
            EncounterId = EncounterId,
            Outcome = outcome,
            DurationTicks = Math.Max(0, endTick - StartTick),
            TeamDamage = TeamDamage,
            BossBodyDamage = BossBodyDamage,
            UnattributedDamage = UnattributedDamage,
            Bosses = _bosses.Values.Select(value => new BossPresentation
            {
                Key = value.Descriptor.Key,
                NameLocalizationKey = value.Descriptor.DisplayNameLocalizationKey,
                NameNpcType = value.Descriptor.DisplayNameNpcType,
                Name = value.Descriptor.DisplayName,
                PortraitNpcType = value.Descriptor.PortraitNpcType
            }).ToList(),
            Players = _players.Values
                .OrderByDescending(value => value.Damage)
                .Select(value => new PlayerResultRow
                {
                    PlayerIndex = value.Key.PlayerIndex,
                    ConnectionGeneration = value.Key.Generation,
                    PlayerName = value.Name,
                    Damage = value.Damage
                }).ToList()
        };
    }

    public List<BossPresentation> BuildBossPresentation() => _bosses.Values.Select(value => new BossPresentation
    {
        Key = value.Descriptor.Key,
        NameLocalizationKey = value.Descriptor.DisplayNameLocalizationKey,
        NameNpcType = value.Descriptor.DisplayNameNpcType,
        Name = value.Descriptor.DisplayName,
        PortraitNpcType = value.Descriptor.PortraitNpcType
    }).ToList();

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

    private sealed class SessionBoss(BossDescriptor descriptor, bool downedAtArm)
    {
        public BossDescriptor Descriptor { get; } = descriptor;
        public bool DownedAtArm { get; } = downedAtArm;
    }

    private sealed class MutablePlayerLedger(EncounterPlayerKey key, string name)
    {
        public EncounterPlayerKey Key { get; } = key;
        public string Name { get; } = name;
        public long Damage { get; set; }
    }

    private readonly record struct NpcInstanceKey(int Slot, long SpawnSerial);
}
