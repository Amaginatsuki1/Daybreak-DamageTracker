# Server configuration

The mod creates this file when a server or single-player world is first loaded:

`<tModLoader SavePath>/ModConfigs/DaybreakDamageTracker.Server.json`

On a Linux dedicated server, the account running tModLoader must own or have write access to the
`ModConfigs` directory. A typical private service directory is owner `terraria:terraria`, mode
`0750`; the generated JSON can remain mode `0640`.

The server sends the effective content/recipient snapshot. Client ModConfig preferences only control
whether a hidden result panel opens automatically (`AutoShowResults`), how many of the most recent
results are kept locally (`HistoryCount`, 1–10), and the dark background opacity
(`PanelBackgroundOpacity`, 0–1). `AutoHideSeconds` controls the automatic panel lifetime from
1–120 seconds. They cannot enable server-disabled sections or reveal another player's private
source tree.

Useful fields:

- `FadeSeconds` remains server-controlled. The legacy `AutoShowSeconds` and
  `Presentation.AutoShow` JSON fields remain accepted for schema/wire compatibility; automatic
  opening and its total lifetime now use the local `AutoShowResults` and `AutoHideSeconds`
  preferences.
- per-section `Show...` switches
- `RankingRows`, `SourceRootRows`, `SourceLeafRows`; `SourceRootRows` is the compact number of
  individual weapon/source roots shown before the clickable `Other N sources` aggregate.
  `SourceLeafRows` is the compact number of individual projectile kinds shown under each source
  before `Other N projectiles`. The weapon-body row is separate and does not consume the leaf
  limit. DoT debuff rows are separate details and do not reduce the configured projectile-kind
  limit. The defaults are `6` roots and `3` projectile kinds.
- `ChatSummary`; the server retains and can sync up to ten per-boss public results. The legacy server
  `Presentation.HistoryCount` field remains accepted but is sanitized to ten; each client applies its
  own 1–10 local history preference and exposes `/dt`, then `/dt1` through `/dt9` as needed.
- `Recipients`: `AllActivePlayers` or `Contributors`
- `GenericDespawnFallbackSeconds`: `0` disables guessed no-NPC timeout (default)
- `BossOverrides`: lifecycle and presentation adapters for unusual mod bosses

Schema 2 adds `EncounterGroupKey` and `ContinueAfterPartyWipe`. Existing schema-1 files remain
valid; omitted fields keep ordinary single-boss behavior.

Boss names discovered through Boss Checklist or `npc.boss` are localized independently by each
client. For a server override, `NameOverride` is a fixed literal fallback. Set
`NameLocalizationKey` when the target mod exposes a suitable localization key; clients resolve
that key in their own language, and fall back to `NameOverride` if the key is unavailable.

NPC type values in overrides accept either a numeric runtime ID or `ModInternalName/NpcInternalName`. Because vanilla internal-name lookup is not stable through this weak integration, use numeric IDs for vanilla overrides.

Override roles:

- `EncounterGroupKey` relates separately named Boss Checklist entries/typed adapters that are
  sequential waves of one encounter. `BossKey` still controls each wave's identity and name.
- `TriggerNpcTypes` and `BodyNpcTypes` can arm an encounter. A Body match also lets the mod recover when an intro form transformed before the first scan.
- `BodyNpcTypes` is the boss-body subtotal. If omitted, all Trigger types except Add/Barrier types are treated as the body.
- `AddNpcTypes` and `BarrierNpcTypes` are excluded only from the boss-body subtotal. Their valid player damage still counts toward team total, ranking, and the player's private source tree.
- `KeepAliveNpcTypes` says which forms keep the encounter active through transitions.
- `FinalNpcTypes` is a strong victory signal when one of those forms receives `OnKill`.
- `ContinueAfterPartyWipe` keeps a configured encounter group pending while players respawn.
  It is `false` by default. Victory still wins a same-tick race with a party wipe.
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

Simultaneous bosses with different keys keep fully separate damage ledgers, durations, rankings,
private source trees, and outcomes. Each result is delivered as soon as that boss resolves; for
example, a killed boss can produce `Victory` while a sibling remains active, and that sibling can
later produce `Escaped`. If a result panel is already open, the next result is appended below it.
A scripted
NPC-free phase that can outlive the normal synchronization window should set
`FinishOnBodyDeathWhenNoParticipants` to `false` and provide a reliable `FinalNpcTypes`, downed
signal, or controller in `KeepAliveNpcTypes`. That opt-out prevents a completed sibling from
turning the scripted transition into an escape result.

For a sequential gauntlet with separately named waves, use one override per wave, keep each
`BossKey` distinct, and share one group key. The group policy and lifecycle NPC sets are combined
before later waves spawn:

```json
[
  {
    "BossKey": "SomeMod:WaveOne",
    "EncounterGroupKey": "SomeMod:Trial",
    "TriggerNpcTypes": ["SomeMod/WaveOneBoss"],
    "BodyNpcTypes": ["SomeMod/WaveOneBoss"],
    "ContinueAfterPartyWipe": true,
    "FinishOnBodyDeathWhenNoParticipants": false
  },
  {
    "BossKey": "SomeMod:WaveTwo",
    "EncounterGroupKey": "SomeMod:Trial",
    "TriggerNpcTypes": ["SomeMod/WaveTwoBoss"],
    "BodyNpcTypes": ["SomeMod/WaveTwoBoss"],
    "FinalNpcTypes": ["SomeMod/WaveTwoBoss"],
    "ContinueAfterPartyWipe": true,
    "FinishOnBodyDeathWhenNoParticipants": false
  }
]
```

An individual wave's Boss Checklist `downed` flag and generic body-death fallback do not finish a
grouped gauntlet. Configure `FinalNpcTypes` on the true final wave. `KeepAliveNpcTypes` can include
an invisible controller or intermission NPC when the encounter has one.

On a dedicated server console:

- `dtserver reload`
- `dtserver status`
- `dtserver finish victory|defeat|escaped|manual`

`dtserver reload` also broadcasts the new presentation settings. Existing encounter state is
not retroactively rebuilt, so lifecycle-role edits are safest between boss fights.

Lifecycle logs contain encounter IDs, Boss/group keys, state changes, counts, and outcomes. They do
not write player names or per-hit messages.

Expanding/collapsing the omitted-source list or projectile rows, folding an entire source branch,
and scrolling a result are client-side viewing interactions, not configuration choices. They do
not change the server-selected compact limits, sections, recipients, damage data, or privacy
boundary.
