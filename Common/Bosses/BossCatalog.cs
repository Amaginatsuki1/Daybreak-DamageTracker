using DaybreakDamageTracker.Common.Config;
using DaybreakDamageTracker.Common.NPCs;

namespace DaybreakDamageTracker.Common.Bosses;

internal sealed class BossDescriptor
{
    public string Key { get; set; } = string.Empty;
    public string EncounterGroupKey { get; set; } = string.Empty;
    public string DisplayNameLocalizationKey { get; set; } = string.Empty;
    public int DisplayNameNpcType { get; set; } = -1;
    public string DisplayName { get; set; } = string.Empty;
    public int PortraitNpcType { get; set; }
    public HashSet<int> BodyNpcTypes { get; set; } = [];
    public HashSet<int> ControllerNpcTypes { get; set; } = [];
    public HashSet<int> KeepAliveNpcTypes { get; set; } = [];
    public HashSet<int> FinalNpcTypes { get; set; } = [];
    public HashSet<int> ExcludedBodyNpcTypes { get; set; } = [];
    public Func<bool>? Downed { get; set; }
    public bool FinishOnBodyDeathWhenNoParticipants { get; set; }
    public bool ContinueAfterPartyWipe { get; set; }
    public bool ForceAutomaticFinalization { get; set; }
    public bool HasExplicitServerOverride { get; set; }
    public bool AllowGroupedDisappearanceBoundary { get; set; }

    public BossDescriptor Clone() => new()
    {
        Key = Key,
        EncounterGroupKey = EncounterGroupKey,
        DisplayNameLocalizationKey = DisplayNameLocalizationKey,
        DisplayNameNpcType = DisplayNameNpcType,
        DisplayName = DisplayName,
        PortraitNpcType = PortraitNpcType,
        BodyNpcTypes = [.. BodyNpcTypes],
        ControllerNpcTypes = [.. ControllerNpcTypes],
        KeepAliveNpcTypes = [.. KeepAliveNpcTypes],
        FinalNpcTypes = [.. FinalNpcTypes],
        ExcludedBodyNpcTypes = [.. ExcludedBodyNpcTypes],
        Downed = Downed,
        FinishOnBodyDeathWhenNoParticipants = FinishOnBodyDeathWhenNoParticipants,
        ContinueAfterPartyWipe = ContinueAfterPartyWipe,
        ForceAutomaticFinalization = ForceAutomaticFinalization,
        HasExplicitServerOverride = HasExplicitServerOverride,
        AllowGroupedDisappearanceBoundary = AllowGroupedDisappearanceBoundary
    };
}

internal static class BossCatalog
{
    private static readonly Version BossChecklistApiVersion = new(1, 6);
    private static readonly Dictionary<int, List<CatalogEntry>> EntriesByNpcType = [];
    private static readonly Dictionary<string, CatalogEntry> EntriesByKey = new(StringComparer.Ordinal);
    private static List<RuntimeOverride> _overrides = [];
    private static long _calamityBossRushQueryTick = -1;
    private static bool _cachedCalamityBossRushActive;
    private static int _calamityBossRushQueryFailures;
    private static long _calamityBossRushNextRetryTick;

