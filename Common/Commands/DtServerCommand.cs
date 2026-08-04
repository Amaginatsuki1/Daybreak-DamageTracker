using DaybreakDamageTracker.Common.Config;
using DaybreakDamageTracker.Common.Data;
using DaybreakDamageTracker.Common.Networking;
using DaybreakDamageTracker.Common.Systems;

namespace DaybreakDamageTracker.Common.Commands;

internal sealed class DtServerCommand : ModCommand
{
    public override CommandType Type => CommandType.Console;
    public override string Command => "dtserver";
    public override string Usage => "dtserver reload | status | finish <victory|defeat|escaped|manual>";
    public override string Description => "Manage the Daybreak DamageTracker server state.";

    public override void Action(CommandCaller caller, string input, string[] args)
    {
        if (args.Length == 0 || args[0].Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            caller.Reply($"State={EncounterSystem.State}, EncounterId={EncounterSystem.ActiveEncounterId}, History={ServerResultHistory.All.Count}");
            return;
        }

        if (args[0].Equals("reload", StringComparison.OrdinalIgnoreCase))
        {
            bool success = ServerConfigService.Reload(out string message);
            ServerResultHistory.Trim(ServerConfigService.Current.Presentation.HistoryCount);
            DamageNetwork.BroadcastPresentation();
            caller.Reply(message, success ? Color.LightGreen : Color.OrangeRed);
            return;
        }

        if (args[0].Equals("finish", StringComparison.OrdinalIgnoreCase) && args.Length >= 2 &&
            TryParseOutcome(args[1], out EncounterOutcome outcome))
        {
            caller.Reply(EncounterSystem.ForceFinish(outcome)
                ? $"Encounter finished as {outcome}."
                : "There is no fighting/transitioning encounter to finish.");
            return;
        }

        throw new UsageException(Usage);
    }

    private static bool TryParseOutcome(string value, out EncounterOutcome outcome)
    {
        if (Enum.TryParse(value, true, out outcome) && outcome != EncounterOutcome.Unknown)
            return true;
        outcome = EncounterOutcome.Manual;
        return false;
    }
}
