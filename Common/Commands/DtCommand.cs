using DaybreakDamageTracker.Client;
using DaybreakDamageTracker.Client.UI;
using DaybreakDamageTracker.Common.Data;

namespace DaybreakDamageTracker.Common.Commands;

[Autoload(Side = ModSide.Client)]
internal sealed class DtCommand : ModCommand
{
    public override CommandType Type => CommandType.Chat;
    public override string Command => "dt";
    public override string Usage => "/dt | /dt1 ... /dt9 | /dt list | /dt chat | /dt close";
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
            if (ClientResultHistory.Latest is not null)
                caller.Reply(ClientRuntime.FormatHistoryMap(), new Color(125, 220, 255));
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
                caller.Reply($"[DT] {ClientRuntime.FormatCompactSummary(ClientResultHistory.Entries[i].Public, i)}", Color.LightBlue);
            return;
        }

        if (int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) &&
            ClientResultHistory.GetZeroBased(index) is ClientHistoryEntry selected)
        {
            hud.Show(selected, automatic: false);
            return;
        }

        throw new UsageException(Usage);
    }

    internal static void OpenHistoryIndex(CommandCaller caller, int index)
    {
        if (ClientResultHistory.GetZeroBased(index) is not ClientHistoryEntry selected)
        {
            caller.Reply(ClientRuntime.Text(
                "Commands.Dt.NoHistoryAtIndex",
                "No saved damage result is available through /dt{0}.",
                index), Color.Gray);
            return;
        }

        ModContent.GetInstance<DamageResultHudSystem>().Show(selected, automatic: false);
    }
}

[Autoload(Side = ModSide.Client)]
internal sealed class Dt1Command : DtIndexedCommand
{
    public override string Command => "dt1";
    protected override int HistoryIndex => 1;
}

[Autoload(Side = ModSide.Client)]
internal sealed class Dt2Command : DtIndexedCommand
{
    public override string Command => "dt2";
    protected override int HistoryIndex => 2;
}

[Autoload(Side = ModSide.Client)]
internal sealed class Dt3Command : DtIndexedCommand
{
    public override string Command => "dt3";
    protected override int HistoryIndex => 3;
}

[Autoload(Side = ModSide.Client)]
internal sealed class Dt4Command : DtIndexedCommand
{
    public override string Command => "dt4";
    protected override int HistoryIndex => 4;
}

[Autoload(Side = ModSide.Client)]
internal sealed class Dt5Command : DtIndexedCommand
{
    public override string Command => "dt5";
    protected override int HistoryIndex => 5;
}

[Autoload(Side = ModSide.Client)]
internal sealed class Dt6Command : DtIndexedCommand
{
    public override string Command => "dt6";
    protected override int HistoryIndex => 6;
}

[Autoload(Side = ModSide.Client)]
internal sealed class Dt7Command : DtIndexedCommand
{
    public override string Command => "dt7";
    protected override int HistoryIndex => 7;
}

[Autoload(Side = ModSide.Client)]
internal sealed class Dt8Command : DtIndexedCommand
{
    public override string Command => "dt8";
    protected override int HistoryIndex => 8;
}

[Autoload(Side = ModSide.Client)]
internal sealed class Dt9Command : DtIndexedCommand
{
    public override string Command => "dt9";
    protected override int HistoryIndex => 9;
}

internal abstract class DtIndexedCommand : ModCommand
{
    public override CommandType Type => CommandType.Chat;
    public override string Usage => $"/{Command}";
    public override string Description => ClientRuntime.Text("Commands.Dt.Description", "Open or close saved damage results.");
    protected abstract int HistoryIndex { get; }

    public override void Action(CommandCaller caller, string input, string[] args)
        => DtCommand.OpenHistoryIndex(caller, HistoryIndex);
}
