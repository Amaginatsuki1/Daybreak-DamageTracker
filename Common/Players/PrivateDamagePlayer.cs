using DaybreakDamageTracker.Client;
using DaybreakDamageTracker.Common.Config;
using DaybreakDamageTracker.Common.Data;
using DaybreakDamageTracker.Common.Networking;
using DaybreakDamageTracker.Common.Projectiles;
using DaybreakDamageTracker.Common.Systems;

namespace DaybreakDamageTracker.Common.Players;

internal sealed class PrivateDamagePlayer : ModPlayer
{
    public override void OnEnterWorld()
    {
        if (Main.dedServ || Player.whoAmI != Main.myPlayer)
            return;

        ClientRuntime.ResetForWorld();
        if (Main.netMode == NetmodeID.MultiplayerClient)
            DamageNetwork.RequestServerState();
        else
            ClientRuntime.ApplyPresentation(ServerConfigService.Current.Presentation.SanitizedClone());
    }

    public override void PlayerDisconnect()
    {
        if (Main.netMode == NetmodeID.Server)
        {
            DamageNetwork.ForgetStateRequestsForPlayer(Player.whoAmI);
            PlayerConnectionRegistry.MarkDisconnected(Player.whoAmI);
        }
    }

    public override void OnHitNPCWithItem(Item item, NPC target, NPC.HitInfo hit, int damageDone)
    {
        if (Main.dedServ || Player.whoAmI != Main.myPlayer || damageDone <= 0 || !TargetClassifier.IsEligibleHostile(target))
            return;

        ClientRuntime.RecordPrivateDamage(
            target,
            new DamageRootKey(DamageRootKind.Item, item.type),
            new DamageLeafKey(DamageLeafKind.ItemBody, item.type),
            damageDone);
    }

    public override void OnHitNPCWithProj(Projectile proj, NPC target, NPC.HitInfo hit, int damageDone)
    {
        if (Main.dedServ || Player.whoAmI != Main.myPlayer || damageDone <= 0 || !TargetClassifier.IsEligibleHostile(target))
            return;

        SourceTrackingGlobalProjectile source = proj.GetGlobalProjectile<SourceTrackingGlobalProjectile>();
        DamageRootKey root = source.HasSource
            ? source.RootSource
            : new DamageRootKey(DamageRootKind.ProjectileFallback, proj.type);

        ClientRuntime.RecordPrivateDamage(
            target,
            root,
            new DamageLeafKey(DamageLeafKind.Projectile, proj.type),
            damageDone);
    }
}
