using DaybreakDamageTracker.Common.Config;
using DaybreakDamageTracker.Common.Data;
using DaybreakDamageTracker.Common.Networking;
using DaybreakDamageTracker.Common.Systems;

namespace DaybreakDamageTracker.Common.Commands;

internal sealed class DtServerCommand : ModCommand
{
    private const string UsageText = "dtserver reload | status | config grant <slot> | config revoke <slot> | config clear | finish <victory|defeat|escaped|manual>";

    public override CommandType Type => CommandType.Console;
    public override string Command => "dtserver";
    public override string Usage => UsageText;
    public override string Description => "Manage the Daybreak DamageTracker server state.";

    public override void Action(CommandCaller caller, string input, string[] args)
    {
        if (args.Length == 0 || args[0].Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            caller.Reply(
                $"State={EncounterSystem.State}, EncounterId={EncounterSystem.ActiveEncounterId}, " +
                $"History={ServerResultHistory.All.Count}, ConfigGrants={ServerConfigEditAuthorization.Count}, " +
                $"ConfigApplyPending={ServerConfigService.HasPendingRuntimeApply}");
            return;
        }

        if (args[0].Equals("reload", StringComparison.OrdinalIgnoreCase))
        {
            bool success = ServerConfigService.Reload(out string message);
            caller.Reply(message, success ? Color.LightGreen : Color.OrangeRed);
            return;
        }

        if (args[0].Equals("config", StringComparison.OrdinalIgnoreCase) && args.Length >= 2)
        {
            HandleConfigAuthorization(caller, args);
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

    private static void HandleConfigAuthorization(CommandCaller caller, string[] args)
    {
        if (args[1].Equals("clear", StringComparison.OrdinalIgnoreCase) && args.Length == 2)
        {
            ServerConfigEditAuthorization.Reset();
            caller.Reply("Cleared all temporary server-config editing grants.", Color.LightGreen);
            return;
        }

        if (args.Length != 3 || !int.TryParse(args[2], out int slot))
            throw new UsageException(UsageText);

        if (args[1].Equals("grant", StringComparison.OrdinalIgnoreCase))
        {
            bool success = ServerConfigEditAuthorization.Grant(slot, out string message);
            caller.Reply(message, success ? Color.LightGreen : Color.OrangeRed);
            return;
        }

        if (args[1].Equals("revoke", StringComparison.OrdinalIgnoreCase))
        {
            bool removed = ServerConfigEditAuthorization.Revoke(slot);
            caller.Reply(
                removed
                    ? $"Revoked server-config editing from slot {slot}."
                    : $"Slot {slot} had no temporary server-config editing grant.",
                removed ? Color.LightGreen : Color.LightGray);
            return;
        }

        throw new UsageException(UsageText);
    }

    private static bool TryParseOutcome(string value, out EncounterOutcome outcome)
    {
        if (Enum.TryParse(value, true, out outcome) && outcome != EncounterOutcome.Unknown)
            return true;
        outcome = EncounterOutcome.Manual;
        return false;
    }
}
