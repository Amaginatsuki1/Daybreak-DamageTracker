using DaybreakDamageTracker.Client;
using DaybreakDamageTracker.Common.Data;
using DaybreakDamageTracker.Common.Networking;
using DaybreakDamageTracker.Common.NPCs;
using DaybreakDamageTracker.Common.Projectiles;

namespace DaybreakDamageTracker.Common.Systems;

internal readonly record struct DotDamageOwner(EncounterPlayerKey Key, string PlayerName);
internal readonly record struct DotDamageClaimKey(
    int BuffType,
    bool HasOwner,
    EncounterPlayerKey OwnerKey,
    bool HasPrivateRoot,
    DamageRootKey PrivateRoot);

internal sealed class DotDamageSystem : ModSystem
{
    private static readonly Dictionary<int, int> SupportedDotWeights = new()
    {
        [BuffID.Poisoned] = 12,
        [BuffID.OnFire] = 8,
        [BuffID.CursedInferno] = 48,
        [BuffID.Frostburn] = 16,
        [BuffID.Venom] = 60,
        [BuffID.ShadowFlame] = 30,
        [BuffID.OnFire3] = 30,
        [BuffID.Frostburn2] = 50,
        [BuffID.SoulDrain] = 50
    };

    private static readonly Dictionary<int, ProjectileDotDescriptor> ProjectileDotEffects = new()
    {
        [ProjectileID.BoneJavelin] = new(BuffID.BoneJavelin, 6),
        [ProjectileID.TentacleSpike] = new(BuffID.TentacleSpike, 6),
        [ProjectileID.BloodButcherer] = new(BuffID.BloodButcherer, 8),
        [ProjectileID.Daybreak] = new(BuffID.Daybreak, 200),
        [ProjectileID.StardustCellMinionShot] = new(BuffID.StardustMinionBleed, 40)
    };

    [ThreadStatic]
    private static DotApplicationContext? _application;

    public override void Load()
    {
        On_NPC.AddBuff += HookAddBuff;
        On_NPC.UpdateNPC_BuffApplyDOTs += HookUpdateNpcBuffApplyDots;
        // StatusToNPC (including melee imbues and vanilla item debuffs) and the
        // modded OnHitNPCWithItem hooks both run inside ProcessHitAgainstNPC.
        // ApplyNPCOnHitEffects runs too late: StatusToNPC has already called AddBuff.
        On_Player.ProcessHitAgainstNPC += HookProcessHitAgainstNpc;
        On_Projectile.StatusNPC += HookProjectileStatusNpc;
    }

    public override void Unload()
    {
        On_NPC.AddBuff -= HookAddBuff;
        On_NPC.UpdateNPC_BuffApplyDOTs -= HookUpdateNpcBuffApplyDots;
        On_Player.ProcessHitAgainstNPC -= HookProcessHitAgainstNpc;
        On_Projectile.StatusNPC -= HookProjectileStatusNpc;
        _application = null;
    }

    internal static void ReceivePrivateDamage(
        int npcIndex,
        int npcType,
        int rootNpcType,
        int buffType,
        int damage,
        DamageRootKey? serverRoot)
    {
        if (Main.dedServ || damage <= 0 || !IsKnownDotBuff(buffType))
            return;

        DamageRootKey root = serverRoot ?? new DamageRootKey(DamageRootKind.Unclassified, 0);
        bool preferLocalSource = !serverRoot.HasValue || serverRoot.Value.Kind == DamageRootKind.ProjectileFallback;
        if (preferLocalSource && npcIndex >= 0 && npcIndex < Main.maxNPCs)
        {
            NPC npc = Main.npc[npcIndex];
            // A lethal DoT update can deactivate the NPC before this owner-only
            // packet is processed. The same NPC object still owns its per-entity
            // source registry; a reused slot creates a fresh object/registry.
            if (npc.type == npcType)
            {
                DotTrackingGlobalNpc tracking = npc.GetGlobalNPC<DotTrackingGlobalNpc>();
                if (tracking.TryGetLocalSource(
                        buffType,
                        (long)Main.GameUpdateCount,
                        out DamageRootKey localRoot))
                {
                    root = localRoot;
                }
            }
        }

        ClientRuntime.RecordPrivateDamage(
            new TargetSnapshot(true, npcType, rootNpcType),
            root,
            new DamageLeafKey(DamageLeafKind.Debuff, buffType),
            damage);
    }

