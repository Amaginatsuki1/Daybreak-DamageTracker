namespace DaybreakDamageTracker.Common.Data;

public enum DamageRootKind : byte
{
    Item,
    Buff,
    Mount,
    ProjectileFallback,
    Unclassified
}

public readonly record struct DamageRootKey(DamageRootKind Kind, int PrimaryType);

public enum DamageLeafKind : byte
{
    ItemBody,
    Projectile
}

public readonly record struct DamageLeafKey(DamageLeafKind Kind, int Type);

public sealed class SourceLeafSnapshot
{
    public DamageLeafKey Key { get; set; }
    public long Damage { get; set; }
}

public sealed class SourceRootSnapshot
{
    public DamageRootKey Key { get; set; }
    public long Damage { get; set; }
    public List<SourceLeafSnapshot> Leaves { get; set; } = [];
}

public sealed class SourceTreeSnapshot
{
    public long CanonicalTotal { get; set; }
    public bool CanonicalTotalKnown { get; set; }
    public bool SourceDetailsAvailable { get; set; }
    public long ClassifiedTotal { get; set; }
    public bool WasScaledToCanonicalTotal { get; set; }
    public List<SourceRootSnapshot> Roots { get; set; } = [];

    public static SourceTreeSnapshot Unavailable(long? canonicalTotal = null) => new()
    {
        CanonicalTotal = Math.Max(0, canonicalTotal ?? 0),
        CanonicalTotalKnown = canonicalTotal.HasValue,
        SourceDetailsAvailable = false,
        ClassifiedTotal = 0
    };
}

internal sealed class MutableSourceTree
{
    private readonly Dictionary<DamageRootKey, Dictionary<DamageLeafKey, long>> _roots = [];

    public bool IsEmpty => _roots.Count == 0;

    public void Clear() => _roots.Clear();

    public void MergeFrom(MutableSourceTree source)
    {
        if (ReferenceEquals(this, source))
            return;

        foreach ((DamageRootKey root, Dictionary<DamageLeafKey, long> sourceLeaves) in source._roots)
        {
            if (!_roots.TryGetValue(root, out Dictionary<DamageLeafKey, long>? leaves))
                _roots[root] = leaves = [];

            foreach ((DamageLeafKey leaf, long damage) in sourceLeaves)
            {
                leaves.TryGetValue(leaf, out long current);
                leaves[leaf] = checked(current + damage);
            }
        }
        source.Clear();
    }

    public void Add(DamageRootKey root, DamageLeafKey leaf, int damage)
    {
        if (damage <= 0)
            return;

        if (!_roots.TryGetValue(root, out Dictionary<DamageLeafKey, long>? leaves))
            _roots[root] = leaves = [];

        leaves.TryGetValue(leaf, out long current);
        leaves[leaf] = checked(current + damage);
    }

    public SourceTreeSnapshot Reconcile(long canonicalTotal)
    {
        canonicalTotal = Math.Max(0, canonicalTotal);
        List<MutableAtom> atoms = [];

        foreach ((DamageRootKey root, Dictionary<DamageLeafKey, long> leaves) in _roots)
        {
            foreach ((DamageLeafKey leaf, long damage) in leaves)
            {
                if (damage > 0)
                    atoms.Add(new MutableAtom(root, leaf, damage));
            }
        }

        long classifiedTotal = atoms.Aggregate(0L, (sum, atom) => checked(sum + atom.RawDamage));
        bool scaled = classifiedTotal > canonicalTotal && classifiedTotal > 0;

        if (scaled)
            ScaleAtoms(atoms, classifiedTotal, canonicalTotal);
        else
            atoms.ForEach(atom => atom.FinalDamage = atom.RawDamage);

        Dictionary<DamageRootKey, SourceRootSnapshot> rebuilt = [];
        foreach (MutableAtom atom in atoms.Where(atom => atom.FinalDamage > 0))
        {
            if (!rebuilt.TryGetValue(atom.Root, out SourceRootSnapshot? root))
            {
                root = new SourceRootSnapshot { Key = atom.Root };
                rebuilt[atom.Root] = root;
            }

            root.Leaves.Add(new SourceLeafSnapshot { Key = atom.Leaf, Damage = atom.FinalDamage });
            root.Damage = checked(root.Damage + atom.FinalDamage);
        }

        long finalKnown = rebuilt.Values.Sum(root => root.Damage);
        if (finalKnown < canonicalTotal)
        {
            long missing = canonicalTotal - finalKnown;
            rebuilt[new DamageRootKey(DamageRootKind.Unclassified, 0)] = new SourceRootSnapshot
            {
                Key = new DamageRootKey(DamageRootKind.Unclassified, 0),
                Damage = missing,
                Leaves = []
            };
        }

        return new SourceTreeSnapshot
        {
            CanonicalTotal = canonicalTotal,
            CanonicalTotalKnown = true,
            SourceDetailsAvailable = true,
            ClassifiedTotal = Math.Min(classifiedTotal, canonicalTotal),
            WasScaledToCanonicalTotal = scaled,
            Roots = rebuilt.Values.OrderByDescending(root => root.Damage).ToList()
        };
    }

    private static void ScaleAtoms(List<MutableAtom> atoms, long sourceTotal, long targetTotal)
    {
        decimal ratio = targetTotal / (decimal)sourceTotal;
        foreach (MutableAtom atom in atoms)
        {
            decimal exact = atom.RawDamage * ratio;
            atom.FinalDamage = (long)decimal.Floor(exact);
            atom.Fraction = exact - atom.FinalDamage;
        }

        long assigned = atoms.Sum(atom => atom.FinalDamage);
        long remainder = targetTotal - assigned;
        foreach (MutableAtom atom in atoms.OrderByDescending(atom => atom.Fraction).ThenByDescending(atom => atom.RawDamage))
        {
            if (remainder-- <= 0)
                break;
            atom.FinalDamage++;
        }
    }

    private sealed class MutableAtom(DamageRootKey root, DamageLeafKey leaf, long rawDamage)
    {
        public DamageRootKey Root { get; } = root;
        public DamageLeafKey Leaf { get; } = leaf;
        public long RawDamage { get; } = rawDamage;
        public long FinalDamage { get; set; }
        public decimal Fraction { get; set; }
    }
}
