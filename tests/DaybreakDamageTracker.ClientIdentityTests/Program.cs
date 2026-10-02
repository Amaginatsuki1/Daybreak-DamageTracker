using DaybreakDamageTracker.Client;
using DaybreakDamageTracker.Common.Data;
using DaybreakDamageTracker.Common.Networking;
using DaybreakDamageTracker.Common.NPCs;
using DaybreakDamageTracker.Common.Systems;
using Terraria.ModLoader.IO;

List<(string Name, Action Run)> tests =
[
    ("pending ordinary damage cannot follow a reused slot", () =>
    {
        NPC old = Spawn(12, 20, 100);
        BossPresentation boss = Boss("A");
        Arm(boss);
        Hit(old, 11, 80);
        NPC replacement = Spawn(12, 20, 101);
        boss.AssociatedNpcInstances = [new(12, 20, 101)];
        Arm(boss);
        Hit(replacement, 22, 20);
        AssertTree(Finish(boss, 20), (22, 20));
    }),
    ("suspended pending damage cannot follow a reused slot", () =>
    {
        NPC old = Spawn(12, 20, 200);
        BossPresentation boss = Boss("A");
        Arm(boss);
        ClientRuntime.OnEncounterSuspended(1);
        Hit(old, 11, 80);
        NPC replacement = Spawn(12, 20, 201);
        boss.AssociatedNpcInstances = [new(12, 20, 201)];
        Arm(boss);
        Hit(replacement, 22, 20);
        AssertTree(Finish(boss, 20), (22, 20));
    }),
    ("resolved sibling associations cannot make replacement ambiguous", () =>
    {
        NPC old = Spawn(12, 20, 300);
        BossPresentation a = Boss("A", new AssociatedNpcInstance(12, 20, 300));
        BossPresentation b = Boss("B");
        Arm(a, b);
        Hit(old, 11, 10);
        Finish(a, 10);
        NPC replacement = Spawn(12, 20, 301);
        b.AssociatedNpcInstances = [new(12, 20, 301)];
        Arm(b);
        Hit(replacement, 22, 20);
        AssertTree(Finish(b, 20), (22, 20));
    }),
    ("attributed old damage and delayed authoritative damage retain their Boss", () =>
    {
        NPC old = Spawn(12, 20, 400);
        BossPresentation a = Boss("A", new AssociatedNpcInstance(12, 20, 400));
        BossPresentation b = Boss("B");
        Arm(a, b);
        Hit(old, 11, 15);
        NPC replacement = Spawn(12, 20, 401);
        b.AssociatedNpcInstances = [new(12, 20, 401)];
        Arm(a, b);
        ClientRuntime.RecordPrivateDamage(new TargetSnapshot(true, 20, -1, new(12, 400), "A"),
            new(DamageRootKind.Item, 33), new(DamageLeafKind.Debuff, 1), 3);
        Hit(replacement, 22, 20);
        AssertTree(Finish(a, 18), (11, 15), (33, 3));
        AssertTree(Finish(b, 20), (22, 20));
    }),
    ("Armed metadata arriving before SyncNPC survives SetDefaults", () =>
    {
        Spawn(12, 20, 500);
        BossPresentation boss = Boss("B", new AssociatedNpcInstance(12, 20, 501));
        Arm(boss);
        NPC replacement = Spawn(12, 20, 501);
        Hit(replacement, 22, 20);
        AssertTree(Finish(boss, 20), (22, 20));
    }),
    ("ordinary repeated ExtraAI does not erase pending current-instance hits", () =>
    {
        NPC npc = Spawn(12, 20, 600);
        BossPresentation boss = Boss("A");
        Arm(boss);
        Hit(npc, 11, 10);
        Receive(npc, 600);
        boss.AssociatedNpcInstances = [new(12, 20, 600)];
        Arm(boss);
        AssertTree(Finish(boss, 10), (11, 10));
    }),
    ("template NPC initialization cannot erase a live slot's pending hits", () =>
    {
        NPC npc = Spawn(12, 20, 700);
        BossPresentation boss = Boss("A");
        Arm(boss);
        Hit(npc, 11, 10);
        NPC template = new() { whoAmI = 12, type = 20 };
        template.GetGlobalNPC<DamageTrackingGlobalNpc>().SetDefaults(template);
        boss.AssociatedNpcInstances = [new(12, 20, 700)];
        Arm(boss);
        AssertTree(Finish(boss, 10), (11, 10));
    }),
    ("unsynchronized NPC hits cannot invent generation one", () =>
    {
        NPC npc = new() { whoAmI = 12, type = 20 };
        Main.npc[12] = npc;
        npc.GetGlobalNPC<DamageTrackingGlobalNpc>().SetDefaults(npc);
        BossPresentation boss = Boss("A", new AssociatedNpcInstance(12, 20, 800));
        Arm(boss);
        Hit(npc, 11, 999);
        Receive(npc, 800);
        Hit(npc, 22, 20);
        AssertTree(Finish(boss, 20), (22, 20));
    }),
    ("late nonauthoritative atoms cannot claim a replacement type", () =>
    {
        Spawn(12, 20, 900);
        BossPresentation boss = Boss("A");
        boss.AssociatedNpcTypes = [20];
        Arm(boss);
        NPC replacement = Spawn(12, 20, 901);
        ClientRuntime.RecordPrivateDamage(new TargetSnapshot(true, 20, -1, new(12, 900)),
            new(DamageRootKind.Item, 11), new(DamageLeafKind.ItemBody, 11), 80);
        Hit(replacement, 22, 20);
        AssertTree(Finish(boss, 20), (22, 20));
    }),
    ("same-object slot reuse is detected through ExtraAI without OnSpawn", () =>
    {
        NPC npc = Spawn(12, 20, 1000);
        BossPresentation boss = Boss("A");
        Arm(boss);
        Hit(npc, 11, 80);
        Receive(npc, 1001);
        boss.AssociatedNpcInstances = [new(12, 20, 1001)];
        Arm(boss);
        Hit(npc, 22, 20);
        AssertTree(Finish(boss, 20), (22, 20));
    }),
    ("same-object replacement clears actual per-NPC DoT leases", () =>
    {
        NPC npc = Spawn(12, 20, 1100);
        DotTrackingGlobalNpc dot = npc.GetGlobalNPC<DotTrackingGlobalNpc>();
        dot.RegisterLocalExtension(1, 0, 120, new DamageRootKey(DamageRootKind.Item, 11), 0);
        dot.RegisterServerExtension(1, 0, 120, new DotDamageOwner(new(1, 1), "test-owner"), 0);
        Equal(true, dot.TryGetLocalSource(1, 1, out _));
        Receive(npc, 1101); // No SetDefaults or OnSpawn on this path.
        Equal(false, dot.TryGetLocalSource(1, 1, out _));
        Equal(false, dot.GetServerOwner(1, 1).HasValue);
        dot.RegisterLocalExtension(1, 0, 120, new DamageRootKey(DamageRootKind.Item, 22), 2);
        Equal(true, dot.TryGetLocalSource(1, 3, out DamageRootKey current));
        Equal(new DamageRootKey(DamageRootKind.Item, 22), current);
    }),
    ("same-generation ExtraAI preserves actual per-NPC DoT leases", () =>
    {
        NPC npc = Spawn(12, 20, 1200);
        DotTrackingGlobalNpc dot = npc.GetGlobalNPC<DotTrackingGlobalNpc>();
        dot.RegisterLocalExtension(1, 0, 120, new DamageRootKey(DamageRootKind.Item, 11), 0);
        Receive(npc, 1200);
        Equal(true, dot.TryGetLocalSource(1, 1, out DamageRootKey current));
        Equal(new DamageRootKey(DamageRootKind.Item, 11), current);
    }),
    ("server SendExtraAI and client ReceiveExtraAI share one authoritative serial", () =>
    {
        Main.netMode = NetmodeID.Server;
        NPC serverNpc = new() { whoAmI = 12, type = 20 };
        Main.npc[12] = serverNpc;
        DamageTrackingGlobalNpc serverGlobal = serverNpc.GetGlobalNPC<DamageTrackingGlobalNpc>();
        serverGlobal.OnSpawn(serverNpc, new EntitySource_Parent(new object()));
        long expected = DamageTrackingGlobalNpc.GetSpawnSerial(serverNpc);
        using MemoryStream stream = new();
        using (BinaryWriter writer = new(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            serverGlobal.SendExtraAI(serverNpc, new BitWriter(), writer);
        Main.netMode = NetmodeID.MultiplayerClient;
        NPC clientNpc = new() { whoAmI = 12, type = 20 };
        Main.npc[12] = clientNpc;
        stream.Position = 0;
        using BinaryReader reader = new(stream);
        clientNpc.GetGlobalNPC<DamageTrackingGlobalNpc>().ReceiveExtraAI(clientNpc, new BitReader(), reader);
        Equal(expected, DamageTrackingGlobalNpc.GetSpawnSerial(clientNpc));
        Equal(true, expected > 0);
    }),
    ("Boss and result packets retain spawn serials", () =>
    {
        BossPresentation boss = Boss("A", new AssociatedNpcInstance(12, 20, 4_000_000_001L));
        PublicResultSnapshot value = new() { EncounterId = 1, Bosses = [boss] };
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        PacketIO.WriteResult(writer, value, 50, new PresentationSettings());
        stream.Position = 0;
        using BinaryReader reader = new(stream);
        ResultDelivery roundTrip = PacketIO.ReadResult(reader);
        Equal(4_000_000_001L, roundTrip.Result.Bosses[0].AssociatedNpcInstances[0].SpawnSerial);
        Equal(50L, roundTrip.OwnDamage!.Value);
        Equal(stream.Length, stream.Position);
    }),
    ("invalid NPC ExtraAI generation fails closed", () =>
    {
        NPC npc = new() { whoAmI = 12, type = 20 };
        Main.npc[12] = npc;
        try { Receive(npc, 0); throw new Exception("invalid serial accepted"); }
        catch (IOException) { }
        Equal(0L, DamageTrackingGlobalNpc.GetSpawnSerial(npc));
    })
];

foreach ((string name, Action run) in tests)
{
    Main.netMode = NetmodeID.MultiplayerClient;
    ClientRuntime.ResetForWorld();
    Main.npc = Enumerable.Range(0, Main.maxNPCs).Select(i => new NPC { whoAmI = i }).ToArray();
    run();
    Console.WriteLine($"PASS {name}");
}
Console.WriteLine($"Client identity integration: {tests.Count}/{tests.Count} passed.");

static NPC Spawn(int slot, int type, long serial)
{
    NPC npc = new() { whoAmI = slot, type = type };
    Main.npc[slot] = npc;
    npc.GetGlobalNPC<DamageTrackingGlobalNpc>().SetDefaults(npc);
    Receive(npc, serial);
    return npc;
}

static void Receive(NPC npc, long serial)
{
    using MemoryStream stream = new();
    using (BinaryWriter writer = new(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        writer.Write(serial);
    stream.Position = 0;
    using BinaryReader reader = new(stream);
    npc.GetGlobalNPC<DamageTrackingGlobalNpc>().ReceiveExtraAI(npc, new BitReader(), reader);
}

static BossPresentation Boss(string key, params AssociatedNpcInstance[] instances)
    => new() { Key = key, Name = key, AssociatedNpcInstances = [.. instances] };

static void Arm(params BossPresentation[] bosses) => ClientRuntime.OnEncounterArmed(1, bosses);

static void Hit(NPC npc, int weapon, int damage)
    => ClientRuntime.RecordPrivateDamage(npc, new(DamageRootKind.Item, weapon),
        new(DamageLeafKind.ItemBody, weapon), damage);

static SourceTreeSnapshot Finish(BossPresentation boss, long damage)
{
    ClientRuntime.OnPublicResult(new PublicResultSnapshot
    {
        EncounterId = 1,
        Bosses = [boss],
        TeamDamage = damage,
        Outcome = EncounterOutcome.Victory
    }, damage);
    return ClientResultHistory.Latest!.PrivateSources;
}

static void AssertTree(SourceTreeSnapshot actual, params (int Weapon, long Damage)[] expected)
{
    Equal(expected.Sum(row => row.Damage), actual.Roots.Sum(root => root.Damage));
    Equal(expected.Length, actual.Roots.Count);
    foreach ((int weapon, long damage) in expected)
        Equal(damage, actual.Roots.Single(root => root.Key == new DamageRootKey(DamageRootKind.Item, weapon)).Damage);
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"Expected {expected}; received {actual}.");
}
