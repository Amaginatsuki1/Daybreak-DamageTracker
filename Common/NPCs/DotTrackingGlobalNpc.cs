using DaybreakDamageTracker.Common.Data;
using DaybreakDamageTracker.Common.Systems;

namespace DaybreakDamageTracker.Common.NPCs;

internal sealed class DotTrackingGlobalNpc : GlobalNPC
{
    private readonly DotLeaseRegistry<DotDamageOwner> _serverOwners = new();
    private readonly DotLeaseRegistry<DamageRootKey> _localSources = new();

    public override bool InstancePerEntity => true;
    internal WeightedDamageAccumulator<DotDamageClaimKey> DamageAllocator { get; } = new();

    internal void ResetForNpcInstance()
    {
        // SyncNPC can reuse the same active object without SetDefaults when its
        // type is unchanged. Its new server generation must start with no leases
        // or fractional damage credit from the previous slot occupant.
        _serverOwners.Clear();
        _localSources.Clear();
        DamageAllocator.Reset();
    }

    internal void RegisterServerExtension(
        int buffType,
        int previousTime,
        int currentTime,
        DotDamageOwner? owner,
        long now)
        => _serverOwners.RegisterExtension(buffType, previousTime, currentTime, owner, now);

    internal void RegisterLocalExtension(
        int buffType,
        int previousTime,
        int currentTime,
        DamageRootKey? root,
        long now)
        => _localSources.RegisterExtension(buffType, previousTime, currentTime, root, now);

    internal DotDamageOwner? GetServerOwner(int buffType, long now)
        => _serverOwners.GetAttribution(buffType, now);

    internal bool TryGetLocalSource(int buffType, long now, out DamageRootKey root)
    {
        DamageRootKey? attribution = _localSources.GetAttribution(buffType, now);
        root = attribution.GetValueOrDefault();
        return attribution.HasValue;
    }
}
