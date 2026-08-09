namespace DaybreakDamageTracker.Common.Systems;

internal readonly record struct WeightedDamageClaim<T>(T Id, int Weight) where T : notnull;
internal readonly record struct WeightedDamageShare<T>(T Id, int Damage) where T : notnull;
internal readonly record struct DotLeaseWindow(long StartTick, long EndTick);

internal static class DotDamageAttributionPolicy
{
    public static int GetUnattributedRegenWeight(
        int finalLifeRegen,
        IEnumerable<int> supportedWeights)
    {
        long negativeRegen = finalLifeRegen < 0 ? -(long)finalLifeRegen : 0;
        long supportedWeight = supportedWeights
            .Where(weight => weight > 0)
            .Sum(weight => (long)weight);
        return (int)Math.Min(int.MaxValue, Math.Max(0, negativeRegen - supportedWeight));
    }

    public static DotLeaseWindow? GetExtensionWindow(
        long now,
        int previousTime,
        int currentTime)
    {
        previousTime = Math.Max(0, previousTime);
        currentTime = Math.Max(0, currentTime);
        if (currentTime <= previousTime)
            return null;

        long start = previousTime > 0 ? now + previousTime : now;
        long end = now + currentTime;
        return end > start ? new DotLeaseWindow(start, end) : null;
    }

    public static bool ShouldResetLeaseSegments(int previousTime)
        // A zero previous duration means the buff is not currently present. Any
        // predicted-but-unexpired segment therefore came from an early removal,
        // slot eviction, cleanse, or transformation and must not retain ownership.
        => previousTime <= 0;

    public static List<WeightedDamageShare<T>> Allocate<T>(
        int actualDamage,
        IReadOnlyList<WeightedDamageClaim<T>> claims)
        where T : notnull
        => new WeightedDamageAccumulator<T>().Allocate(actualDamage, claims);
}

internal sealed class WeightedDamageAccumulator<T> where T : notnull
{
    private const int MaximumExactTail = 4096;
    private readonly Dictionary<T, decimal> _credits = [];
    private readonly Dictionary<T, int> _activeWeights = [];

    public List<WeightedDamageShare<T>> Allocate(
        int actualDamage,
        IReadOnlyList<WeightedDamageClaim<T>> claims)
    {
        if (actualDamage <= 0 || claims.Count == 0)
            return [];

        List<WeightedDamageClaim<T>> active = claims
            .Where(claim => claim.Weight > 0)
            .GroupBy(claim => claim.Id)
            .Select(group => new WeightedDamageClaim<T>(
                group.Key,
                (int)Math.Min(int.MaxValue, group.Sum(claim => (long)claim.Weight))))
            .ToList();
        long totalWeight = active.Sum(claim => (long)claim.Weight);
        if (totalWeight <= 0)
            return [];

        if (WeightsChanged(active))
        {
            _credits.Clear();
            _activeWeights.Clear();
            foreach (WeightedDamageClaim<T> claim in active)
            {
                _credits[claim.Id] = 0m;
                _activeWeights[claim.Id] = claim.Weight;
            }
        }

        Dictionary<T, int> allocated = active.ToDictionary(claim => claim.Id, _ => 0);
        int remainingDamage = actualDamage;

        // Integer weights produce a periodic fair schedule. A complete cycle of
        // sum(weight / gcd) points assigns exactly weight / gcd points to every
        // claim and returns all fractional credits to their starting values. Skip
        // those cycles in O(claims), then run the original point allocator only for
        // the short tail so ordinary and vanilla-supported cases stay bit-for-bit
        // compatible with the previous allocator.
        long weightGcd = active
            .Select(claim => (long)claim.Weight)
            .Aggregate(GreatestCommonDivisor);
        long normalizedTotalWeight = totalWeight / weightGcd;
        if (normalizedTotalWeight > 0 && normalizedTotalWeight <= remainingDamage)
        {
            long cycles = remainingDamage / normalizedTotalWeight;
            foreach (WeightedDamageClaim<T> claim in active)
            {
                long cycleDamage = cycles * (claim.Weight / weightGcd);
                allocated[claim.Id] = (int)Math.Min(int.MaxValue, cycleDamage);
            }
            remainingDamage -= (int)(cycles * normalizedTotalWeight);
        }

        if (remainingDamage <= MaximumExactTail)
        {
            for (int point = 0; point < remainingDamage; point++)
            {
                foreach (WeightedDamageClaim<T> claim in active)
                    _credits[claim.Id] += claim.Weight / (decimal)totalWeight;

                WeightedDamageClaim<T> winner = active
                    .Select((claim, index) => (claim, index))
                    .OrderByDescending(entry => _credits[entry.claim.Id])
                    .ThenByDescending(entry => entry.claim.Weight)
                    .ThenBy(entry => entry.index)
                    .First().claim;
                _credits[winner.Id] -= 1m;
                allocated[winner.Id]++;
            }
        }
        else
        {
            AllocateBoundedTail(remainingDamage, active, totalWeight, allocated);
        }

        return active
            .Where(claim => allocated[claim.Id] > 0)
            .Select(claim => new WeightedDamageShare<T>(claim.Id, allocated[claim.Id]))
            .ToList();
    }

