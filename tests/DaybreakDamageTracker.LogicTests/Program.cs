using DaybreakDamageTracker.Common.Data;
using DaybreakDamageTracker.Common.Systems;

List<(string Name, Action Test)> tests =
[
    ("overlapping foreign boss stays in simultaneous encounter", () =>
        AssertFalse(EncounterPolicy.ShouldStartNewEncounter(true, true, false))),
    ("related sequential wave stays in encounter", () =>
        AssertFalse(EncounterPolicy.ShouldStartNewEncounter(true, false, true))),
    ("unrelated boss after old set disappeared starts new encounter", () =>
        AssertTrue(EncounterPolicy.ShouldStartNewEncounter(true, false, false))),
    ("no active boss cannot start a boundary", () =>
        AssertFalse(EncounterPolicy.ShouldStartNewEncounter(false, false, false))),
    ("a resolved Boss key stays blocked while any matching NPC remains", () =>
    {
        HashSet<string> held = new(StringComparer.Ordinal) { "Boss" };
        EncounterPolicy.ReleaseAbsentResolvedBossKeys(held, ["Boss", "Sibling"]);
        AssertTrue(held.Contains("Boss"));
    }),
    ("a resolved Boss key rearms only after a fully absent scan", () =>
    {
        HashSet<string> held = new(StringComparer.Ordinal) { "Boss" };
        EncounterPolicy.ReleaseAbsentResolvedBossKeys(held, ["Sibling"]);
        AssertFalse(held.Contains("Boss"));
        EncounterPolicy.ReleaseAbsentResolvedBossKeys(held, ["Boss", "Sibling"]);
        AssertFalse(held.Contains("Boss"));
    }),
    ("victory wins a same-tick party wipe race", () =>
        AssertEqual(EncounterOutcome.Victory, Resolve(victory: true, wipe: true))),
    ("ordinary party wipe becomes defeat", () =>
        AssertEqual(EncounterOutcome.Defeat, Resolve(wipe: true))),
    ("configured gauntlet waits across a party wipe", () =>
        AssertEqual(null, Resolve(wipe: true, continueAfterWipe: true))),
    ("foreign encounter closes a wiped gauntlet as defeat", () =>
        AssertEqual(EncounterOutcome.Defeat, Resolve(wipe: true, continueAfterWipe: true, foreign: true))),
    ("disconnect without wipe becomes escape", () =>
        AssertEqual(EncounterOutcome.Escaped, Resolve(noActivePlayers: true))),
    ("disengagement before NPC loss becomes escape", () =>
        AssertEqual(EncounterOutcome.Escaped, Resolve(livingEngaged: false))),
    ("living player near an NPC-free transition keeps waiting", () =>
        AssertEqual(null, Resolve(livingEngaged: true))),
    ("fallback resolves an otherwise ambiguous transition", () =>
        AssertEqual(EncounterOutcome.Escaped, Resolve(livingEngaged: true, fallback: true))),
    ("resolved sibling closes a disappeared boss as escaped", () =>
        AssertEqual(EncounterOutcome.Escaped, ResolveBoss(otherBossResolved: true))),
    ("scripted NPC-free boss can reject a sibling close boundary", () =>
        AssertEqual(null, ResolveBoss(otherBossResolved: true, allowSiblingBoundary: false))),
    ("ordinary NPC-free phase remains pending without a sibling boundary", () =>
        AssertEqual(null, ResolveBoss())),
    ("ordinary non-kill disappearance resolves immediately", () =>
        AssertEqual(EncounterOutcome.Escaped, ResolveBoss(nonKillDisappearance: true))),
    ("scripted NPC-free phase can reject a disappearance boundary", () =>
        AssertEqual(null, ResolveBoss(nonKillDisappearance: true, allowDisappearanceBoundary: false))),
    ("wipe-continuing boss remains pending after a party wipe", () =>
        AssertEqual(null, ResolveBoss(wipe: true, continueAfterWipe: true))),
    ("non-continuing boss resolves as defeat after a party wipe", () =>
        AssertEqual(EncounterOutcome.Defeat, ResolveBoss(wipe: true))),
    ("matching per-boss outcomes retain their shared result", () =>
        AssertEqual(EncounterOutcome.Victory, EncounterPolicy.AggregateOutcomes(
            [EncounterOutcome.Victory, EncounterOutcome.Victory]))),
    ("different per-boss outcomes aggregate as mixed", () =>
        AssertEqual(EncounterOutcome.Mixed, EncounterPolicy.AggregateOutcomes(
            [EncounterOutcome.Victory, EncounterOutcome.Escaped]))),
    ("grouped wave cannot use its individual downed flag", () =>
        AssertFalse(EncounterPolicy.CanUseEntryDownedSignal("WaveA", "Gauntlet"))),
    ("ordinary boss can use its downed flag", () =>
        AssertTrue(EncounterPolicy.CanUseEntryDownedSignal("Boss", "Boss"))),
    ("grouped wave cannot auto-finish on an intermediate body death", () =>
        AssertFalse(EncounterPolicy.CanUseAutomaticBodyFinal("WaveA", "Gauntlet", true, false, false, false))),
    ("wipe-continuing encounter requires an explicit final signal", () =>
        AssertFalse(EncounterPolicy.CanUseAutomaticBodyFinal("Boss", "Boss", true, true, false, false))),
    ("repeat clear of an ordinary boss can use body final", () =>
        AssertTrue(EncounterPolicy.CanUseAutomaticBodyFinal("Boss", "Boss", true, false, true, true))),
    ("explicit final kill wins while a keep-alive controller remains", () =>
        AssertTrue(EncounterPolicy.ShouldResolveVictory(true, true, false, false))),
    ("downed transition wins while a keep-alive add remains", () =>
        AssertTrue(EncounterPolicy.ShouldResolveVictory(true, false, false, true))),
    ("automatic body final still requires the last participant window", () =>
    {
        AssertFalse(EncounterPolicy.ShouldResolveVictory(true, false, true, false));
        AssertTrue(EncounterPolicy.ShouldResolveVictory(false, false, true, false));
    }),
    ("same inactive name is a reconnect candidate", () =>
        AssertTrue(PlayerIdentityPolicy.CanMergeReconnect("Player", false, "Player"))),
    ("overlapping same name is not merged", () =>
        AssertFalse(PlayerIdentityPolicy.CanMergeReconnect("Player", true, "Player"))),
    ("name comparison is exact", () =>
        AssertFalse(PlayerIdentityPolicy.CanMergeReconnect("Player", false, "player"))),
    ("one inactive same-name ledger is selected", () =>
        AssertEqual(0, PlayerIdentityPolicy.FindReconnectCandidateIndex(
            [new LogicalPlayerCandidate("Player", false)], "Player"))),
    ("an ambiguous pair of inactive same-name ledgers is not merged", () =>
        AssertEqual(-1, PlayerIdentityPolicy.FindReconnectCandidateIndex(
            [new LogicalPlayerCandidate("Player", false), new LogicalPlayerCandidate("Player", false)], "Player"))),
    ("an active same-name ledger is not selected", () =>
        AssertEqual(-1, PlayerIdentityPolicy.FindReconnectCandidateIndex(
            [new LogicalPlayerCandidate("Player", true)], "Player"))),
    ("an active duplicate makes an inactive same-name ledger ambiguous", () =>
        AssertEqual(-1, PlayerIdentityPolicy.FindReconnectCandidateIndex(
            [new LogicalPlayerCandidate("Player", false), new LogicalPlayerCandidate("Player", true)], "Player"))),
    ("a direct target is assigned to exactly one boss", () =>
    {
        TestBoss first = new("first", [10]);
        TestBoss second = new("second", [20]);
        AssertEqual(first, BossAttributionPolicy.FindUnique([first, second], 10, -1, boss => boss.Types));
    }),
    ("a realLife root assigns a segmented target", () =>
    {
        TestBoss boss = new("segmented", [10]);
        AssertEqual(boss, BossAttributionPolicy.FindUnique([boss], 11, 10, value => value.Types));
    }),
    ("an ambiguous target is never double-assigned", () =>
    {
        TestBoss first = new("first", [10]);
        TestBoss second = new("second", [10]);
        AssertEqual<TestBoss?>(null, BossAttributionPolicy.FindUnique([first, second], 10, -1, boss => boss.Types));
    }),
    ("explicit add damage is associated but not boss body damage", () =>
        AssertFalse(BossAttributionPolicy.IsBody(new HashSet<int> { 10 }, new HashSet<int> { 20 }, 20, -1))),
    ("body damage follows a segmented root", () =>
        AssertTrue(BossAttributionPolicy.IsBody(new HashSet<int> { 10 }, new HashSet<int>(), 11, 10))),
    ("DoT allocation preserves the exact observed damage", () =>
    {
        List<WeightedDamageShare<int>> shares = DotDamageAttributionPolicy.Allocate(
            17,
            [new WeightedDamageClaim<int>(1, 12), new WeightedDamageClaim<int>(2, 48)]);
        AssertEqual(17, shares.Sum(share => share.Damage));
        AssertEqual(3, shares.Single(share => share.Id == 1).Damage);
        AssertEqual(14, shares.Single(share => share.Id == 2).Damage);
    }),
    ("DoT allocation ignores invalid claims", () =>
        AssertEqual(0, DotDamageAttributionPolicy.Allocate(
            5,
            [new WeightedDamageClaim<int>(1, 0)]).Count)),
    ("unknown negative regeneration remains unattributed", () =>
        AssertEqual(40, DotDamageAttributionPolicy.GetUnattributedRegenWeight(-100, [12, 48]))),
    ("known effects are scaled without inventing an unattributed remainder", () =>
        AssertEqual(0, DotDamageAttributionPolicy.GetUnattributedRegenWeight(-40, [12, 48]))),
    ("fractional DoT shares remain fair across one-point ticks", () =>
    {
        WeightedDamageAccumulator<int> accumulator = new();
        Dictionary<int, int> totals = [];
        for (int tick = 0; tick < 5; tick++)
        {
            foreach (WeightedDamageShare<int> share in accumulator.Allocate(
                         1,
                         [new WeightedDamageClaim<int>(1, 12), new WeightedDamageClaim<int>(2, 48)]))
            {
                totals.TryGetValue(share.Id, out int current);
                totals[share.Id] = current + share.Damage;
            }
        }
        AssertEqual(1, totals[1]);
        AssertEqual(4, totals[2]);
    }),
    ("fresh DoT ownership begins immediately", () =>
        AssertEqual(
            new DotLeaseWindow(100, 340),
            DotDamageAttributionPolicy.GetExtensionWindow(100, 0, 240))),
    ("DoT refresh owns only the extended tail", () =>
        AssertEqual(
            new DotLeaseWindow(280, 340),
            DotDamageAttributionPolicy.GetExtensionWindow(100, 180, 240))),
    ("shorter DoT reapplication cannot steal ownership", () =>
        AssertEqual<DotLeaseWindow?>(
            null,
            DotDamageAttributionPolicy.GetExtensionWindow(100, 240, 180))),
    ("a fresh application clears leases left by an early buff removal", () =>
        AssertTrue(DotDamageAttributionPolicy.ShouldResetLeaseSegments(0))),
    ("an active buff refresh preserves its existing lease segments", () =>
        AssertFalse(DotDamageAttributionPolicy.ShouldResetLeaseSegments(120))),
    ("large modded DoT allocation stays exact without per-point iteration", () =>
    {
        WeightedDamageAccumulator<int> accumulator = new();
        List<WeightedDamageShare<int>> shares = accumulator.Allocate(
            1_000_000,
            [new WeightedDamageClaim<int>(1, 12), new WeightedDamageClaim<int>(2, 48)]);
        AssertEqual(1_000_000, shares.Sum(share => share.Damage));
        AssertEqual(200_000, shares.Single(share => share.Id == 1).Damage);
        AssertEqual(800_000, shares.Single(share => share.Id == 2).Damage);
    }),
    ("batched DoT allocation matches the original point allocator", () =>
    {
        WeightedDamageAccumulator<int> batched = new();
        ReferencePointDamageAccumulator<int> reference = new();
        (int Damage, WeightedDamageClaim<int>[] Claims)[] steps =
        [
            (1, [new(1, 12), new(2, 48)]),
            (2, [new(1, 12), new(2, 48)]),
            (17, [new(1, 12), new(2, 48)]),
            (31, [new(1, 7), new(2, 11), new(3, 19)]),
            (9, [new(1, 7), new(2, 11), new(3, 19)]),
            (23, [new(3, 19), new(2, 11), new(1, 7)]),
            (5, [new(1, 6), new(1, 4), new(2, 15)]),
            (1, [new(1, 6), new(1, 4), new(2, 15)])
        ];

        foreach ((int damage, WeightedDamageClaim<int>[] claims) in steps)
        {
            AssertSharesEqual(
                reference.Allocate(damage, claims),
                batched.Allocate(damage, claims));
        }

        Random random = new(8675309);
        for (int step = 0; step < 500; step++)
        {
            int claimCount = random.Next(1, 6);
            WeightedDamageClaim<int>[] claims = Enumerable.Range(0, claimCount)
                .Select(index => new WeightedDamageClaim<int>(
                    random.Next(0, Math.Max(1, claimCount - 1)),
                    random.Next(1, 250)))
                .ToArray();
            int damage = random.Next(1, 80);
            AssertSharesEqual(
                reference.Allocate(damage, claims),
                batched.Allocate(damage, claims));
        }
    }),
    ("pathological coprime DoT weights use a bounded exact-total fallback", () =>
    {
        WeightedDamageAccumulator<int> accumulator = new();
        List<WeightedDamageShare<int>> shares = accumulator.Allocate(
            1_000_000,
            [
                new WeightedDamageClaim<int>(1, int.MaxValue),
                new WeightedDamageClaim<int>(2, int.MaxValue - 2)
            ]);
        AssertEqual(1_000_000, shares.Sum(share => share.Damage));
        AssertTrue(shares.All(share => share.Damage > 0));
    }),
    ("private source target is frozen only when exactly one Boss matches", () =>
    {
        AssertEqual(
            new PrivateSourceMatch(PrivateSourceMatchKind.Unique, "BossA"),
            PrivateSourceAttributionPolicy.Resolve(["BossA"]));
        AssertEqual(
            PrivateSourceMatchKind.Ambiguous,
            PrivateSourceAttributionPolicy.Resolve(["BossA", "BossB"]).Kind);
        AssertEqual(
            PrivateSourceMatchKind.Pending,
            PrivateSourceAttributionPolicy.Resolve([]).Kind);
    }),
    ("DoT source leaf survives canonical reconciliation", () =>
    {
        MutableSourceTree tree = new();
        DamageRootKey root = new(DamageRootKind.Buff, 73);
        DamageLeafKey leaf = new(DamageLeafKind.Debuff, 39);
        tree.Add(root, leaf, 24);
        SourceTreeSnapshot snapshot = tree.Reconcile(24);
        AssertEqual(leaf, snapshot.Roots.Single().Leaves.Single().Key);
    }),
    ("suspended private damage moves into the resumed source tree", () =>
    {
        MutableSourceTree current = new();
        MutableSourceTree suspended = new();
        DamageRootKey root = new(DamageRootKind.Item, 1);
        DamageLeafKey leaf = new(DamageLeafKind.Projectile, 2);
        suspended.Add(root, leaf, 37);

        current.MergeFrom(suspended);
        SourceTreeSnapshot snapshot = current.Reconcile(37);

        AssertTrue(suspended.IsEmpty);
        AssertEqual(37L, snapshot.Roots.Single().Damage);
        AssertEqual(root, snapshot.Roots.Single().Key);
    })
];

