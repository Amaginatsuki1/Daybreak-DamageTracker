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
    bool otherBossResolved = false,
    bool allowSiblingBoundary = true)
    => EncounterPolicy.ResolveUnresolvedBoss(new UnresolvedBossFacts(
        wipe,
        continueAfterWipe,
        noActivePlayers,
        livingEngaged,
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

internal sealed record TestBoss(string Name, HashSet<int> Types);
