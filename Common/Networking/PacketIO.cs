using DaybreakDamageTracker.Common.Data;

namespace DaybreakDamageTracker.Common.Networking;

internal readonly record struct ResultDelivery(PublicResultSnapshot Result, long? OwnDamage);

internal static class PacketIO
{
    private const int MaximumBossTextLength = 80;
    private const int MaximumLocalizationKeyLength = 256;
    private const int MaximumPlayerNameLength = 24;
    private const int MaximumNpcTypesPerBoss = 256;
    private const int MaximumNpcInstancesPerBoss = 256;

    public static void WritePresentation(BinaryWriter writer, PresentationSettings value)
    {
        writer.Write((byte)2);
        writer.Write(value.AutoShow);
        writer.Write(value.AutoShowSeconds);
        writer.Write(value.FadeSeconds);
        writer.Write(value.ShowBossHeader);
        writer.Write(value.ShowTeamTotal);
        writer.Write(value.ShowBossBodyDamage);
        writer.Write(value.ShowDuration);
        writer.Write(value.ShowRanking);
        writer.Write(value.ShowPersonalBreakdown);
        writer.Write(value.ShowUnattributedDamage);
        writer.Write(value.ShowFinalBlow);
        writer.Write7BitEncodedInt(value.RankingRows);
        writer.Write7BitEncodedInt(value.SourceRootRows);
        writer.Write7BitEncodedInt(value.SourceLeafRows);
        writer.Write7BitEncodedInt(value.HistoryCount);
        writer.Write(value.ChatSummary);
    }

    public static PresentationSettings ReadPresentation(BinaryReader reader)
    {
        byte version = reader.ReadByte();
        if (version is not (1 or 2))
            throw new IOException($"Unsupported presentation settings version {version}.");

        return new PresentationSettings
        {
            AutoShow = reader.ReadBoolean(),
            AutoShowSeconds = reader.ReadSingle(),
            FadeSeconds = reader.ReadSingle(),
            ShowBossHeader = reader.ReadBoolean(),
            ShowTeamTotal = reader.ReadBoolean(),
            ShowBossBodyDamage = reader.ReadBoolean(),
            ShowDuration = reader.ReadBoolean(),
            ShowRanking = reader.ReadBoolean(),
            ShowPersonalBreakdown = reader.ReadBoolean(),
            ShowUnattributedDamage = reader.ReadBoolean(),
            // Presentation version 1 predates the final-blow line; keep it visible.
            ShowFinalBlow = version >= 2 ? reader.ReadBoolean() : true,
            RankingRows = reader.Read7BitEncodedInt(),
            SourceRootRows = reader.Read7BitEncodedInt(),
            SourceLeafRows = reader.Read7BitEncodedInt(),
            HistoryCount = reader.Read7BitEncodedInt(),
            ChatSummary = reader.ReadBoolean()
        }.SanitizedClone();
    }

    public static void WriteBosses(
        BinaryWriter writer,
        IReadOnlyList<BossPresentation> bosses,
        int maximumBossCount = 32,
        bool includeAssociationMetadata = true,
        bool includeBodyDamage = true,
        bool includeFinalBlow = true)
    {
        writer.Write((byte)1); // Association metadata includes authoritative spawn serials.
        int bossCount = Math.Clamp(maximumBossCount, 0, 32);
        writer.Write7BitEncodedInt(Math.Min(bosses.Count, bossCount));
        foreach (BossPresentation boss in bosses.Take(bossCount))
        {
            writer.Write(BoundedStringIO.StableIdentity(boss.Key, MaximumBossTextLength));
            writer.Write(Bounded(boss.NameLocalizationKey, MaximumLocalizationKeyLength));
            writer.Write7BitEncodedInt(Math.Max(-1, boss.NameNpcType) + 1);
            writer.Write(Bounded(boss.Name, MaximumBossTextLength));
            writer.Write7BitEncodedInt(Math.Max(0, boss.PortraitNpcType));
            writer.Write((byte)boss.Outcome);
            writer.Write(includeBodyDamage ? Math.Max(0, boss.BodyDamage) : 0);
            writer.Write(includeFinalBlow
                ? Bounded(boss.FinalBlowPlayerName, MaximumPlayerNameLength)
                : string.Empty);
            writer.Write((byte)(includeFinalBlow ? boss.FinalBlowKind : FinalBlowKind.None));
            WriteNpcTypes(writer, includeAssociationMetadata ? boss.BodyNpcTypes : []);
            WriteNpcTypes(writer, includeAssociationMetadata ? boss.AssociatedNpcTypes : []);
            WriteNpcInstances(writer, includeAssociationMetadata ? boss.AssociatedNpcInstances : []);
        }
    }