int failures = 0;
foreach ((string name, Action test) in tests)
{
    try
    {
        test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {name}: {exception.Message}");
    }
}

Console.WriteLine($"{tests.Count - failures}/{tests.Count} logic tests passed.");
return failures == 0 ? 0 : 1;

static EncounterOutcome? Resolve(
    bool victory = false,
    bool wipe = false,
    bool continueAfterWipe = false,
    bool noActivePlayers = false,
    bool livingEngaged = true,
    bool foreign = false,
    bool fallback = false)
    => EncounterPolicy.ResolveInactiveBoundary(new EncounterBoundaryFacts(
        victory,
        wipe,
        continueAfterWipe,
        noActivePlayers,
        livingEngaged,
        foreign,
        fallback));

static EncounterOutcome? ResolveBoss(
    bool wipe = false,
    bool continueAfterWipe = false,
    bool noActivePlayers = false,
    bool livingEngaged = true,
    bool foreign = false,
    bool fallback = false,
    bool nonKillDisappearance = false,
    bool allowDisappearanceBoundary = true,
    bool otherBossResolved = false,
    bool allowSiblingBoundary = true)
    => EncounterPolicy.ResolveUnresolvedBoss(new UnresolvedBossFacts(
        wipe,
        continueAfterWipe,
        noActivePlayers,
        livingEngaged,
        nonKillDisappearance,
        allowDisappearanceBoundary,
        foreign,
        fallback,
        otherBossResolved,
        allowSiblingBoundary));

