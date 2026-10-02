using DaybreakDamageTracker.Common.Data;
using Terraria.ID;
using Terraria.ModLoader;

namespace DaybreakDamageTracker.Client.UI;

internal static class ResultPanelPreviewData
{
    public static ClientHistoryEntry Create()
    {
        (int npcType, string name) = ResolvePreviewBoss();
        return new()
    {
        Public = new PublicResultSnapshot
        {
            EncounterId = -1,
            BossOccurrence = 1,
            EncounterComplete = true,
            Outcome = EncounterOutcome.Victory,
            DurationTicks = 4_560,
            TeamDamage = 260_000,
            BossBodyDamage = 220_000,
            UnattributedDamage = 10_000,
            Bosses =
            [
                new BossPresentation
                {
                    Key = "Preview/CalamityMod/Yharon",
                    NameNpcType = npcType,
                    Name = name,
                    PortraitNpcType = npcType,
                    Outcome = EncounterOutcome.Victory,
                    BodyDamage = 220_000,
                    FinalBlowPlayerName = ClientRuntime.Text("UI.PreviewPlayerYou", "You"),
                    FinalBlowKind = FinalBlowKind.DirectHit
                }
            ],
            Players = PreviewPlayers()
        },
        PrivateSources = new SourceTreeSnapshot
        {
            CanonicalTotal = 100_000,
            CanonicalTotalKnown = true,
            SourceDetailsAvailable = true,
            ClassifiedTotal = 100_000,
            Roots =
            [
                new SourceRootSnapshot
                {
                    Key = new DamageRootKey(DamageRootKind.Item, ItemID.StarCannon),
                    Damage = 60_000,
                    Leaves =
                    [
                        new SourceLeafSnapshot
                        {
                            Key = new DamageLeafKey(DamageLeafKind.ItemBody, ItemID.StarCannon),
                            Damage = 10_000
                        },
                        new SourceLeafSnapshot
                        {
                            Key = new DamageLeafKey(DamageLeafKind.Projectile, ProjectileID.StarCannonStar),
                            Damage = 14_000
                        },
                        new SourceLeafSnapshot
                        {
                            Key = new DamageLeafKey(DamageLeafKind.Projectile, ProjectileID.TerraBeam),
                            Damage = 12_000
                        },
                        new SourceLeafSnapshot
                        {
                            Key = new DamageLeafKey(DamageLeafKind.Projectile, ProjectileID.BulletHighVelocity),
                            Damage = 10_000
                        },
                        new SourceLeafSnapshot
                        {
                            Key = new DamageLeafKey(DamageLeafKind.Projectile, ProjectileID.HolyArrow),
                            Damage = 8_000
                        },
                        new SourceLeafSnapshot
                        {
                            Key = new DamageLeafKey(DamageLeafKind.Projectile, ProjectileID.Daybreak),
                            Damage = 6_000
                        }
                    ]
                },
                new SourceRootSnapshot
                {
                    Key = new DamageRootKey(DamageRootKind.Item, ItemID.TerraBlade),
                    Damage = 15_000,
                    Leaves =
                    [
                        new SourceLeafSnapshot
                        {
                            Key = new DamageLeafKey(DamageLeafKind.ItemBody, ItemID.TerraBlade),
                            Damage = 5_000
                        },
                        new SourceLeafSnapshot
                        {
                            Key = new DamageLeafKey(DamageLeafKind.Projectile, ProjectileID.TerraBeam),
                            Damage = 10_000
                        }
                    ]
                },
                new SourceRootSnapshot
                {
                    Key = new DamageRootKey(DamageRootKind.Buff, BuffID.CursedInferno),
                    Damage = 8_000,
                    Leaves =
                    [
                        new SourceLeafSnapshot
                        {
                            Key = new DamageLeafKey(DamageLeafKind.Debuff, BuffID.CursedInferno),
                            Damage = 8_000
                        }
                    ]
                },
                ItemBodyRoot(ItemID.Megashark, 6_000),
                ItemBodyRoot(ItemID.WoodenSword, 4_000),
                ItemBodyRoot(ItemID.CopperShortsword, 3_000),
                ItemBodyRoot(ItemID.IronBroadsword, 2_000),
                ItemBodyRoot(ItemID.GoldBroadsword, 2_000)
            ]
        }
    };
    }

    private static (int NpcType, string Name) ResolvePreviewBoss()
    {
        if (ModContent.TryFind<ModNPC>("CalamityMod/Yharon", out ModNPC yharon))
            return (yharon.Type, yharon.DisplayName.Value);

        // The preview must remain usable when Calamity is not installed. In that
        // case it keeps Yharon's localized name and intentionally omits a portrait.
        return (NPCID.None, ClientRuntime.Text("UI.PreviewYharon", "Yharon"));
    }

    private static List<PlayerResultRow> PreviewPlayers()
    {
        long[] damage = [100_000, 50_000, 30_000, 20_000, 15_000, 10_000, 8_000, 6_000, 4_000, 3_000, 2_000, 2_000];
        List<PlayerResultRow> rows = new(damage.Length);
        for (int index = 0; index < damage.Length; index++)
        {
            string name = index switch
            {
                0 => ClientRuntime.Text("UI.PreviewPlayerYou", "You"),
                1 => ClientRuntime.Text("UI.PreviewPlayerTwo", "Player 2"),
                2 => ClientRuntime.Text("UI.PreviewPlayerThree", "Player 3"),
                3 => ClientRuntime.Text("UI.PreviewPlayerFour", "Player 4"),
                _ => ClientRuntime.Text("UI.PreviewPlayer", "Player {0}", index + 1)
            };
            rows.Add(new PlayerResultRow { PlayerName = name, Damage = damage[index] });
        }
        return rows;
    }

    private static SourceRootSnapshot ItemBodyRoot(int itemType, long damage) => new()
    {
        Key = new DamageRootKey(DamageRootKind.Item, itemType),
        Damage = damage,
        Leaves =
        [
            new SourceLeafSnapshot
            {
                Key = new DamageLeafKey(DamageLeafKind.ItemBody, itemType),
                Damage = damage
            }
        ]
    };
}
