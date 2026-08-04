# Server configuration

The mod creates this file when a server or single-player world is first loaded:

`<tModLoader SavePath>/ModConfigs/DaybreakDamageTracker.Server.json`

On a Linux dedicated server, the account running tModLoader must own or have write access to the
`ModConfigs` directory. A typical private service directory is owner `terraria:terraria`, mode
`0750`; the generated JSON can remain mode `0640`.

Clients do not get a ModConfig page. The server sends only the effective presentation snapshot.

Useful fields:

- `Presentation.AutoShow`, `AutoShowSeconds`, `FadeSeconds`
- per-section `Show...` switches
- `RankingRows`, `SourceRootRows`, `SourceLeafRows`; `SourceRootRows` is the compact number of
  individual weapon/source roots shown before the clickable `Other N sources` aggregate.
  `SourceLeafRows` is the compact number of individual projectile kinds shown under each source
  before `Other N projectiles`. The weapon-body row is separate and does not consume the leaf
  limit. The defaults are `6` roots and `3` projectile kinds.
- `HistoryCount` and `ChatSummary`
- `Recipients`: `AllActivePlayers` or `Contributors`
- `GenericDespawnFallbackSeconds`: `0` disables guessed no-NPC timeout (default)
- `BossOverrides`: lifecycle and presentation adapters for unusual mod bosses

Boss names discovered through Boss Checklist or `npc.boss` are localized independently by each
client. For a server override, `NameOverride` is a fixed literal fallback. Set
`NameLocalizationKey` when the target mod exposes a suitable localization key; clients resolve
that key in their own language, and fall back to `NameOverride` if the key is unavailable.

NPC type values in overrides accept either a numeric runtime ID or `ModInternalName/NpcInternalName`. Because vanilla internal-name lookup is not stable through this weak integration, use numeric IDs for vanilla overrides.

Override roles:

- `TriggerNpcTypes` and `BodyNpcTypes` can arm an encounter. A Body match also lets the mod recover when an intro form transformed before the first scan.
- `BodyNpcTypes` is the boss-body subtotal. If omitted, all Trigger types except Add/Barrier types are treated as the body.
- `AddNpcTypes` and `BarrierNpcTypes` are excluded only from the boss-body subtotal. Their valid player damage still counts toward team total, ranking, and the player's private source tree.
- `KeepAliveNpcTypes` says which forms keep the encounter active through transitions.
- `FinalNpcTypes` is a strong victory signal when one of those forms receives `OnKill`.
- Set `FinishOnBodyDeathWhenNoParticipants` to `false` for a boss with intentional NPC-free scenes, and provide a reliable `FinalNpcTypes` or Boss Checklist `downed` signal.

At least `BossKey` or one Trigger type is required for an override to be retained. A custom
BossKey with Body types can also arm directly; for a standalone custom adapter, supplying both
Trigger and Body is the clearest configuration.

Example:

```json
{
  "BossKey": "SomeMod:SomeBoss",
  "NameLocalizationKey": "Mods.SomeMod.NPCs.SomeBoss.DisplayName",
  "NameOverride": "Some Boss",
  "PortraitNpcType": "SomeMod/SomeBossFinal",
  "TriggerNpcTypes": ["SomeMod/SomeBossIntro"],
  "BodyNpcTypes": ["SomeMod/SomeBossPhase1", "SomeMod/SomeBossFinal"],
  "AddNpcTypes": ["SomeMod/SomeBossMinion"],
  "BarrierNpcTypes": ["SomeMod/SomeBossShield"],
  "KeepAliveNpcTypes": ["SomeMod/SomeBossIntro", "SomeMod/SomeBossPhase1", "SomeMod/SomeBossFinal"],
  "FinalNpcTypes": ["SomeMod/SomeBossFinal"],
  "FinishOnBodyDeathWhenNoParticipants": false
}
```

On a dedicated server console:

- `dtserver reload`
- `dtserver status`
- `dtserver finish victory|defeat|escaped|manual`

`dtserver reload` also broadcasts the new presentation settings. Existing encounter state is
not retroactively rebuilt, so lifecycle-role edits are safest between boss fights.

Expanding/collapsing the omitted-source list or projectile rows, folding an entire source branch,
and scrolling a result are client-side viewing interactions, not configuration choices. They do
not change the server-selected compact limits, sections, recipients, damage data, or privacy
boundary.
