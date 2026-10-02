using DaybreakDamageTracker.Common.Data;

namespace DaybreakDamageTracker.Common.Systems;

internal readonly record struct FinalBlowAttribution(string PlayerName, FinalBlowKind Kind);
internal readonly record struct FinalBlowOwner(EncounterPlayerKey Key, string PlayerName);

internal static class FinalBlowPolicy
{
    public static bool IsEligibleTerminal(
        bool isExcludedBody,
        bool isLifecycleController,
        bool isExplicitFinal)
        => !isExcludedBody && (isLifecycleController || isExplicitFinal);

    // A lethal damage-over-time tick is one atomic life change split between several
    // effects, so no single point of damage "carried" the kill. Combine all effects
    // owned by the same exact connection identity, and treat every unowned share as
    // one competing bucket. Only one strictly dominant owned bucket is defensible.
    // Display names are deliberately not identities: two different connections may
    // legitimately use the same name.
    public static FinalBlowOwner? FindDominantDotOwner(
        IEnumerable<(FinalBlowOwner? Owner, int Damage)> contributions)
    {
        Dictionary<EncounterPlayerKey, (string PlayerName, long Damage)> owned = [];
        long unownedDamage = 0;
        foreach ((FinalBlowOwner? owner, int damage) in contributions)
        {
            if (damage <= 0)
                continue;

            if (!owner.HasValue || string.IsNullOrEmpty(owner.Value.PlayerName))
            {
                unownedDamage = SaturatingAdd(unownedDamage, damage);
                continue;
            }

            FinalBlowOwner value = owner.Value;
            owned.TryGetValue(value.Key, out (string PlayerName, long Damage) existing);
            string playerName = string.IsNullOrEmpty(existing.PlayerName)
                ? value.PlayerName
                : existing.PlayerName;
            owned[value.Key] = (playerName, SaturatingAdd(existing.Damage, damage));
        }

        if (owned.Count == 0)
            return null;

        long maximum = Math.Max(unownedDamage, owned.Values.Max(value => value.Damage));
        if (unownedDamage == maximum)
            return null;

        KeyValuePair<EncounterPlayerKey, (string PlayerName, long Damage)>[] winners = owned
            .Where(pair => pair.Value.Damage == maximum)
            .Take(2)
            .ToArray();
        return winners.Length == 1
            ? new FinalBlowOwner(winners[0].Key, winners[0].Value.PlayerName)
            : null;
    }

    private static long SaturatingAdd(long current, int addition)
        => current > long.MaxValue - addition ? long.MaxValue : current + addition;
}

/// <summary>
/// Holds exact-instance lethal-hit candidates until the boss lifecycle provides matching
/// terminal evidence. A later-tick candidate replaces stale phase evidence; multiple
/// candidates in one tick remain ambiguous and are deliberately not guessed.
/// </summary>
internal sealed class FinalBlowCandidateTracker<T> where T : notnull
{
    private readonly Dictionary<T, Candidate> _candidates = [];

    public IReadOnlySet<T> CandidateInstances => _candidates.Keys.ToHashSet();

    public void Stage(T instance, FinalBlowAttribution attribution, long tick)
    {
        if (string.IsNullOrEmpty(attribution.PlayerName) || attribution.Kind == FinalBlowKind.None)
            return;

        if (_candidates.Count > 0 && _candidates.Values.Any(candidate => candidate.Tick < tick))
            _candidates.Clear();
        _candidates[instance] = new Candidate(attribution, tick);
    }

    public void CancelForLiveControllers(IReadOnlySet<T> liveControllerInstances)
    {
        if (liveControllerInstances.Count > 0)
            _candidates.Clear();
    }

    public FinalBlowAttribution? Confirm(IReadOnlySet<T> terminalEvidence)
    {
        // A multi-controller terminal boundary has no defensible single last hitter
        // unless every terminal instance can be ordered by the game itself. Do not let
        // one staged candidate stand in for sibling roots which also ended the fight.
        if (terminalEvidence.Count != 1)
        {
            _candidates.Clear();
            return null;
        }

        T terminalInstance = terminalEvidence.First();
        bool matched = _candidates.TryGetValue(terminalInstance, out Candidate candidate);
        _candidates.Clear();
        return matched ? candidate.Attribution : null;
    }

    public void Reset() => _candidates.Clear();

    private readonly record struct Candidate(FinalBlowAttribution Attribution, long Tick);
}
