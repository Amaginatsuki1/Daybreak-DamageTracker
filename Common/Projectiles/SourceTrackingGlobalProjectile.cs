using DaybreakDamageTracker.Common.Data;
using DaybreakDamageTracker.Common.Networking;

namespace DaybreakDamageTracker.Common.Projectiles;

internal sealed class SourceTrackingGlobalProjectile : GlobalProjectile
{
    public override bool InstancePerEntity => true;

    public bool HasSource { get; private set; }
    public DamageRootKey RootSource { get; private set; }
    private bool CanShareWithOwner { get; set; }

    public override void OnSpawn(Projectile projectile, IEntitySource source)
    {
        HasSource = false;
        CanShareWithOwner = false;

        if (projectile.owner < 0 || projectile.owner >= Main.maxPlayers)
            return;

        if (source is IEntitySource_WithStatsFromItem itemSource && itemSource.Item.type > ItemID.None)
        {
            Set(new DamageRootKey(DamageRootKind.Item, itemSource.Item.type), canShareWithOwner: true);
            ShareServerSourceWithOwner(projectile);
            return;
        }

        if (source is IEntitySource_OnHit onHit && onHit.Attacker is Projectile attackingProjectile &&
            TryCopy(attackingProjectile))
        {
            ShareServerSourceWithOwner(projectile);
            return;
        }

        if (source is EntitySource_Buff buff && buff.Entity is Player)
        {
            Set(new DamageRootKey(DamageRootKind.Buff, buff.BuffId), canShareWithOwner: true);
            ShareServerSourceWithOwner(projectile);
            return;
        }

        if (source is EntitySource_Mount mount && mount.Entity is Player)
        {
            Set(new DamageRootKey(DamageRootKind.Mount, mount.MountId), canShareWithOwner: true);
            ShareServerSourceWithOwner(projectile);
            return;
        }

        if (source is EntitySource_Parent parent && parent.Entity is Projectile parentProjectile && TryCopy(parentProjectile))
        {
            ShareServerSourceWithOwner(projectile);
            return;
        }

        Set(new DamageRootKey(DamageRootKind.ProjectileFallback, projectile.type), canShareWithOwner: false);
    }

    internal void SetFromOwnerPacket(DamageRootKey source)
        => Set(source, canShareWithOwner: false);

    private bool TryCopy(Projectile parent)
    {
        SourceTrackingGlobalProjectile source = parent.GetGlobalProjectile<SourceTrackingGlobalProjectile>();
        if (!source.HasSource)
            return false;

        Set(source.RootSource, source.CanShareWithOwner);
        return true;
    }

    private void Set(DamageRootKey source, bool canShareWithOwner)
    {
        RootSource = source;
        HasSource = true;
        CanShareWithOwner = canShareWithOwner;
    }

    private void ShareServerSourceWithOwner(Projectile projectile)
    {
        if (Main.netMode == NetmodeID.Server && CanShareWithOwner && projectile.identity >= 0)
            DamageNetwork.SendProjectileProvenance(projectile, RootSource);
    }
}
