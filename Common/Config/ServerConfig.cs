using System.Text.Json;
using System.Text.Json.Serialization;
using DaybreakDamageTracker.Common.Bosses;
using DaybreakDamageTracker.Common.Data;

namespace DaybreakDamageTracker.Common.Config;

public enum ResultRecipients
{
    AllActivePlayers,
    Contributors
}

public sealed class BossOverrideConfig
{
    public string BossKey { get; set; } = string.Empty;
    public string NameLocalizationKey { get; set; } = string.Empty;
    public string NameOverride { get; set; } = string.Empty;
    public string PortraitNpcType { get; set; } = string.Empty;
    public List<string> TriggerNpcTypes { get; set; } = [];
    public List<string> BodyNpcTypes { get; set; } = [];
    public List<string> AddNpcTypes { get; set; } = [];
    public List<string> BarrierNpcTypes { get; set; } = [];
    public List<string> KeepAliveNpcTypes { get; set; } = [];
    public List<string> FinalNpcTypes { get; set; } = [];
    public bool? FinishOnBodyDeathWhenNoParticipants { get; set; }
}

public sealed class DamageServerConfig
{
    public int SchemaVersion { get; set; } = 1;
    public PresentationSettings Presentation { get; set; } = new();
    public ResultRecipients Recipients { get; set; } = ResultRecipients.AllActivePlayers;
    public int TransitionSyncGraceTicks { get; set; } = 2;
    public int GenericDespawnFallbackSeconds { get; set; }
    public List<BossOverrideConfig> BossOverrides { get; set; } = [];

    internal void Sanitize()
    {
        Presentation = (Presentation ?? new PresentationSettings()).SanitizedClone();
        TransitionSyncGraceTicks = Math.Clamp(TransitionSyncGraceTicks, 0, 120);
        GenericDespawnFallbackSeconds = Math.Clamp(GenericDespawnFallbackSeconds, 0, 3600);
        BossOverrides ??= [];
    }
}

internal static class ServerConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static DamageServerConfig Current { get; private set; } = new();

    public static string ConfigPath => Path.Combine(Main.SavePath, "ModConfigs", "DaybreakDamageTracker.Server.json");

    public static bool Reload(out string message)
    {
        try
        {
            string path = ConfigPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            if (!File.Exists(path))
            {
                Current = new DamageServerConfig();
                Current.Sanitize();
                File.WriteAllText(path, JsonSerializer.Serialize(Current, JsonOptions));
                message = $"Created default server config: {path}";
            }
            else
            {
                Current = JsonSerializer.Deserialize<DamageServerConfig>(File.ReadAllText(path), JsonOptions)
                    ?? new DamageServerConfig();
                Current.Sanitize();
                message = $"Reloaded server config: {path}";
            }

            BossCatalog.ApplyServerOverrides(Current.BossOverrides);
            return true;
        }
        catch (Exception exception)
        {
            Current = new DamageServerConfig();
            Current.Sanitize();
            BossCatalog.ApplyServerOverrides(Current.BossOverrides);
            message = $"Failed to load {ConfigPath}; defaults are active. {exception.Message}";
            DaybreakDamageTrackerMod.Instance.Logger.Error(message, exception);
            return false;
        }
    }
}