    public static void LoadBossChecklist(Mod requestingMod)
    {
        EntriesByNpcType.Clear();
        EntriesByKey.Clear();

        if (!ModLoader.TryGetMod("BossChecklist", out Mod? bossChecklist) || bossChecklist.Version < BossChecklistApiVersion)
            return;

        try
        {
            object response = bossChecklist.Call(
                "GetBossInfoDictionary",
                requestingMod,
                BossChecklistApiVersion.ToString());

            if (response is not Dictionary<string, Dictionary<string, object>> entries)
                return;

            foreach ((string dictionaryKey, Dictionary<string, object> data) in entries)
            {
                bool isBoss = data.TryGetValue("isBoss", out object? isBossValue) && Convert.ToBoolean(isBossValue);
                if (!isBoss)
                    continue;

                List<int> npcIds = data.TryGetValue("npcIDs", out object? npcValue) && npcValue is List<int> ids
                    ? [.. ids]
                    : [];
                if (npcIds.Count == 0)
                    continue;

                string key = data.TryGetValue("key", out object? keyValue) && keyValue is string keyText
                    ? keyText
                    : dictionaryKey;
                LocalizedText? localizedName = data.TryGetValue("displayName", out object? nameValue)
                    ? nameValue as LocalizedText
                    : null;
                string displayName = localizedName?.Value ?? key;
                string displayNameLocalizationKey = localizedName?.Key ?? string.Empty;
                Func<bool>? downed = data.TryGetValue("downed", out object? downedValue)
                    ? downedValue as Func<bool>
                    : null;

                CatalogEntry entry = new(key, displayNameLocalizationKey, displayName, npcIds, downed);
                EntriesByKey[key] = entry;

                foreach (int npcType in npcIds.Distinct())
                {
                    if (!EntriesByNpcType.TryGetValue(npcType, out List<CatalogEntry>? typeEntries))
                        EntriesByNpcType[npcType] = typeEntries = [];
                    typeEntries.Add(entry);
                }
            }

            DaybreakDamageTrackerMod.Instance.Logger.Info($"Boss Checklist integration loaded {EntriesByKey.Count} boss entries.");
        }
        catch (Exception exception)
        {
            DaybreakDamageTrackerMod.Instance.Logger.Warn($"Boss Checklist integration failed; npc.boss fallback remains active. {exception}");
            EntriesByNpcType.Clear();
            EntriesByKey.Clear();
        }
    }

    public static void ApplyServerOverrides(IEnumerable<BossOverrideConfig> configs)
    {
        List<RuntimeOverride> resolved = [];
        int index = 0;

        foreach (BossOverrideConfig config in configs ?? [])
        {
            RuntimeOverride runtime = new()
            {
                Index = index++,
                BossKey = config.BossKey?.Trim() ?? string.Empty,
                EncounterGroupKey = config.EncounterGroupKey?.Trim() ?? string.Empty,
                NameLocalizationKey = config.NameLocalizationKey?.Trim() ?? string.Empty,
                NameOverride = config.NameOverride?.Trim() ?? string.Empty,
                PortraitNpcType = ResolveSingle(config.PortraitNpcType),
                TriggerNpcTypes = ResolveMany(config.TriggerNpcTypes),
                BodyNpcTypes = ResolveMany(config.BodyNpcTypes),
                ControllerNpcTypes = ResolveMany(config.ControllerNpcTypes),
                AddNpcTypes = ResolveMany(config.AddNpcTypes),
                BarrierNpcTypes = ResolveMany(config.BarrierNpcTypes),
                KeepAliveNpcTypes = ResolveMany(config.KeepAliveNpcTypes),
                FinalNpcTypes = ResolveMany(config.FinalNpcTypes),
                FinishOnBodyDeathWhenNoParticipants = config.FinishOnBodyDeathWhenNoParticipants,
                ContinueAfterPartyWipe = config.ContinueAfterPartyWipe
            };

            if (!string.IsNullOrWhiteSpace(runtime.BossKey) || runtime.TriggerNpcTypes.Count > 0)
                resolved.Add(runtime);
        }

        _overrides = resolved;
    }

