using DaybreakDamageTracker.Common.Networking;

namespace DaybreakDamageTracker;

public sealed class DaybreakDamageTrackerMod : Mod
{
    internal static DaybreakDamageTrackerMod Instance { get; private set; } = null!;

    public override void Load() => Instance = this;

    public override void Unload() => Instance = null!;

    public override void HandlePacket(BinaryReader reader, int whoAmI)
        => DamageNetwork.HandlePacket(reader, whoAmI);
}