    private static void HookProcessHitAgainstNpc(
        On_Player.orig_ProcessHitAgainstNPC orig,
        Player self,
        Item item,
        Rectangle itemRectangle,
        int damage,
        float knockBack,
        int npcIndex)
    {
        DotApplicationContext? previous = _application;
        _application = new DotApplicationContext(
            self.whoAmI,
            new DamageRootKey(DamageRootKind.Item, item.type),
            self.meleeEnchant);
        try
        {
            orig(self, item, itemRectangle, damage, knockBack, npcIndex);
        }
        finally
        {
            _application = previous;
        }
    }

    private static void HookProjectileStatusNpc(
        On_Projectile.orig_StatusNPC orig,
        Projectile self,
        int npcIndex)
    {
        DotApplicationContext? previous = _application;
        _application = null;
        if (IsPlayerOwnedDamageProjectile(self))
        {
            SourceTrackingGlobalProjectile source = self.GetGlobalProjectile<SourceTrackingGlobalProjectile>();
            DamageRootKey root = source.HasSource
                ? source.RootSource
                : new DamageRootKey(DamageRootKind.ProjectileFallback, self.type);
            _application = new DotApplicationContext(self.owner, root, 0);
        }

        try
        {
            orig(self, npcIndex);
        }
        finally
        {
            _application = previous;
        }
    }

    private static void HookAddBuff(
        On_NPC.orig_AddBuff orig,
        NPC self,
        int type,
        int time,
        bool quiet)
    {
        int previousTime = GetBuffTime(self, type);
        orig(self, type, time, quiet);
        int currentTime = GetBuffTime(self, type);
        if (currentTime <= previousTime || !SupportedDotWeights.ContainsKey(type))
            return;

        long now = (long)Main.GameUpdateCount;
        DotTrackingGlobalNpc tracking = self.GetGlobalNPC<DotTrackingGlobalNpc>();

        int ownerIndex = _application is not null
            ? _application.PlayerIndex
            : DamageCaptureSystem.TryGetPendingBuffPlayer(out int packetPlayer) ? packetPlayer : -1;

        if (Main.netMode != NetmodeID.MultiplayerClient)
        {
            DotDamageOwner? owner = EncounterSystem.CaptureDotOwner(ownerIndex);
            if (owner.HasValue)
                tracking.RegisterServerExtension(type, previousTime, currentTime, owner.Value, now);
        }

        if (!Main.dedServ && _application is not null && ownerIndex == Main.myPlayer)
        {
            DamageRootKey root = ResolveLocalRoot(_application, type);
            tracking.RegisterLocalExtension(type, previousTime, currentTime, root, now);
        }
    }

