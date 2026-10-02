using DaybreakDamageTracker.Common.Data;
using DaybreakDamageTracker.Common.Systems;

internal static class DamageAttributionRegressionTests
{
    public static IEnumerable<(string Name, Action Test)> Cases()
    {
        yield return ("reconciliation preserves observed unclassified damage when adding a missing remainder", () =>
        {
            MutableSourceTree tree = new();
            DamageRootKey item = new(DamageRootKind.Item, 1);
            DamageRootKey unknown = new(DamageRootKind.Unclassified, 0);
            DamageLeafKey dot = new(DamageLeafKind.Debuff, 20);
            tree.Add(item, new DamageLeafKey(DamageLeafKind.ItemBody, 1), 50);
            tree.Add(unknown, dot, 30);

            SourceTreeSnapshot result = tree.Reconcile(100);
            Equal(100L, result.Roots.Sum(root => root.Damage));
            Equal(80L, result.ClassifiedTotal);
            SourceRootSnapshot unclassified = result.Roots.Single(root => root.Key == unknown);
            Equal(50L, unclassified.Damage);
            Equal(30L, unclassified.Leaves.Single(leaf => leaf.Key == dot).Damage);
            Equal(50L, result.Roots.Single(root => root.Key == item).Damage);
            // Reconciliation is a snapshot operation; the added remainder must not
            // become an observed atom in later reconciliations of the same ledger.
            Equal(80L, tree.Reconcile(80).Roots.Sum(root => root.Damage));
            Equal(30L, tree.Reconcile(80).Roots.Single(root => root.Key == unknown).Damage);
        });

        yield return ("reconciliation keeps canonical totals across missing scaled and empty source trees", () =>
        {
            Random random = new(7788);
            for (int sample = 0; sample < 300; sample++)
            {
                MutableSourceTree tree = new();
                long observed = 0;
                for (int atom = 0; atom < random.Next(0, 18); atom++)
                {
                    int amount = random.Next(1, 1000);
                    DamageRootKey root = atom % 3 == 0
                        ? new DamageRootKey(DamageRootKind.Unclassified, 0)
                        : new DamageRootKey(DamageRootKind.Item, atom % 4);
                    tree.Add(root, new DamageLeafKey(DamageLeafKind.Debuff, atom % 5), amount);
                    observed += amount;
                }
                foreach (long canonical in new[] { 0L, observed / 2, observed, observed + 1000 })
                {
                    SourceTreeSnapshot result = tree.Reconcile(canonical);
                    Equal(canonical, result.Roots.Sum(root => root.Damage));
                    Equal(Math.Min(observed, canonical), result.ClassifiedTotal);
                    Equal(observed > canonical, result.WasScaledToCanonicalTotal);
                    Equal(result.Roots.Count, result.Roots.Select(root => root.Key).Distinct().Count());
                    foreach (SourceRootSnapshot root in result.Roots)
                    {
                        if (root.Damage < 0 || root.Leaves.Any(leaf => leaf.Damage < 0) ||
                            root.Leaves.Sum(leaf => leaf.Damage) > root.Damage)
                            throw new InvalidOperationException("Source root/leaf conservation failed.");
                    }
                }
            }
        });

        yield return ("ownerless reapplication after a cleanse clears a predicted player lease", () =>
        {
            DotLeaseRegistry<EncounterPlayerKey> registry = new();
            EncounterPlayerKey owner = new(1, 1);
            registry.RegisterExtension(20, 0, 600, owner, 100);
            Equal<EncounterPlayerKey?>(owner, registry.GetAttribution(20, 199));
            // The buff has been removed early. Its replacement has no attributable
            // player, so the old lease must not survive until the predicted tick 700.
            registry.RegisterExtension(20, 0, 300, null, 200);
            Equal<EncounterPlayerKey?>(null, registry.GetAttribution(20, 200));
            Equal<EncounterPlayerKey?>(null, registry.GetAttribution(20, 499));
        });

        yield return ("a nonlocal reapplication after a cleanse clears the old local source", () =>
        {
            DotLeaseRegistry<DamageRootKey> registry = new();
            DamageRootKey weapon = new(DamageRootKind.Item, 1);
            registry.RegisterExtension(20, 0, 600, weapon, 100);
            registry.RegisterExtension(20, 0, 300, null, 200);
            Equal<DamageRootKey?>(null, registry.GetAttribution(20, 200));
        });

        yield return ("ownerless refresh preserves the current owner but leaves its added tail unattributed", () =>
        {
            DotLeaseRegistry<int> registry = new();
            registry.RegisterExtension(20, 0, 100, 1, 100);
            registry.RegisterExtension(20, 50, 150, null, 150);
            Equal<int?>(1, registry.GetAttribution(20, 199));
            Equal<int?>(null, registry.GetAttribution(20, 200));
            Equal<int?>(null, registry.GetAttribution(20, 299));
            // A later known refresh owns only the new tail after the ownerless span.
            registry.RegisterExtension(20, 100, 200, 2, 200);
            Equal<int?>(null, registry.GetAttribution(20, 299));
            Equal<int?>(2, registry.GetAttribution(20, 300));
            Equal<int?>(2, registry.GetAttribution(20, 399));
            Equal<int?>(null, registry.GetAttribution(20, 400));
        });

        yield return ("known reapplication replaces only that buff and keeps extension ownership boundaries", () =>
        {
            DotLeaseRegistry<int> registry = new();
            registry.RegisterExtension(20, 0, 600, 1, 100);
            registry.RegisterExtension(24, 0, 600, 3, 100);
            registry.RegisterExtension(20, 0, 100, 2, 200);
            Equal<int?>(2, registry.GetAttribution(20, 200));
            Equal<int?>(3, registry.GetAttribution(24, 200));
            registry.RegisterExtension(20, 50, 100, 4, 250);
            Equal<int?>(2, registry.GetAttribution(20, 299));
            Equal<int?>(4, registry.GetAttribution(20, 300));
            Equal<int?>(null, registry.GetAttribution(20, 350));
        });

        yield return ("NPC generation reset clears all leases before accepting a replacement instance", () =>
        {
            DotLeaseRegistry<int> registry = new();
            registry.RegisterExtension(20, 0, 600, 1, 100);
            registry.RegisterExtension(24, 0, 600, 2, 100);
            registry.Clear();
            Equal<int?>(null, registry.GetAttribution(20, 200));
            Equal<int?>(null, registry.GetAttribution(24, 200));
            registry.RegisterExtension(20, 0, 100, 3, 200);
            Equal<int?>(3, registry.GetAttribution(20, 200));
            Equal<int?>(null, registry.GetAttribution(20, 300));
        });

        yield return ("NPC generation reset removes fractional damage credit from the old instance", () =>
        {
            WeightedDamageAccumulator<int> allocator = new();
            List<WeightedDamageClaim<int>> claims = [new(1, 1), new(2, 1)];
            Equal(1, allocator.Allocate(1, claims).Single().Id);
            // Without a reset, the next point belongs to 2. A different NPC begins
            // a fresh allocation schedule even if it has identical active effects.
            allocator.Reset();
            Equal(1, allocator.Allocate(1, claims).Single().Id);
            Equal(2, allocator.Allocate(1, claims).Single().Id);
        });
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
}
