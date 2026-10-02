using System.ComponentModel;
using DaybreakDamageTracker.Client.UI;
using Terraria.ModLoader.Config;

namespace DaybreakDamageTracker.Client;

[Autoload(Side = ModSide.Client)]
public sealed class ClientConfig : ModConfig
{
    public const bool DefaultAutoShowResults = true;
    public const int DefaultHistoryCount = 4;
    public const int DefaultAutoHideSeconds = 12;
    public const float DefaultPanelBackgroundOpacity = 0.90f;

    public override ConfigScope Mode => ConfigScope.ClientSide;

    [Header("Preview")]
    [CustomModConfigItem(typeof(ResultPanelPreviewConfigElement))]
    public object PanelPreview { get; set; } = null!;

    [Header("Behavior")]
    [DefaultValue(DefaultAutoShowResults)]
    public bool AutoShowResults { get; set; } = DefaultAutoShowResults;

    [DefaultValue(DefaultHistoryCount)]
    [Range(1, 10)]
    [Increment(1)]
    [Slider]
    public int HistoryCount { get; set; } = DefaultHistoryCount;

    [DefaultValue(DefaultAutoHideSeconds)]
    [Range(1, 120)]
    [Increment(1)]
    [Slider]
    public int AutoHideSeconds { get; set; } = DefaultAutoHideSeconds;

    [DefaultValue(DefaultPanelBackgroundOpacity)]
    [Range(0f, 1f)]
    [Increment(0.05f)]
    [Slider]
    [CustomModConfigItem(typeof(PanelBackgroundOpacityConfigElement))]
    public float PanelBackgroundOpacity { get; set; } = DefaultPanelBackgroundOpacity;

    [Header("Layout")]
    [CustomModConfigItem(typeof(ResultPanelSectionOrderConfigElement))]
    public List<ResultPanelSection> PanelSectionOrder { get; set; } = ResultPanelSectionOrderPolicy.CreateDefault();

    [Header("Reset")]
    [CustomModConfigItem(typeof(ResetDefaultsConfigElement))]
    public object ResetAllToDefaults { get; set; } = null!;

    public override void OnLoaded()
    {
        Sanitize();
    }

    public override ModConfig Clone()
    {
        ClientConfig clone = (ClientConfig)base.Clone();
        clone.PanelSectionOrder = [.. PanelSectionOrder ?? []];
        return clone;
    }

    public override void OnChanged()
    {
        Sanitize();
        ClientRuntime.ApplyLocalPreferences(this);
    }

    internal void ResetToDefaults()
    {
        AutoShowResults = DefaultAutoShowResults;
        HistoryCount = DefaultHistoryCount;
        AutoHideSeconds = DefaultAutoHideSeconds;
        PanelBackgroundOpacity = DefaultPanelBackgroundOpacity;
        PanelSectionOrder = ResultPanelSectionOrderPolicy.CreateDefault();
    }

    private void Sanitize()
    {
        HistoryCount = Math.Clamp(HistoryCount, 1, 10);
        AutoHideSeconds = Math.Clamp(AutoHideSeconds, 1, 120);
        PanelBackgroundOpacity = Math.Clamp(PanelBackgroundOpacity, 0f, 1f);
        PanelSectionOrder = ResultPanelSectionOrderPolicy.Sanitize(PanelSectionOrder);
    }
}
