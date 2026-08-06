using DaybreakDamageTracker.Client;
using DaybreakDamageTracker.Common.Config;
using DaybreakDamageTracker.Common.Data;
using DaybreakDamageTracker.Common.Projectiles;
using DaybreakDamageTracker.Common.Systems;

namespace DaybreakDamageTracker.Common.Networking;

internal enum DamagePacketType : byte
{
    RequestServerState,
    Presentation,
    EncounterArmed,
    EncounterStarted,
    EncounterCancelled,
    PublicResult,
    HistorySyncBegin,
    HistorySyncEntry,
    ProjectileProvenance,
    EncounterSuspended,
    EncounterCompleted
}

internal static class DamageNetwork
{
    private static int _nextHistoryBatchId;
    private static readonly Dictionary<EncounterPlayerKey, long> LastStateRequestTick = [];

    public static void Reset()
    {
        _nextHistoryBatchId = 0;
        LastStateRequestTick.Clear();
    }

    public static void ForgetStateRequestsForPlayer(int playerIndex)
    {
        if (playerIndex < 0 || playerIndex >= Main.maxPlayers || LastStateRequestTick.Count == 0)
            return;

        foreach (EncounterPlayerKey key in LastStateRequestTick.Keys
                     .Where(key => key.PlayerIndex == playerIndex)
                     .ToArray())
        {
            LastStateRequestTick.Remove(key);
        }
    }

    public static void HandlePacket(BinaryReader reader, int whoAmI)
    {
        DamagePacketType type = (DamagePacketType)reader.ReadByte();
        switch (type)
        {
            case DamagePacketType.RequestServerState when Main.netMode == NetmodeID.Server:
                if (!CanServeStateRequest(whoAmI))
                    break;
                SendPresentation(whoAmI);
                SendHistory(whoAmI);
                EncounterSystem.SendCurrentStateToClient(whoAmI);
                break;

            case DamagePacketType.Presentation when Main.netMode == NetmodeID.MultiplayerClient:
                ClientRuntime.ApplyPresentation(PacketIO.ReadPresentation(reader));
                break;

            case DamagePacketType.EncounterArmed when Main.netMode == NetmodeID.MultiplayerClient:
                ClientRuntime.OnEncounterArmed(reader.ReadInt64(), PacketIO.ReadBosses(reader));
                break;

            case DamagePacketType.EncounterStarted when Main.netMode == NetmodeID.MultiplayerClient:
                ClientRuntime.OnEncounterStarted(reader.ReadInt64());
                break;

            case DamagePacketType.EncounterCancelled when Main.netMode == NetmodeID.MultiplayerClient:
                ClientRuntime.OnEncounterCancelled(reader.ReadInt64());
                break;

            case DamagePacketType.EncounterSuspended when Main.netMode == NetmodeID.MultiplayerClient:
                ClientRuntime.OnEncounterSuspended(reader.ReadInt64());
                break;

            case DamagePacketType.EncounterCompleted when Main.netMode == NetmodeID.MultiplayerClient:
                ClientRuntime.OnEncounterCompleted(reader.ReadInt64());
                break;

            case DamagePacketType.PublicResult when Main.netMode == NetmodeID.MultiplayerClient:
            {
                ResultDelivery delivery = PacketIO.ReadResult(reader);
                ClientRuntime.OnPublicResult(delivery.Result, delivery.OwnDamage);
                break;
            }

            case DamagePacketType.HistorySyncBegin when Main.netMode == NetmodeID.MultiplayerClient:
            {
                int batchId = reader.ReadInt32();
                int count = reader.Read7BitEncodedInt();
                if (count < 0 || count > 10)
                    throw new IOException($"Invalid history count {count}.");
                ClientRuntime.OnHistorySyncBegin(batchId, count);
                break;
            }

            case DamagePacketType.HistorySyncEntry when Main.netMode == NetmodeID.MultiplayerClient:
            {
                int batchId = reader.ReadInt32();
                int index = reader.Read7BitEncodedInt();
                ResultDelivery delivery = PacketIO.ReadResult(reader);
                ClientRuntime.OnHistorySyncEntry(batchId, index, delivery);
                break;
            }

            case DamagePacketType.ProjectileProvenance when Main.netMode == NetmodeID.MultiplayerClient:
            {
                int owner = reader.ReadByte();
                int identity = reader.ReadInt32();
                int projectileType = reader.Read7BitEncodedInt();
                DamageRootKind kind = (DamageRootKind)reader.ReadByte();
                int primaryType = reader.Read7BitEncodedInt();
                if (projectileType >= ProjectileID.None &&
                    projectileType < ProjectileLoader.ProjectileCount &&
                    Enum.IsDefined(kind) &&
                    primaryType >= 0)
                {
                    ProjectileProvenanceRegistry.ReceiveOwnerOnly(
                        owner,
                        identity,
                        projectileType,
                        new DamageRootKey(kind, primaryType));
                }
                break;
            }
        }
    }

    public static void RequestServerState()
    {
        if (Main.netMode != NetmodeID.MultiplayerClient)
            return;

        ModPacket packet = NewPacket(DamagePacketType.RequestServerState);
        packet.Send();
    }

    public static void SendPresentation(int toClient)
    {
        ModPacket packet = NewPacket(DamagePacketType.Presentation);
        PacketIO.WritePresentation(packet, ServerConfigService.Current.Presentation);
        packet.Send(toClient);
    }

    public static void BroadcastPresentation()
    {
        if (Main.netMode != NetmodeID.Server)
            return;
        for (int i = 0; i < Main.maxPlayers; i++)
        {
            if (Main.player[i].active)
                SendPresentation(i);
        }
    }

