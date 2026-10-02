using DaybreakDamageTracker.Client;
using DaybreakDamageTracker.Common.Systems;
using Terraria.ModLoader.IO;

namespace DaybreakDamageTracker.Common.NPCs;

internal sealed class DamageTrackingGlobalNpc : GlobalNPC
{
    private static long _nextSpawnSerial;
    private static readonly long[] SpawnSerialBySlot = new long[Main.maxNPCs];

    private PendingLocalHit _itemHit;
    private PendingLocalHit _projectileHit;
    private int _spawnOriginNpcType = -1;
    private NpcInstanceKey _spawnOriginNpcInstance;
    private long _receivedSpawnSerial;

    public override bool InstancePerEntity => true;

    public override void SetDefaults(NPC npc)
    {
        _receivedSpawnSerial = 0;
        // SetDefaults also runs for bestiary/preview templates. Only a real world
        // slot may invalidate pending source observations. Its authoritative ID
        // arrives later in the same SyncNPC message through ReceiveExtraAI.
        if (Main.netMode == NetmodeID.MultiplayerClient && IsWorldSlot(npc))
            ClientRuntime.OnNpcInstanceReset(npc.whoAmI);
    }

    public override void SendExtraAI(NPC npc, BitWriter bitWriter, BinaryWriter binaryWriter)
        => binaryWriter.Write(GetSpawnSerial(npc));

    public override void ReceiveExtraAI(NPC npc, BitReader bitReader, BinaryReader binaryReader)
    {
        long spawnSerial = binaryReader.ReadInt64();
        if (spawnSerial <= 0)
            throw new IOException("Invalid NPC spawn serial.");
        if (_receivedSpawnSerial == spawnSerial)
            return;

        _receivedSpawnSerial = spawnSerial;
        // SyncNPC may replace an active same-type slot without SetDefaults. Reset
        // all per-NPC DoT attribution before the new identity becomes observable.
        npc.GetGlobalNPC<DotTrackingGlobalNpc>().ResetForNpcInstance();
        if (IsWorldSlot(npc))
            ClientRuntime.OnNpcInstanceAvailable(new NpcInstanceKey(npc.whoAmI, spawnSerial));
    }

    public override void OnSpawn(NPC npc, IEntitySource source)
    {
        // NPC slots and per-entity globals can be reused. Never let the previous slot
        // occupant's parentage or pending local hit leak into the new NPC instance.
        _spawnOriginNpcType = -1;
        _spawnOriginNpcInstance = default;
        _itemHit = default;
        _projectileHit = default;
        if (npc.whoAmI >= 0 && npc.whoAmI < SpawnSerialBySlot.Length)
            SpawnSerialBySlot[npc.whoAmI] = NextSpawnSerial();

        if (source is EntitySource_Parent parentSource && parentSource.Entity is NPC parentNpc)
        {
            DamageTrackingGlobalNpc parentGlobal = parentNpc.GetGlobalNPC<DamageTrackingGlobalNpc>();
            _spawnOriginNpcType = parentGlobal._spawnOriginNpcType >= 0
                ? parentGlobal._spawnOriginNpcType
                : parentNpc.type;
            _spawnOriginNpcInstance = parentGlobal._spawnOriginNpcInstance.IsValid
                ? parentGlobal._spawnOriginNpcInstance
                : new NpcInstanceKey(parentNpc.whoAmI, GetSpawnSerial(parentNpc));
        }

        if (Main.netMode == NetmodeID.SinglePlayer)
            ClientRuntime.OnNpcInstanceAvailable(new NpcInstanceKey(npc.whoAmI, GetSpawnSerial(npc)));
        EncounterSystem.MarkNpcSpawned(npc);
    }

    public override void ModifyHitByItem(NPC npc, Player player, Item item, ref NPC.HitModifiers modifiers)
    {
        if (Main.netMode != NetmodeID.SinglePlayer)
            return;

        _itemHit = new PendingLocalHit(
            true,
            player.whoAmI,
            EncounterSystem.PrepareTarget(npc));
    }

    public override void OnHitByItem(NPC npc, Player player, Item item, NPC.HitInfo hit, int damageDone)
    {
        if (Main.netMode != NetmodeID.SinglePlayer || !_itemHit.Active || _itemHit.PlayerIndex != player.whoAmI)
            return;

        PendingLocalHit pending = _itemHit;
        _itemHit = default;
        EncounterSystem.RecordPlayerDamage(player.whoAmI, pending.Target, Math.Max(0, damageDone));
    }

    public override void ModifyHitByProjectile(NPC npc, Projectile projectile, ref NPC.HitModifiers modifiers)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient || !IsValidPlayerProjectile(projectile))
            return;

        _projectileHit = new PendingLocalHit(
            true,
            projectile.owner,
            EncounterSystem.PrepareTarget(npc));
    }

    public override void OnHitByProjectile(NPC npc, Projectile projectile, NPC.HitInfo hit, int damageDone)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient || !_projectileHit.Active || _projectileHit.PlayerIndex != projectile.owner)
            return;

        PendingLocalHit pending = _projectileHit;
        _projectileHit = default;
        EncounterSystem.RecordPlayerDamage(projectile.owner, pending.Target, Math.Max(0, damageDone));
    }

    public override void OnKill(NPC npc) => EncounterSystem.MarkNpcKilled(npc);

    internal static long GetSpawnSerial(NPC npc)
    {
        // Never invent a client-side generation: it cannot be compared with the
        // server's association/DoT metadata until SyncNPC supplies the real ID.
        if (Main.netMode == NetmodeID.MultiplayerClient)
            return npc.GetGlobalNPC<DamageTrackingGlobalNpc>()._receivedSpawnSerial;

        if (npc.whoAmI < 0 || npc.whoAmI >= SpawnSerialBySlot.Length)
            return NextSpawnSerial();

        ref long serial = ref SpawnSerialBySlot[npc.whoAmI];
        if (serial == 0)
            serial = NextSpawnSerial();
        return serial;
    }

    internal static void ResetSpawnSerials() => Array.Clear(SpawnSerialBySlot);

    private static bool IsWorldSlot(NPC npc)
        => npc.whoAmI >= 0 && npc.whoAmI < Main.maxNPCs &&
           ReferenceEquals(Main.npc[npc.whoAmI], npc);

    internal static bool TryGetSpawnOrigin(
        NPC npc,
        out NpcInstanceKey originInstance,
        out int originNpcType)
    {
        DamageTrackingGlobalNpc global = npc.GetGlobalNPC<DamageTrackingGlobalNpc>();
        originInstance = global._spawnOriginNpcInstance;
        originNpcType = global._spawnOriginNpcType;
        return originInstance.IsValid && originNpcType >= 0;
    }

    private static long NextSpawnSerial()
    {
        long serial = System.Threading.Interlocked.Increment(ref _nextSpawnSerial);
        return serial != 0 ? serial : System.Threading.Interlocked.Increment(ref _nextSpawnSerial);
    }

    private static bool IsValidPlayerProjectile(Projectile projectile)
        => projectile.owner >= 0 &&
           projectile.owner < Main.maxPlayers &&
           Main.player[projectile.owner].active &&
           projectile.friendly &&
           !projectile.hostile &&
           !projectile.npcProj &&
           !projectile.trap;

    private readonly record struct PendingLocalHit(
        bool Active,
        int PlayerIndex,
        TargetSnapshot Target);
}