    public static List<BossDescriptor> ScanActiveBosses()
    {
        Dictionary<string, BossDescriptor> found = new(StringComparer.Ordinal);
        List<NPC> fallbackCandidates = [];
        List<NPC> activeNpcs = [];
        foreach (NPC activeNpc in Main.ActiveNPCs)
            activeNpcs.Add(activeNpc);

        foreach (NPC npc in activeNpcs)
        {
            bool matchedCatalog = false;
            bool matchedOverride = false;
            // A typed adapter may be discovered after its intro trigger has already
            // transformed (or after a hot reload). Body forms are therefore valid arm
            // points too; add/barrier/keep-alive-only forms deliberately are not.
            List<RuntimeOverride> armOverrides = _overrides
                .Where(runtime => runtime.TriggerNpcTypes.Contains(npc.type) ||
                                  runtime.BodyNpcTypes.Contains(npc.type))
                .ToList();
            bool managedByTypedAdapter = _overrides.Any(runtime =>
                (runtime.TriggerNpcTypes.Count > 0 || runtime.BodyNpcTypes.Count > 0) &&
                runtime.ManagesNpcType(npc.type));

            if (!managedByTypedAdapter && EntriesByNpcType.TryGetValue(npc.type, out List<CatalogEntry>? entries))
            {
                matchedCatalog = true;
                foreach (CatalogEntry entry in entries)
                    found.TryAdd(entry.Key, BuildCatalogDescriptor(entry, npc.type));
            }

            foreach (RuntimeOverride runtime in armOverrides)
            {
                matchedOverride = true;
                string key = string.IsNullOrWhiteSpace(runtime.BossKey)
                    ? $"server-override:{runtime.Index}"
                    : runtime.BossKey;

                if (EntriesByKey.TryGetValue(key, out CatalogEntry? catalogEntry))
                    found.TryAdd(key, BuildCatalogDescriptor(catalogEntry, npc.type));
                else
                    found.TryAdd(key, BuildOverrideDescriptor(runtime, npc));
            }

            if (npc.boss && !matchedCatalog && !matchedOverride && !managedByTypedAdapter)
                fallbackCandidates.Add(npc);
        }

        // Build unmatched roots before their dependent segments so the result is
        // independent of NPC slot iteration order.
        foreach (NPC fallback in fallbackCandidates.Where(value => !HasRealLifeDependency(value)))
            AddOrMergeFallback(found, fallback, fallback.type);

        foreach (NPC segment in fallbackCandidates.Where(HasRealLifeDependency))
        {
            if (!TryGetRealLifeController(segment, out NPC controller))
                continue;
            List<string> ownerKeys = BossFallbackPolicy.FindOwnerKeys(
                found.Values,
                controller.type,
                descriptor => descriptor.Key,
                DescriptorManagesNpcType);
            if (ownerKeys.Count == 1)
            {
                string ownerKey = ownerKeys[0];
                MergeDependentType(found[ownerKey], controller.type, segment.type);
                continue;
            }

            // A controller shared by multiple catalog entries is ambiguous, but the
            // dependent still cannot be a third independent Boss result.
            if (ownerKeys.Count > 1)
                continue;

            // A configured adapter may intentionally treat the controller as only a
            // keep-alive/add form. Do not bypass that adapter with a generic result.
            if (_overrides.Any(runtime => runtime.ManagesNpcType(controller.type)))
                continue;

            AddOrMergeFallback(found, controller, segment.type);
        }

        foreach (BossDescriptor descriptor in found.Values)
        {
            RuntimeOverride? runtime = _overrides.FirstOrDefault(value =>
                !string.IsNullOrWhiteSpace(value.BossKey) && value.BossKey.Equals(descriptor.Key, StringComparison.Ordinal));
            if (runtime is not null)
                ApplyOverride(descriptor, runtime);
        }

        if (found.Count > 0 && IsCalamityBossRushActive())
        {
            foreach (BossDescriptor descriptor in found.Values)
                descriptor.ForceAutomaticFinalization = true;
        }

        return found.Values.ToList();
    }