static void AssertTrue(bool value)
{
    if (!value)
        throw new InvalidOperationException("Expected true.");
}

static void AssertFalse(bool value) => AssertTrue(!value);

static void AssertEqual<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected {expected}; got {actual}.");
}

static void AssertSharesEqual<T>(
    IReadOnlyCollection<WeightedDamageShare<T>> expected,
    IReadOnlyCollection<WeightedDamageShare<T>> actual)
    where T : notnull
{
    Dictionary<T, int> expectedById = expected.ToDictionary(share => share.Id, share => share.Damage);
    Dictionary<T, int> actualById = actual.ToDictionary(share => share.Id, share => share.Damage);
    AssertEqual(expectedById.Count, actualById.Count);
    foreach ((T id, int damage) in expectedById)
    {
        if (!actualById.TryGetValue(id, out int actualDamage))
            throw new InvalidOperationException($"Missing share for {id}.");
        AssertEqual(damage, actualDamage);
    }
}

internal sealed record TestBoss(string Name, HashSet<int> Types);

internal sealed class ReferencePointDamageAccumulator<T> where T : notnull
{
    private readonly Dictionary<T, decimal> _credits = [];
    private readonly Dictionary<T, int> _activeWeights = [];

    public List<WeightedDamageShare<T>> Allocate(
        int actualDamage,
        IReadOnlyList<WeightedDamageClaim<T>> claims)
    {
        List<WeightedDamageClaim<T>> active = claims
            .Where(claim => claim.Weight > 0)
            .GroupBy(claim => claim.Id)
            .Select(group => new WeightedDamageClaim<T>(group.Key, group.Sum(claim => claim.Weight)))
            .ToList();
        long totalWeight = active.Sum(claim => (long)claim.Weight);
        if (actualDamage <= 0 || totalWeight <= 0)
            return [];

        if (_activeWeights.Count != active.Count || active.Any(claim =>
                !_activeWeights.TryGetValue(claim.Id, out int weight) || weight != claim.Weight))
        {
            _credits.Clear();
            _activeWeights.Clear();
            foreach (WeightedDamageClaim<T> claim in active)
            {
                _credits[claim.Id] = 0m;
                _activeWeights[claim.Id] = claim.Weight;
            }
        }

        Dictionary<T, int> assigned = [];
        for (int point = 0; point < actualDamage; point++)
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
            assigned.TryGetValue(winner.Id, out int current);
            assigned[winner.Id] = current + 1;
        }

        return active
            .Where(claim => assigned.ContainsKey(claim.Id))
            .Select(claim => new WeightedDamageShare<T>(claim.Id, assigned[claim.Id]))
            .ToList();
    }
}
