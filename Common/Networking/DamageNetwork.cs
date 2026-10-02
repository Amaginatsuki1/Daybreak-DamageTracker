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
    EncounterCompleted,
    PrivateDotDamage,
    ServerStateResponse
}

internal static class DamageNetwork
{
    // Provenance updates are pure data, arrive in bursts whenever minions fire, and
    // are tolerated late by the client registry (120-tick pending window). Batching
    // them per recipient per tick keeps summon-heavy fights from sending one packet
    // per spawned projectile.
    private const int MaximumProvenanceBatchSize = 32;
    private const int MaximumQueuedProvenanceEntriesPerRecipient = 256;
    private const int MaximumProvenanceBatchesPerTick = 32;
    private const int MaximumPrivateDotBatchSize = 32;
    private const byte PrivateDotBatchProtocolVersion = 2;
    private const int MaximumQueuedPrivateDotEntriesPerRecipient = 256;
    private const int MaximumPrivateDotBatchesPerTick = 32;
    private const int StateRequestCooldownTicks = 180;
    private const int MaximumPendingStateRequests = 256;

    private static readonly RecipientBatchQueue<EncounterPlayerKey, PendingProvenanceEntry> QueuedProvenance =
        new(
            MaximumQueuedProvenanceEntriesPerRecipient,
            MaximumProvenanceBatchSize,
            MaximumProvenanceBatchesPerTick);
    private static readonly RecipientBatchQueue<EncounterPlayerKey, PendingPrivateDotEntry> QueuedPrivateDot =
        new(
            MaximumQueuedPrivateDotEntriesPerRecipient,
            MaximumPrivateDotBatchSize,
            MaximumPrivateDotBatchesPerTick);
    private static readonly PendingStateRequestQueue QueuedStateRequests =
        new(MaximumPendingStateRequests);
    private static readonly Dictionary<EncounterPlayerKey, Queue<PendingOrderedMessage>> PendingOrderedMessages = [];
    private static int _nextHistoryBatchId;
    private static long _nextProvenanceQueueWarningTick;
    private static long _nextPrivateDotQueueWarningTick;
    private static readonly Dictionary<EncounterPlayerKey, long> LastStateRequestTick = [];
    private static readonly object StateRequestSync = new();
    private static int _nextClientStateRequestId;
    private static int _pendingClientStateRequestId;
    private static long _nextClientStateRequestTick;

    public static void Reset()
    {
        _nextHistoryBatchId = 0;
        _nextProvenanceQueueWarningTick = 0;
        _nextPrivateDotQueueWarningTick = 0;
        QueuedProvenance.Clear();
        QueuedPrivateDot.Clear();
        QueuedStateRequests.Clear();
        PendingOrderedMessages.Clear();
        lock (StateRequestSync)
            LastStateRequestTick.Clear();
        _nextClientStateRequestId = 0;
        _pendingClientStateRequestId = 0;
        _nextClientStateRequestTick = 0;
    }

    private readonly record struct PendingProvenanceEntry(int Identity, int ProjectileType, DamageRootKey Root);
    private readonly record struct PendingPrivateDotEntry(
        int NpcIndex,
        long NpcSpawnSerial,
        int NpcType,
        int RootNpcType,
        int BuffType,
        int Damage,
        DamageRootKey? ServerRoot,
        string ExactOwnerBossKey);
    private readonly record struct PendingOrderedMessage(
        long RequiredPrivateDotSequence,
        Action<int> Send);

    private static void ForgetStateRequest(EncounterPlayerKey connection)
    {
        lock (StateRequestSync)
            LastStateRequestTick.Remove(connection);
    }

    private static void ForgetPrivateQueues(EncounterPlayerKey connection)
    {
        QueuedProvenance.RemoveRecipientsWhere(key => key == connection);
        QueuedPrivateDot.RemoveRecipientsWhere(key => key == connection);
        PendingOrderedMessages.Remove(connection);
    }

    private static void FlushDisconnectedPlayers()
    {
        PlayerConnectionRegistry.FlushPendingDisconnects(connection =>
        {
            ServerConfigEditAuthorization.RevokeConnection(connection);
            ForgetStateRequest(connection);
            ForgetPrivateQueues(connection);
        });
    }