    internal static bool IsCalamityBossRushActive()
    {
        long nowTick = (long)Main.GameUpdateCount;
        if (_calamityBossRushQueryTick == nowTick)
            return _cachedCalamityBossRushActive;

        _calamityBossRushQueryTick = nowTick;
        _cachedCalamityBossRushActive = false;
        if (!ModLoader.TryGetMod("CalamityMod", out Mod? calamity) ||
            nowTick < _calamityBossRushNextRetryTick)
            return false;

        try
        {
            _cachedCalamityBossRushActive = calamity.Call("GetDifficultyActive", "boss rush") is bool active && active;
            _calamityBossRushQueryFailures = 0;
            _calamityBossRushNextRetryTick = 0;
            return _cachedCalamityBossRushActive;
        }
        catch (Exception exception)
        {
            _calamityBossRushQueryFailures = Math.Min(16, _calamityBossRushQueryFailures + 1);
            long retryDelayTicks = CalamityCompatibilityPolicy.BossRushQueryRetryDelayTicks(
                _calamityBossRushQueryFailures);
            _calamityBossRushNextRetryTick = nowTick + retryDelayTicks;
            DaybreakDamageTrackerMod.Instance.Logger.Warn(
                $"Calamity Boss Rush query failed; ordinary lifecycle detection remains active " +
                $"and the integration will retry in {retryDelayTicks} ticks. {exception}");
            return false;
        }
    }

    private static bool HasRealLifeDependency(NPC npc)
        => BossFallbackPolicy.HasRealLifeDependency(npc.whoAmI, npc.realLife, Main.maxNPCs);

    private static bool TryGetRealLifeController(NPC npc, out NPC controller)
    {
        if (!HasRealLifeDependency(npc))
        {
            controller = null!;
            return false;
        }

        controller = Main.npc[npc.realLife];
        return controller.whoAmI == npc.realLife &&
               BossFallbackPolicy.CanUseRealLifeController(
                   controller.active,
                   DamageTrackingGlobalNpc.GetSpawnSerial(controller),
                   DamageTrackingGlobalNpc.GetSpawnSerial(npc));
    }

    internal static bool DescriptorManagesNpcType(BossDescriptor descriptor, int npcType)
        => descriptor.BodyNpcTypes.Contains(npcType) ||
           descriptor.ControllerNpcTypes.Contains(npcType) ||
           descriptor.ExcludedBodyNpcTypes.Contains(npcType) ||
           descriptor.KeepAliveNpcTypes.Contains(npcType) ||
           descriptor.FinalNpcTypes.Contains(npcType);

    private static void MergeDependentType(BossDescriptor descriptor, int controllerType, int dependentType)
    {
        BossFallbackRole roles = BossFallbackPolicy.InheritDependentRoles(
            descriptor.BodyNpcTypes.Contains(controllerType),
            descriptor.KeepAliveNpcTypes.Contains(controllerType),
            descriptor.ExcludedBodyNpcTypes.Contains(controllerType));

        if ((roles & BossFallbackRole.ExcludedBody) != 0)
        {
            descriptor.ExcludedBodyNpcTypes.Add(dependentType);
            descriptor.BodyNpcTypes.Remove(dependentType);
        }
        else if ((roles & BossFallbackRole.Body) != 0)
        {
            descriptor.BodyNpcTypes.Add(dependentType);
        }

        if ((roles & BossFallbackRole.KeepAlive) != 0)
            descriptor.KeepAliveNpcTypes.Add(dependentType);
    }

    private static void AddOrMergeFallback(
        Dictionary<string, BossDescriptor> found,
        NPC controller,
        int observedType)
    {
        string key = BossFallbackPolicy.FallbackKey(controller.type);
        if (!found.TryGetValue(key, out BossDescriptor? descriptor))
        {
            descriptor = new BossDescriptor
            {
                Key = key,
                EncounterGroupKey = key,
                DisplayNameLocalizationKey = Lang.GetNPCName(controller.type).Key,
                DisplayNameNpcType = controller.type,
                DisplayName = controller.FullName,
                PortraitNpcType = controller.type,
                BodyNpcTypes = [controller.type],
                ControllerNpcTypes = [controller.type],
                KeepAliveNpcTypes = [controller.type],
                FinalNpcTypes = controller.type == NPCID.MoonLordCore ? [NPCID.MoonLordCore] : [],
                FinishOnBodyDeathWhenNoParticipants = true
            };
            found.Add(key, descriptor);
        }

        descriptor.BodyNpcTypes.Add(observedType);
        descriptor.KeepAliveNpcTypes.Add(observedType);
    }

