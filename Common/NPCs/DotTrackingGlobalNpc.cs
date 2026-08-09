using DaybreakDamageTracker.Common.Data;
using DaybreakDamageTracker.Common.Systems;

namespace DaybreakDamageTracker.Common.NPCs;

internal sealed class DotTrackingGlobalNpc : GlobalNPC
{
    private readonly Dictionary<int, List<OwnerSegment>> _serverOwners = [];
    private readonly Dictionary<int, List<SourceSegment>> _localSources = [];

    public override bool InstancePerEntity => true;
    internal WeightedDamageAccumulator<DotDamageClaimKey> DamageAllocator { get; } = new();

    internal void RegisterServerExtension(
        int buffType,
        int previousTime,
        int currentTime,
        DotDamageOwner owner,
        long now)
    {
        if (!_serverOwners.TryGetValue(buffType, out List<OwnerSegment>? segments))
            _serverOwners[buffType] = segments = [];
        if (DotDamageAttributionPolicy.ShouldResetLeaseSegments(previousTime))
            segments.Clear();

        AddExtension(
            segments,
            Math.Max(0, previousTime),
            Math.Max(0, currentTime),
            owner,
            now,
            segment => segment.EndTick,
            (start, end, value) => new OwnerSegment(start, end, value));
    }

    internal void RegisterLocalExtension(
        int buffType,
        int previousTime,
        int currentTime,
        DamageRootKey root,
        long now)
    {
        if (!_localSources.TryGetValue(buffType, out List<SourceSegment>? segments))
            _localSources[buffType] = segments = [];
        if (DotDamageAttributionPolicy.ShouldResetLeaseSegments(previousTime))
            segments.Clear();

        AddExtension(
            segments,
            Math.Max(0, previousTime),
            Math.Max(0, currentTime),
            root,
            now,
            segment => segment.EndTick,
            (start, end, value) => new SourceSegment(start, end, value));
    }

    internal DotDamageOwner? GetServerOwner(int buffType, long now)
    {
        if (!_serverOwners.TryGetValue(buffType, out List<OwnerSegment>? segments))
            return null;

        segments.RemoveAll(segment => segment.EndTick <= now);
        OwnerSegment? match = segments.FirstOrDefault(segment => segment.StartTick <= now && now < segment.EndTick);
        return match?.Owner;
    }

    internal bool TryGetLocalSource(int buffType, long now, out DamageRootKey root)
    {
        root = default;
        if (!_localSources.TryGetValue(buffType, out List<SourceSegment>? segments))
            return false;

        segments.RemoveAll(segment => segment.EndTick <= now);
        SourceSegment? match = segments.FirstOrDefault(segment => segment.StartTick <= now && now < segment.EndTick);
        if (match is null)
            return false;

        root = match.Root;
        return true;
    }

    private static void AddExtension<TSegment, TValue>(
        List<TSegment> segments,
        int previousTime,
        int currentTime,
        TValue value,
        long now,
        Func<TSegment, long> endSelector,
        Func<long, long, TValue, TSegment> create)
    {
        segments.RemoveAll(segment => endSelector(segment) <= now);
        DotLeaseWindow? window = DotDamageAttributionPolicy.GetExtensionWindow(
            now,
            previousTime,
            currentTime);
        if (!window.HasValue)
            return;

        segments.Add(create(window.Value.StartTick, window.Value.EndTick, value));
    }

    private sealed record OwnerSegment(long StartTick, long EndTick, DotDamageOwner Owner);
    private sealed record SourceSegment(long StartTick, long EndTick, DamageRootKey Root);
}
