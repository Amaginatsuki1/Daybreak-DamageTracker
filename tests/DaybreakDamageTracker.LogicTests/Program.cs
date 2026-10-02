using DaybreakDamageTracker.Common.Data;
using DaybreakDamageTracker.Common.Bosses;
using DaybreakDamageTracker.Common.Networking;
using DaybreakDamageTracker.Common.Systems;
using DaybreakDamageTracker.Client;
using DaybreakDamageTracker.Common.Config;

List<(string Name, Action Test)> tests =
[
    ("unrelated and ambiguous targets cannot start or contaminate a Boss ledger", () =>
    {
        AssertFalse(EncounterPolicy.CanRecordDamage(true, 100, false));
        AssertFalse(EncounterPolicy.CanRecordDamage(false, 100, true));
        AssertFalse(EncounterPolicy.CanRecordDamage(true, 0, true));
        AssertTrue(EncounterPolicy.CanRecordDamage(true, 100, true));
    }),
    ("a later same-tick NPC spawn invalidates the damage-path Boss scan", () =>
    {
        AssertFalse(EncounterPolicy.ShouldReconcileDamagePath(50, 50, 10, 10));
        AssertTrue(EncounterPolicy.ShouldReconcileDamagePath(50, 50, 10, 11));
        AssertTrue(EncounterPolicy.ShouldReconcileDamagePath(50, 51, 10, 10));
    }),
    ("simultaneous Boss groups evaluate party wipes from their own participants", () =>
    {
        HashSet<int> defeatedGroup = [1];
        HashSet<int> livingGroup = [2];
        AssertTrue(EncounterPolicy.AllCurrentParticipantsDead(
            defeatedGroup,
            _ => true,
            player => player == 1));
        AssertFalse(EncounterPolicy.AllCurrentParticipantsDead(
            livingGroup,
            _ => true,
            player => player == 1));
        AssertFalse(EncounterPolicy.AllCurrentParticipantsDead(
            defeatedGroup.Concat(livingGroup),
            _ => true,
            player => player == 1));
    }),
    ("disconnect and wipe continuation remain scoped to one encounter group", () =>
    {
        HashSet<int> disconnectedGroup = [1];
        AssertTrue(EncounterPolicy.HasNoCurrentParticipants(
            disconnectedGroup,
            player => player == 2));
        AssertTrue(EncounterPolicy.ShouldRecoverPartyWipe(true, true, true));
        AssertFalse(EncounterPolicy.ShouldRecoverPartyWipe(true, false, true));
        AssertFalse(EncounterPolicy.ShouldRecoverPartyWipe(true, true, false));
    }),
    ("a distant target from another simultaneous fight does not join this Boss group", () =>
    {
        AssertFalse(EncounterPolicy.CanAssociateNpcTargetWithGroup(false, false));
        AssertTrue(EncounterPolicy.CanAssociateNpcTargetWithGroup(false, true));
        AssertTrue(EncounterPolicy.CanAssociateNpcTargetWithGroup(true, false));
    }),
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
    ("a key published after an absent scan is immediately released", () =>
    {
        HashSet<string> held = new(StringComparer.Ordinal);
        EncounterPolicy.ReleaseAbsentResolvedBossKeys(held, ["Sibling"]);
        held.Add("Boss");
        EncounterPolicy.ReleaseAbsentResolvedBossKeys(held, ["Sibling"]);
        AssertFalse(held.Contains("Boss"));
    }),
    ("a key published with visible residue remains held", () =>
    {
        HashSet<string> held = new(StringComparer.Ordinal);
        EncounterPolicy.ReleaseAbsentResolvedBossKeys(held, ["Boss"]);
        held.Add("Boss");
        EncounterPolicy.ReleaseAbsentResolvedBossKeys(held, ["Boss"]);
        AssertTrue(held.Contains("Boss"));
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
    ("an escaped sibling resolves while another Boss remains active", () =>
        AssertEqual(
            EncounterOutcome.Escaped,
            ResolveInactiveSibling(nonKillDisappearance: true))),
    ("an inactive sibling waits for the controller transition grace window", () =>
        AssertEqual(
            null,
            ResolveInactiveSibling(
                inactiveGraceElapsed: false,
                nonKillDisappearance: true))),
    ("an inactive sibling becomes defeat on a same-tick party wipe", () =>
        AssertEqual(
            EncounterOutcome.Defeat,
            ResolveInactiveSibling(wipe: true, nonKillDisappearance: true))),
    ("an automatic final candidate blocks a sibling escape boundary", () =>
        AssertEqual(
            null,
            ResolveInactiveSibling(controllerKillCandidate: true, nonKillDisappearance: true))),
    ("an NPC-free cinematic sibling remains pending", () =>
        AssertEqual(
            null,
            ResolveInactiveSibling(nonKillDisappearance: true, allowDisappearanceBoundary: false))),
    ("another Boss losing engagement cannot close an NPC-free cinematic sibling", () =>
        AssertEqual(
            null,
            ResolveInactiveSibling(
                livingEngaged: false,
                nonKillDisappearance: true,
                allowDisappearanceBoundary: false))),
    ("session-wide disengagement alone cannot close an inactive sibling", () =>
        AssertEqual(
            null,
            ResolveInactiveSibling(livingEngaged: false))),
    ("an armed encounter never publishes an inactive sibling", () =>
        AssertEqual(
            null,
            ResolveInactiveSibling(encounterStarted: false, nonKillDisappearance: true))),
    ("an active group never resolves through the sibling boundary", () =>
        AssertEqual(
            null,
            ResolveInactiveSibling(groupActive: true, nonKillDisappearance: true))),
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
    ("ordinary grouped wave cannot close on disappearance", () =>
        AssertFalse(EncounterPolicy.CanUseDisappearanceBoundary("WaveA", "Gauntlet", false))),
    ("a verified final transform may close its group on disappearance", () =>
        AssertTrue(EncounterPolicy.CanUseDisappearanceBoundary("FinalWave", "Gauntlet", true))),
    ("grouped wave cannot auto-finish on an intermediate body death", () =>
        AssertFalse(EncounterPolicy.CanUseAutomaticBodyFinal("WaveA", "Gauntlet", true, false, false, false))),
    ("wipe-continuing encounter requires an explicit final signal", () =>
        AssertFalse(EncounterPolicy.CanUseAutomaticBodyFinal("Boss", "Boss", true, true, false, false))),
    ("repeat clear of an ordinary boss can use body final", () =>
        AssertTrue(EncounterPolicy.CanUseAutomaticBodyFinal("Boss", "Boss", true, false, true, true))),
    ("Boss Rush can use controller final without a downed transition", () =>
        AssertTrue(EncounterPolicy.CanUseAutomaticBodyFinal("Boss", "Boss", true, false, true, false, true))),
    ("Boss Rush force does not bypass a grouped encounter lifecycle", () =>
        AssertFalse(EncounterPolicy.CanUseAutomaticBodyFinal("WaveA", "Gauntlet", true, false, true, false, true))),
    ("Boss Rush force does not bypass a wipe-continuing lifecycle", () =>
        AssertFalse(EncounterPolicy.CanUseAutomaticBodyFinal("Boss", "Boss", true, true, true, false, true))),
    ("explicit final kill wins while a keep-alive controller remains", () =>
        AssertTrue(EncounterPolicy.ShouldResolveVictory(true, true, false, false))),
    ("downed transition wins while a keep-alive add remains", () =>
        AssertTrue(EncounterPolicy.ShouldResolveVictory(true, false, false, true))),
    ("automatic body final still requires the last participant window", () =>
    {
        AssertFalse(EncounterPolicy.ShouldResolveVictory(true, false, true, false));
        AssertTrue(EncounterPolicy.ShouldResolveVictory(false, false, true, false));
    }),
    ("segmented controller detection excludes boss-marked body and tail instances", () =>
    {
        AssertTrue(AutomaticFinalizationPolicy.IsControllerNpc(Set(100), 100, true, 10, -1));
        AssertTrue(AutomaticFinalizationPolicy.IsControllerNpc(Set(100), 100, true, 10, 10));
        AssertFalse(AutomaticFinalizationPolicy.IsControllerNpc(Set(100), 100, true, 11, 10));
    }),
    ("known controller types qualify without the boss flag", () =>
    {
        HashSet<int> controllers = Set(100);
        AssertTrue(AutomaticFinalizationPolicy.IsControllerNpc(controllers, 100, false, 10, -1));
        AssertTrue(AutomaticFinalizationPolicy.IsControllerNpc(controllers, 100, false, 10, 10));
    }),
    ("unknown types without the boss flag never become controllers", () =>
    {
        HashSet<int> controllers = Set(100);
        AssertFalse(AutomaticFinalizationPolicy.IsControllerNpc(controllers, 101, false, 10, -1));
        AssertFalse(AutomaticFinalizationPolicy.IsControllerNpc(controllers, 101, false, 11, 10));
        // An unknown boss-flagged root is still a learnable controller.
        AssertTrue(AutomaticFinalizationPolicy.IsControllerNpc(controllers, 101, true, 10, -1));
    }),
    ("final blow aggregates multiple effects owned by one exact connection", () =>
    {
        FinalBlowOwner alice = new(new EncounterPlayerKey(1, 4), "Alice");
        FinalBlowOwner bob = new(new EncounterPlayerKey(2, 7), "Bob");
        FinalBlowOwner? result = FinalBlowPolicy.FindDominantDotOwner(
            [(alice, 22), (bob, 40), (alice, 23), (null, 5)]);
        AssertTrue(result.HasValue);
        AssertEqual(alice, result.GetValueOrDefault());
    }),
    ("an unowned DoT bucket can defeat an owned final-blow claim", () =>
    {
        FinalBlowOwner bob = new(new EncounterPlayerKey(2, 7), "Bob");
        AssertEqual<FinalBlowOwner?>(null,
            FinalBlowPolicy.FindDominantDotOwner([(null, 90), (bob, 5)]));
        AssertEqual<FinalBlowOwner?>(null,
            FinalBlowPolicy.FindDominantDotOwner([(null, 90)]));
        AssertEqual<FinalBlowOwner?>(null, FinalBlowPolicy.FindDominantDotOwner([]));
    }),
    ("an exact DoT ownership tie remains unattributed", () =>
    {
        FinalBlowOwner first = new(new EncounterPlayerKey(1, 1), "First");
        FinalBlowOwner second = new(new EncounterPlayerKey(2, 1), "Second");
        AssertEqual<FinalBlowOwner?>(null,
            FinalBlowPolicy.FindDominantDotOwner([(first, 30), (second, 30)]));
    }),
    ("same-name DoT owners stay separate exact identities", () =>
    {
        FinalBlowOwner first = new(new EncounterPlayerKey(1, 1), "Same");
        FinalBlowOwner second = new(new EncounterPlayerKey(2, 1), "Same");
        AssertEqual<FinalBlowOwner?>(null,
            FinalBlowPolicy.FindDominantDotOwner([(first, 30), (second, 30)]));
    }),
    ("final blow candidates reject adds and accept lifecycle terminals", () =>
    {
        AssertFalse(FinalBlowPolicy.IsEligibleTerminal(true, true, true));
        AssertFalse(FinalBlowPolicy.IsEligibleTerminal(false, false, false));
        AssertTrue(FinalBlowPolicy.IsEligibleTerminal(false, true, false));
        AssertTrue(FinalBlowPolicy.IsEligibleTerminal(false, false, true));
    }),
    ("final blow commits only one exact terminal candidate", () =>
    {
        FinalBlowCandidateTracker<int> tracker = new();
        tracker.Stage(10, new FinalBlowAttribution("Alice", FinalBlowKind.DirectHit), 100);
        tracker.Stage(11, new FinalBlowAttribution("Bob", FinalBlowKind.DirectHit), 100);

        FinalBlowAttribution? result = tracker.Confirm(Set(11));

        AssertTrue(result.HasValue);
        FinalBlowAttribution attribution = result.GetValueOrDefault();
        AssertEqual("Bob", attribution.PlayerName);
        AssertEqual(FinalBlowKind.DirectHit, attribution.Kind);
    }),
    ("same-tick multi-controller final blows remain ambiguous", () =>
    {
        FinalBlowCandidateTracker<int> tracker = new();
        tracker.Stage(10, new FinalBlowAttribution("Alice", FinalBlowKind.DirectHit), 100);
        tracker.Stage(11, new FinalBlowAttribution("Bob", FinalBlowKind.DamageOverTime), 100);

        AssertEqual<FinalBlowAttribution?>(null, tracker.Confirm(Set(10, 11)));
    }),
    ("one staged hitter cannot stand in for a multi-controller terminal boundary", () =>
    {
        FinalBlowCandidateTracker<int> tracker = new();
        tracker.Stage(10, new FinalBlowAttribution("Alice", FinalBlowKind.DirectHit), 100);

        AssertEqual<FinalBlowAttribution?>(null, tracker.Confirm(Set(10, 11)));
    }),
    ("a living sibling or successor cancels a staged final blow", () =>
    {
        FinalBlowCandidateTracker<int> tracker = new();
        tracker.Stage(10, new FinalBlowAttribution("Alice", FinalBlowKind.DirectHit), 100);
        tracker.CancelForLiveControllers(Set(11));

        AssertEqual<FinalBlowAttribution?>(null, tracker.Confirm(Set(10)));
    }),
    ("a later-tick controller candidate replaces stale phase evidence", () =>
    {
        FinalBlowCandidateTracker<int> tracker = new();
        tracker.Stage(10, new FinalBlowAttribution("Alice", FinalBlowKind.DirectHit), 100);
        tracker.Stage(20, new FinalBlowAttribution("Bob", FinalBlowKind.DamageOverTime), 101);

        FinalBlowAttribution? result = tracker.Confirm(Set(20));
        AssertTrue(result.HasValue);
        FinalBlowAttribution attribution = result.GetValueOrDefault();
        AssertEqual("Bob", attribution.PlayerName);
        AssertEqual(FinalBlowKind.DamageOverTime, attribution.Kind);
    }),
    ("server host may edit the native server config", () =>
        AssertTrue(ServerConfigAuthorizationPolicy.CanEdit(
            isHost: true,
            hasExplicitConnectionGrant: false))),
    ("an explicitly granted dedicated-server connection may edit config", () =>
        AssertTrue(ServerConfigAuthorizationPolicy.CanEdit(
            isHost: false,
            hasExplicitConnectionGrant: true))),
    ("ordinary remote clients cannot edit the server config", () =>
        AssertFalse(ServerConfigAuthorizationPolicy.CanEdit(
            isHost: false,
            hasExplicitConnectionGrant: false))),
    ("projectile provenance accepts only real projectile types", () =>
    {
        AssertFalse(ProjectileProvenancePolicy.IsValidProjectileType(-1, 0, 1000));
        AssertFalse(ProjectileProvenancePolicy.IsValidProjectileType(0, 0, 1000));
        AssertTrue(ProjectileProvenancePolicy.IsValidProjectileType(1, 0, 1000));
        AssertTrue(ProjectileProvenancePolicy.IsValidProjectileType(999, 0, 1000));
        AssertFalse(ProjectileProvenancePolicy.IsValidProjectileType(1000, 0, 1000));
    }),
    ("projectile provenance drain materializes entries before clearing", () =>
    {
        RecipientBatchQueue<int, int> queue = new(256, 32, 8);
        AssertTrue(queue.TryEnqueue(7, 42));

        List<RecipientBatch<int, int>> batches = queue.Drain();

        AssertEqual(1, batches.Count);
        AssertEqual(7, batches[0].Recipient);
        AssertEqual(1, batches[0].Entries.Count);
        AssertEqual(42, batches[0].Entries[0]);
        AssertEqual(0, queue.RecipientCount);
        AssertEqual(0, queue.Drain().Count);
    }),
    ("projectile provenance batches are bounded and recipients are independent", () =>
    {
        RecipientBatchQueue<int, int> queue = new(256, 32, 8);
        for (int i = 0; i < 33; i++)
            AssertTrue(queue.TryEnqueue(1, i));
        AssertTrue(queue.TryEnqueue(2, 900));

        List<RecipientBatch<int, int>> batches = queue.Drain();
        List<RecipientBatch<int, int>> first = batches.Where(batch => batch.Recipient == 1).ToList();
        List<RecipientBatch<int, int>> second = batches.Where(batch => batch.Recipient == 2).ToList();
        AssertEqual(2, first.Count);
        AssertEqual(32, first[0].Entries.Count);
        AssertEqual(1, first[1].Entries.Count);
        AssertEqual(1, second.Count);
        AssertEqual(900, second[0].Entries.Single());
    }),
    ("projectile provenance capacity is per recipient", () =>
    {
        RecipientBatchQueue<int, int> queue = new(2, 2, 8);
        AssertTrue(queue.TryEnqueue(1, 10));
        AssertTrue(queue.TryEnqueue(1, 11));
        AssertFalse(queue.TryEnqueue(1, 12));
        AssertTrue(queue.TryEnqueue(2, 20));

        List<RecipientBatch<int, int>> batches = queue.Drain();
        AssertEqual(2, batches.Count);
        AssertEqual(2, batches.Single(batch => batch.Recipient == 1).Entries.Count);
        AssertEqual(1, batches.Single(batch => batch.Recipient == 2).Entries.Count);
    }),
    ("projectile provenance keeps the global packet budget and rotates recipients", () =>
    {
        RecipientBatchQueue<int, int> queue = new(4, 2, 2);
        for (int recipient = 1; recipient <= 3; recipient++)
            AssertTrue(queue.TryEnqueue(recipient, recipient * 10));

        List<RecipientBatch<int, int>> firstTick = queue.Drain();
        AssertEqual(2, firstTick.Count);
        AssertTrue(firstTick.Any(batch => batch.Recipient == 1));
        AssertTrue(firstTick.Any(batch => batch.Recipient == 2));
        AssertEqual(1, queue.RecipientCount);

        for (int recipient = 1; recipient <= 3; recipient++)
            AssertTrue(queue.TryEnqueue(recipient, recipient * 100));

        List<RecipientBatch<int, int>> secondTick = queue.Drain();
        AssertEqual(2, secondTick.Count);
        AssertTrue(secondTick.Any(batch => batch.Recipient == 3));
    }),
    ("projectile provenance serves the ninth recipient on the next bounded drain", () =>
    {
        RecipientBatchQueue<int, int> queue = new(256, 32, 8);
        for (int recipient = 1; recipient <= 9; recipient++)
            AssertTrue(queue.TryEnqueue(recipient, recipient));

        AssertEqual(8, queue.Drain().Count);
        List<RecipientBatch<int, int>> secondTick = queue.Drain();
        AssertEqual(1, secondTick.Count);
        AssertEqual(9, secondTick[0].Recipient);
    }),
    ("disconnect cleanup removes stale recipients without consuming a batch", () =>
    {
        RecipientBatchQueue<int, int> queue = new(256, 32, 1);
        AssertTrue(queue.TryEnqueue(1, 10));
        AssertTrue(queue.TryEnqueue(2, 20));

        AssertEqual(1, queue.RemoveRecipientsWhere(recipient => recipient == 1));
        List<RecipientBatch<int, int>> batches = queue.Drain();
        AssertEqual(1, batches.Count);
        AssertEqual(2, batches[0].Recipient);
        AssertEqual(20, batches[0].Entries.Single());
    }),
    ("spawn tracking accepts known head types but rejects Body and Tail types", () =>
    {
        HashSet<int> controllers = Set(100);
        AssertTrue(AutomaticFinalizationPolicy.IsKnownControllerType(controllers, 100));
        AssertFalse(AutomaticFinalizationPolicy.IsKnownControllerType(controllers, 101));
        AssertFalse(AutomaticFinalizationPolicy.IsKnownControllerType(controllers, 102));
    }),
    ("a uniquely managed realLife root owns an omitted boss-marked segment", () =>
    {
        TestBoss catalogBoss = new("CatalogBoss", Set(100));
        TestBoss sibling = new("Sibling", Set(200));
        AssertEqual(
            "CatalogBoss",
            BossFallbackPolicy.FindOwnerKeys(
                new[] { catalogBoss, sibling },
                100,
                boss => boss.Name,
                (boss, type) => boss.Types.Contains(type)).Single());
    }),
    ("an ambiguous realLife root is not merged into either boss", () =>
    {
        TestBoss first = new("First", Set(100));
        TestBoss second = new("Second", Set(100));
        AssertEqual(2, BossFallbackPolicy.FindOwnerKeys(
            new[] { first, second },
            100,
            boss => boss.Name,
            (boss, type) => boss.Types.Contains(type)).Count);
    }),
    ("generic segmented fallbacks share the controller key", () =>
    {
        AssertEqual("npc:100", BossFallbackPolicy.FallbackKey(100));
        AssertEqual("npc:200", BossFallbackPolicy.FallbackKey(200));
    }),
    ("an inactive realLife parent keeps a segment from becoming a fallback root", () =>
    {
        AssertTrue(BossFallbackPolicy.HasRealLifeDependency(12, 4, 200));
        AssertFalse(BossFallbackPolicy.CanUseRealLifeController(
            controllerActive: false,
            controllerSpawnSerial: 10,
            dependentSpawnSerial: 11));
    }),
    ("a reused realLife slot cannot own an older lingering segment", () =>
    {
        AssertFalse(BossFallbackPolicy.CanUseRealLifeController(
            controllerActive: true,
            controllerSpawnSerial: 12,
            dependentSpawnSerial: 11));
        AssertTrue(BossFallbackPolicy.CanUseRealLifeController(
            controllerActive: true,
            controllerSpawnSerial: 10,
            dependentSpawnSerial: 11));
    }),
    ("a parent-spawned ordinary target follows its unique Boss owner", () =>
    {
        SpawnedNpcAttribution? result = SpawnedNpcAttributionPolicy.Resolve(
            ["Parent"],
            [],
            []);
        AssertEqual(new SpawnedNpcAttribution("Parent", null), result!.Value);
    }),
    ("a parent-spawned catalog Boss is treated as a nested Boss", () =>
    {
        SpawnedNpcAttribution? result = SpawnedNpcAttributionPolicy.Resolve(
            ["Parent"],
            ["Nested"],
            []);
        AssertEqual(new SpawnedNpcAttribution("Parent", "Nested"), result!.Value);
    }),
    ("an ambiguous spawn origin is not guessed", () =>
        AssertEqual<SpawnedNpcAttribution?>(null, SpawnedNpcAttributionPolicy.Resolve(
            ["ParentA", "ParentB"],
            [],
            []))),
    ("an ambiguously catalogued spawned target is not collapsed", () =>
        AssertEqual<SpawnedNpcAttribution?>(null, SpawnedNpcAttributionPolicy.Resolve(
            ["Parent"],
            ["NestedA", "NestedB"],
            []))),
    ("an explicit server adapter protects a spawned nested Boss", () =>
        AssertEqual<SpawnedNpcAttribution?>(null, SpawnedNpcAttributionPolicy.Resolve(
            ["Parent"],
            ["Nested"],
            ["Nested"]))),
    ("a target already managed by its spawn owner needs no nested result", () =>
    {
        SpawnedNpcAttribution? result = SpawnedNpcAttributionPolicy.Resolve(
            ["Parent"],
            ["Parent"],
            []);
        AssertEqual(new SpawnedNpcAttribution("Parent", null), result!.Value);
    }),
    ("only the exact parent-spawned instance follows its Boss owner", () =>
    {
        TestBoss parent = new("Parent", Set(100));
        TestBoss unrelated = new("Unrelated", Set(200));
        AssertEqual(parent, BossAttributionPolicy.FindUnique(
            new[] { parent, unrelated },
            999,
            -1,
            boss => boss.Types,
            "Parent",
            boss => boss.Name));
        AssertEqual<TestBoss?>(null, BossAttributionPolicy.FindUnique(
            new[] { parent, unrelated },
            999,
            -1,
            boss => boss.Types));
    }),
    ("a spawned nested instance does not suppress an independent instance of the same Boss", () =>
    {
        TestNpcInstance spawned = new(10, 1);
        TestNpcInstance independent = new(11, 1);
        AssertFalse(SpawnedNpcInstancePolicy.ShouldSuppressNestedDescriptor(
            new[] { spawned, independent },
            new HashSet<TestNpcInstance> { spawned }));
        AssertTrue(SpawnedNpcInstancePolicy.ShouldSuppressNestedDescriptor(
            new[] { spawned },
            new HashSet<TestNpcInstance> { spawned }));
    }),
    ("spawned ownership is scoped to the exact Boss occurrence and root generation", () =>
    {
        TestNpcInstance oldRoot = new(5, 10);
        TestNpcInstance newRoot = new(5, 12);
        AssertTrue(SpawnedNpcInstancePolicy.IsOwnershipCurrent(
            2,
            2,
            oldRoot,
            new HashSet<TestNpcInstance> { oldRoot }));
        AssertFalse(SpawnedNpcInstancePolicy.IsOwnershipCurrent(
            2,
            3,
            oldRoot,
            new HashSet<TestNpcInstance> { oldRoot, newRoot }));
        AssertFalse(SpawnedNpcInstancePolicy.IsOwnershipCurrent(
            3,
            3,
            oldRoot,
            new HashSet<TestNpcInstance> { newRoot }));
    }),
    ("verified zenith Desert Scourge parent spawn groups Aquatic Scourge", () =>
        AssertTrue(CalamityCompatibilityPolicy.ShouldGroupZenithScourgeTransition(
            true,
            [CalamityCompatibilityPolicy.DesertScourgeKey],
            CalamityCompatibilityPolicy.AquaticScourgeKey,
            false,
            true))),
    ("a transient Calamity Boss Rush query failure uses bounded retries", () =>
    {
        AssertEqual(60L, CalamityCompatibilityPolicy.BossRushQueryRetryDelayTicks(1));
        AssertEqual(120L, CalamityCompatibilityPolicy.BossRushQueryRetryDelayTicks(2));
        AssertEqual(480L, CalamityCompatibilityPolicy.BossRushQueryRetryDelayTicks(4));
        AssertEqual(480L, CalamityCompatibilityPolicy.BossRushQueryRetryDelayTicks(16));
    }),
    ("mere Scourge co-presence in a zenith world does not imply a transition", () =>
        AssertFalse(CalamityCompatibilityPolicy.ShouldGroupZenithScourgeTransition(
            true,
            [CalamityCompatibilityPolicy.DesertScourgeKey],
            CalamityCompatibilityPolicy.AquaticScourgeKey,
            false,
            false))),
    ("ordinary worlds keep both Scourges independent", () =>
        AssertFalse(CalamityCompatibilityPolicy.ShouldGroupZenithScourgeTransition(
            false,
            [CalamityCompatibilityPolicy.DesertScourgeKey],
            CalamityCompatibilityPolicy.AquaticScourgeKey,
            false,
            true))),
    ("an explicit Scourge adapter wins over built-in zenith grouping", () =>
        AssertFalse(CalamityCompatibilityPolicy.ShouldGroupZenithScourgeTransition(
            true,
            [CalamityCompatibilityPolicy.DesertScourgeKey],
            CalamityCompatibilityPolicy.AquaticScourgeKey,
            true,
            true))),
    ("Boss Rush accepts a killed Astrum Deus split controller while its sibling still exists", () =>
    {
        bool progressionFinal = CalamityCompatibilityPolicy.IsAstrumDeusBossRushProgressionFinal(
            true,
            CalamityCompatibilityPolicy.AstrumDeusKey,
            "CalamityMod",
            "AstrumDeusHead",
            false,
            true,
            true);
        AssertTrue(progressionFinal);
        AssertTrue(EncounterPolicy.ShouldResolveVictory(
            hasActiveKeepAliveNpc: true,
            explicitFinalKill: progressionFinal,
            provisionalLastParticipantKill: false,
            downedTransition: false));
    }),
    ("Astrum Deus fallback identity works without a Boss Checklist key", () =>
        AssertTrue(CalamityCompatibilityPolicy.IsAstrumDeusBossRushProgressionFinal(
            true,
            "npc:1234",
            "CalamityMod",
            "AstrumDeusHead",
            false,
            true,
            true))),
    ("ordinary Astrum Deus fights do not use the Boss Rush progression final", () =>
        AssertFalse(CalamityCompatibilityPolicy.IsAstrumDeusBossRushProgressionFinal(
            false,
            CalamityCompatibilityPolicy.AstrumDeusKey,
            "CalamityMod",
            "AstrumDeusHead",
            false,
            true,
            true))),
    ("Astrum Deus requires an observed split before accepting Boss Rush progression", () =>
        AssertFalse(CalamityCompatibilityPolicy.IsAstrumDeusBossRushProgressionFinal(
            true,
            CalamityCompatibilityPolicy.AstrumDeusKey,
            "CalamityMod",
            "AstrumDeusHead",
            false,
            false,
            true))),
    ("Astrum Deus dependent segments cannot become Boss Rush progression finals", () =>
        AssertFalse(CalamityCompatibilityPolicy.IsAstrumDeusBossRushProgressionFinal(
            true,
            CalamityCompatibilityPolicy.AstrumDeusKey,
            "CalamityMod",
            "AstrumDeusBody",
            false,
            true,
            false))),
    ("other Boss Rush controllers keep the ordinary lifecycle boundary", () =>
        AssertFalse(CalamityCompatibilityPolicy.IsAstrumDeusBossRushProgressionFinal(
            true,
            "CalamityMod DevourerofGods",
            "CalamityMod",
            "DevourerofGodsHead",
            false,
            true,
            true))),
    ("an explicit Astrum Deus server adapter overrides the built-in Boss Rush rule", () =>
        AssertFalse(CalamityCompatibilityPolicy.IsAstrumDeusBossRushProgressionFinal(
            true,
            CalamityCompatibilityPolicy.AstrumDeusKey,
            "CalamityMod",
            "AstrumDeusHead",
            true,
            true,
            true))),
    ("dependent segments inherit body and keep-alive roles", () =>
    {
        BossFallbackRole roles = BossFallbackPolicy.InheritDependentRoles(true, true, false);
        AssertTrue((roles & BossFallbackRole.Body) != 0);
        AssertTrue((roles & BossFallbackRole.KeepAlive) != 0);
        AssertFalse((roles & BossFallbackRole.ExcludedBody) != 0);
    }),
    ("excluded adds do not become body or implicit keep-alive forms", () =>
    {
        BossFallbackRole roles = BossFallbackPolicy.InheritDependentRoles(true, false, true);
        AssertFalse((roles & BossFallbackRole.Body) != 0);
        AssertFalse((roles & BossFallbackRole.KeepAlive) != 0);
        AssertTrue((roles & BossFallbackRole.ExcludedBody) != 0);
    }),
    ("chat history mapping flushes once after a same-tick result burst", () =>
    {
        ChatSummaryBatcher batcher = new();
        batcher.Queue(10);
        batcher.Queue(10);
        batcher.Queue(10);
        AssertFalse(batcher.TryConsume(10));
        AssertFalse(batcher.TryConsume(15));
        AssertTrue(batcher.TryConsume(16));
        AssertFalse(batcher.TryConsume(16));
    }),
    ("a later result extends the chat history quiet window", () =>
    {
        ChatSummaryBatcher batcher = new();
        batcher.Queue(10);
        AssertFalse(batcher.TryConsume(15));
        batcher.Queue(15);
        AssertFalse(batcher.TryConsume(20));
        AssertTrue(batcher.TryConsume(21));
        batcher.Queue(22);
        batcher.Reset();
        AssertFalse(batcher.HasPending);
        AssertFalse(batcher.TryConsume(30));
    }),
    ("pending controller kill defers wipe and despawn boundaries", () =>
        AssertEqual(
            AutomaticFinalizationDecision.Pending,
            AutomaticFinalizationPolicy.ResolveBoundary(true, false))),
    ("a foreign encounter confirms an already pending controller kill", () =>
        AssertEqual(
            AutomaticFinalizationDecision.Victory,
            AutomaticFinalizationPolicy.ResolveBoundary(true, true))),
    ("a boundary without controller kill evidence remains on normal policy", () =>
        AssertEqual(
            AutomaticFinalizationDecision.None,
            AutomaticFinalizationPolicy.ResolveBoundary(false, true))),
    ("same-tick phase replacement cancels an old controller kill", () =>
    {
        AutomaticFinalizationTracker<string> tracker = new();
        AssertEqual(AutomaticFinalizationDecision.None, tracker.Refresh(
            Set("head-a", "body-a"), Set("head-a"), Set<string>(), Set<string>(), 0, 2));
        AssertEqual(AutomaticFinalizationDecision.None, tracker.Refresh(
            Set("head-b", "head-c", "body-b"),
            Set("head-b", "head-c"),
            Set("head-a"),
            Set("head-a"),
            1,
            2));
        AssertFalse(tracker.HasCandidate);
    }),
    ("killing one split controller does not finish while its sibling lives", () =>
    {
        AutomaticFinalizationTracker<string> tracker = new();
        tracker.Refresh(Set("head-a", "head-b"), Set("head-a", "head-b"), Set<string>(), Set<string>(), 0, 2);
        AssertEqual(AutomaticFinalizationDecision.None, tracker.Refresh(
            Set("head-b"), Set("head-b"), Set("head-a"), Set("head-a"), 1, 2));
        AssertFalse(tracker.HasCandidate);
    }),
    ("final controller kill survives residual segments disappearing without OnKill", () =>
    {
        AutomaticFinalizationTracker<string> tracker = new();
        tracker.Refresh(Set("head", "body", "tail"), Set("head"), Set<string>(), Set<string>(), 0, 2);
        AssertEqual(AutomaticFinalizationDecision.Pending, tracker.Refresh(
            Set("body", "tail"), Set<string>(), Set("head"), Set("head"), 10, 2));
        AssertEqual(AutomaticFinalizationDecision.Pending, tracker.Refresh(
            Set<string>(), Set<string>(), Set<string>(), Set<string>(), 11, 2));
        AssertEqual(AutomaticFinalizationDecision.Pending, tracker.Refresh(
            Set<string>(), Set<string>(), Set<string>(), Set<string>(), 12, 2));
        AssertEqual(AutomaticFinalizationDecision.Victory, tracker.Refresh(
            Set<string>(), Set<string>(), Set<string>(), Set<string>(), 13, 2));
        AssertEqual("head", string.Join(",", tracker.LastConfirmedControllerInstances.Order()));
    }),
    ("Astrum Deus Body and Tail churn cannot contaminate a final Head kill", () =>
    {
        AutomaticFinalizationTracker<string> tracker = new();
        tracker.Refresh(Set("head", "body-old", "tail-old"), Set("head"), Set<string>(), Set<string>(), 0, 2);

        // The old implementation passed every Boss Checklist body type through the
        // unobserved-controller spawn set. Only the explicitly known Head is eligible
        // now, so directly removed replacement segments cannot poison the root epoch.
        AssertEqual(AutomaticFinalizationDecision.Pending, tracker.Refresh(
            Set<string>(),
            Set<string>(),
            Set("head"),
            Set("head"),
            10,
            2,
            Set("head")));
        AssertFalse(tracker.HasUnresolvedControllerDisappearance);
        tracker.Refresh(Set<string>(), Set<string>(), Set<string>(), Set<string>(), 11, 2);
        AssertEqual(AutomaticFinalizationDecision.Victory, tracker.Refresh(
            Set<string>(), Set<string>(), Set<string>(), Set<string>(), 12, 2));
    }),
    ("pure controller despawn creates no victory candidate", () =>
    {
        AutomaticFinalizationTracker<string> tracker = new();
        tracker.Refresh(Set("head", "body"), Set("head"), Set<string>(), Set<string>(), 0, 2);
        AssertEqual(AutomaticFinalizationDecision.None, tracker.Refresh(
            Set<string>(), Set<string>(), Set<string>(), Set<string>(), 1, 2));
        AssertFalse(tracker.HasCandidate);
    }),
    ("one killed controller cannot cover a sibling that vanished without OnKill", () =>
    {
        AutomaticFinalizationTracker<string> tracker = new();
        tracker.Refresh(Set("head-a", "head-b"), Set("head-a", "head-b"), Set<string>(), Set<string>(), 0, 2);
        AssertEqual(AutomaticFinalizationDecision.None, tracker.Refresh(
            Set<string>(), Set<string>(), Set("head-a"), Set("head-a"), 1, 2));
        AssertFalse(tracker.HasCandidate);
    }),
    ("a controller lost without OnKill contaminates the surviving sibling epoch", () =>
    {
        AutomaticFinalizationTracker<string> tracker = new();
        tracker.Refresh(Set("head-a", "head-b"), Set("head-a", "head-b"), Set<string>(), Set<string>(), 0, 2);
        AssertEqual(AutomaticFinalizationDecision.None, tracker.Refresh(
            Set("head-a"), Set("head-a"), Set<string>(), Set<string>(), 1, 2));
        AssertEqual(AutomaticFinalizationDecision.None, tracker.Refresh(
            Set<string>(), Set<string>(), Set("head-a"), Set("head-a"), 2, 2));
        AssertFalse(tracker.HasCandidate);
        AssertTrue(tracker.HasUnresolvedControllerDisappearance);
        AssertEqual(EncounterOutcome.Escaped, ResolveBoss(
            nonKillDisappearance: tracker.HasUnresolvedControllerDisappearance));
    }),
    ("a complete controller-set replacement starts a clean phase epoch", () =>
    {
        AutomaticFinalizationTracker<string> tracker = new();
        tracker.Refresh(Set("head-a"), Set("head-a"), Set<string>(), Set<string>(), 0, 2);
        tracker.Refresh(Set("head-b", "head-c"), Set("head-b", "head-c"), Set<string>(), Set<string>(), 1, 2);
        tracker.Refresh(
            Set<string>(),
            Set<string>(),
            Set("head-b", "head-c"),
            Set("head-b", "head-c"),
            2,
            2);
        tracker.Refresh(Set<string>(), Set<string>(), Set<string>(), Set<string>(), 3, 2);
        AssertEqual(AutomaticFinalizationDecision.Victory, tracker.Refresh(
            Set<string>(), Set<string>(), Set<string>(), Set<string>(), 4, 2));
    }),
    ("overlapping phase successors replace a directly deactivated predecessor", () =>
    {
        AutomaticFinalizationTracker<string> tracker = new();
        tracker.Refresh(Set("old-head"), Set("old-head"), Set<string>(), Set<string>(), 0, 2);
        tracker.Refresh(
            Set("old-head", "new-head-a", "new-head-b"),
            Set("old-head", "new-head-a", "new-head-b"),
            Set<string>(),
            Set<string>(),
            1,
            2);
        AssertEqual(AutomaticFinalizationDecision.None, tracker.Refresh(
            Set("new-head-a", "new-head-b"),
            Set("new-head-a", "new-head-b"),
            Set<string>(),
            Set<string>(),
            2,
            2));
        AssertTrue(tracker.LastRefreshAcceptedOverlapHandoff);
        AssertFalse(tracker.HasUnresolvedControllerDisappearance);

        AssertEqual(AutomaticFinalizationDecision.Pending, tracker.Refresh(
            Set<string>(),
            Set<string>(),
            Set("new-head-a", "new-head-b"),
            Set("new-head-a", "new-head-b"),
            3,
            2));
        tracker.Refresh(Set<string>(), Set<string>(), Set<string>(), Set<string>(), 4, 2);
        AssertEqual(AutomaticFinalizationDecision.Victory, tracker.Refresh(
            Set<string>(), Set<string>(), Set<string>(), Set<string>(), 5, 2));
    }),
    ("a new root does not excuse partial loss while an old sibling survives", () =>
    {
        AutomaticFinalizationTracker<string> tracker = new();
        tracker.Refresh(Set("old-a", "old-b"), Set("old-a", "old-b"), Set<string>(), Set<string>(), 0, 2);
        tracker.Refresh(
            Set("old-a", "old-b", "new-c"),
            Set("old-a", "old-b", "new-c"),
            Set<string>(),
            Set<string>(),
            1,
            2);
        tracker.Refresh(
            Set("old-b", "new-c"),
            Set("old-b", "new-c"),
            Set<string>(),
            Set<string>(),
            2,
            2);
        AssertFalse(tracker.LastRefreshAcceptedOverlapHandoff);
        AssertTrue(tracker.HasUnresolvedControllerDisappearance);
    }),
    ("a long-lived controller overlap cannot disguise a later escape", () =>
    {
        AutomaticFinalizationTracker<string> tracker = new();
        tracker.Refresh(Set("old", "new"), Set("old"), Set<string>(), Set<string>(), 0, 2);
        tracker.Refresh(Set("old", "new"), Set("old", "new"), Set<string>(), Set<string>(), 1, 2);
        tracker.Refresh(Set("old", "new"), Set("old", "new"), Set<string>(), Set<string>(), 2, 2);
        tracker.Refresh(Set("old", "new"), Set("old", "new"), Set<string>(), Set<string>(), 3, 2);
        tracker.Refresh(Set("old", "new"), Set("old", "new"), Set<string>(), Set<string>(), 4, 2);
        tracker.Refresh(Set("new"), Set("new"), Set<string>(), Set<string>(), 5, 2);
        AssertFalse(tracker.LastRefreshAcceptedOverlapHandoff);
        AssertTrue(tracker.HasUnresolvedControllerDisappearance);
    }),
    ("an unscanned killed controller cannot cover an unscanned sibling disappearance", () =>
    {
        AutomaticFinalizationTracker<string> tracker = new();
        AssertEqual(AutomaticFinalizationDecision.None, tracker.Refresh(
            Set<string>(),
            Set<string>(),
            Set("head-a"),
            Set("head-a"),
            1,
            2,
            Set("head-a", "head-b")));
        AssertFalse(tracker.HasCandidate);
    }),
    ("a fully accounted one-tick controller kill can still confirm", () =>
    {
        AutomaticFinalizationTracker<string> tracker = new();
        AssertEqual(AutomaticFinalizationDecision.Pending, tracker.Refresh(
            Set<string>(),
            Set<string>(),
            Set("head"),
            Set("head"),
            10,
            2,
            Set("head")));
        tracker.Refresh(Set<string>(), Set<string>(), Set<string>(), Set<string>(), 11, 2);
        AssertEqual(AutomaticFinalizationDecision.Victory, tracker.Refresh(
            Set<string>(), Set<string>(), Set<string>(), Set<string>(), 12, 2));
    }),
    ("a successor controller on the next scan cancels the confirmation window", () =>
    {
        AutomaticFinalizationTracker<string> tracker = new();
        tracker.Refresh(Set("head-a"), Set("head-a"), Set<string>(), Set<string>(), 0, 2);
        AssertEqual(AutomaticFinalizationDecision.Pending, tracker.Refresh(
            Set<string>(), Set<string>(), Set("head-a"), Set("head-a"), 1, 2));
        AssertEqual(AutomaticFinalizationDecision.None, tracker.Refresh(
            Set("head-b"), Set("head-b"), Set<string>(), Set<string>(), 2, 2));
        AssertFalse(tracker.HasCandidate);
    }),
    ("an OnKill controller may remain active across multiple death-animation scans", () =>
    {
        AutomaticFinalizationTracker<string> tracker = new();
        tracker.Refresh(Set("head"), Set("head"), Set<string>(), Set<string>(), 0, 2);
        AssertEqual(AutomaticFinalizationDecision.Pending, tracker.Refresh(
            Set("head"), Set("head"), Set("head"), Set("head"), 1, 2));
        AssertEqual(AutomaticFinalizationDecision.Pending, tracker.Refresh(
            Set("head"), Set("head"), Set<string>(), Set<string>(), 2, 2));
        AssertTrue(tracker.HasCandidate);
        tracker.Refresh(Set<string>(), Set<string>(), Set<string>(), Set<string>(), 3, 2);
        tracker.Refresh(Set<string>(), Set<string>(), Set<string>(), Set<string>(), 4, 2);
        AssertEqual(AutomaticFinalizationDecision.Victory, tracker.Refresh(
            Set<string>(), Set<string>(), Set<string>(), Set<string>(), 5, 2));
    }),
    ("all split controllers killed in one window can confirm victory", () =>
    {
        AutomaticFinalizationTracker<string> tracker = new();
        tracker.Refresh(Set("head-a", "head-b"), Set("head-a", "head-b"), Set<string>(), Set<string>(), 0, 2);
        AssertEqual(AutomaticFinalizationDecision.Pending, tracker.Refresh(
            Set<string>(), Set<string>(), Set("head-a", "head-b"), Set("head-a", "head-b"), 1, 2));
        AssertEqual(
            "head-a,head-b",
            string.Join(",", tracker.CandidateControllerInstances.Order()));
        tracker.Refresh(Set<string>(), Set<string>(), Set<string>(), Set<string>(), 2, 2);
        AssertEqual(AutomaticFinalizationDecision.Victory, tracker.Refresh(
            Set<string>(), Set<string>(), Set<string>(), Set<string>(), 3, 2));
        AssertEqual(
            "head-a,head-b",
            string.Join(",", tracker.LastConfirmedControllerInstances.Order()));
    }),
    ("a dependent segment reappearing restarts but does not erase final kill evidence", () =>
    {
        AutomaticFinalizationTracker<string> tracker = new();
        tracker.Refresh(Set("head", "body"), Set("head"), Set<string>(), Set<string>(), 0, 2);
        tracker.Refresh(Set("body"), Set<string>(), Set("head"), Set("head"), 1, 2);
        tracker.Refresh(Set<string>(), Set<string>(), Set<string>(), Set<string>(), 2, 2);
        AssertEqual(AutomaticFinalizationDecision.Pending, tracker.Refresh(
            Set("body"), Set<string>(), Set<string>(), Set<string>(), 3, 2));
        tracker.Refresh(Set<string>(), Set<string>(), Set<string>(), Set<string>(), 4, 2);
        tracker.Refresh(Set<string>(), Set<string>(), Set<string>(), Set<string>(), 5, 2);
        AssertEqual(AutomaticFinalizationDecision.Victory, tracker.Refresh(
            Set<string>(), Set<string>(), Set<string>(), Set<string>(), 6, 2));
    }),
    ("automatic final confirmation always spans at least two authoritative scans", () =>
    {
        AutomaticFinalizationTracker<string> tracker = new();
        tracker.Refresh(Set("head"), Set("head"), Set<string>(), Set<string>(), 0, 0);
        AssertEqual(AutomaticFinalizationDecision.Pending, tracker.Refresh(
            Set<string>(), Set<string>(), Set("head"), Set("head"), 10, 0));
        AssertEqual(AutomaticFinalizationDecision.Pending, tracker.Refresh(
            Set<string>(), Set<string>(), Set<string>(), Set<string>(), 11, 0));
        AssertEqual(AutomaticFinalizationDecision.Victory, tracker.Refresh(
            Set<string>(), Set<string>(), Set<string>(), Set<string>(), 12, 0));
    }),
    ("same-slot successor uses its new spawn serial and cancels the old kill", () =>
    {
        AutomaticFinalizationTracker<TestNpcInstance> tracker = new();
        TestNpcInstance oldHead = new(7, 100);
        TestNpcInstance newHead = new(7, 101);
        tracker.Refresh(Set(oldHead), Set(oldHead), Set<TestNpcInstance>(), Set<TestNpcInstance>(), 0, 2);
        AssertEqual(AutomaticFinalizationDecision.None, tracker.Refresh(
            Set(newHead), Set(newHead), Set(oldHead), Set(oldHead), 1, 2));
        AssertFalse(tracker.HasCandidate);
    }),
    ("simultaneous bosses keep independent automatic-final confirmation windows", () =>
    {
        AutomaticFinalizationTracker<string> first = new();
        AutomaticFinalizationTracker<string> second = new();
        first.Refresh(Set("first"), Set("first"), Set<string>(), Set<string>(), 0, 2);
        second.Refresh(Set("second"), Set("second"), Set<string>(), Set<string>(), 0, 2);
        first.Refresh(Set<string>(), Set<string>(), Set("first"), Set("first"), 1, 2);
        AssertEqual(AutomaticFinalizationDecision.None, second.Refresh(
            Set("second"), Set("second"), Set<string>(), Set<string>(), 1, 2));
        first.Refresh(Set<string>(), Set<string>(), Set<string>(), Set<string>(), 2, 2);
        AssertEqual(AutomaticFinalizationDecision.Victory, first.Refresh(
            Set<string>(), Set<string>(), Set<string>(), Set<string>(), 3, 2));
        AssertFalse(second.HasCandidate);
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
    ("an exact parent-spawned target contributes ranking damage but not Boss body damage", () =>
        AssertFalse(BossAttributionPolicy.IsBody(new HashSet<int> { 999 }, new HashSet<int>(), 999, -1, "Parent"))),
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
    }),
    ("panel section defaults preserve the established layout", () =>
    {
        ResultPanelSection[] expected =
        [
            ResultPanelSection.TeamTotal,
            ResultPanelSection.BossBodyDamage,
            ResultPanelSection.UnattributedDamage,
            ResultPanelSection.Duration,
            ResultPanelSection.Ranking,
            ResultPanelSection.PersonalBreakdown
        ];
        AssertTrue(ResultPanelSectionOrderPolicy.CreateDefault().SequenceEqual(expected));
    }),
    ("missing panel section order restores every default section", () =>
    {
        AssertTrue(ResultPanelSectionOrderPolicy.Sanitize(null)
            .SequenceEqual(ResultPanelSectionOrderPolicy.CreateDefault()));
        AssertTrue(ResultPanelSectionOrderPolicy.Sanitize([])
            .SequenceEqual(ResultPanelSectionOrderPolicy.CreateDefault()));
    }),
    ("panel section order removes duplicates and invalid values", () =>
    {
        List<ResultPanelSection> sanitized = ResultPanelSectionOrderPolicy.Sanitize(
        [
            ResultPanelSection.Ranking,
            (ResultPanelSection)999,
            ResultPanelSection.Ranking,
            ResultPanelSection.TeamTotal
        ]);
        AssertEqual(ResultPanelSection.Ranking, sanitized[0]);
        AssertEqual(ResultPanelSection.TeamTotal, sanitized[1]);
        AssertEqual(6, sanitized.Count);
        AssertEqual(6, sanitized.Distinct().Count());
        AssertFalse(sanitized.Contains((ResultPanelSection)999));
    }),
    ("partial panel section order appends newly introduced sections", () =>
    {
        List<ResultPanelSection> sanitized = ResultPanelSectionOrderPolicy.Sanitize(
            [ResultPanelSection.PersonalBreakdown, ResultPanelSection.Duration]);
        AssertEqual(ResultPanelSection.PersonalBreakdown, sanitized[0]);
        AssertEqual(ResultPanelSection.Duration, sanitized[1]);
        AssertEqual(6, sanitized.Count);
    }),
    ("historical recipient policy keeps the stricter settlement/current boundary", () =>
    {
        AssertTrue(ResultRecipientPolicy.CanReceive(
            ResultRecipients.AllActivePlayers,
            ResultRecipients.AllActivePlayers,
            contributed: false));
        AssertFalse(ResultRecipientPolicy.CanReceive(
            ResultRecipients.Contributors,
            ResultRecipients.AllActivePlayers,
            contributed: false));
        AssertFalse(ResultRecipientPolicy.CanReceive(
            ResultRecipients.AllActivePlayers,
            ResultRecipients.Contributors,
            contributed: false));
        AssertTrue(ResultRecipientPolicy.CanReceive(
            ResultRecipients.Contributors,
            ResultRecipients.Contributors,
            contributed: true));
    }),
    ("undefined recipient policy fails closed", () =>
    {
        ResultRecipients invalid = (ResultRecipients)999;
        AssertEqual(ResultRecipients.Contributors, ResultRecipientPolicy.FailClosed(invalid));
        AssertFalse(ResultRecipientPolicy.CanReceive(
            invalid,
            ResultRecipients.AllActivePlayers,
            contributed: false));
    }),
    ("delivery projection omits disabled wire fields while preserving percentage dependencies", () =>
    {
        ResultDeliveryProjection hidden = ResultDeliveryProjectionPolicy.Create(
            false, false, false, false, false, false);
        AssertFalse(hidden.IncludeDuration);
        AssertFalse(hidden.IncludeTeamDamage);
        AssertFalse(hidden.IncludeBossBodyDamage);
        AssertFalse(hidden.IncludeUnattributedDamage);
        AssertFalse(hidden.IncludeRanking);
        AssertFalse(hidden.IncludeFinalBlow);

        ResultDeliveryProjection ranking = ResultDeliveryProjectionPolicy.Create(
            false, false, false, true, false, false);
        AssertTrue(ranking.IncludeRanking);
        AssertTrue(ranking.IncludeTeamDamage);

        ResultDeliveryProjection body = ResultDeliveryProjectionPolicy.Create(
            false, true, false, false, false, false);
        AssertTrue(body.IncludeBossBodyDamage);
        AssertTrue(body.IncludeTeamDamage);
    }),
    ("ModPacket budget reserves framework and packet-type headers", () =>
    {
        AssertEqual(65_529, ModPacketBudget.MaximumPayloadBytes);
        AssertTrue(ModPacketBudget.FitsCompletePacket(ushort.MaxValue));
        AssertFalse(ModPacketBudget.FitsCompletePacket((long)ushort.MaxValue + 1));
    }),
    ("state request retry reports the exact remaining cooldown", () =>
    {
        AssertEqual(180, StateRequestRetryPolicy.GetRetryAfterTicks(100, 100, 180));
        AssertEqual(1, StateRequestRetryPolicy.GetRetryAfterTicks(279, 100, 180));
        AssertEqual(0, StateRequestRetryPolicy.GetRetryAfterTicks(280, 100, 180));
        AssertEqual(180, StateRequestRetryPolicy.GetRetryAfterTicks(50, 100, 180));
    }),
    ("bounded string reader accepts its exact character limit", () =>
    {
        const string value = "测测测测";
        using MemoryStream stream = new();
        using (BinaryWriter writer = new(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            writer.Write(value);
        stream.Position = 0;
        using BinaryReader reader = new(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        AssertEqual(value, BoundedStringIO.Read(reader, 4, "test"));
    }),
    ("bounded string reader rejects oversized and malformed input", () =>
    {
        using (MemoryStream stream = new())
        {
            using (BinaryWriter writer = new(stream, System.Text.Encoding.UTF8, leaveOpen: true))
                writer.Write(new string('x', 81));
            stream.Position = 0;
            using BinaryReader reader = new(stream, System.Text.Encoding.UTF8, leaveOpen: true);
            AssertThrows<IOException>(() => BoundedStringIO.Read(reader, 80, "test"));
        }

        using (MemoryStream stream = new())
        {
            using (BinaryWriter writer = new(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writer.Write7BitEncodedInt(System.Text.Encoding.UTF8.GetMaxByteCount(80) + 1);
            }
            stream.Position = 0;
            using BinaryReader reader = new(stream, System.Text.Encoding.UTF8, leaveOpen: true);
            AssertThrows<IOException>(() => BoundedStringIO.Read(reader, 80, "test"));
        }

        using (MemoryStream stream = new([1, 0xFF]))
        using (BinaryReader reader = new(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            AssertThrows<IOException>(() => BoundedStringIO.Read(reader, 80, "test"));
    }),
    ("oversized protocol identities retain a stable collision-resistant suffix", () =>
    {
        string common = new('x', 100);
        string first = BoundedStringIO.StableIdentity(common + "a", 80);
        string second = BoundedStringIO.StableIdentity(common + "b", 80);
        AssertEqual(80, first.Length);
        AssertEqual(first, BoundedStringIO.StableIdentity(common + "a", 80));
        AssertFalse(first == second);
    }),
    ("accepted state requests stay bounded and preserve exact connection identity", () =>
    {
        PendingStateRequestQueue queue = new(2);
        PendingStateRequest first = new(new EncounterPlayerKey(7, 1), 11);
        PendingStateRequest second = new(new EncounterPlayerKey(7, 2), 12);
        AssertTrue(queue.TryEnqueue(first));
        AssertTrue(queue.TryEnqueue(second));
        AssertFalse(queue.TryEnqueue(new PendingStateRequest(new EncounterPlayerKey(8, 1), 13)));
        AssertTrue(queue.Drain().SequenceEqual([first, second]));
        AssertTrue(queue.TryEnqueue(new PendingStateRequest(new EncounterPlayerKey(8, 1), 13)));
    }),
    ("disconnect notifications can enqueue concurrently and drain once", () =>
    {
        PendingDisconnectQueue queue = new();
        Parallel.For(0, 1_000, index => queue.Enqueue(index, index + 1));
        List<PendingConnectionDisconnect> drained = queue.Drain();
        AssertEqual(1_000, drained.Count);
        AssertEqual(1_000, drained.Select(value => value.PlayerIndex).Distinct().Count());
        AssertEqual(0, queue.Drain().Count);
    }),
    ("two connection generations in one slot each retain a disconnect cleanup", () =>
    {
        AssertTrue(ConnectionGenerationPolicy.ShouldQueueDisconnect(-1, 7));
        AssertFalse(ConnectionGenerationPolicy.ShouldQueueDisconnect(7, 7));
        AssertTrue(ConnectionGenerationPolicy.ShouldQueueDisconnect(7, 8));
    }),
    ("a pending disconnect advances a reused active slot before main-thread cleanup", () =>
    {
        AssertTrue(ConnectionGenerationPolicy.ShouldAdvance(
            wasActive: true,
            isActive: true,
            currentGeneration: 7,
            pendingDisconnectGeneration: 7));
        AssertFalse(ConnectionGenerationPolicy.ShouldAdvance(
            wasActive: true,
            isActive: true,
            currentGeneration: 8,
            pendingDisconnectGeneration: 7));
        AssertTrue(ConnectionGenerationPolicy.ShouldAdvance(
            wasActive: false,
            isActive: true,
            currentGeneration: 8,
            pendingDisconnectGeneration: -1));
        AssertFalse(ConnectionGenerationPolicy.ShouldAdvance(
            wasActive: false,
            isActive: false,
            currentGeneration: 8,
            pendingDisconnectGeneration: -1));
    }),
    ("server config text bounds do not split a surrogate pair", () =>
    {
        string value = "a\U0001F525b";
        AssertEqual("a", ServerConfigValuePolicy.NormalizeText(value, 2));
        AssertEqual("a\U0001F525", ServerConfigValuePolicy.NormalizeText(value, 3));
    }),
    ("server config role sanitation preserves the bound list instance", () =>
    {
        List<string> values = ["  CalamityMod:Boss  ", "calamitymod:boss", "", " OtherMod:Boss "];
        List<string> sanitized = ServerConfigValuePolicy.SanitizeListInPlace(values);
        AssertTrue(ReferenceEquals(values, sanitized));
        AssertEqual(2, values.Count);
        AssertEqual("CalamityMod:Boss", values[0]);
        AssertEqual("OtherMod:Boss", values[1]);
    }),
    ("server config global type budget trims deterministically by role order", () =>
    {
        List<string> first = ["a", "b"];
        List<string> second = ["c", "d"];
        AssertEqual(3, ServerConfigValuePolicy.TrimListsToTotal(3, first, second));
        AssertTrue(first.SequenceEqual(["a", "b"]));
        AssertTrue(second.SequenceEqual(["c"]));
    }),
    ("private queue sequence proves a result cutoff drained before publication", () =>
    {
        RecipientBatchQueue<int, int> queue = new(8, 1, 1);
        AssertTrue(queue.TryEnqueue(1, 10));
        AssertTrue(queue.TryEnqueue(1, 20));
        long cutoff = queue.GetEnqueuedSequence(1);
        AssertEqual(2L, cutoff);
        AssertFalse(queue.IsDrainedThrough(1, cutoff));
        AssertEqual(1, queue.Drain().Count);
        AssertFalse(queue.IsDrainedThrough(1, cutoff));
        AssertEqual(1, queue.Drain().Count);
        AssertTrue(queue.IsDrainedThrough(1, cutoff));
    }),
    ("ranking projection never exposes names beyond the server row limit", () =>
    {
        AssertEqual(3, RankingProjectionPolicy.GetVisiblePlayerCount(true, 3, 10, 10));
        AssertEqual(2, RankingProjectionPolicy.GetVisiblePlayerCount(true, 10, 10, 2));
        AssertEqual(0, RankingProjectionPolicy.GetVisiblePlayerCount(false, 10, 10, 10));
    }),
    ("server config duplicate boss keys keep only the earliest override", () =>
    {
        List<string> values = ["BossA", "BossB", "BossA", ""];
        ServerConfigValuePolicy.RemoveDuplicateBossKeys(values, value => value);
        AssertTrue(values.SequenceEqual(["BossA", "BossB", ""]));
        AssertTrue(ServerConfigValuePolicy.FitsSerializedConfigBudget(
            ServerConfigValuePolicy.MaximumSerializedConfigUtf8Bytes));
        AssertFalse(ServerConfigValuePolicy.FitsSerializedConfigBudget(
            ServerConfigValuePolicy.MaximumSerializedConfigUtf8Bytes + 1));
    })
];

int failures = 0;
tests.AddRange(DamageAttributionRegressionTests.Cases());

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

static EncounterOutcome? ResolveInactiveSibling(
    bool encounterStarted = true,
    bool anotherGroupActive = true,
    bool groupActive = false,
    bool inactiveGraceElapsed = true,
    bool controllerKillCandidate = false,
    bool wipe = false,
    bool continueAfterWipe = false,
    bool livingEngaged = true,
    bool nonKillDisappearance = false,
    bool allowDisappearanceBoundary = true)
    => EncounterPolicy.ResolveInactiveSiblingGroup(new InactiveSiblingGroupFacts(
        encounterStarted,
        anotherGroupActive,
        groupActive,
        inactiveGraceElapsed,
        controllerKillCandidate,
        new UnresolvedBossFacts(
            wipe,
            continueAfterWipe,
            HasNoActivePlayers: false,
            LastHadLivingEngagedPlayer: livingEngaged,
            nonKillDisappearance,
            allowDisappearanceBoundary,
            ForeignEncounterStarted: false,
            GenericFallbackExpired: false,
            OtherBossAlreadyResolved: false,
            AllowSiblingBoundary: allowDisappearanceBoundary)));

static void AssertTrue(bool value)
{
    if (!value)
        throw new InvalidOperationException("Expected true.");
}

static void AssertFalse(bool value) => AssertTrue(!value);

static void AssertThrows<TException>(Action action) where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

static HashSet<T> Set<T>(params T[] values) where T : notnull => [.. values];

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

internal readonly record struct TestNpcInstance(int Slot, long SpawnSerial);

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
