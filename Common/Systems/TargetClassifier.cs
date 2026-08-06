namespace DaybreakDamageTracker.Common.Systems;

internal readonly record struct TargetSnapshot(bool Eligible, int NpcType, int RootNpcType);

internal static class TargetClassifier
{
    public static bool IsEligibleHostile(NPC npc)
    {
        if (npc.type < NPCID.None || npc.type >= NPCLoader.NPCCount)
            return false;
        if (npc.friendly || npc.townNPC || npc.type == NPCID.TargetDummy || npc.lifeMax <= 0)
            return false;
        if (npc.type < NPCID.Sets.CountsAsCritter.Length && NPCID.Sets.CountsAsCritter[npc.type])
            return false;
        return true;
    }
}