    public static bool HasActiveBossClientSide()
    {
        foreach (NPC npc in Main.ActiveNPCs)
        {
            if (npc.boss || EntriesByNpcType.ContainsKey(npc.type))
                return true;
        }
        return false;
    }

    public static void Clear()
    {
        EntriesByNpcType.Clear();
        EntriesByKey.Clear();
        _overrides = [];
        ResetWorldState();
    }

    public static void ResetWorldState()
    {
        _calamityBossRushQueryTick = -1;
        _cachedCalamityBossRushActive = false;
        _calamityBossRushQueryFailures = 0;
        _calamityBossRushNextRetryTick = 0;
    }

    private static BossDescriptor BuildCatalogDescriptor(CatalogEntry entry, int portraitNpcType)
    {
        HashSet<int> body = [.. entry.NpcTypes];
        int representativeType = entry.NpcTypes.FirstOrDefault(portraitNpcType);
        HashSet<int> knownFinalTypes = body.Contains(NPCID.MoonLordCore)
            ? [NPCID.MoonLordCore]
            : [];
        BossDescriptor descriptor = new()
        {
            Key = entry.Key,
            EncounterGroupKey = entry.Key,
            DisplayNameLocalizationKey = entry.DisplayNameLocalizationKey,
            DisplayNameNpcType = representativeType,
            DisplayName = entry.DisplayName,
            // Boss Checklist orders npcIDs with the representative form first. Using that
            // stable ID avoids a segmented boss portrait depending on NPC slot scan order.
            PortraitNpcType = representativeType,
            BodyNpcTypes = body,
            // Boss Checklist places the representative/root form first. Additional
            // root types (for example a paired boss) are learned from authoritative
            // active NPC scans, while dependent Body/Tail types are never promoted.
            ControllerNpcTypes = [representativeType],
            KeepAliveNpcTypes = [.. body],
            FinalNpcTypes = knownFinalTypes,
            Downed = entry.Downed,
            // On repeat clears the downed flag is already true. The encounter then uses
            // an OnKill-backed final participant window rather than an NPC-free timeout.
            FinishOnBodyDeathWhenNoParticipants = true
        };
        return descriptor;
    }

    private static BossDescriptor BuildOverrideDescriptor(RuntimeOverride runtime, NPC trigger)
    {
        HashSet<int> body = runtime.BodyNpcTypes.Count > 0
            ? [.. runtime.BodyNpcTypes]
            : [.. runtime.TriggerNpcTypes];
        if (body.Count == 0)
            body.Add(trigger.type);
        body.ExceptWith(runtime.AddNpcTypes);
        body.ExceptWith(runtime.BarrierNpcTypes);
        HashSet<int> keepAlive = runtime.KeepAliveNpcTypes.Count > 0
            ? [.. body, .. runtime.TriggerNpcTypes, .. runtime.KeepAliveNpcTypes]
            : [.. body, .. runtime.TriggerNpcTypes];
        HashSet<int> controllers = runtime.ControllerNpcTypes.Count > 0
            ? [.. runtime.ControllerNpcTypes]
            : runtime.TriggerNpcTypes.Count > 0
                ? [runtime.TriggerNpcTypes.Min()]
                : [body.Min()];
        keepAlive.UnionWith(controllers);

        BossDescriptor descriptor = new()
        {
            Key = string.IsNullOrWhiteSpace(runtime.BossKey) ? $"server-override:{runtime.Index}" : runtime.BossKey,
            EncounterGroupKey = ResolveEncounterGroupKey(runtime),
            DisplayNameLocalizationKey = !string.IsNullOrWhiteSpace(runtime.NameLocalizationKey)
                ? runtime.NameLocalizationKey
                : string.IsNullOrWhiteSpace(runtime.NameOverride) ? Lang.GetNPCName(trigger.type).Key : string.Empty,
            DisplayNameNpcType = string.IsNullOrWhiteSpace(runtime.NameLocalizationKey) &&
                                 string.IsNullOrWhiteSpace(runtime.NameOverride)
                ? trigger.type
                : -1,
            DisplayName = string.IsNullOrWhiteSpace(runtime.NameOverride) ? trigger.FullName : runtime.NameOverride,
            PortraitNpcType = runtime.PortraitNpcType >= 0 ? runtime.PortraitNpcType : trigger.type,
            BodyNpcTypes = body,
            ControllerNpcTypes = controllers,
            KeepAliveNpcTypes = keepAlive,
            FinalNpcTypes = [.. runtime.FinalNpcTypes],
            ExcludedBodyNpcTypes = [.. runtime.AddNpcTypes, .. runtime.BarrierNpcTypes],
            FinishOnBodyDeathWhenNoParticipants = runtime.FinishOnBodyDeathWhenNoParticipants ?? true,
            ContinueAfterPartyWipe = runtime.ContinueAfterPartyWipe ?? false,
            HasExplicitServerOverride = true
        };
        ApplyGroupLifecycle(descriptor, runtime);
        return descriptor;
    }