    private static void HookUpdateNpcBuffApplyDots(
        On_NPC.orig_UpdateNPC_BuffApplyDOTs orig,
        NPC self)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient || !TargetClassifier.IsEligibleHostile(self))
        {
            orig(self);
            return;
        }

        long now = (long)Main.GameUpdateCount;
        DotTrackingGlobalNpc tracking = self.GetGlobalNPC<DotTrackingGlobalNpc>();
        List<ActiveDotEffect> effects = CaptureActiveEffects(self, tracking, now);
        TargetSnapshot target = EncounterSystem.PrepareTarget(self);
        NPC lifeTarget = ResolveLifeTarget(self);
        int lifeBefore = Math.Max(0, lifeTarget.life);

        orig(self);

        int actualDamage = Math.Clamp(lifeBefore - Math.Max(0, lifeTarget.life), 0, lifeBefore);
        if (actualDamage <= 0 || !target.Eligible)
            return;

        AddUnattributedRegenRemainder(effects, self.lifeRegen);
        if (effects.Count == 0)
        {
            EncounterSystem.RecordDotDamage(null, target, actualDamage);
            return;
        }

        List<WeightedDamageShare<DotDamageClaimKey>> shares = tracking.DamageAllocator.Allocate(
            actualDamage,
            effects.Select(effect => new WeightedDamageClaim<DotDamageClaimKey>(
                effect.ClaimKey,
                effect.Weight)).ToList());
        Dictionary<DotDamageClaimKey, ActiveDotEffect> effectsByKey = effects
            .ToDictionary(effect => effect.ClaimKey);
        int assigned = 0;
        foreach (WeightedDamageShare<DotDamageClaimKey> share in shares)
        {
            ActiveDotEffect effect = effectsByKey[share.Id];
            if (!EncounterSystem.RecordDotDamage(effect.Owner, target, share.Damage))
                continue;

            assigned += share.Damage;
            if (effect.Owner.HasValue)
            {
                DamageNetwork.SendPrivateDotDamage(
                    effect.Owner.Value,
                    self.whoAmI,
                    target,
                    effect.BuffType,
                    share.Damage,
                    effect.PrivateRoot);
            }
        }

        if (assigned < actualDamage)
            EncounterSystem.RecordDotDamage(null, target, actualDamage - assigned);
    }

    private static List<ActiveDotEffect> CaptureActiveEffects(
        NPC npc,
        DotTrackingGlobalNpc tracking,
        long now)
    {
        List<ActiveDotEffect> effects = [];
        HashSet<int> seen = [];
        for (int index = 0; index < npc.buffType.Length; index++)
        {
            int buffType = npc.buffType[index];
            if (npc.buffTime[index] <= 0 || !seen.Add(buffType) ||
                !SupportedDotWeights.TryGetValue(buffType, out int weight))
            {
                continue;
            }

            // Vanilla applies Soul Drain's -50 life regeneration only to a root NPC.
            // A segment can still carry the flag, but must not steal another active
            // DoT's share when its own regeneration penalty is skipped.
            if (buffType == BuffID.SoulDrain && npc.realLife != -1)
                continue;

            effects.Add(new ActiveDotEffect(
                buffType,
                weight,
                tracking.GetServerOwner(buffType, now)));
        }

        foreach (Projectile projectile in Main.ActiveProjectiles)
        {
            if (!ProjectileDotEffects.TryGetValue(projectile.type, out ProjectileDotDescriptor descriptor) ||
                projectile.ai[0] != 1f ||
                (int)projectile.ai[1] != npc.whoAmI)
            {
                continue;
            }

            SourceTrackingGlobalProjectile source = projectile.GetGlobalProjectile<SourceTrackingGlobalProjectile>();
            DamageRootKey privateRoot = source.HasSource
                ? source.RootSource
                : new DamageRootKey(DamageRootKind.ProjectileFallback, projectile.type);
            effects.Add(new ActiveDotEffect(
                descriptor.BuffType,
                descriptor.Weight,
                source.DotOwner,
                privateRoot));
        }

        return effects
            .GroupBy(effect => effect.ClaimKey)
            .Select(group =>
            {
                ActiveDotEffect first = group.First();
                long combinedWeight = group.Sum(effect => (long)effect.Weight);
                return first with { Weight = (int)Math.Min(int.MaxValue, combinedWeight) };
            })
            .ToList();
    }

    private static void AddUnattributedRegenRemainder(
        List<ActiveDotEffect> effects,
        int finalLifeRegen)
    {
        int remainder = DotDamageAttributionPolicy.GetUnattributedRegenWeight(
            finalLifeRegen,
            effects.Select(effect => effect.Weight));
        if (remainder > 0)
            effects.Add(new ActiveDotEffect(0, remainder, null, null));
    }

    private static NPC ResolveLifeTarget(NPC npc)
        => npc.realLife >= 0 && npc.realLife < Main.maxNPCs
            ? Main.npc[npc.realLife]
            : npc;

    private static int GetBuffTime(NPC npc, int buffType)
    {
        for (int index = 0; index < npc.buffType.Length; index++)
        {
            if (npc.buffType[index] == buffType)
                return Math.Max(0, npc.buffTime[index]);
        }
        return 0;
    }

    private static DamageRootKey ResolveLocalRoot(DotApplicationContext application, int debuffType)
    {
        int imbueBuff = !application.MeleeEnchantConsumed
            ? (application.MeleeEnchant, debuffType) switch
            {
                (1, BuffID.Venom) => BuffID.WeaponImbueVenom,
                (2, BuffID.CursedInferno) => BuffID.WeaponImbueCursedFlames,
                (3, BuffID.OnFire) => BuffID.WeaponImbueFire,
                (8, BuffID.Poisoned) => BuffID.WeaponImbuePoison,
                _ => 0
            }
            : 0;
        if (imbueBuff > 0)
        {
            application.MeleeEnchantConsumed = true;
            return new DamageRootKey(DamageRootKind.Buff, imbueBuff);
        }

        // These two vanilla effects are applied by player equipment before an
        // item's intrinsic StatusToNPC debuffs. They do not expose one unique
        // accessory/armor item, so keep them as effect roots instead of falsely
        // assigning them to the weapon being swung.
        if (debuffType == BuffID.Frostburn2 && !application.FrostBurnConsumed &&
            IsCurrentPlayerEffectActive(application.PlayerIndex, player => player.frostBurn))
        {
            application.FrostBurnConsumed = true;
            return new DamageRootKey(DamageRootKind.Buff, BuffID.Frostburn2);
        }
        if (debuffType == BuffID.OnFire3 && !application.MagmaStoneConsumed &&
            IsCurrentPlayerEffectActive(application.PlayerIndex, player => player.magmaStone))
        {
            application.MagmaStoneConsumed = true;
            return new DamageRootKey(DamageRootKind.Buff, BuffID.OnFire3);
        }

        return application.Root;
    }

    private static bool IsCurrentPlayerEffectActive(int playerIndex, Func<Player, bool> predicate)
        => playerIndex >= 0 && playerIndex < Main.maxPlayers && predicate(Main.player[playerIndex]);

    private static bool IsPlayerOwnedDamageProjectile(Projectile projectile)
        => projectile.owner >= 0 &&
           projectile.owner < Main.maxPlayers &&
           Main.player[projectile.owner].active &&
           projectile.friendly &&
           !projectile.hostile &&
           !projectile.npcProj &&
           !projectile.trap;

    private static bool IsKnownDotBuff(int buffType)
        => SupportedDotWeights.ContainsKey(buffType) ||
           ProjectileDotEffects.Values.Any(value => value.BuffType == buffType);

    private sealed class DotApplicationContext(
        int playerIndex,
        DamageRootKey root,
        byte meleeEnchant)
    {
        public int PlayerIndex { get; } = playerIndex;
        public DamageRootKey Root { get; } = root;
        public byte MeleeEnchant { get; } = meleeEnchant;
        public bool MeleeEnchantConsumed { get; set; }
        public bool FrostBurnConsumed { get; set; }
        public bool MagmaStoneConsumed { get; set; }
    }

    private readonly record struct ActiveDotEffect(
        int BuffType,
        int Weight,
        DotDamageOwner? Owner,
        DamageRootKey? PrivateRoot = null)
    {
        public DotDamageClaimKey ClaimKey => new(
            BuffType,
            Owner.HasValue,
            Owner?.Key ?? default,
            PrivateRoot.HasValue,
            PrivateRoot ?? default);
    }

    private readonly record struct ProjectileDotDescriptor(int BuffType, int Weight);
}
