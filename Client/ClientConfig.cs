using System.ComponentModel;
using Terraria.ModLoader.Config;

namespace DaybreakDamageTracker.Client;

[Autoload(Side = ModSide.Client)]
public sealed class ClientConfig : ModConfig
{
    public override ConfigScope Mode => ConfigScope.ClientSide;

    [DefaultValue(true)]
    public bool AutoShowResults { get; set; } = true;

    [DefaultValue(4)]
    [Range(1, 10)]
    [Increment(1)]
    [Slider]
    public int HistoryCount { get; set; } = 4;

    [DefaultValue(12)]
    [Range(1, 120)]
    [Increment(1)]
    [Slider]
    public int AutoHideSeconds { get; set; } = 12;

    [DefaultValue(0.94f)]
    [Range(0f, 1f)]
    [Increment(0.05f)]
    [Slider]
    public float PanelBackgroundOpacity { get; set; } = 0.94f;

    public override void OnChanged()
    {
        HistoryCount = Math.Clamp(HistoryCount, 1, 10);
        AutoHideSeconds = Math.Clamp(AutoHideSeconds, 1, 120);
        PanelBackgroundOpacity = Math.Clamp(PanelBackgroundOpacity, 0f, 1f);
        ClientRuntime.ApplyLocalPreferences(this);
    }
}