    public static List<BossPresentation> ReadBosses(BinaryReader reader)
    {
        byte version = reader.ReadByte();
        if (version != 1)
            throw new IOException($"Unsupported Boss metadata version {version}.");
        int count = ReadBoundedCount(reader, 32, "boss");
        List<BossPresentation> bosses = new(count);
        for (int i = 0; i < count; i++)
        {
            bosses.Add(new BossPresentation
            {
                Key = BoundedStringIO.Read(reader, MaximumBossTextLength, "boss key"),
                NameLocalizationKey = BoundedStringIO.Read(reader, MaximumLocalizationKeyLength, "localization key"),
                NameNpcType = reader.Read7BitEncodedInt() - 1,
                Name = BoundedStringIO.Read(reader, MaximumBossTextLength, "boss name"),
                PortraitNpcType = reader.Read7BitEncodedInt(),
                Outcome = ReadOutcome(reader),
                BodyDamage = Math.Max(0, reader.ReadInt64()),
                FinalBlowPlayerName = BoundedStringIO.Read(reader, MaximumPlayerNameLength, "final-blow player name"),
                FinalBlowKind = ReadFinalBlowKind(reader),
                BodyNpcTypes = ReadNpcTypes(reader),
                AssociatedNpcTypes = ReadNpcTypes(reader),
                AssociatedNpcInstances = ReadNpcInstances(reader)
            });
        }
        return bosses;
    }

    public static void WriteResult(
        BinaryWriter writer,
        PublicResultSnapshot result,
        long? ownDamage,
        PresentationSettings presentation,
        int maximumBossCount = 32,
        int maximumPlayerCount = int.MaxValue,
        bool includeAssociationMetadata = true)
    {
        ResultDeliveryProjection projection = ResultDeliveryProjectionPolicy.Create(
            presentation.ShowTeamTotal,
            presentation.ShowBossBodyDamage,
            presentation.ShowDuration,
            presentation.ShowRanking,
            presentation.ShowUnattributedDamage,
            presentation.ShowFinalBlow);

        writer.Write((byte)10);
        writer.Write(result.EncounterId);
        writer.Write7BitEncodedInt(Math.Max(1, result.BossOccurrence));
        writer.Write(result.EncounterComplete);
        writer.Write((byte)result.Outcome);
        writer.Write(projection.IncludeDuration ? Math.Max(0, result.DurationTicks) : 0);
        writer.Write(projection.IncludeTeamDamage ? Math.Max(0, result.TeamDamage) : 0);
        writer.Write(projection.IncludeBossBodyDamage ? Math.Max(0, result.BossBodyDamage) : 0);
        writer.Write(projection.IncludeUnattributedDamage ? Math.Max(0, result.UnattributedDamage) : 0);
        WriteBosses(
            writer,
            result.Bosses,
            maximumBossCount,
            includeAssociationMetadata,
            projection.IncludeBossBodyDamage,
            projection.IncludeFinalBlow);

        int visiblePlayerCount = RankingProjectionPolicy.GetVisiblePlayerCount(
            projection.IncludeRanking,
            presentation.RankingRows,
            result.Players.Count,
            Math.Min(maximumPlayerCount, Main.maxPlayers));
        writer.Write7BitEncodedInt(visiblePlayerCount);
        foreach (PlayerResultRow row in result.Players.Take(visiblePlayerCount))
        {
            writer.Write(Bounded(row.PlayerName, MaximumPlayerNameLength));
            writer.Write(row.Damage);
        }

        int omittedPlayerCount = projection.IncludeRanking
            ? (int)Math.Min(
                Main.maxPlayers,
                (long)Math.Max(0, result.OmittedPlayerCount) +
                Math.Max(0, result.Players.Count - visiblePlayerCount))
            : 0;
        long omittedPlayerDamage = projection.IncludeRanking
            ? Math.Max(0, result.OmittedPlayerDamage)
            : 0;
        if (projection.IncludeRanking)
        {
            foreach (PlayerResultRow row in result.Players.Skip(visiblePlayerCount))
                omittedPlayerDamage = SaturatingAdd(omittedPlayerDamage, Math.Max(0, row.Damage));
        }
        writer.Write7BitEncodedInt(omittedPlayerCount);
        writer.Write(omittedPlayerDamage);

        writer.Write(ownDamage.HasValue);
        if (ownDamage.HasValue)
            writer.Write(Math.Max(0, ownDamage.Value));
    }