    private static void ApplyOverride(BossDescriptor descriptor, RuntimeOverride runtime)
    {
        descriptor.HasExplicitServerOverride = true;
        if (!string.IsNullOrWhiteSpace(runtime.EncounterGroupKey))
            descriptor.EncounterGroupKey = runtime.EncounterGroupKey;
        if (!string.IsNullOrWhiteSpace(runtime.NameLocalizationKey))
        {
            descriptor.DisplayNameLocalizationKey = runtime.NameLocalizationKey;
            descriptor.DisplayNameNpcType = -1;
            if (!string.IsNullOrWhiteSpace(runtime.NameOverride))
                descriptor.DisplayName = runtime.NameOverride;
        }
        else if (!string.IsNullOrWhiteSpace(runtime.NameOverride))
        {
            descriptor.DisplayNameLocalizationKey = string.Empty;
            descriptor.DisplayNameNpcType = -1;
            descriptor.DisplayName = runtime.NameOverride;
        }
        if (runtime.PortraitNpcType >= 0)
            descriptor.PortraitNpcType = runtime.PortraitNpcType;
        if (runtime.BodyNpcTypes.Count > 0)
            descriptor.BodyNpcTypes = [.. runtime.BodyNpcTypes];
        if (runtime.ControllerNpcTypes.Count > 0)
            descriptor.ControllerNpcTypes = [.. runtime.ControllerNpcTypes];
        descriptor.ExcludedBodyNpcTypes.UnionWith(runtime.AddNpcTypes);
        descriptor.ExcludedBodyNpcTypes.UnionWith(runtime.BarrierNpcTypes);
        descriptor.BodyNpcTypes.ExceptWith(descriptor.ExcludedBodyNpcTypes);

        if (runtime.KeepAliveNpcTypes.Count > 0 || runtime.BodyNpcTypes.Count > 0)
        {
            // An explicit lifecycle list replaces Boss Checklist's broad npcIDs set so a
            // server adapter can remove phase/barrier types that would otherwise hang.
            descriptor.KeepAliveNpcTypes = [.. descriptor.BodyNpcTypes, .. runtime.TriggerNpcTypes, .. runtime.KeepAliveNpcTypes];
        }
        else
        {
            descriptor.KeepAliveNpcTypes.UnionWith(descriptor.BodyNpcTypes);
            descriptor.KeepAliveNpcTypes.UnionWith(runtime.TriggerNpcTypes);
        }
        descriptor.KeepAliveNpcTypes.UnionWith(descriptor.ControllerNpcTypes);

        descriptor.FinalNpcTypes.UnionWith(runtime.FinalNpcTypes);
        if (runtime.FinishOnBodyDeathWhenNoParticipants is bool finish)
            descriptor.FinishOnBodyDeathWhenNoParticipants = finish;
        if (runtime.ContinueAfterPartyWipe is bool continueAfterWipe)
            descriptor.ContinueAfterPartyWipe = continueAfterWipe;
        ApplyGroupLifecycle(descriptor, runtime);
    }