    public static void SendEncounterArmed(long encounterId, IReadOnlyList<BossPresentation> bosses, int toClient = -1)
    {
        ModPacket packet = NewPacket(DamagePacketType.EncounterArmed);
        packet.Write(encounterId);
        PacketIO.WriteBosses(packet, bosses);
        packet.Send(toClient);
    }

    public static void SendEncounterStarted(long encounterId, int toClient = -1)
    {
        ModPacket packet = NewPacket(DamagePacketType.EncounterStarted);
        packet.Write(encounterId);
        packet.Send(toClient);
    }

    public static void SendEncounterCancelled(long encounterId)
    {
        ModPacket packet = NewPacket(DamagePacketType.EncounterCancelled);
        packet.Write(encounterId);
        packet.Send();
    }

    public static void SendEncounterSuspended(long encounterId, int toClient = -1)
    {
        ModPacket packet = NewPacket(DamagePacketType.EncounterSuspended);
        packet.Write(encounterId);
        packet.Send(toClient);
    }

    public static void SendEncounterCompleted(long encounterId)
    {
        ModPacket packet = NewPacket(DamagePacketType.EncounterCompleted);
        packet.Write(encounterId);
        packet.Send();
    }

    public static void SendProjectileProvenance(Projectile projectile, DamageRootKey root)
    {
        if (Main.netMode != NetmodeID.Server ||
            projectile.owner < 0 ||
            projectile.owner >= Main.maxPlayers ||
            !Main.player[projectile.owner].active ||
            projectile.identity < 0 ||
            root.PrimaryType < 0)
        {
            return;
        }

        ModPacket packet = NewPacket(DamagePacketType.ProjectileProvenance);
        packet.Write((byte)projectile.owner);
        packet.Write(projectile.identity);
        packet.Write7BitEncodedInt(projectile.type);
        packet.Write((byte)root.Kind);
        packet.Write7BitEncodedInt(root.PrimaryType);
        packet.Send(projectile.owner);
    }

    public static void SendResultToConfiguredRecipients(PublicResultSnapshot result)
    {
        PlayerConnectionRegistry.Update();
        for (int i = 0; i < Main.maxPlayers; i++)
        {
            if (!Main.player[i].active)
                continue;

            EncounterPlayerKey recipient = PlayerConnectionRegistry.GetCurrent(i);
            long? ownDamage = FindOwnDamage(result, recipient);
            if (ServerConfigService.Current.Recipients == ResultRecipients.Contributors && !ownDamage.HasValue)
                continue;

            // For a live delivery the active recipient is known to have dealt either the
            // matched amount or zero, even when the public ledger has no row for them.
            SendResult(result, i, ownDamage ?? 0);
        }
    }

    public static void SendHistory(int toClient)
    {
        if (toClient < 0 || toClient >= Main.maxPlayers || !Main.player[toClient].active)
            return;

        PlayerConnectionRegistry.Update();
        EncounterPlayerKey recipient = PlayerConnectionRegistry.GetCurrent(toClient);
        List<(PublicResultSnapshot Result, long? OwnDamage)> deliveries = [];
        foreach (PublicResultSnapshot result in ServerResultHistory.All.Take(PresentationSettings.MaximumHistoryCount))
        {
            long? ownDamage = FindOwnDamage(result, recipient);
            if (ServerConfigService.Current.Recipients == ResultRecipients.Contributors && !ownDamage.HasValue)
                continue;

            // A missing exact identity on an old result is deliberately unknown, not zero:
            // the current connection may have joined after that encounter.
            deliveries.Add((result, ownDamage));
        }

        int batchId;
        if (_nextHistoryBatchId == int.MaxValue)
            batchId = _nextHistoryBatchId = 1;
        else
            batchId = ++_nextHistoryBatchId;
        ModPacket begin = NewPacket(DamagePacketType.HistorySyncBegin);
        begin.Write(batchId);
        begin.Write7BitEncodedInt(deliveries.Count);
        begin.Send(toClient);

        // One result per packet stays safely below ModPacket's 65535-byte ceiling even
        // with a full 255-player ranking.
        for (int index = 0; index < deliveries.Count; index++)
        {
            (PublicResultSnapshot result, long? ownDamage) = deliveries[index];
            ModPacket entry = NewPacket(DamagePacketType.HistorySyncEntry);
            entry.Write(batchId);
            entry.Write7BitEncodedInt(index);
            PacketIO.WriteResult(entry, result, ownDamage);
            entry.Send(toClient);
        }
    }

    private static void SendResult(PublicResultSnapshot result, int toClient, long ownDamage)
    {
        ModPacket packet = NewPacket(DamagePacketType.PublicResult);
        PacketIO.WriteResult(packet, result, ownDamage);
        packet.Send(toClient);
    }

    private static long? FindOwnDamage(PublicResultSnapshot result, EncounterPlayerKey recipient)
        => result.RecipientDamage.TryGetValue(recipient, out long damage) ? damage : null;

    private static bool CanServeStateRequest(int whoAmI)
    {
        if (whoAmI < 0 || whoAmI >= Main.maxPlayers || !Main.player[whoAmI].active)
            return false;

        EncounterPlayerKey key = PlayerConnectionRegistry.GetCurrent(whoAmI);
        long now = (long)Main.GameUpdateCount;
        if (LastStateRequestTick.TryGetValue(key, out long last) && now - last < 180)
            return false;

        LastStateRequestTick[key] = now;
        return true;
    }

    private static ModPacket NewPacket(DamagePacketType type)
    {
        ModPacket packet = DaybreakDamageTrackerMod.Instance.GetPacket();
        packet.Write((byte)type);
        return packet;
    }
}
