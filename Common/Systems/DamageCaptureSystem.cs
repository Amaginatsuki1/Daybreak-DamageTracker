using DaybreakDamageTracker.Common.Data;

namespace DaybreakDamageTracker.Common.Systems;

internal sealed class DamageCaptureSystem : ModSystem
{
    [ThreadStatic]
    private static PendingDamagePacket _pendingDamage;

    [ThreadStatic]
    private static PendingBuffPacket _pendingBuff;

    public override void Load()
    {
        On_MessageBuffer.GetData += HookGetData;
        On_NPC.StrikeNPC_HitInfo_bool_bool += HookStrikeNpc;
    }

    public override void Unload()
    {
        On_MessageBuffer.GetData -= HookGetData;
        On_NPC.StrikeNPC_HitInfo_bool_bool -= HookStrikeNpc;
        _pendingDamage = default;
        _pendingBuff = default;
    }

    public override bool HijackGetData(ref byte messageType, ref BinaryReader reader, int playerNumber)
    {
        if (Main.netMode != NetmodeID.Server)
            return false;

        if (messageType == MessageID.AddNPCBuff)
        {
            if (playerNumber >= 0 && playerNumber < Main.maxPlayers)
                _pendingBuff = new PendingBuffPacket(true, playerNumber);
            return false;
        }

        if (messageType != MessageID.DamageNPC)
            return false;

        long position = reader.BaseStream.Position;
        try
        {
            int npcIndex = reader.ReadInt16();
            int packetDamage = reader.Read7BitEncodedInt();
            if (npcIndex >= 0 && npcIndex < Main.maxNPCs && playerNumber >= 0 && playerNumber < Main.maxPlayers)
                _pendingDamage = new PendingDamagePacket(true, playerNumber, npcIndex, packetDamage);
        }
        catch (Exception exception)
        {
            Mod.Logger.Debug($"Could not inspect DamageNPC packet: {exception.Message}");
            _pendingDamage = default;
        }
        finally
        {
            if (reader.BaseStream.CanSeek)
                reader.BaseStream.Position = position;
        }

        return false;
    }

    private static void HookGetData(
        On_MessageBuffer.orig_GetData orig,
        MessageBuffer self,
        int start,
        int length,
        out int messageType)
    {
        _pendingDamage = default;
        _pendingBuff = default;
        try
        {
            orig(self, start, length, out messageType);
        }
        finally
        {
            _pendingDamage = default;
            _pendingBuff = default;
        }
    }

    internal static bool TryGetPendingBuffPlayer(out int playerIndex)
    {
        playerIndex = _pendingBuff.PlayerIndex;
        return Main.netMode == NetmodeID.Server && _pendingBuff.Active;
    }

    private static int HookStrikeNpc(
        On_NPC.orig_StrikeNPC_HitInfo_bool_bool orig,
        NPC self,
        NPC.HitInfo hit,
        bool fromNet,
        bool noPlayerInteraction)
    {
        bool attributedNetworkHit = TryConsumeNetworkHit(self, hit, fromNet, out int playerIndex);
        TargetSnapshot target = default;

        if (attributedNetworkHit)
            target = EncounterSystem.PrepareTarget(self);

        int result = orig(self, hit, fromNet, noPlayerInteraction);

        if (attributedNetworkHit)
            EncounterSystem.RecordPlayerDamage(playerIndex, target, Math.Max(0, result));

        return result;
    }

    private static bool TryConsumeNetworkHit(NPC npc, NPC.HitInfo hit, bool fromNet, out int playerIndex)
    {
        playerIndex = -1;
        PendingDamagePacket pending = _pendingDamage;
        if (Main.netMode != NetmodeID.Server || !fromNet || !pending.Active || pending.NpcIndex != npc.whoAmI)
            return false;
        if (pending.PacketDamage >= 0 && pending.PacketDamage != hit.Damage)
            return false;

        _pendingDamage = default;
        playerIndex = pending.PlayerIndex;
        return true;
    }

    private readonly record struct PendingDamagePacket(bool Active, int PlayerIndex, int NpcIndex, int PacketDamage);
    private readonly record struct PendingBuffPacket(bool Active, int PlayerIndex);
}
