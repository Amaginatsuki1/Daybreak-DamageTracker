namespace DaybreakDamageTracker.Client;

public enum ResultPanelSection
{
    TeamTotal,
    BossBodyDamage,
    UnattributedDamage,
    Duration,
    Ranking,
    PersonalBreakdown
}

internal static class ResultPanelSectionOrderPolicy
{
    private static readonly ResultPanelSection[] DefaultValues =
    [
        ResultPanelSection.TeamTotal,
        ResultPanelSection.BossBodyDamage,
        ResultPanelSection.UnattributedDamage,
        ResultPanelSection.Duration,
        ResultPanelSection.Ranking,
        ResultPanelSection.PersonalBreakdown
    ];

    public static List<ResultPanelSection> CreateDefault() => [.. DefaultValues];

    public static List<ResultPanelSection> Sanitize(IEnumerable<ResultPanelSection>? values)
    {
        HashSet<ResultPanelSection> seen = [];
        List<ResultPanelSection> result = [];
        if (values is not null)
        {
            foreach (ResultPanelSection value in values)
            {
                if (Enum.IsDefined(value) && seen.Add(value))
                    result.Add(value);
            }
        }

        foreach (ResultPanelSection value in DefaultValues)
        {
            if (seen.Add(value))
                result.Add(value);
        }
        return result;
    }
}