    public static void HandlePacket(BinaryReader reader, int whoAmI)
    {
        DamagePacketType type = (DamagePacketType)reader.ReadByte();
        switch (type)
        {
            case DamagePacketType.RequestServerState when Main.netMode == NetmodeID.Server:
            {
                int requestId = reader.ReadInt32();
                if (!CanServeStateRequest(
                        whoAmI,
                        out EncounterPlayerKey connection,
                        out int retryAfterTicks))
                {
                    SendStateResponse(whoAmI, requestId, retryAfterTicks);
                    break;
                }

                // HandlePacket can run on tML's network thread. Queue the accepted
                // request by exact connection generation; presentation, history and
                // the active encounter are all snapshotted/sent from PostUpdateWorld.
                if (!QueuedStateRequests.TryEnqueue(new PendingStateRequest(connection, requestId)))
                    SendStateResponse(whoAmI, requestId, StateRequestCooldownTicks);
                break;
            }

            case DamagePacketType.ServerStateResponse when Main.netMode == NetmodeID.MultiplayerClient:
            {
                int requestId = reader.ReadInt32();
                int retryAfterTicks = reader.Read7BitEncodedInt();
                if (retryAfterTicks < 0 || retryAfterTicks > StateRequestCooldownTicks)
                    throw new IOException($"Invalid state-request retry delay {retryAfterTicks}.");
                HandleStateResponse(requestId, retryAfterTicks);
                break;
            }

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
                int count = reader.ReadByte();
                if (count > MaximumProvenanceBatchSize)
                    throw new IOException($"Invalid provenance batch size {count}.");
                for (int i = 0; i < count; i++)
                {
                    int identity = reader.ReadInt32();
                    int projectileType = reader.Read7BitEncodedInt();
                    DamageRootKind kind = (DamageRootKind)reader.ReadByte();
                    int primaryType = reader.Read7BitEncodedInt();
                    if (ProjectileProvenancePolicy.IsValidProjectileType(
                            projectileType,
                            ProjectileID.None,
                            ProjectileLoader.ProjectileCount) &&
                        Enum.IsDefined(kind) &&
                        primaryType >= 0)
                    {
                        ProjectileProvenanceRegistry.ReceiveOwnerOnly(
                            Main.myPlayer,
                            identity,
                            projectileType,
                            new DamageRootKey(kind, primaryType));
                    }
                }
                break;
            }

            case DamagePacketType.PrivateDotDamage when Main.netMode == NetmodeID.MultiplayerClient:
            {
                byte version = reader.ReadByte();
                if (version != PrivateDotBatchProtocolVersion)
                    throw new IOException($"Unsupported private DoT batch version {version}.");
                int count = reader.ReadByte();
                if (count > MaximumPrivateDotBatchSize)
                    throw new IOException($"Invalid private DoT batch size {count}.");
                for (int i = 0; i < count; i++)
                {
                    int npcIndex = reader.ReadInt16();
                    long npcSpawnSerial = reader.ReadInt64();
                    int npcType = reader.Read7BitEncodedInt();
                    int rootNpcType = reader.ReadInt32();
                    int buffType = reader.Read7BitEncodedInt();
                    int damage = reader.Read7BitEncodedInt();
                    string exactOwnerBossKey = BoundedStringIO.Read(reader, 80, "private DoT owner key");
                    DamageRootKey? serverRoot = null;
                    if (reader.ReadBoolean())
                    {
                        DamageRootKind rootKind = (DamageRootKind)reader.ReadByte();
                        int primaryType = reader.Read7BitEncodedInt();
                        if (Enum.IsDefined(rootKind) && primaryType >= 0)
                            serverRoot = new DamageRootKey(rootKind, primaryType);
                    }
                    if (npcIndex >= 0 && npcIndex < Main.maxNPCs && npcSpawnSerial > 0 &&
                        npcType > NPCID.None && npcType < NPCLoader.NPCCount &&
                        rootNpcType >= -1 && rootNpcType < NPCLoader.NPCCount &&
                        buffType > 0 && buffType < BuffLoader.BuffCount &&
                        damage > 0)
                    {
                        DotDamageSystem.ReceivePrivateDamage(
                            npcIndex,
                            npcSpawnSerial,
                            npcType,
                            rootNpcType,
                            buffType,
                            damage,
                            serverRoot,
                            exactOwnerBossKey);
                    }
                }
                break;
            }
        }
    }

    public static void RequestServerState()
    {
        if (Main.netMode != NetmodeID.MultiplayerClient)
            return;

        _nextClientStateRequestId = _nextClientStateRequestId == int.MaxValue
            ? 1
            : _nextClientStateRequestId + 1;
        _pendingClientStateRequestId = _nextClientStateRequestId;
        SendStateRequest(_pendingClientStateRequestId);
        _nextClientStateRequestTick = (long)Main.GameUpdateCount + StateRequestCooldownTicks;
    }

    public static void UpdateClientStateRequest()
    {
        if (Main.netMode != NetmodeID.MultiplayerClient ||
            _pendingClientStateRequestId == 0 ||
            (long)Main.GameUpdateCount < _nextClientStateRequestTick)
        {
            return;
        }

        SendStateRequest(_pendingClientStateRequestId);
        _nextClientStateRequestTick = (long)Main.GameUpdateCount + StateRequestCooldownTicks;
    }

    private static void SendStateRequest(int requestId)
    {
        ModPacket packet = NewPacket(DamagePacketType.RequestServerState);
        packet.Write(requestId);
        SendChecked(packet, label: "server-state request");
    }

    private static void SendStateResponse(int toClient, int requestId, int retryAfterTicks)
    {
        if (toClient < 0 || toClient >= Main.maxPlayers || !Main.player[toClient].active)
            return;

        ModPacket packet = NewPacket(DamagePacketType.ServerStateResponse);
        packet.Write(requestId);
        packet.Write7BitEncodedInt(Math.Clamp(retryAfterTicks, 0, StateRequestCooldownTicks));
        SendChecked(packet, toClient, "server-state response");
    }

    private static void HandleStateResponse(int requestId, int retryAfterTicks)
    {
        if (requestId == 0 || requestId != _pendingClientStateRequestId)
            return;

        if (retryAfterTicks == 0)
        {
            _pendingClientStateRequestId = 0;
            _nextClientStateRequestTick = 0;
            return;
        }

        _nextClientStateRequestTick = (long)Main.GameUpdateCount + retryAfterTicks;
    }

    public static void SendPresentation(int toClient)
    {
        ModPacket packet = NewPacket(DamagePacketType.Presentation);
        PacketIO.WritePresentation(packet, ServerConfigService.Current.Presentation);
        SendChecked(packet, toClient, "presentation");
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
        if (toClient < 0)
        {
            SendOrderedToActiveConnections(client => SendEncounterArmedImmediate(encounterId, bosses, client));
            return;
        }

        SendEncounterArmedImmediate(encounterId, bosses, toClient);
    }

    private static void SendEncounterArmedImmediate(
        long encounterId,
        IReadOnlyList<BossPresentation> bosses,
        int toClient)
    {
        ModPacket packet = NewPacket(DamagePacketType.EncounterArmed);
        packet.Write(encounterId);
        PacketIO.WriteBosses(packet, bosses);
        if (!ModPacketBudget.FitsCompletePacket(packet.BaseStream.Position))
        {
            packet.Close();
            packet = NewPacket(DamagePacketType.EncounterArmed);
            packet.Write(encounterId);
            PacketIO.WriteBosses(packet, bosses, includeAssociationMetadata: false);
            DaybreakDamageTrackerMod.Instance.Logger.Warn(
                $"Trimmed association metadata from an oversized encounter-armed packet for encounter {encounterId}.");
        }
        SendChecked(packet, toClient, "encounter armed");
    }

    public static void SendEncounterStarted(long encounterId, int toClient = -1)
    {
        if (toClient < 0)
        {
            SendOrderedToActiveConnections(client => SendEncounterStartedImmediate(encounterId, client));
            return;
        }

        SendEncounterStartedImmediate(encounterId, toClient);
    }

    private static void SendEncounterStartedImmediate(long encounterId, int toClient)
    {
        ModPacket packet = NewPacket(DamagePacketType.EncounterStarted);
        packet.Write(encounterId);
        SendChecked(packet, toClient, "encounter started");
    }

    public static void SendEncounterCancelled(long encounterId)
    {
        SendOrderedToActiveConnections(client => SendEncounterCancelledImmediate(encounterId, client));
    }

    private static void SendEncounterCancelledImmediate(long encounterId, int toClient)
    {
        ModPacket packet = NewPacket(DamagePacketType.EncounterCancelled);
        packet.Write(encounterId);
        SendChecked(packet, toClient, "encounter cancelled");
    }

    public static void SendEncounterSuspended(long encounterId, int toClient = -1)
    {
        if (toClient < 0)
        {
            SendOrderedToActiveConnections(client => SendEncounterSuspendedImmediate(encounterId, client));
            return;
        }

        SendEncounterSuspendedImmediate(encounterId, toClient);
    }

    private static void SendEncounterSuspendedImmediate(long encounterId, int toClient)
    {
        ModPacket packet = NewPacket(DamagePacketType.EncounterSuspended);
        packet.Write(encounterId);
        SendChecked(packet, toClient, "encounter suspended");
    }

    public static void SendEncounterCompleted(long encounterId)
    {
        SendOrderedToActiveConnections(client => SendEncounterCompletedImmediate(encounterId, client));
    }

    private static void SendEncounterCompletedImmediate(long encounterId, int toClient)
    {
        ModPacket packet = NewPacket(DamagePacketType.EncounterCompleted);
        packet.Write(encounterId);
        SendChecked(packet, toClient, "encounter completed");
    }

    public static void EnqueueProjectileProvenance(
        int owner,
        int identity,
        int projectileType,
        DamageRootKey root)
    {
        if (Main.netMode != NetmodeID.Server ||
            owner < 0 ||
            owner >= Main.maxPlayers ||
            !Main.player[owner].active ||
            identity < 0 ||
            !ProjectileProvenancePolicy.IsValidProjectileType(
                projectileType,
                ProjectileID.None,
                ProjectileLoader.ProjectileCount) ||
            root.PrimaryType < 0)
        {
            return;
        }

        EncounterPlayerKey recipient = PlayerConnectionRegistry.GetCurrent(owner);
        bool queued = QueuedProvenance.TryEnqueue(
            recipient,
            new PendingProvenanceEntry(identity, projectileType, root));
        if (!queued)
        {
            long nowTick = (long)Main.GameUpdateCount;
            if (nowTick >= _nextProvenanceQueueWarningTick)
            {
                DaybreakDamageTrackerMod.Instance.Logger.Warn(
                    "Projectile provenance queue reached its bounded per-recipient capacity; " +
                    "some private source leaves may use projectile-type fallback.");
                _nextProvenanceQueueWarningTick = nowTick + 600;
            }
        }
    }

    public static void FlushProjectileProvenance()
    {
        if (Main.netMode != NetmodeID.Server)
            return;

        // This is the first networking action in PostUpdateWorld. Drain disconnects
        // before either private queue so a reused slot can never receive its previous
        // connection's provenance, DoT rows, grants, or request throttle state.
        FlushDisconnectedPlayers();

        if (QueuedProvenance.RecipientCount > 0)
        {
            foreach (RecipientBatch<EncounterPlayerKey, PendingProvenanceEntry> batch in QueuedProvenance.Drain())
            {
                if (!PlayerConnectionRegistry.IsCurrent(batch.Recipient))
                    continue;

                ModPacket packet = NewPacket(DamagePacketType.ProjectileProvenance);
                packet.Write((byte)batch.Entries.Count);
                foreach (PendingProvenanceEntry entry in batch.Entries)
                {
                    packet.Write(entry.Identity);
                    packet.Write7BitEncodedInt(entry.ProjectileType);
                    packet.Write((byte)entry.Root.Kind);
                    packet.Write7BitEncodedInt(entry.Root.PrimaryType);
                }
                SendChecked(packet, batch.Recipient.PlayerIndex, "projectile provenance");
            }
        }

        FlushPrivateDotDamage();
        FlushPendingOrderedMessages();
        FlushStateRequests();
    }

    private static void FlushStateRequests()
    {
        foreach (PendingStateRequest request in QueuedStateRequests.Drain())
        {
            if (!PlayerConnectionRegistry.IsCurrent(request.Connection))
                continue;
            if (PendingOrderedMessages.ContainsKey(request.Connection))
            {
                QueuedStateRequests.TryEnqueue(request);
                continue;
            }

            int toClient = request.Connection.PlayerIndex;
            SendStateResponse(toClient, request.RequestId, retryAfterTicks: 0);
            SendPresentation(toClient);
            SendHistory(toClient);
            EncounterSystem.SendCurrentStateToClient(toClient);
        }
    }

    public static void SendPrivateDotDamage(
        DotDamageOwner owner,
        int npcIndex,
        TargetSnapshot target,
        int buffType,
        int damage,
        DamageRootKey? serverRoot)
    {
        if (damage <= 0)
            return;

        // Freeze the authoritative owner with the damage atom. A bounded queue may
        // deliver this after its NPC slot has already been reused by another Boss.
        string exactOwnerBossKey = EncounterSystem.GetPrivateTargetOwnerKey(target);
        if (Main.netMode == NetmodeID.Server)
            exactOwnerBossKey = BoundedStringIO.StableIdentity(exactOwnerBossKey, 80);

        if (Main.netMode == NetmodeID.SinglePlayer)
        {
            if (owner.Key.PlayerIndex == Main.myPlayer)
            {
                DotDamageSystem.ReceivePrivateDamage(
                    npcIndex,
                    target.Instance.SpawnSerial,
                    target.NpcType,
                    target.RootNpcType,
                    buffType,
                    damage,
                    serverRoot,
                    exactOwnerBossKey);
            }
            return;
        }

        if (Main.netMode != NetmodeID.Server ||
            !PlayerConnectionRegistry.IsCurrent(owner.Key) ||
            !target.Instance.IsValid || target.Instance.Slot != npcIndex ||
            npcIndex < 0 || npcIndex >= Main.maxNPCs ||
            target.NpcType <= NPCID.None || target.NpcType >= NPCLoader.NPCCount ||
            target.RootNpcType < -1 || target.RootNpcType >= NPCLoader.NPCCount ||
            buffType <= 0 || buffType >= BuffLoader.BuffCount)
        {
            return;
        }

        if (serverRoot.HasValue &&
            (!Enum.IsDefined(serverRoot.Value.Kind) || serverRoot.Value.PrimaryType < 0))
        {
            serverRoot = null;
        }

        bool queued = QueuedPrivateDot.TryEnqueue(
            owner.Key,
            new PendingPrivateDotEntry(
                npcIndex,
                target.Instance.SpawnSerial,
                target.NpcType,
                target.RootNpcType,
                buffType,
                damage,
                serverRoot,
                exactOwnerBossKey));
        if (!queued)
        {
            long nowTick = (long)Main.GameUpdateCount;
            if (nowTick >= _nextPrivateDotQueueWarningTick)
            {
                DaybreakDamageTrackerMod.Instance.Logger.Warn(
                    "Private DoT queue reached its bounded per-recipient capacity; " +
                    "the final private source tree may reconcile part of the excess as unknown damage.");
                _nextPrivateDotQueueWarningTick = nowTick + 600;
            }
        }
    }

    private static void FlushPrivateDotDamage()
    {
        if (QueuedPrivateDot.RecipientCount == 0)
            return;

        foreach (RecipientBatch<EncounterPlayerKey, PendingPrivateDotEntry> batch in QueuedPrivateDot.Drain())
        {
            if (!PlayerConnectionRegistry.IsCurrent(batch.Recipient))
                continue;

            ModPacket packet = NewPacket(DamagePacketType.PrivateDotDamage);
            packet.Write(PrivateDotBatchProtocolVersion);
            packet.Write((byte)batch.Entries.Count);
            foreach (PendingPrivateDotEntry entry in batch.Entries)
            {
                packet.Write((short)entry.NpcIndex);
                packet.Write(entry.NpcSpawnSerial);
                packet.Write7BitEncodedInt(entry.NpcType);
                packet.Write(entry.RootNpcType);
                packet.Write7BitEncodedInt(entry.BuffType);
                packet.Write7BitEncodedInt(entry.Damage);
                packet.Write(entry.ExactOwnerBossKey);
                packet.Write(entry.ServerRoot.HasValue);
                if (entry.ServerRoot.HasValue)
                {
                    packet.Write((byte)entry.ServerRoot.Value.Kind);
                    packet.Write7BitEncodedInt(entry.ServerRoot.Value.PrimaryType);
                }
            }
            SendChecked(packet, batch.Recipient.PlayerIndex, "private DoT batch");
        }
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
            ResultRecipients recipients = ResultRecipientPolicy.FailClosed(ServerConfigService.Current.Recipients);
            if (!ResultRecipientPolicy.CanReceive(recipients, recipients, ownDamage.HasValue))
                continue;

            // For a live delivery the active recipient is known to have dealt either the
            // matched amount or zero, even when the public ledger has no row for them.
            // Hold the result behind that connection's current private-DoT cutoff so
            // OnPublicResult cannot freeze/reconcile the source tree before older rows.
            SendOrdered(
                recipient,
                QueuedPrivateDot.GetEnqueuedSequence(recipient),
                client => SendResult(result, client, ownDamage ?? 0));
        }
    }

    private static void SendOrderedToActiveConnections(Action<int> send)
    {
        PlayerConnectionRegistry.Update();
        for (int playerIndex = 0; playerIndex < Main.maxPlayers; playerIndex++)
        {
            if (!Main.player[playerIndex].active)
                continue;

            EncounterPlayerKey recipient = PlayerConnectionRegistry.GetCurrent(playerIndex);
            SendOrdered(
                recipient,
                QueuedPrivateDot.GetEnqueuedSequence(recipient),
                send);
        }
    }

    private static void SendOrdered(
        EncounterPlayerKey recipient,
        long requiredPrivateDotSequence,
        Action<int> send)
    {
        if (!PendingOrderedMessages.TryGetValue(recipient, out Queue<PendingOrderedMessage>? pending) &&
            QueuedPrivateDot.IsDrainedThrough(recipient, requiredPrivateDotSequence))
        {
            send(recipient.PlayerIndex);
            return;
        }

        if (pending is null)
            PendingOrderedMessages[recipient] = pending = [];
        pending.Enqueue(new PendingOrderedMessage(requiredPrivateDotSequence, send));
    }

    private static void FlushPendingOrderedMessages()
    {
        foreach (EncounterPlayerKey recipient in PendingOrderedMessages.Keys.ToArray())
        {
            if (!PlayerConnectionRegistry.IsCurrent(recipient))
            {
                PendingOrderedMessages.Remove(recipient);
                continue;
            }

            Queue<PendingOrderedMessage> pending = PendingOrderedMessages[recipient];
            while (pending.Count > 0)
            {
                PendingOrderedMessage next = pending.Peek();
                if (!QueuedPrivateDot.IsDrainedThrough(recipient, next.RequiredPrivateDotSequence))
                    break;

                pending.Dequeue();
                next.Send(recipient.PlayerIndex);
            }

            if (pending.Count == 0)
                PendingOrderedMessages.Remove(recipient);
        }
    }

    public static void SendHistory(int toClient)
    {
        if (toClient < 0 || toClient >= Main.maxPlayers || !Main.player[toClient].active)
            return;

        PlayerConnectionRegistry.Update();
        EncounterPlayerKey recipient = PlayerConnectionRegistry.GetCurrent(toClient);
        List<(PublicResultSnapshot Result, long? OwnDamage)> deliveries = [];
        ResultRecipients currentRecipients = ResultRecipientPolicy.FailClosed(
            ServerConfigService.Current.Recipients);
        foreach (ServerResultHistoryEntry history in ServerResultHistory.All.Take(PresentationSettings.MaximumHistoryCount))
        {
            PublicResultSnapshot result = history.Result;
            long? ownDamage = FindOwnDamage(result, recipient);
            if (!ResultRecipientPolicy.CanReceive(
                    history.FrozenRecipients,
                    currentRecipients,
                    ownDamage.HasValue))
            {
                continue;
            }

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
        SendChecked(begin, toClient, "history begin");

        for (int index = 0; index < deliveries.Count; index++)
        {
            (PublicResultSnapshot result, long? ownDamage) = deliveries[index];
            ModPacket entry = BuildBudgetedResultPacket(
                DamagePacketType.HistorySyncEntry,
                result,
                ownDamage,
                packet =>
                {
                    packet.Write(batchId);
                    packet.Write7BitEncodedInt(index);
                });
            SendChecked(entry, toClient, "history result");
        }
    }

    private static void SendResult(PublicResultSnapshot result, int toClient, long ownDamage)
    {
        ModPacket packet = BuildBudgetedResultPacket(
            DamagePacketType.PublicResult,
            result,
            ownDamage,
            prefix: null);
        SendChecked(packet, toClient, "public result");
    }

    private static long? FindOwnDamage(PublicResultSnapshot result, EncounterPlayerKey recipient)
        => result.RecipientDamage.TryGetValue(recipient, out long damage) ? damage : null;

    private static bool CanServeStateRequest(
        int whoAmI,
        out EncounterPlayerKey connection,
        out int retryAfterTicks)
    {
        connection = default;
        retryAfterTicks = 0;
        if (whoAmI < 0 || whoAmI >= Main.maxPlayers || !Main.player[whoAmI].active)
            return false;

        connection = PlayerConnectionRegistry.GetCurrent(whoAmI);
        long now = (long)Main.GameUpdateCount;
        lock (StateRequestSync)
        {
            if (LastStateRequestTick.TryGetValue(connection, out long last))
            {
                retryAfterTicks = StateRequestRetryPolicy.GetRetryAfterTicks(
                    now,
                    last,
                    StateRequestCooldownTicks);
                if (retryAfterTicks > 0)
                    return false;
            }

            LastStateRequestTick[connection] = now;
        }
        return true;
    }

    private static ModPacket BuildBudgetedResultPacket(
        DamagePacketType type,
        PublicResultSnapshot result,
        long? ownDamage,
        Action<ModPacket>? prefix)
    {
        PresentationSettings presentation = ServerConfigService.Current.Presentation.SanitizedClone();
        int maximumPlayers = RankingProjectionPolicy.GetVisiblePlayerCount(
            presentation.ShowRanking,
            presentation.RankingRows,
            result.Players.Count,
            Main.maxPlayers);
        int maximumBosses = Math.Min(result.Bosses.Count, 32);

        ModPacket packet = BuildResultPacket(
            type,
            result,
            ownDamage,
            presentation,
            prefix,
            maximumBosses,
            maximumPlayers,
            includeAssociationMetadata: true);
        if (ModPacketBudget.FitsCompletePacket(packet.BaseStream.Position))
            return packet;

        packet.Close();
        bool omittedAssociations = true;
        while (true)
        {
            packet = BuildResultPacket(
                type,
                result,
                ownDamage,
                presentation,
                prefix,
                maximumBosses,
                maximumPlayers,
                includeAssociationMetadata: false);
            if (ModPacketBudget.FitsCompletePacket(packet.BaseStream.Position))
                break;

            packet.Close();
            if (maximumPlayers > 0)
            {
                maximumPlayers /= 2;
                continue;
            }
            if (maximumBosses > 1)
            {
                maximumBosses /= 2;
                continue;
            }

            throw new InvalidOperationException(
                $"A compact {type} result still exceeds the {ModPacketBudget.MaximumPacketBytes}-byte ModPacket limit.");
        }

        DaybreakDamageTrackerMod.Instance.Logger.Warn(
            $"Trimmed oversized {type} result for encounter {result.EncounterId}: " +
            $"associationMetadataOmitted={omittedAssociations}, players={maximumPlayers}, bosses={maximumBosses}.");
        return packet;
    }

    private static ModPacket BuildResultPacket(
        DamagePacketType type,
        PublicResultSnapshot result,
        long? ownDamage,
        PresentationSettings presentation,
        Action<ModPacket>? prefix,
        int maximumBosses,
        int maximumPlayers,
        bool includeAssociationMetadata)
    {
        ModPacket packet = NewPacket(type);
        prefix?.Invoke(packet);
        PacketIO.WriteResult(
            packet,
            result,
            ownDamage,
            presentation,
            maximumBosses,
            maximumPlayers,
            includeAssociationMetadata);
        return packet;
    }

    private static bool SendChecked(
        ModPacket packet,
        int toClient = -1,
        string label = "packet")
    {
        long bytes = packet.BaseStream.Position;
        if (!ModPacketBudget.FitsCompletePacket(bytes))
        {
            packet.Close();
            DaybreakDamageTrackerMod.Instance.Logger.Error(
                $"Refused oversized {label}: {bytes} bytes exceeds the " +
                $"{ModPacketBudget.MaximumPacketBytes}-byte ModPacket limit.");
            return false;
        }

        packet.Send(toClient);
        return true;
    }

    private static ModPacket NewPacket(DamagePacketType type)
    {
        ModPacket packet = DaybreakDamageTrackerMod.Instance.GetPacket();
        packet.Write((byte)type);
        return packet;
    }
}
