using DaybreakDamageTracker.Client.UI;
using DaybreakDamageTracker.Common.Bosses;
using DaybreakDamageTracker.Common.Data;
using DaybreakDamageTracker.Common.Networking;

namespace DaybreakDamageTracker.Client;

internal static class ClientRuntime
{
    private static readonly MutableSourceTree CurrentSources = new();
    private static bool _encounterPrepared;
    private static int _historyBatchId;
    private static ResultDelivery?[] _pendingHistory = [];

    public static PresentationSettings Presentation { get; private set; } = new();
    public static long ActiveEncounterId { get; private set; }

    public static void ResetForWorld()
    {
        Presentation = new PresentationSettings();
        ActiveEncounterId = 0;
        _encounterPrepared = false;
        _historyBatchId = 0;
        _pendingHistory = [];
        CurrentSources.Clear();
        ClientResultHistory.Reset();
        if (!Main.dedServ)
            ModContent.GetInstance<DamageResultHudSystem>()?.Hide();
    }

    public static void ApplyPresentation(PresentationSettings presentation)
    {
        Presentation = presentation.SanitizedClone();
        ClientResultHistory.Trim(Presentation.HistoryCount);
        if (!Main.dedServ)
            ModContent.GetInstance<DamageResultHudSystem>().RefreshPresentation();
    }

    public static void OnEncounterArmed(long encounterId, IReadOnlyList<BossPresentation> bosses)
    {
        if (!_encounterPrepared || (ActiveEncounterId != 0 && ActiveEncounterId != encounterId))
            CurrentSources.Clear();

        ActiveEncounterId = encounterId;
        _encounterPrepared = true;
    }

    public static void OnEncounterStarted(long encounterId)
    {
        if (!_encounterPrepared || (ActiveEncounterId != 0 && ActiveEncounterId != encounterId))
            CurrentSources.Clear();

        ActiveEncounterId = encounterId;
        _encounterPrepared = true;
    }

    public static void OnEncounterCancelled(long encounterId)
    {
        if (ActiveEncounterId != encounterId && ActiveEncounterId != 0)
            return;

        ActiveEncounterId = 0;
        _encounterPrepared = false;
        CurrentSources.Clear();
    }

    public static void RecordPrivateDamage(NPC target, DamageRootKey root, DamageLeafKey leaf, int damage)
    {
        if (!_encounterPrepared)
        {
            if (!BossCatalog.HasActiveBossClientSide())
                return;

            ActiveEncounterId = 0;
            _encounterPrepared = true;
            CurrentSources.Clear();
        }

        CurrentSources.Add(root, leaf, damage);
    }

    public static void OnPublicResult(PublicResultSnapshot result, long? localTotal)
    {
        bool matchesCurrent = _encounterPrepared && (ActiveEncounterId == 0 || ActiveEncounterId == result.EncounterId);
        SourceTreeSnapshot privateTree = matchesCurrent && localTotal.HasValue
            ? CurrentSources.Reconcile(localTotal.Value)
            : SourceTreeSnapshot.Unavailable(localTotal);

        ClientHistoryEntry entry = new()
        {
            Public = result,
            PrivateSources = privateTree
        };
        ClientResultHistory.Add(entry, Presentation.HistoryCount);

        ActiveEncounterId = 0;
        _encounterPrepared = false;
        CurrentSources.Clear();

        if (Presentation.ChatSummary)
            Main.NewText(FormatCompactSummary(result), new Color(125, 220, 255));
        if (Presentation.AutoShow)
            ModContent.GetInstance<DamageResultHudSystem>().Show(entry, automatic: true);
    }

    public static void OnHistorySyncBegin(int batchId, int count)
    {
        _historyBatchId = batchId;
        _pendingHistory = new ResultDelivery?[count];
        if (count == 0)
            ClientResultHistory.ReplacePublicHistory([], Presentation.HistoryCount);
    }

    public static void OnHistorySyncEntry(int batchId, int index, ResultDelivery delivery)
    {
        if (batchId != _historyBatchId || index < 0 || index >= _pendingHistory.Length)
            return;

        _pendingHistory[index] = delivery;
        if (_pendingHistory.Any(value => !value.HasValue))
            return;

        ResultDelivery[] complete = _pendingHistory.Select(value => value!.Value).ToArray();
        _pendingHistory = [];
        ClientResultHistory.ReplacePublicHistory(complete, Presentation.HistoryCount);
    }

    public static string FormatCompactSummary(PublicResultSnapshot result)
    {
        string boss = result.Bosses.Count == 0
            ? Text("UI.UnknownBoss", "Unknown boss")
            : string.Join(" + ", result.Bosses.Select(ResolveBossName));
        string duration = FormatDuration(result.DurationTicks);
        return Text("Chat.SavedResult", $"[DT] {boss} — team {result.TeamDamage:N0}, {duration}. /dt to reopen",
            boss, result.TeamDamage.ToString("N0", CultureInfo.CurrentCulture), duration);
    }

    public static string FormatDuration(long ticks)
    {
        TimeSpan span = TimeSpan.FromSeconds(Math.Max(0, ticks) / 60d);
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{span.Minutes:00}:{span.Seconds:00}";
    }

    public static string ResolveBossName(BossPresentation boss)
    {
        if (!string.IsNullOrWhiteSpace(boss.NameLocalizationKey) && Language.Exists(boss.NameLocalizationKey))
        {
            string localized = Language.GetTextValue(boss.NameLocalizationKey);
            if (!string.IsNullOrWhiteSpace(localized))
                return localized;
        }

        if (boss.NameNpcType > NPCID.None && boss.NameNpcType < NPCLoader.NPCCount)
        {
            string localized = Lang.GetNPCNameValue(boss.NameNpcType);
            if (!string.IsNullOrWhiteSpace(localized))
                return localized;
        }

        return string.IsNullOrWhiteSpace(boss.Name)
            ? Text("UI.UnknownBoss", "Unknown boss")
            : boss.Name;
    }

    internal static string Text(string suffix, string fallback, params object[] args)
    {
        string key = $"Mods.DaybreakDamageTracker.{suffix}";
        string value = Language.Exists(key) ? Language.GetTextValue(key) : fallback;
        return args.Length == 0 ? value : string.Format(CultureInfo.CurrentCulture, value, args);
    }
}