    private static void ApplyGroupLifecycle(BossDescriptor descriptor, RuntimeOverride runtime)
    {
        string groupKey = ResolveEncounterGroupKey(runtime);
        foreach (RuntimeOverride member in _overrides.Where(candidate =>
                     ResolveEncounterGroupKey(candidate).Equals(groupKey, StringComparison.Ordinal)))
        {
            // A group must know its future controller/wave types before the next member
            // appears, otherwise a wipe during the NPC-free gap is indistinguishable
            // from a completed ordinary encounter.
            descriptor.KeepAliveNpcTypes.UnionWith(member.TriggerNpcTypes);
            descriptor.KeepAliveNpcTypes.UnionWith(member.BodyNpcTypes);
            descriptor.KeepAliveNpcTypes.UnionWith(member.KeepAliveNpcTypes);
            descriptor.ControllerNpcTypes.UnionWith(member.ControllerNpcTypes);
            descriptor.FinalNpcTypes.UnionWith(member.FinalNpcTypes);
            descriptor.ContinueAfterPartyWipe |= member.ContinueAfterPartyWipe ?? false;
        }
    }

    private static string ResolveEncounterGroupKey(RuntimeOverride runtime)
    {
        if (!string.IsNullOrWhiteSpace(runtime.EncounterGroupKey))
            return runtime.EncounterGroupKey;
        return string.IsNullOrWhiteSpace(runtime.BossKey)
            ? $"server-override:{runtime.Index}"
            : runtime.BossKey;
    }

    private static HashSet<int> ResolveMany(IEnumerable<string>? values)
    {
        HashSet<int> result = [];
        foreach (string value in values ?? [])
        {
            if (TryResolveNpcType(value, out int type))
                result.Add(type);
            else if (!string.IsNullOrWhiteSpace(value))
                DaybreakDamageTrackerMod.Instance.Logger.Warn($"Unknown NPC type in server config: {value}");
        }
        return result;
    }

    private static int ResolveSingle(string? value)
        => TryResolveNpcType(value, out int type) ? type : -1;

    private static bool TryResolveNpcType(string? value, out int type)
    {
        type = -1;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string normalized = value.Trim().Replace(':', '/');
        if (int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out type))
            return type >= 0 && type < NPCLoader.NPCCount;

        if (ModContent.TryFind(normalized, out ModNPC modNpc))
        {
            type = modNpc.Type;
            return true;
        }

        return false;
    }

    private sealed record CatalogEntry(
        string Key,
        string DisplayNameLocalizationKey,
        string DisplayName,
        List<int> NpcTypes,
        Func<bool>? Downed);

    private sealed class RuntimeOverride
    {
        public int Index { get; set; }
        public string BossKey { get; set; } = string.Empty;
        public string EncounterGroupKey { get; set; } = string.Empty;
        public string NameLocalizationKey { get; set; } = string.Empty;
        public string NameOverride { get; set; } = string.Empty;
        public int PortraitNpcType { get; set; } = -1;
        public HashSet<int> TriggerNpcTypes { get; set; } = [];
        public HashSet<int> BodyNpcTypes { get; set; } = [];
        public HashSet<int> ControllerNpcTypes { get; set; } = [];
        public HashSet<int> AddNpcTypes { get; set; } = [];
        public HashSet<int> BarrierNpcTypes { get; set; } = [];
        public HashSet<int> KeepAliveNpcTypes { get; set; } = [];
        public HashSet<int> FinalNpcTypes { get; set; } = [];
        public bool? FinishOnBodyDeathWhenNoParticipants { get; set; }
        public bool? ContinueAfterPartyWipe { get; set; }

        public bool ManagesNpcType(int npcType)
            => TriggerNpcTypes.Contains(npcType) ||
               BodyNpcTypes.Contains(npcType) ||
               ControllerNpcTypes.Contains(npcType) ||
               AddNpcTypes.Contains(npcType) ||
               BarrierNpcTypes.Contains(npcType) ||
               KeepAliveNpcTypes.Contains(npcType) ||
               FinalNpcTypes.Contains(npcType);
    }
}
