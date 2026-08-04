using DaybreakDamageTracker.Common.Data;

namespace DaybreakDamageTracker.Common.Projectiles;

internal static class ProjectileProvenanceRegistry
{
    private const int MaximumPendingEntries = 512;
    private const int PendingLifetimeTicks = 120;
    private static readonly Dictionary<ProjectileKey, PendingSource> Pending = [];

    public static void Reset() => Pending.Clear();

    public static void ReceiveOwnerOnly(int owner, int identity, int projectileType, DamageRootKey source)
    {
        if (Main.netMode != NetmodeID.MultiplayerClient || owner != Main.myPlayer || identity < 0)
            return;

        if (TryApply(owner, identity, projectileType, source))
            return;

        if (Pending.Count >= MaximumPendingEntries)
            Pending.Clear();
        Pending[new ProjectileKey(owner, identity)] = new PendingSource(
            projectileType,
            source,
            (long)Main.GameUpdateCount + PendingLifetimeTicks);
    }

    public static void ApplyPending()
    {
        if (Main.netMode != NetmodeID.MultiplayerClient || Pending.Count == 0)
            return;

        long now = (long)Main.GameUpdateCount;
        foreach ((ProjectileKey key, PendingSource pending) in Pending.ToArray())
        {
            if (pending.ExpiresAtTick < now ||
                TryApply(key.Owner, key.Identity, pending.ProjectileType, pending.Root))
            {
                Pending.Remove(key);
            }
        }
    }

    private static bool TryApply(int owner, int identity, int projectileType, DamageRootKey source)
    {
        foreach (Projectile projectile in Main.ActiveProjectiles)
        {
            if (projectile.owner != owner ||
                projectile.identity != identity ||
                projectile.type != projectileType)
            {
                continue;
            }

            SourceTrackingGlobalProjectile tracked = projectile.GetGlobalProjectile<SourceTrackingGlobalProjectile>();
            // A locally spawned owner projectile already has the richer IEntitySource.
            // Only fill the gap left by network SyncProjectile, which skips OnSpawn.
            if (!tracked.HasSource)
                tracked.SetFromOwnerPacket(source);
            return true;
        }

        return false;
    }

    private readonly record struct ProjectileKey(int Owner, int Identity);
    private readonly record struct PendingSource(int ProjectileType, DamageRootKey Root, long ExpiresAtTick);
}

internal sealed class ProjectileProvenanceSystem : ModSystem
{
    public override void OnWorldLoad() => ProjectileProvenanceRegistry.Reset();
    public override void OnWorldUnload() => ProjectileProvenanceRegistry.Reset();
    public override void PreUpdateProjectiles() => ProjectileProvenanceRegistry.ApplyPending();
    public override void Unload() => ProjectileProvenanceRegistry.Reset();
}
