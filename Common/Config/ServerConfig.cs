using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DaybreakDamageTracker.Client;
using DaybreakDamageTracker.Client.UI;
using DaybreakDamageTracker.Common.Bosses;
using DaybreakDamageTracker.Common.Data;
using DaybreakDamageTracker.Common.Networking;
using DaybreakDamageTracker.Common.Systems;
using Newtonsoft.Json;
using Terraria.Localization;
using Terraria.ModLoader.Config;
using LegacyJsonSerializer = System.Text.Json.JsonSerializer;

namespace DaybreakDamageTracker.Common.Config;

[Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
public enum OverrideBooleanMode
{
    Inherit,
    Enabled,
    Disabled
}

public sealed class BossOverrideConfig
{
    public string BossKey { get; set; } = string.Empty;
    public string EncounterGroupKey { get; set; } = string.Empty;
    public string NameLocalizationKey { get; set; } = string.Empty;
    public string NameOverride { get; set; } = string.Empty;
    public string PortraitNpcType { get; set; } = string.Empty;
    public List<string> TriggerNpcTypes { get; set; } = [];
    public List<string> BodyNpcTypes { get; set; } = [];
    public List<string> ControllerNpcTypes { get; set; } = [];
    public List<string> AddNpcTypes { get; set; } = [];
    public List<string> BarrierNpcTypes { get; set; } = [];
    public List<string> KeepAliveNpcTypes { get; set; } = [];
    public List<string> FinalNpcTypes { get; set; } = [];
    public bool? FinishOnBodyDeathWhenNoParticipants { get; set; }
    public bool? ContinueAfterPartyWipe { get; set; }

    internal BossOverrideConfig DeepClone() => new()
    {
        BossKey = BossKey ?? string.Empty,
        EncounterGroupKey = EncounterGroupKey ?? string.Empty,
        NameLocalizationKey = NameLocalizationKey ?? string.Empty,
        NameOverride = NameOverride ?? string.Empty,
        PortraitNpcType = PortraitNpcType ?? string.Empty,
        TriggerNpcTypes = [.. TriggerNpcTypes ?? []],
        BodyNpcTypes = [.. BodyNpcTypes ?? []],
        ControllerNpcTypes = [.. ControllerNpcTypes ?? []],
        AddNpcTypes = [.. AddNpcTypes ?? []],
        BarrierNpcTypes = [.. BarrierNpcTypes ?? []],
        KeepAliveNpcTypes = [.. KeepAliveNpcTypes ?? []],
        FinalNpcTypes = [.. FinalNpcTypes ?? []],
        FinishOnBodyDeathWhenNoParticipants = FinishOnBodyDeathWhenNoParticipants,
        ContinueAfterPartyWipe = ContinueAfterPartyWipe
    };

    internal void Sanitize()
    {
        BossKey = ServerConfigValuePolicy.NormalizeText(BossKey, ServerConfigValuePolicy.MaximumBossKeyLength);
        EncounterGroupKey = ServerConfigValuePolicy.NormalizeText(EncounterGroupKey, ServerConfigValuePolicy.MaximumTextLength);
        NameLocalizationKey = ServerConfigValuePolicy.NormalizeText(NameLocalizationKey, ServerConfigValuePolicy.MaximumTextLength);
        NameOverride = ServerConfigValuePolicy.NormalizeText(NameOverride, ServerConfigValuePolicy.MaximumLiteralNameLength);
        PortraitNpcType = ServerConfigValuePolicy.NormalizeText(PortraitNpcType, ServerConfigValuePolicy.MaximumTextLength);
        TriggerNpcTypes = ServerConfigValuePolicy.SanitizeListInPlace(TriggerNpcTypes);
        BodyNpcTypes = ServerConfigValuePolicy.SanitizeListInPlace(BodyNpcTypes);
        ControllerNpcTypes = ServerConfigValuePolicy.SanitizeListInPlace(ControllerNpcTypes);
        AddNpcTypes = ServerConfigValuePolicy.SanitizeListInPlace(AddNpcTypes);
        BarrierNpcTypes = ServerConfigValuePolicy.SanitizeListInPlace(BarrierNpcTypes);
        KeepAliveNpcTypes = ServerConfigValuePolicy.SanitizeListInPlace(KeepAliveNpcTypes);
        FinalNpcTypes = ServerConfigValuePolicy.SanitizeListInPlace(FinalNpcTypes);
        TrimTypeValuesToTotal(ServerConfigValuePolicy.MaximumTypeValuesPerOverride);
    }

    internal int TrimTextToTotal(int maximumTotal)
    {
        int remaining = Math.Max(0, maximumTotal);
        BossKey = ServerConfigValuePolicy.TrimToRemaining(BossKey, ref remaining);
        EncounterGroupKey = ServerConfigValuePolicy.TrimToRemaining(EncounterGroupKey, ref remaining);
        NameLocalizationKey = ServerConfigValuePolicy.TrimToRemaining(NameLocalizationKey, ref remaining);
        NameOverride = ServerConfigValuePolicy.TrimToRemaining(NameOverride, ref remaining);
        PortraitNpcType = ServerConfigValuePolicy.TrimToRemaining(PortraitNpcType, ref remaining);
        ServerConfigValuePolicy.TrimListTextToRemaining(TriggerNpcTypes, ref remaining);
        ServerConfigValuePolicy.TrimListTextToRemaining(BodyNpcTypes, ref remaining);
        ServerConfigValuePolicy.TrimListTextToRemaining(ControllerNpcTypes, ref remaining);
        ServerConfigValuePolicy.TrimListTextToRemaining(AddNpcTypes, ref remaining);
        ServerConfigValuePolicy.TrimListTextToRemaining(BarrierNpcTypes, ref remaining);
        ServerConfigValuePolicy.TrimListTextToRemaining(KeepAliveNpcTypes, ref remaining);
        ServerConfigValuePolicy.TrimListTextToRemaining(FinalNpcTypes, ref remaining);
        return Math.Max(0, maximumTotal) - remaining;
    }

    internal int TrimTypeValuesToTotal(int maximumTotal)
        => ServerConfigValuePolicy.TrimListsToTotal(
            maximumTotal,
            TriggerNpcTypes,
            BodyNpcTypes,
            ControllerNpcTypes,
            AddNpcTypes,
            BarrierNpcTypes,
            KeepAliveNpcTypes,
            FinalNpcTypes);
}

// Native UI model. Nullable booleans are kept only in the legacy/runtime DTO above:
// tModLoader's stock config UI does not provide a safe editor for bool?, so the page
// exposes the same meaning as an explicit three-state enum.
public sealed class BossOverrideEntryConfig
{
    public string BossKey { get; set; } = string.Empty;
    public string EncounterGroupKey { get; set; } = string.Empty;
    public string NameLocalizationKey { get; set; } = string.Empty;
    public string NameOverride { get; set; } = string.Empty;
    public string PortraitNpcType { get; set; } = string.Empty;
    public List<string> TriggerNpcTypes { get; set; } = [];
    public List<string> BodyNpcTypes { get; set; } = [];
    public List<string> ControllerNpcTypes { get; set; } = [];
    public List<string> AddNpcTypes { get; set; } = [];
    public List<string> BarrierNpcTypes { get; set; } = [];
    public List<string> KeepAliveNpcTypes { get; set; } = [];
    public List<string> FinalNpcTypes { get; set; } = [];
    [DefaultValue(OverrideBooleanMode.Inherit)]
    public OverrideBooleanMode FinishOnBodyDeathWhenNoParticipants { get; set; }

    [DefaultValue(OverrideBooleanMode.Inherit)]
    public OverrideBooleanMode ContinueAfterPartyWipe { get; set; }

    internal BossOverrideEntryConfig DeepClone() => new()
    {
        BossKey = BossKey ?? string.Empty,
        EncounterGroupKey = EncounterGroupKey ?? string.Empty,
        NameLocalizationKey = NameLocalizationKey ?? string.Empty,
        NameOverride = NameOverride ?? string.Empty,
        PortraitNpcType = PortraitNpcType ?? string.Empty,
        TriggerNpcTypes = [.. TriggerNpcTypes ?? []],
        BodyNpcTypes = [.. BodyNpcTypes ?? []],
        ControllerNpcTypes = [.. ControllerNpcTypes ?? []],
        AddNpcTypes = [.. AddNpcTypes ?? []],
        BarrierNpcTypes = [.. BarrierNpcTypes ?? []],
        KeepAliveNpcTypes = [.. KeepAliveNpcTypes ?? []],
        FinalNpcTypes = [.. FinalNpcTypes ?? []],
        FinishOnBodyDeathWhenNoParticipants = FinishOnBodyDeathWhenNoParticipants,
        ContinueAfterPartyWipe = ContinueAfterPartyWipe
    };

    internal void Sanitize()
    {
        BossKey = ServerConfigValuePolicy.NormalizeText(BossKey, ServerConfigValuePolicy.MaximumBossKeyLength);
        EncounterGroupKey = ServerConfigValuePolicy.NormalizeText(EncounterGroupKey, ServerConfigValuePolicy.MaximumTextLength);
        NameLocalizationKey = ServerConfigValuePolicy.NormalizeText(NameLocalizationKey, ServerConfigValuePolicy.MaximumTextLength);
        NameOverride = ServerConfigValuePolicy.NormalizeText(NameOverride, ServerConfigValuePolicy.MaximumLiteralNameLength);
        PortraitNpcType = ServerConfigValuePolicy.NormalizeText(PortraitNpcType, ServerConfigValuePolicy.MaximumTextLength);
        TriggerNpcTypes = ServerConfigValuePolicy.SanitizeListInPlace(TriggerNpcTypes);
        BodyNpcTypes = ServerConfigValuePolicy.SanitizeListInPlace(BodyNpcTypes);
        ControllerNpcTypes = ServerConfigValuePolicy.SanitizeListInPlace(ControllerNpcTypes);
        AddNpcTypes = ServerConfigValuePolicy.SanitizeListInPlace(AddNpcTypes);
        BarrierNpcTypes = ServerConfigValuePolicy.SanitizeListInPlace(BarrierNpcTypes);
        KeepAliveNpcTypes = ServerConfigValuePolicy.SanitizeListInPlace(KeepAliveNpcTypes);
        FinalNpcTypes = ServerConfigValuePolicy.SanitizeListInPlace(FinalNpcTypes);
        TrimTypeValuesToTotal(ServerConfigValuePolicy.MaximumTypeValuesPerOverride);
        if (!Enum.IsDefined(FinishOnBodyDeathWhenNoParticipants))
            FinishOnBodyDeathWhenNoParticipants = OverrideBooleanMode.Inherit;
        if (!Enum.IsDefined(ContinueAfterPartyWipe))
            ContinueAfterPartyWipe = OverrideBooleanMode.Inherit;
    }

    internal int TrimTextToTotal(int maximumTotal)
    {
        int remaining = Math.Max(0, maximumTotal);
        BossKey = ServerConfigValuePolicy.TrimToRemaining(BossKey, ref remaining);
        EncounterGroupKey = ServerConfigValuePolicy.TrimToRemaining(EncounterGroupKey, ref remaining);
        NameLocalizationKey = ServerConfigValuePolicy.TrimToRemaining(NameLocalizationKey, ref remaining);
        NameOverride = ServerConfigValuePolicy.TrimToRemaining(NameOverride, ref remaining);
        PortraitNpcType = ServerConfigValuePolicy.TrimToRemaining(PortraitNpcType, ref remaining);
        ServerConfigValuePolicy.TrimListTextToRemaining(TriggerNpcTypes, ref remaining);
        ServerConfigValuePolicy.TrimListTextToRemaining(BodyNpcTypes, ref remaining);
        ServerConfigValuePolicy.TrimListTextToRemaining(ControllerNpcTypes, ref remaining);
        ServerConfigValuePolicy.TrimListTextToRemaining(AddNpcTypes, ref remaining);
        ServerConfigValuePolicy.TrimListTextToRemaining(BarrierNpcTypes, ref remaining);
        ServerConfigValuePolicy.TrimListTextToRemaining(KeepAliveNpcTypes, ref remaining);
        ServerConfigValuePolicy.TrimListTextToRemaining(FinalNpcTypes, ref remaining);
        return Math.Max(0, maximumTotal) - remaining;
    }

    internal int TrimTypeValuesToTotal(int maximumTotal)
        => ServerConfigValuePolicy.TrimListsToTotal(
            maximumTotal,
            TriggerNpcTypes,
            BodyNpcTypes,
            ControllerNpcTypes,
            AddNpcTypes,
            BarrierNpcTypes,
            KeepAliveNpcTypes,
            FinalNpcTypes);

    internal BossOverrideConfig ToRuntime()
    {
        BossOverrideConfig runtime = new()
        {
            FinishOnBodyDeathWhenNoParticipants = ToNullable(FinishOnBodyDeathWhenNoParticipants),
            ContinueAfterPartyWipe = ToNullable(ContinueAfterPartyWipe)
        };
        CopyCommonTo(runtime);
        return runtime;
    }

    internal static BossOverrideEntryConfig FromRuntime(BossOverrideConfig source)
    {
        BossOverrideEntryConfig entry = new()
        {
            FinishOnBodyDeathWhenNoParticipants = FromNullable(source.FinishOnBodyDeathWhenNoParticipants),
            ContinueAfterPartyWipe = FromNullable(source.ContinueAfterPartyWipe)
        };
        entry.CopyCommonFrom(source);
        entry.Sanitize();
        return entry;
    }

    private void CopyCommonTo(BossOverrideConfig target)
    {
        target.BossKey = BossKey ?? string.Empty;
        target.EncounterGroupKey = EncounterGroupKey ?? string.Empty;
        target.NameLocalizationKey = NameLocalizationKey ?? string.Empty;
        target.NameOverride = NameOverride ?? string.Empty;
        target.PortraitNpcType = PortraitNpcType ?? string.Empty;
        target.TriggerNpcTypes = [.. TriggerNpcTypes ?? []];
        target.BodyNpcTypes = [.. BodyNpcTypes ?? []];
        target.ControllerNpcTypes = [.. ControllerNpcTypes ?? []];
        target.AddNpcTypes = [.. AddNpcTypes ?? []];
        target.BarrierNpcTypes = [.. BarrierNpcTypes ?? []];
        target.KeepAliveNpcTypes = [.. KeepAliveNpcTypes ?? []];
        target.FinalNpcTypes = [.. FinalNpcTypes ?? []];
    }

    private void CopyCommonFrom(BossOverrideConfig source)
    {
        BossKey = source.BossKey ?? string.Empty;
        EncounterGroupKey = source.EncounterGroupKey ?? string.Empty;
        NameLocalizationKey = source.NameLocalizationKey ?? string.Empty;
        NameOverride = source.NameOverride ?? string.Empty;
        PortraitNpcType = source.PortraitNpcType ?? string.Empty;
        TriggerNpcTypes = [.. source.TriggerNpcTypes ?? []];
        BodyNpcTypes = [.. source.BodyNpcTypes ?? []];
        ControllerNpcTypes = [.. source.ControllerNpcTypes ?? []];
        AddNpcTypes = [.. source.AddNpcTypes ?? []];
        BarrierNpcTypes = [.. source.BarrierNpcTypes ?? []];
        KeepAliveNpcTypes = [.. source.KeepAliveNpcTypes ?? []];
        FinalNpcTypes = [.. source.FinalNpcTypes ?? []];
    }

    private static bool? ToNullable(OverrideBooleanMode value) => value switch
    {
        OverrideBooleanMode.Enabled => true,
        OverrideBooleanMode.Disabled => false,
        _ => null
    };

    private static OverrideBooleanMode FromNullable(bool? value) => value switch
    {
        true => OverrideBooleanMode.Enabled,
        false => OverrideBooleanMode.Disabled,
        null => OverrideBooleanMode.Inherit
    };
}

// Runtime snapshot and legacy DaybreakDamageTracker.Server.json schema. The active
// native ModConfig is flattened below so tModLoader can render a clear settings page.
public sealed class DamageServerConfig
{
    public int SchemaVersion { get; set; } = 4;
    public PresentationSettings Presentation { get; set; } = new();
    public ResultRecipients Recipients { get; set; } = ResultRecipients.AllActivePlayers;
    public int TransitionSyncGraceTicks { get; set; } = 2;
    public int GenericDespawnFallbackSeconds { get; set; }
    public List<BossOverrideConfig> BossOverrides { get; set; } = [];

    internal void Sanitize()
    {
        SchemaVersion = 4;
        Presentation = (Presentation ?? new PresentationSettings()).SanitizedClone();
        Recipients = ResultRecipientPolicy.FailClosed(Recipients);
        TransitionSyncGraceTicks = Math.Clamp(TransitionSyncGraceTicks, 0, 120);
        GenericDespawnFallbackSeconds = Math.Clamp(GenericDespawnFallbackSeconds, 0, 3600);
        BossOverrides = (BossOverrides ?? [])
            .Where(value => value is not null)
            .Select(value => value.DeepClone())
            .Take(ServerConfigValuePolicy.MaximumBossOverrides)
            .ToList();
        int remainingTypeValues = ServerConfigValuePolicy.MaximumTypeValuesAcrossOverrides;
        int remainingText = ServerConfigValuePolicy.MaximumTextCodeUnitsAcrossOverrides;
        foreach (BossOverrideConfig value in BossOverrides)
        {
            value.Sanitize();
            remainingTypeValues -= value.TrimTypeValuesToTotal(remainingTypeValues);
            remainingText -= value.TrimTextToTotal(remainingText);
        }
        ServerConfigValuePolicy.RemoveDuplicateBossKeys(BossOverrides, value => value.BossKey);
    }

    internal DamageServerConfig DeepClone()
    {
        DamageServerConfig clone = new()
        {
            SchemaVersion = SchemaVersion,
            Presentation = (Presentation ?? new PresentationSettings()).SanitizedClone(),
            Recipients = Recipients,
            TransitionSyncGraceTicks = TransitionSyncGraceTicks,
            GenericDespawnFallbackSeconds = GenericDespawnFallbackSeconds,
            BossOverrides = (BossOverrides ?? [])
                .Where(value => value is not null)
                .Select(value => value.DeepClone())
                .ToList()
        };
        clone.Sanitize();
        return clone;
    }
}

[Autoload(Side = ModSide.Both)]
public sealed class ServerConfig : ModConfig
{
    public const bool DefaultShowBossHeader = true;
    public const bool DefaultShowTeamTotal = true;
    public const bool DefaultShowBossBodyDamage = true;
    public const bool DefaultShowDuration = true;
    public const bool DefaultShowRanking = true;
    public const bool DefaultShowPersonalBreakdown = true;
    public const bool DefaultShowUnattributedDamage = true;
    public const bool DefaultShowFinalBlow = true;
    public const bool DefaultChatSummary = true;
    public const int DefaultRankingRows = 10;
    public const int DefaultSourceRootRows = 6;
    public const int DefaultSourceLeafRows = 3;
    public const ResultRecipients DefaultRecipients = ResultRecipients.AllActivePlayers;
    public const int DefaultTransitionSyncGraceTicks = 2;
    public const int DefaultGenericDespawnFallbackSeconds = 0;

    public static ServerConfig Instance { get; private set; } = null!;

    public override ConfigScope Mode => ConfigScope.ServerSide;

    [Header("Preview")]
    [CustomModConfigItem(typeof(ResultPanelPreviewConfigElement))]
    public object PanelPreview { get; set; } = null!;

    [Header("Presentation")]
    [DefaultValue(DefaultShowBossHeader)]
    public bool ShowBossHeader { get; set; } = DefaultShowBossHeader;

    [DefaultValue(DefaultShowTeamTotal)]
    public bool ShowTeamTotal { get; set; } = DefaultShowTeamTotal;

    [DefaultValue(DefaultShowBossBodyDamage)]
    public bool ShowBossBodyDamage { get; set; } = DefaultShowBossBodyDamage;

    [DefaultValue(DefaultShowDuration)]
    public bool ShowDuration { get; set; } = DefaultShowDuration;

    [DefaultValue(DefaultShowRanking)]
    public bool ShowRanking { get; set; } = DefaultShowRanking;

    [DefaultValue(DefaultShowPersonalBreakdown)]
    public bool ShowPersonalBreakdown { get; set; } = DefaultShowPersonalBreakdown;

    [DefaultValue(DefaultShowUnattributedDamage)]
    public bool ShowUnattributedDamage { get; set; } = DefaultShowUnattributedDamage;

    [DefaultValue(DefaultShowFinalBlow)]
    public bool ShowFinalBlow { get; set; } = DefaultShowFinalBlow;

    [DefaultValue(DefaultChatSummary)]
    public bool ChatSummary { get; set; } = DefaultChatSummary;

    [Header("RowLimits")]
    [DefaultValue(DefaultRankingRows)]
    [Range(1, 64)]
    [Increment(1)]
    [Slider]
    public int RankingRows { get; set; } = DefaultRankingRows;

    [DefaultValue(DefaultSourceRootRows)]
    [Range(1, 32)]
    [Increment(1)]
    [Slider]
    public int SourceRootRows { get; set; } = DefaultSourceRootRows;

    [DefaultValue(DefaultSourceLeafRows)]
    [Range(1, 16)]
    [Increment(1)]
    [Slider]
    public int SourceLeafRows { get; set; } = DefaultSourceLeafRows;

    [Header("Delivery")]
    [DefaultValue(DefaultRecipients)]
    public ResultRecipients Recipients { get; set; } = DefaultRecipients;

    [Header("Lifecycle")]
    [DefaultValue(DefaultTransitionSyncGraceTicks)]
    [Range(0, 120)]
    [Increment(1)]
    [Slider]
    public int TransitionSyncGraceTicks { get; set; } = DefaultTransitionSyncGraceTicks;

    [DefaultValue(DefaultGenericDespawnFallbackSeconds)]
    [Range(0, 3600)]
    [Increment(1)]
    public int GenericDespawnFallbackSeconds { get; set; } = DefaultGenericDespawnFallbackSeconds;

    [Header("BossOverrides")]
    [SeparatePage]
    public List<BossOverrideEntryConfig> BossOverrides { get; set; } = [];

    [Header("Reset")]
    [CustomModConfigItem(typeof(ResetDefaultsConfigElement))]
    public object ResetAllToDefaults { get; set; } = null!;

    public override void OnLoaded()
    {
        Instance = this;
        ServerConfigService.TryImportLegacy(this);
        bool exceededBounds = !TryValidateBounds(this, out string boundsMessage);
        Sanitize();
        if (exceededBounds)
        {
            Mod.Logger.Warn(
                $"Server config exceeded safe bounds and was deterministically sanitized: {boundsMessage}");
        }
    }

    public override void OnChanged()
    {
        Sanitize();
        ServerConfigService.NotifyChanged(this);
    }

    // tML evaluates NeedsReload before a multiplayer client serializes and sends a
    // ServerSide config. Treat an invalid/oversized candidate as unsavable here so a
    // local UI edit cannot construct an over-limit native config packet.
    public override bool NeedsReload(ModConfig pendingConfig)
        => pendingConfig is ServerConfig pending && !TryValidateBounds(pending, out _);

    public override ModConfig Clone()
    {
        ServerConfig clone = (ServerConfig)base.Clone();
        clone.BossOverrides = (BossOverrides ?? [])
            .Where(value => value is not null)
            .Select(value => value.DeepClone())
            .ToList();
        return clone;
    }

    public override bool AcceptClientChanges(
        ModConfig pendingConfig,
        int whoAmI,
        ref NetworkText message)
    {
        if (pendingConfig is not ServerConfig pending)
        {
            message = NetworkText.FromKey("Mods.DaybreakDamageTracker.ConfigMessages.InvalidServerConfig");
            return false;
        }

        if (!ServerConfigEditAuthorization.CanEdit(whoAmI))
        {
            message = NetworkText.FromKey("Mods.DaybreakDamageTracker.ConfigMessages.ServerEditDenied");
            return false;
        }

        if (!Enum.IsDefined(pending.Recipients))
        {
            message = NetworkText.FromKey("Mods.DaybreakDamageTracker.ConfigMessages.InvalidServerConfig");
            return false;
        }

        if (!TryValidateBounds(pending, out _))
        {
            message = NetworkText.FromKey("Mods.DaybreakDamageTracker.ConfigMessages.InvalidServerConfig");
            return false;
        }

        pending.Sanitize();
        return true;
    }

    internal DamageServerConfig ToRuntimeSnapshot()
    {
        Sanitize();
        DamageServerConfig snapshot = new()
        {
            SchemaVersion = 4,
            Presentation = ToPresentationSnapshot(),
            Recipients = Recipients,
            TransitionSyncGraceTicks = TransitionSyncGraceTicks,
            GenericDespawnFallbackSeconds = GenericDespawnFallbackSeconds,
            BossOverrides = (BossOverrides ?? [])
                .Where(value => value is not null)
                .Select(value => value.ToRuntime())
                .ToList()
        };
        snapshot.Sanitize();
        return snapshot;
    }

    internal PresentationSettings ToPresentationSnapshot() => new PresentationSettings
    {
        AutoShow = true,
        AutoShowSeconds = 12f,
        // Retained only for the version-2 wire shape. Current clients use a
        // fixed local fade and never accept panel timing from the server.
        FadeSeconds = PresentationSettings.DefaultFadeSeconds,
        ShowBossHeader = ShowBossHeader,
        ShowTeamTotal = ShowTeamTotal,
        ShowBossBodyDamage = ShowBossBodyDamage,
        ShowDuration = ShowDuration,
        ShowRanking = ShowRanking,
        ShowPersonalBreakdown = ShowPersonalBreakdown,
        ShowUnattributedDamage = ShowUnattributedDamage,
        ShowFinalBlow = ShowFinalBlow,
        RankingRows = Math.Clamp(RankingRows, 1, 64),
        SourceRootRows = Math.Clamp(SourceRootRows, 1, 32),
        SourceLeafRows = Math.Clamp(SourceLeafRows, 1, 16),
        HistoryCount = PresentationSettings.MaximumHistoryCount,
        ChatSummary = ChatSummary
    };

    internal void ImportLegacy(DamageServerConfig legacy)
    {
        legacy.Sanitize();
        PresentationSettings presentation = legacy.Presentation;
        ShowBossHeader = presentation.ShowBossHeader;
        ShowTeamTotal = presentation.ShowTeamTotal;
        ShowBossBodyDamage = presentation.ShowBossBodyDamage;
        ShowDuration = presentation.ShowDuration;
        ShowRanking = presentation.ShowRanking;
        ShowPersonalBreakdown = presentation.ShowPersonalBreakdown;
        ShowUnattributedDamage = presentation.ShowUnattributedDamage;
        ShowFinalBlow = presentation.ShowFinalBlow;
        ChatSummary = presentation.ChatSummary;
        RankingRows = presentation.RankingRows;
        SourceRootRows = presentation.SourceRootRows;
        SourceLeafRows = presentation.SourceLeafRows;
        Recipients = legacy.Recipients;
        TransitionSyncGraceTicks = legacy.TransitionSyncGraceTicks;
        GenericDespawnFallbackSeconds = legacy.GenericDespawnFallbackSeconds;
        BossOverrides = legacy.BossOverrides
            .Where(value => value is not null)
            .Select(BossOverrideEntryConfig.FromRuntime)
            .ToList();
        Sanitize();
    }

    internal void ResetToDefaults()
    {
        ShowBossHeader = DefaultShowBossHeader;
        ShowTeamTotal = DefaultShowTeamTotal;
        ShowBossBodyDamage = DefaultShowBossBodyDamage;
        ShowDuration = DefaultShowDuration;
        ShowRanking = DefaultShowRanking;
        ShowPersonalBreakdown = DefaultShowPersonalBreakdown;
        ShowUnattributedDamage = DefaultShowUnattributedDamage;
        ShowFinalBlow = DefaultShowFinalBlow;
        ChatSummary = DefaultChatSummary;
        RankingRows = DefaultRankingRows;
        SourceRootRows = DefaultSourceRootRows;
        SourceLeafRows = DefaultSourceLeafRows;
        Recipients = DefaultRecipients;
        TransitionSyncGraceTicks = DefaultTransitionSyncGraceTicks;
        GenericDespawnFallbackSeconds = DefaultGenericDespawnFallbackSeconds;
        BossOverrides ??= [];
        BossOverrides.Clear();
    }

    private void Sanitize()
    {
        Recipients = ResultRecipientPolicy.FailClosed(Recipients);
        RankingRows = Math.Clamp(RankingRows, 1, 64);
        SourceRootRows = Math.Clamp(SourceRootRows, 1, 32);
        SourceLeafRows = Math.Clamp(SourceLeafRows, 1, 16);
        TransitionSyncGraceTicks = Math.Clamp(TransitionSyncGraceTicks, 0, 120);
        GenericDespawnFallbackSeconds = Math.Clamp(GenericDespawnFallbackSeconds, 0, 3600);
        BossOverrides ??= [];
        for (int index = BossOverrides.Count - 1; index >= 0; index--)
        {
            if (BossOverrides[index] is null)
                BossOverrides.RemoveAt(index);
        }
        if (BossOverrides.Count > ServerConfigValuePolicy.MaximumBossOverrides)
        {
            BossOverrides.RemoveRange(
                ServerConfigValuePolicy.MaximumBossOverrides,
                BossOverrides.Count - ServerConfigValuePolicy.MaximumBossOverrides);
        }

        int remainingTypeValues = ServerConfigValuePolicy.MaximumTypeValuesAcrossOverrides;
        int remainingText = ServerConfigValuePolicy.MaximumTextCodeUnitsAcrossOverrides;
        foreach (BossOverrideEntryConfig value in BossOverrides)
        {
            value.Sanitize();
            remainingTypeValues -= value.TrimTypeValuesToTotal(remainingTypeValues);
            remainingText -= value.TrimTextToTotal(remainingText);
        }
        ServerConfigValuePolicy.RemoveDuplicateBossKeys(BossOverrides, value => value.BossKey);


        // A disk-loaded config may predate the limits above. Preserve the earliest
        // deterministic entries and drop only trailing overrides until the exact
        // compact JSON synchronized by tML fits the reserved packet budget.
        while (BossOverrides.Count > 0 && !SerializedSizeFits(this))
            BossOverrides.RemoveAt(BossOverrides.Count - 1);
    }

    // ServerConfigService reloads a populated clone from disk before asking tML
    // to apply it. Keep the implementation private to the config model while
    // allowing that trusted disk path to deterministically trim invalid input.
    internal void SanitizeForService() => Sanitize();

    internal static bool TryValidateBounds(ServerConfig config, out string message)
    {
        if (!Enum.IsDefined(config.Recipients))
            return Invalid("Recipients is undefined.", out message);
        if (config.RankingRows is < 1 or > 64 ||
            config.SourceRootRows is < 1 or > 32 ||
            config.SourceLeafRows is < 1 or > 16 ||
            config.TransitionSyncGraceTicks is < 0 or > 120 ||
            config.GenericDespawnFallbackSeconds is < 0 or > 3600)
        {
            return Invalid("A numeric setting is outside its supported range.", out message);
        }

        if (config.BossOverrides is null)
            return Invalid("BossOverrides is null.", out message);
        if (config.BossOverrides.Count > ServerConfigValuePolicy.MaximumBossOverrides)
            return Invalid($"BossOverrides exceeds {ServerConfigValuePolicy.MaximumBossOverrides} entries.", out message);

        int globalTypeValues = 0;
        int globalTextCodeUnits = 0;
        foreach (BossOverrideEntryConfig? entry in config.BossOverrides)
        {
            if (entry is null)
                return Invalid("BossOverrides contains a null entry.", out message);
            if (!Enum.IsDefined(entry.FinishOnBodyDeathWhenNoParticipants) ||
                !Enum.IsDefined(entry.ContinueAfterPartyWipe))
            {
                return Invalid("A Boss override contains an undefined three-state value.", out message);
            }

            if (!TextFits(entry.BossKey, ServerConfigValuePolicy.MaximumBossKeyLength) ||
                !TextFits(entry.EncounterGroupKey, ServerConfigValuePolicy.MaximumTextLength) ||
                !TextFits(entry.NameLocalizationKey, ServerConfigValuePolicy.MaximumTextLength) ||
                !TextFits(entry.NameOverride, ServerConfigValuePolicy.MaximumLiteralNameLength) ||
                !TextFits(entry.PortraitNpcType, ServerConfigValuePolicy.MaximumTextLength))
            {
                return Invalid("A Boss override text field is too long.", out message);
            }

            List<string>?[] roles =
            [
                entry.TriggerNpcTypes,
                entry.BodyNpcTypes,
                entry.ControllerNpcTypes,
                entry.AddNpcTypes,
                entry.BarrierNpcTypes,
                entry.KeepAliveNpcTypes,
                entry.FinalNpcTypes
            ];
            int entryTypeValues = 0;
            globalTextCodeUnits +=
                entry.BossKey.Length +
                entry.EncounterGroupKey.Length +
                entry.NameLocalizationKey.Length +
                entry.NameOverride.Length +
                entry.PortraitNpcType.Length;
            foreach (List<string>? role in roles)
            {
                if (role is null)
                    return Invalid("A Boss override type list is null.", out message);
                if (role.Count > ServerConfigValuePolicy.MaximumTypeValuesPerRole)
                    return Invalid("A Boss override type role contains too many values.", out message);
                if (role.Any(value => !TextFits(value, ServerConfigValuePolicy.MaximumTextLength)))
                    return Invalid("A Boss override type value is too long or null.", out message);
                entryTypeValues += role.Count;
                globalTextCodeUnits += role.Sum(value => value.Length);
            }
            if (entryTypeValues > ServerConfigValuePolicy.MaximumTypeValuesPerOverride)
                return Invalid("A Boss override contains too many total type values.", out message);
            globalTypeValues += entryTypeValues;
        }

        if (globalTypeValues > ServerConfigValuePolicy.MaximumTypeValuesAcrossOverrides)
            return Invalid("BossOverrides contains too many total type values.", out message);
        if (globalTextCodeUnits > ServerConfigValuePolicy.MaximumTextCodeUnitsAcrossOverrides)
            return Invalid("BossOverrides contains too much total text.", out message);
        if (!SerializedSizeFits(config))
            return Invalid("The serialized server config exceeds the safe native packet budget.", out message);

        message = string.Empty;
        return true;

        static bool TextFits(string? value, int maximumLength)
            => value is not null && value.Length <= maximumLength;

        static bool Invalid(string reason, out string output)
        {
            output = reason;
            return false;
        }
    }

    private static bool SerializedSizeFits(ServerConfig config)
    {
        try
        {
            string json = JsonConvert.SerializeObject(config, ConfigManager.serializerSettings);
            return ServerConfigValuePolicy.FitsSerializedConfigBudget(Encoding.UTF8.GetByteCount(json));
        }
        catch
        {
            return false;
        }
    }

}

internal static class ServerConfigService
{
    private static readonly JsonSerializerOptions LegacyJsonOptions = new()
    {
        WriteIndented = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly object RuntimeSync = new();
    private static bool _runtimeActive;
    private static DamageServerConfig? _pendingRuntimeSnapshot;
    private static bool _pendingMigrationSave;
    private static string _migrationMessage = string.Empty;

    public static DamageServerConfig Current { get; private set; } = CreateDefaultSnapshot();
    public static bool HasPendingRuntimeApply
    {
        get
        {
            lock (RuntimeSync)
                return _pendingRuntimeSnapshot is not null;
        }
    }
    public static string ConfigPath => Path.Combine(Main.SavePath, "ModConfigs", "DaybreakDamageTracker_ServerConfig.json");
    public static string LegacyConfigPath => Path.Combine(Main.SavePath, "ModConfigs", "DaybreakDamageTracker.Server.json");

    internal static void TryImportLegacy(ServerConfig target)
    {
        if (File.Exists(ConfigPath) || !File.Exists(LegacyConfigPath))
            return;

        try
        {
            DamageServerConfig legacy = LegacyJsonSerializer.Deserialize<DamageServerConfig>(
                File.ReadAllText(LegacyConfigPath),
                LegacyJsonOptions) ?? new DamageServerConfig();
            target.ImportLegacy(legacy);
            _pendingMigrationSave = true;
            _migrationMessage = $"Imported legacy server settings from {LegacyConfigPath}.";
        }
        catch (Exception exception)
        {
            _migrationMessage = $"Could not import legacy server settings; native defaults remain active. {exception.Message}";
        }
    }

    public static void PersistPendingMigration()
    {
        if (!_pendingMigrationSave)
        {
            if (!string.IsNullOrEmpty(_migrationMessage))
            {
                DaybreakDamageTrackerMod.Instance.Logger.Warn(_migrationMessage);
                _migrationMessage = string.Empty;
            }
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            ConfigSaveResult result = ServerConfig.Instance.SaveChanges(
                silent: true,
                broadcast: false);
            if (result != ConfigSaveResult.Success)
                throw new InvalidOperationException($"Migration save returned {result}.");
            _pendingMigrationSave = false;
            DaybreakDamageTrackerMod.Instance.Logger.Info(
                $"{_migrationMessage} Saved native server config to {ConfigPath}; the legacy file was retained.");
            _migrationMessage = string.Empty;
        }
        catch (Exception exception)
        {
            DaybreakDamageTrackerMod.Instance.Logger.Error(
                $"Failed to persist migrated server config to {ConfigPath}; the in-memory import remains active.",
                exception);
        }
    }

    public static void ActivateForWorld()
    {
        if (Main.netMode == NetmodeID.MultiplayerClient)
            return;

        DamageServerConfig initial = ServerConfig.Instance.ToRuntimeSnapshot();
        lock (RuntimeSync)
        {
            _runtimeActive = true;
            // Preserve a newer OnChanged snapshot that raced with world activation.
            _pendingRuntimeSnapshot ??= initial;
        }
        ApplyPendingIfIdle();
    }

    public static void Deactivate()
    {
        lock (RuntimeSync)
        {
            _runtimeActive = false;
            _pendingRuntimeSnapshot = null;
        }
        Current = CreateDefaultSnapshot();
        ServerConfigEditAuthorization.Reset();
    }

    public static void NotifyChanged(ServerConfig config)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient)
            return;

        // OnChanged can be raised from tML's config packet handler. Snapshot only the
        // newly populated config on that thread, then publish the immutable runtime
        // copy through a latest-value mailbox. BossCatalog and encounter state are
        // touched exclusively by ApplyPendingIfIdle on PostUpdateWorld.
        DamageServerConfig snapshot = config.ToRuntimeSnapshot();
        lock (RuntimeSync)
            _pendingRuntimeSnapshot = snapshot;
    }

    public static void ApplyPendingIfIdle()
    {
        if (EncounterSystem.State != EncounterState.Idle)
            return;

        DamageServerConfig? snapshot;
        lock (RuntimeSync)
        {
            if (!_runtimeActive || _pendingRuntimeSnapshot is null)
                return;

            snapshot = _pendingRuntimeSnapshot;
            _pendingRuntimeSnapshot = null;
        }

        ApplyRuntime(snapshot);
    }

    public static bool Reload(out string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            if (!File.Exists(ConfigPath))
            {
                ConfigSaveResult createResult = ServerConfig.Instance.SaveChanges(
                    silent: true,
                    broadcast: false);
                if (createResult != ConfigSaveResult.Success)
                    throw new InvalidOperationException($"Config creation returned {createResult}.");
                message = $"Created native server config: {ConfigPath}";
                return true;
            }

            ModConfig populated = ConfigManager.GeneratePopulatedClone(ServerConfig.Instance);
            if (populated is not ServerConfig pending)
                throw new InvalidOperationException("The native server config clone had an unexpected type.");

            JsonConvert.PopulateObject(
                File.ReadAllText(ConfigPath),
                pending,
                ConfigManager.serializerSettings);
            if (!ServerConfig.TryValidateBounds(pending, out string boundsMessage))
            {
                DaybreakDamageTrackerMod.Instance.Logger.Warn(
                    $"Reloaded server config exceeded safe bounds and was deterministically sanitized: {boundsMessage}");
                pending.SanitizeForService();
                if (!ServerConfig.TryValidateBounds(pending, out string sanitizedBoundsMessage))
                {
                    message =
                        $"Native server config reload was rejected after deterministic sanitation: " +
                        $"{sanitizedBoundsMessage}.";
                    DaybreakDamageTrackerMod.Instance.Logger.Error(message);
                    return false;
                }
            }
            ConfigSaveResult result = ServerConfig.Instance.SaveChanges(pending, silent: true, broadcast: true);
            if (result != ConfigSaveResult.Success)
            {
                message = $"Native server config reload was not applied: {result}.";
                return false;
            }

            message = HasPendingRuntimeApply
                ? $"Reloaded native server config; lifecycle changes will apply after the active encounter: {ConfigPath}"
                : $"Reloaded native server config: {ConfigPath}";
            return true;
        }
        catch (Exception exception)
        {
            message = $"Failed to reload {ConfigPath}; the last applied settings remain active. {exception.Message}";
            DaybreakDamageTrackerMod.Instance.Logger.Error(message, exception);
            return false;
        }
    }

    private static void ApplyRuntime(DamageServerConfig snapshot)
    {
        Current = snapshot.DeepClone();
        BossCatalog.ApplyServerOverrides(Current.BossOverrides);
        ServerResultHistory.Trim(PresentationSettings.MaximumHistoryCount);

        if (Main.netMode == NetmodeID.Server)
            DamageNetwork.BroadcastPresentation();
        else if (!Main.dedServ)
            ClientRuntime.ApplyPresentation(Current.Presentation.SanitizedClone());
    }

    private static DamageServerConfig CreateDefaultSnapshot()
    {
        DamageServerConfig config = new();
        config.Sanitize();
        return config;
    }
}

