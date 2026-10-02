// The harness executes production client state/serialization code; only Terraria's
// world and graphical API surface is substituted. It does not simulate socket IO.
global using System.Globalization;
global using Terraria;
global using Terraria.ID;
global using Terraria.ModLoader;
global using Terraria.DataStructures;
global using Terraria.Localization;
global using Microsoft.Xna.Framework;

namespace Microsoft.Xna.Framework
{
    public readonly record struct Color(byte R, byte G, byte B);
}

namespace Terraria
{
    public static class Main
    {
        public const int maxNPCs = 200;
        public const int maxPlayers = 255;
        public static int netMode = ID.NetmodeID.MultiplayerClient;
        public static bool dedServ;
        public static int myPlayer;
        public static ulong GameUpdateCount;
        public static NPC[] npc = Enumerable.Range(0, maxNPCs).Select(i => new NPC { whoAmI = i }).ToArray();
        public static Player[] player = Enumerable.Range(0, maxPlayers).Select(i => new Player { whoAmI = i }).ToArray();
        public static void NewText(string text, Color color) { }
    }

    public sealed class NPC
    {
        private readonly Dictionary<Type, GlobalNPC> _globals = [];
        public int whoAmI;
        public int type;
        public int realLife = -1;
        public bool friendly;
        public bool townNPC;
        public int lifeMax = 100;
        public T GetGlobalNPC<T>() where T : GlobalNPC, new()
        {
            if (!_globals.TryGetValue(typeof(T), out GlobalNPC? value))
                _globals[typeof(T)] = value = new T();
            return (T)value;
        }
        public readonly record struct HitInfo;
        public struct HitModifiers { }
    }
    public sealed class Player { public int whoAmI; public bool active = true; }
    public sealed class Item { public int type; }
    public sealed class Projectile
    {
        public int owner;
        public bool friendly;
        public bool hostile;
        public bool npcProj;
        public bool trap;
    }
    public static class Lang { public static string GetNPCNameValue(int type) => $"NPC {type}"; }
}

namespace Terraria.ID
{
    public static class NetmodeID { public const int SinglePlayer = 0, MultiplayerClient = 1, Server = 2; }
    public static class NPCID
    {
        public const int None = 0, TargetDummy = 488;
        public static class Sets { public static readonly bool[] CountsAsCritter = new bool[1000]; }
    }
}

namespace Terraria.DataStructures
{
    public interface IEntitySource { }
    public sealed class EntitySource_Parent(object entity) : IEntitySource { public object Entity { get; } = entity; }
}

namespace Terraria.Localization
{
    public static class Language
    {
        public static bool Exists(string key) => false;
        public static string GetTextValue(string key) => key;
    }
}

namespace Terraria.ModLoader.IO
{
    public sealed class BitWriter { }
    public sealed class BitReader { }
}

namespace Terraria.ModLoader
{
    public static class NPCLoader { public static int NPCCount => 1000; }
    public abstract class GlobalNPC
    {
        public virtual bool InstancePerEntity => false;
        public virtual void SetDefaults(NPC npc) { }
        public virtual void OnSpawn(NPC npc, IEntitySource source) { }
        public virtual void SendExtraAI(NPC npc, IO.BitWriter bitWriter, BinaryWriter writer) { }
        public virtual void ReceiveExtraAI(NPC npc, IO.BitReader bitReader, BinaryReader reader) { }
        public virtual void ModifyHitByItem(NPC npc, Player player, Item item, ref NPC.HitModifiers modifiers) { }
        public virtual void OnHitByItem(NPC npc, Player player, Item item, NPC.HitInfo hit, int damageDone) { }
        public virtual void ModifyHitByProjectile(NPC npc, Projectile projectile, ref NPC.HitModifiers modifiers) { }
        public virtual void OnHitByProjectile(NPC npc, Projectile projectile, NPC.HitInfo hit, int damageDone) { }
        public virtual void OnKill(NPC npc) { }
    }
}

namespace DaybreakDamageTracker.Client
{
    public sealed class ClientConfig
    {
        public const float DefaultPanelBackgroundOpacity = 0.9f;
        public bool AutoShowResults = true;
        public int HistoryCount = 10;
        public int AutoHideSeconds = 12;
        public float PanelBackgroundOpacity = 0.9f;
        public List<ResultPanelSection> PanelSectionOrder = [];
    }
}

namespace DaybreakDamageTracker.Client.UI
{
    internal sealed class DamageResultHudSystem
    {
        public bool IsVisible => false;
        public void Hide() { }
        public void RefreshPresentation() { }
        public void ApplyLocalPreferences() { }
        public void Append(ClientHistoryEntry entry) { }
        public void Show(ClientHistoryEntry entry, bool automatic) { }
    }
}

namespace DaybreakDamageTracker.Common.Bosses
{
    internal static class BossCatalog { public static bool HasActiveBossClientSide() => true; }
}

namespace DaybreakDamageTracker.Common.Networking
{
    internal static class DamageNetwork { public static void RequestServerState() { } }
}

namespace DaybreakDamageTracker.Common.Systems
{
    internal readonly record struct EncounterPlayerKey(int PlayerIndex, int Generation);
    internal readonly record struct DotDamageOwner(EncounterPlayerKey Key, string PlayerName);
    internal readonly record struct DotDamageClaimKey(
        int BuffType, bool HasOwner, EncounterPlayerKey OwnerKey,
        bool HasPrivateRoot, Data.DamageRootKey PrivateRoot);
    internal static class EncounterSystem
    {
        public static void MarkNpcSpawned(NPC npc) { }
        public static void MarkNpcKilled(NPC npc) { }
        public static TargetSnapshot PrepareTarget(NPC npc) => new(true, npc.type, -1);
        public static void RecordPlayerDamage(int owner, TargetSnapshot target, int damage) { }
    }
}
