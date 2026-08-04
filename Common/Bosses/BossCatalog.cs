using DaybreakDamageTracker.Common.Config;

namespace DaybreakDamageTracker.Common.Bosses;

internal sealed class BossDescriptor
{
    public string Key { get; set; } = string.Empty;
    public string DisplayNameLocalizationKey { get; set; } = string.Empty;
    public int DisplayNameNpcType { get; set; } = -1;
    public string DisplayName { get; set; } = string.Empty;
    public int PortraitNpcType { get; set; }
    public HashSet<int> BodyNpcTypes { get; set; } = [];
    public HashSet<int> KeepAliveNpcTypes { get; set; } = [];
    public HashSet<int> FinalNpcTypes { get; set; } = [];
    public HashSet<int> ExcludedBodyNpcTypes { get; set; } = [];
    public Func<bool>? Downed { get; set; }
    public bool FinishOnBodyDeathWhenNoParticipants { get; set; }

    public BossDescriptor Clone() => new()
    {
        Key = Key,
        DisplayNameLocalizationKey = DisplayNameLocalizationKey,
        DisplayNameNpcType = DisplayNameNpcType,
        DisplayName = DisplayName,
        PortraitNpcType = PortraitNpcType,
        BodyNpcTypes = [.. BodyNpcTypes],
        KeepAliveNpcTypes = [.. KeepAliveNpcTypes],
        FinalNpcTypes = [.. FinalNpcTypes],
        ExcludedBodyNpcTypes = [.. ExcludedBodyNpcTypes],
        Downed = Downed,
        FinishOnBodyDeathWhenNoParticipants = FinishOnBodyDeathWhenNoParticipants
    };
}

internal static class BossCatalog
{
    private static readonly Version BossChecklistApiVersion = new(1, 6);
    private static readonly Dictionary<int, List<CatalogEntry>> EntriesByNpcType = [];
    private static readonly Dictionary<string, CatalogEntry> EntriesByKey = new(StringComparer.Ordinal);
    private static List<RuntimeOverride> _overrides = [];

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
                NameLocalizationKey = config.NameLocalizationKey?.Trim() ?? string.Empty,
                NameOverride = config.NameOverride?.Trim() ?? string.Empty,
                PortraitNpcType = ResolveSingle(config.PortraitNpcType),
                TriggerNpcTypes = ResolveMany(config.TriggerNpcTypes),
                BodyNpcTypes = ResolveMany(config.BodyNpcTypes),
                AddNpcTypes = ResolveMany(config.AddNpcTypes),
                BarrierNpcTypes = ResolveMany(config.BarrierNpcTypes),
                KeepAliveNpcTypes = ResolveMany(config.KeepAliveNpcTypes),
                FinalNpcTypes = ResolveMany(config.FinalNpcTypes),
                FinishOnBodyDeathWhenNoParticipants = config.FinishOnBodyDeathWhenNoParticipants
            };

            if (!string.IsNullOrWhiteSpace(runtime.BossKey) || runtime.TriggerNpcTypes.Count > 0)
                resolved.Add(runtime);
        }

        _overrides = resolved;
    }

    public static List<BossDescriptor> ScanActiveBosses()
    {
        Dictionary<string, BossDescriptor> found = new(StringComparer.Ordinal);

        foreach (NPC npc in Main.ActiveNPCs)
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
            {
                string key = $"npc:{npc.type}";
                found.TryAdd(key, new BossDescriptor
                {
                    Key = key,
                    DisplayNameLocalizationKey = Lang.GetNPCName(npc.type).Key,
                    DisplayNameNpcType = npc.type,
                    DisplayName = npc.FullName,
                    PortraitNpcType = npc.type,
                    BodyNpcTypes = [npc.type],
                    KeepAliveNpcTypes = [npc.type],
                    FinalNpcTypes = npc.type == NPCID.MoonLordCore ? [NPCID.MoonLordCore] : [],
                    FinishOnBodyDeathWhenNoParticipants = true
                });
            }
        }

        foreach (BossDescriptor descriptor in found.Values)
        {
            RuntimeOverride? runtime = _overrides.FirstOrDefault(value =>
                !string.IsNullOrWhiteSpace(value.BossKey) && value.BossKey.Equals(descriptor.Key, StringComparison.Ordinal));
            if (runtime is not null)
                ApplyOverride(descriptor, runtime);
        }

        return found.Values.ToList();
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
    }

    private static BossDescriptor BuildCatalogDescriptor(CatalogEntry entry, int portraitNpcType)
    {
        HashSet<int> body = [.. entry.NpcTypes];
        HashSet<int> knownFinalTypes = body.Contains(NPCID.MoonLordCore)
            ? [NPCID.MoonLordCore]
            : [];
        return new BossDescriptor
        {
            Key = entry.Key,
            DisplayNameLocalizationKey = entry.DisplayNameLocalizationKey,
            DisplayNameNpcType = entry.NpcTypes.FirstOrDefault(portraitNpcType),
            DisplayName = entry.DisplayName,
            // Boss Checklist orders npcIDs with the representative form first. Using that
            // stable ID avoids a segmented boss portrait depending on NPC slot scan order.
            PortraitNpcType = entry.NpcTypes.FirstOrDefault(portraitNpcType),
            BodyNpcTypes = body,
            KeepAliveNpcTypes = [.. body],
            FinalNpcTypes = knownFinalTypes,
            Downed = entry.Downed,
            // On repeat clears the downed flag is already true. The encounter then uses
            // an OnKill-backed final participant window rather than an NPC-free timeout.
            FinishOnBodyDeathWhenNoParticipants = true
        };
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

        return new BossDescriptor
        {
            Key = string.IsNullOrWhiteSpace(runtime.BossKey) ? $"server-override:{runtime.Index}" : runtime.BossKey,
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
            KeepAliveNpcTypes = keepAlive,
            FinalNpcTypes = [.. runtime.FinalNpcTypes],
            ExcludedBodyNpcTypes = [.. runtime.AddNpcTypes, .. runtime.BarrierNpcTypes],
            FinishOnBodyDeathWhenNoParticipants = runtime.FinishOnBodyDeathWhenNoParticipants ?? true
        };
    }

    private static void ApplyOverride(BossDescriptor descriptor, RuntimeOverride runtime)
    {
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

        descriptor.FinalNpcTypes.UnionWith(runtime.FinalNpcTypes);
        if (runtime.FinishOnBodyDeathWhenNoParticipants is bool finish)
            descriptor.FinishOnBodyDeathWhenNoParticipants = finish;
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
        public string NameLocalizationKey { get; set; } = string.Empty;
        public string NameOverride { get; set; } = string.Empty;
        public int PortraitNpcType { get; set; } = -1;
        public HashSet<int> TriggerNpcTypes { get; set; } = [];
        public HashSet<int> BodyNpcTypes { get; set; } = [];
        public HashSet<int> AddNpcTypes { get; set; } = [];
        public HashSet<int> BarrierNpcTypes { get; set; } = [];
        public HashSet<int> KeepAliveNpcTypes { get; set; } = [];
        public HashSet<int> FinalNpcTypes { get; set; } = [];
        public bool? FinishOnBodyDeathWhenNoParticipants { get; set; }

        public bool ManagesNpcType(int npcType)
            => TriggerNpcTypes.Contains(npcType) ||
               BodyNpcTypes.Contains(npcType) ||
               AddNpcTypes.Contains(npcType) ||
               BarrierNpcTypes.Contains(npcType) ||
               KeepAliveNpcTypes.Contains(npcType) ||
               FinalNpcTypes.Contains(npcType);
    }
}
