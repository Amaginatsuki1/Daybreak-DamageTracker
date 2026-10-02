namespace DaybreakDamageTracker.Common.Config;

[Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
public enum ResultRecipients
{
    AllActivePlayers,
    Contributors
}

internal static class ResultRecipientPolicy
{
    public static ResultRecipients FailClosed(ResultRecipients value)
        => Enum.IsDefined(value) ? value : ResultRecipients.Contributors;

    public static bool CanReceive(
        ResultRecipients historicalPolicy,
        ResultRecipients currentPolicy,
        bool contributed)
    {
        historicalPolicy = FailClosed(historicalPolicy);
        currentPolicy = FailClosed(currentPolicy);
        return contributed ||
               (historicalPolicy == ResultRecipients.AllActivePlayers &&
                currentPolicy == ResultRecipients.AllActivePlayers);
    }
}