    private void AllocateBoundedTail(
        int remainingDamage,
        IReadOnlyList<WeightedDamageClaim<T>> active,
        long totalWeight,
        Dictionary<T, int> allocated)
    {
        // Pathological modded weights can make even the normalized schedule longer
        // than the observed damage. Preserve exact totals and weighted fairness with
        // one bounded apportionment instead of allowing a hostile life-regen hook to
        // monopolize the server thread for millions of iterations.
        List<AllocationCandidate<T>> candidates = active
            .Select((claim, index) =>
            {
                decimal exact = _credits[claim.Id] +
                    remainingDamage * (claim.Weight / (decimal)totalWeight);
                int damage = exact > 0m
                    ? (int)Math.Min(remainingDamage, decimal.Floor(exact))
                    : 0;
                return new AllocationCandidate<T>(claim, index, exact, damage);
            })
            .ToList();

        long assigned = candidates.Sum(candidate => (long)candidate.Damage);
        if (assigned < remainingDamage)
        {
            foreach (AllocationCandidate<T> candidate in candidates
                         .OrderByDescending(candidate => candidate.Exact - candidate.Damage)
                         .ThenByDescending(candidate => candidate.Claim.Weight)
                         .ThenBy(candidate => candidate.Index))
            {
                if (assigned >= remainingDamage)
                    break;
                candidate.Damage++;
                assigned++;
            }
        }
        else if (assigned > remainingDamage)
        {
            foreach (AllocationCandidate<T> candidate in candidates
                         .Where(candidate => candidate.Damage > 0)
                         .OrderBy(candidate => candidate.Exact - candidate.Damage)
                         .ThenBy(candidate => candidate.Claim.Weight)
                         .ThenByDescending(candidate => candidate.Index))
            {
                if (assigned <= remainingDamage)
                    break;
                candidate.Damage--;
                assigned--;
            }
        }

        foreach (AllocationCandidate<T> candidate in candidates)
        {
            _credits[candidate.Claim.Id] = candidate.Exact - candidate.Damage;
            allocated[candidate.Claim.Id] += candidate.Damage;
        }
    }

    private static long GreatestCommonDivisor(long left, long right)
    {
        while (right != 0)
        {
            long remainder = left % right;
            left = right;
            right = remainder;
        }
        return Math.Abs(left);
    }

    private bool WeightsChanged(IReadOnlyCollection<WeightedDamageClaim<T>> active)
    {
        if (_activeWeights.Count != active.Count)
            return true;
        foreach (WeightedDamageClaim<T> claim in active)
        {
            if (!_activeWeights.TryGetValue(claim.Id, out int weight) || weight != claim.Weight)
                return true;
        }
        return false;
    }

    private sealed class AllocationCandidate<TClaim>(
        WeightedDamageClaim<TClaim> claim,
        int index,
        decimal exact,
        int damage)
        where TClaim : notnull
    {
        public WeightedDamageClaim<TClaim> Claim { get; } = claim;
        public int Index { get; } = index;
        public decimal Exact { get; } = exact;
        public int Damage { get; set; } = damage;
    }
}
