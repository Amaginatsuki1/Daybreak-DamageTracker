using DaybreakDamageTracker.Client;
using DaybreakDamageTracker.Client.UI;
using DaybreakDamageTracker.Common.Data;

namespace DaybreakDamageTracker.Common.Commands;

[Autoload(Side = ModSide.Client)]
internal sealed class DtCommand : ModCommand
{
    public override CommandType Type => CommandType.Chat;
    public override string Command => "dt";
    public override string Usage => "/dt | /dt <number> | /dt list | /dt chat | /dt close";
    public override string Description => ClientRuntime.Text("Commands.Dt.Description", "Open or close saved damage results.");

    public override void Action(CommandCaller caller, string input, string[] args)
    {
        DamageResultHudSystem hud = ModContent.GetInstance<DamageResultHudSystem>();
        if (args.Length == 0)
        {
            if (ClientResultHistory.Latest is null)
            {
                caller.Reply(ClientRuntime.Text("Commands.Dt.NoHistory", "No saved damage result is available."), Color.Gray);
                return;
            }
            hud.ToggleLatest();
            return;
        }

        if (args[0].Equals("close", StringComparison.OrdinalIgnoreCase))
        {
            hud.Hide();
            return;
        }

        if (args[0].Equals("chat", StringComparison.OrdinalIgnoreCase))
        {
            if (ClientResultHistory.Latest is ClientHistoryEntry latest)
                caller.Reply(ClientRuntime.FormatCompactSummary(latest.Public), new Color(125, 220, 255));
            else
                caller.Reply(ClientRuntime.Text("Commands.Dt.NoHistory", "No saved damage result is available."), Color.Gray);
            return;
        }

        if (args[0].Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            if (ClientResultHistory.Entries.Count == 0)
            {
                caller.Reply(ClientRuntime.Text("Commands.Dt.NoHistory", "No saved damage result is available."), Color.Gray);
                return;
            }

            for (int i = 0; i < ClientResultHistory.Entries.Count; i++)
            {
                PublicResultSnapshot result = ClientResultHistory.Entries[i].Public;
                string boss = result.Bosses.Count == 0
                    ? ClientRuntime.Text("UI.UnknownBoss", "Unknown boss")
                    : string.Join(" + ", result.Bosses.Select(ClientRuntime.ResolveBossName));
                caller.Reply($"[{i + 1}] {boss} — {result.TeamDamage:N0} — {ClientRuntime.FormatDuration(result.DurationTicks)}", Color.LightBlue);
            }
            return;
        }

        if (int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) &&
            ClientResultHistory.GetOneBased(index) is ClientHistoryEntry selected)
        {
            hud.Show(selected, automatic: false);
            return;
        }

        throw new UsageException(Usage);
    }
}