internal static class ServerConfigEditAuthorization
{
    private static readonly HashSet<EncounterPlayerKey> GrantedConnections = [];
    private static readonly object Sync = new();

    public static bool CanEdit(int playerIndex)
    {
        if (playerIndex < 0 || playerIndex >= Main.maxPlayers || !Main.player[playerIndex].active)
            return false;

        EncounterPlayerKey connection = PlayerConnectionRegistry.GetCurrent(playerIndex);
        bool isHost = NetMessage.DoesPlayerSlotCountAsAHost(playerIndex);
        lock (Sync)
            return ServerConfigAuthorizationPolicy.CanEdit(isHost, GrantedConnections.Contains(connection));
    }

    public static bool Grant(int playerIndex, out string message)
    {
        if (playerIndex < 0 || playerIndex >= Main.maxPlayers || !Main.player[playerIndex].active)
        {
            message = $"Player slot {playerIndex} is not active.";
            return false;
        }

        EncounterPlayerKey connection = PlayerConnectionRegistry.GetCurrent(playerIndex);
        lock (Sync)
        {
            GrantedConnections.RemoveWhere(key => key.PlayerIndex == playerIndex && key != connection);
            GrantedConnections.Add(connection);
        }
        message = $"Granted server-config editing to the current connection in slot {playerIndex}.";
        return true;
    }

    public static bool Revoke(int playerIndex)
    {
        lock (Sync)
            return GrantedConnections.RemoveWhere(key => key.PlayerIndex == playerIndex) > 0;
    }

    public static void RevokePlayer(int playerIndex) => Revoke(playerIndex);

    internal static bool RevokeConnection(EncounterPlayerKey connection)
    {
        lock (Sync)
            return GrantedConnections.Remove(connection);
    }

    public static int Count
    {
        get
        {
            lock (Sync)
            {
                GrantedConnections.RemoveWhere(key => !PlayerConnectionRegistry.IsCurrent(key));
                return GrantedConnections.Count;
            }
        }
    }

    public static void Reset()
    {
        lock (Sync)
            GrantedConnections.Clear();
    }
}
