using DaybreakDamageTracker.Common.Data;

namespace DaybreakDamageTracker.Common.Networking;

internal readonly record struct ResultDelivery(PublicResultSnapshot Result, long? OwnDamage);

internal static class PacketIO
{
    private const int MaximumBossTextLength = 80;
    private const int MaximumLocalizationKeyLength = 256;
    private const int MaximumPlayerNameLength = 24;

    public static void WritePresentation(BinaryWriter writer, PresentationSettings value)
    {
        writer.Write((byte)1);
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
        writer.Write7BitEncodedInt(value.RankingRows);
        writer.Write7BitEncodedInt(value.SourceRootRows);
        writer.Write7BitEncodedInt(value.SourceLeafRows);
        writer.Write7BitEncodedInt(value.HistoryCount);
        writer.Write(value.ChatSummary);
    }

    public static PresentationSettings ReadPresentation(BinaryReader reader)
    {
        byte version = reader.ReadByte();
        if (version != 1)
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
            RankingRows = reader.Read7BitEncodedInt(),
            SourceRootRows = reader.Read7BitEncodedInt(),
            SourceLeafRows = reader.Read7BitEncodedInt(),
            HistoryCount = reader.Read7BitEncodedInt(),
            ChatSummary = reader.ReadBoolean()
        }.SanitizedClone();
    }

    public static void WriteBosses(BinaryWriter writer, IReadOnlyList<BossPresentation> bosses)
    {
        writer.Write7BitEncodedInt(Math.Min(bosses.Count, 32));
        foreach (BossPresentation boss in bosses.Take(32))
        {
            writer.Write(Bounded(boss.Key, MaximumBossTextLength));
            writer.Write(Bounded(boss.NameLocalizationKey, MaximumLocalizationKeyLength));
            writer.Write7BitEncodedInt(Math.Max(-1, boss.NameNpcType) + 1);
            writer.Write(Bounded(boss.Name, MaximumBossTextLength));
            writer.Write7BitEncodedInt(Math.Max(0, boss.PortraitNpcType));
        }
    }

    public static List<BossPresentation> ReadBosses(BinaryReader reader)
    {
        int count = ReadBoundedCount(reader, 32, "boss");
        List<BossPresentation> bosses = new(count);
        for (int i = 0; i < count; i++)
        {
            bosses.Add(new BossPresentation
            {
                Key = reader.ReadString(),
                NameLocalizationKey = reader.ReadString(),
                NameNpcType = reader.Read7BitEncodedInt() - 1,
                Name = reader.ReadString(),
                PortraitNpcType = reader.Read7BitEncodedInt()
            });
        }
        return bosses;
    }

    public static void WriteResult(BinaryWriter writer, PublicResultSnapshot result, long? ownDamage)
    {
        writer.Write((byte)3);
        writer.Write(result.EncounterId);
        writer.Write((byte)result.Outcome);
        writer.Write(result.DurationTicks);
        writer.Write(result.TeamDamage);
        writer.Write(result.BossBodyDamage);
        writer.Write(result.UnattributedDamage);
        WriteBosses(writer, result.Bosses);
        writer.Write7BitEncodedInt(Math.Min(result.Players.Count, Main.maxPlayers));
        foreach (PlayerResultRow row in result.Players.Take(Main.maxPlayers))
        {
            writer.Write(Bounded(row.PlayerName, MaximumPlayerNameLength));
            writer.Write(row.Damage);
        }

        writer.Write(ownDamage.HasValue);
        if (ownDamage.HasValue)
            writer.Write(Math.Max(0, ownDamage.Value));
    }

    public static ResultDelivery ReadResult(BinaryReader reader)
    {
        byte version = reader.ReadByte();
        if (version != 3)
            throw new IOException($"Unsupported result version {version}.");

        PublicResultSnapshot result = new()
        {
            EncounterId = reader.ReadInt64(),
            Outcome = (EncounterOutcome)reader.ReadByte(),
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
                PlayerName = reader.ReadString(),
                Damage = Math.Max(0, reader.ReadInt64())
            });
        }

        long? ownDamage = reader.ReadBoolean()
            ? Math.Max(0, reader.ReadInt64())
            : null;
        return new ResultDelivery(result, ownDamage);
    }

    private static string Bounded(string? value, int maximumLength)
    {
        string text = value ?? string.Empty;
        return text.Length <= maximumLength ? text : text[..maximumLength];
    }

    private static int ReadBoundedCount(BinaryReader reader, int maximum, string label)
    {
        int count = reader.Read7BitEncodedInt();
        if (count < 0 || count > maximum)
            throw new IOException($"Invalid {label} count {count}.");
        return count;
    }
}