    public static ResultDelivery ReadResult(BinaryReader reader)
    {
        byte version = reader.ReadByte();
        if (version != 10)
            throw new IOException($"Unsupported result version {version}.");

        PublicResultSnapshot result = new()
        {
            EncounterId = reader.ReadInt64(),
            BossOccurrence = Math.Max(1, reader.Read7BitEncodedInt()),
            EncounterComplete = reader.ReadBoolean(),
            Outcome = ReadOutcome(reader),
            DurationTicks = Math.Max(0, reader.ReadInt64()),
            TeamDamage = Math.Max(0, reader.ReadInt64()),
            BossBodyDamage = Math.Max(0, reader.ReadInt64()),
            UnattributedDamage = Math.Max(0, reader.ReadInt64()),
            Bosses = ReadBosses(reader)
        };

        int playerCount = ReadBoundedCount(reader, Main.maxPlayers, "player");
        for (int i = 0; i < playerCount; i++)
        {
            result.Players.Add(new PlayerResultRow
            {
                PlayerName = BoundedStringIO.Read(reader, MaximumPlayerNameLength, "player name"),
                Damage = Math.Max(0, reader.ReadInt64())
            });
        }

        result.OmittedPlayerCount = ReadBoundedCount(reader, Main.maxPlayers, "omitted player");
        result.OmittedPlayerDamage = Math.Max(0, reader.ReadInt64());

        long? ownDamage = reader.ReadBoolean()
            ? Math.Max(0, reader.ReadInt64())
            : null;
        return new ResultDelivery(result, ownDamage);
    }

    private static long SaturatingAdd(long left, long right)
        => left > long.MaxValue - right ? long.MaxValue : left + right;

    private static string Bounded(string? value, int maximumLength)
    {
        string text = value ?? string.Empty;
        if (text.Length <= maximumLength)
            return text;

        int length = maximumLength;
        if (length > 0 &&
            char.IsHighSurrogate(text[length - 1]) &&
            length < text.Length &&
            char.IsLowSurrogate(text[length]))
        {
            length--;
        }
        return text[..length];
    }

    private static void WriteNpcTypes(BinaryWriter writer, IEnumerable<int> values)
    {
        int[] types = values
            .Where(type => type >= 0 && type < NPCLoader.NPCCount)
            .Distinct()
            .Take(MaximumNpcTypesPerBoss)
            .ToArray();
        writer.Write7BitEncodedInt(types.Length);
        foreach (int type in types)
            writer.Write7BitEncodedInt(type);
    }

    private static List<int> ReadNpcTypes(BinaryReader reader)
    {
        int count = ReadBoundedCount(reader, MaximumNpcTypesPerBoss, "NPC type");
        List<int> values = new(count);
        for (int i = 0; i < count; i++)
        {
            int type = reader.Read7BitEncodedInt();
            if (type < 0 || type >= NPCLoader.NPCCount)
                throw new IOException($"Invalid NPC type {type}.");
            values.Add(type);
        }
        return values;
    }

    private static void WriteNpcInstances(BinaryWriter writer, IEnumerable<AssociatedNpcInstance> values)
    {
        AssociatedNpcInstance[] instances = values
            .Where(value => value.NpcIndex >= 0 && value.NpcIndex < Main.maxNPCs &&
                            value.NpcType > NPCID.None && value.NpcType < NPCLoader.NPCCount &&
                            value.SpawnSerial > 0)
            .Distinct()
            .Take(MaximumNpcInstancesPerBoss)
            .ToArray();
        writer.Write7BitEncodedInt(instances.Length);
        foreach (AssociatedNpcInstance instance in instances)
        {
            writer.Write((short)instance.NpcIndex);
            writer.Write7BitEncodedInt(instance.NpcType);
            writer.Write(instance.SpawnSerial);
        }
    }

    private static List<AssociatedNpcInstance> ReadNpcInstances(BinaryReader reader)
    {
        int count = ReadBoundedCount(reader, MaximumNpcInstancesPerBoss, "NPC instance");
        List<AssociatedNpcInstance> values = new(count);
        for (int i = 0; i < count; i++)
        {
            int npcIndex = reader.ReadInt16();
            int npcType = reader.Read7BitEncodedInt();
            long spawnSerial = reader.ReadInt64();
            if (npcIndex < 0 || npcIndex >= Main.maxNPCs ||
                npcType <= NPCID.None || npcType >= NPCLoader.NPCCount || spawnSerial <= 0)
            {
                throw new IOException($"Invalid NPC instance {npcIndex}:{npcType}.");
            }
            values.Add(new AssociatedNpcInstance(npcIndex, npcType, spawnSerial));
        }
        return values;
    }

    private static EncounterOutcome ReadOutcome(BinaryReader reader)
    {
        EncounterOutcome outcome = (EncounterOutcome)reader.ReadByte();
        if (!Enum.IsDefined(outcome))
            throw new IOException($"Invalid encounter outcome {(byte)outcome}.");
        return outcome;
    }

    private static FinalBlowKind ReadFinalBlowKind(BinaryReader reader)
    {
        FinalBlowKind kind = (FinalBlowKind)reader.ReadByte();
        if (!Enum.IsDefined(kind))
            throw new IOException($"Invalid final blow kind {(byte)kind}.");
        return kind;
    }

    private static int ReadBoundedCount(BinaryReader reader, int maximum, string label)
    {
        int count = reader.Read7BitEncodedInt();
        if (count < 0 || count > maximum)
            throw new IOException($"Invalid {label} count {count}.");
        return count;
    }
}
