using DaybreakDamageTracker.Common.Systems;

namespace DaybreakDamageTracker.Common.NPCs;

internal sealed class DamageTrackingGlobalNpc : GlobalNPC
{
    private static long _nextSpawnSerial;
    private static readonly long[] SpawnSerialBySlot = new long[Main.maxNPCs];

    private PendingLocalHit _itemHit;
    private PendingLocalHit _projectileHit;

    public override bool InstancePerEntity => true;

    public override void OnSpawn(NPC npc, IEntitySource source)
    {
        if (npc.whoAmI >= 0 && npc.whoAmI < SpawnSerialBySlot.Length)
            SpawnSerialBySlot[npc.whoAmI] = NextSpawnSerial();
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
        if (npc.whoAmI < 0 || npc.whoAmI >= SpawnSerialBySlot.Length)
            return NextSpawnSerial();

        ref long serial = ref SpawnSerialBySlot[npc.whoAmI];
        if (serial == 0)
            serial = NextSpawnSerial();
        return serial;
    }

    internal static void ResetSpawnSerials() => Array.Clear(SpawnSerialBySlot);

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
