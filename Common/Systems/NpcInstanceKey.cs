namespace DaybreakDamageTracker.Common.Systems;

internal readonly record struct NpcInstanceKey(int Slot, long SpawnSerial)
{
    public bool IsValid => Slot >= 0 && SpawnSerial > 0;
}
