# Server configuration

Daybreak now uses tModLoader's native in-game configuration pages:

- `Daybreak DamageTracker — Client Settings` contains only local panel behavior and appearance.
- `Daybreak DamageTracker — Server Settings` contains the server-authoritative presentation,
  delivery, lifecycle, and advanced Boss override settings.

The native server file is:

`<tModLoader SavePath>/ModConfigs/DaybreakDamageTracker_ServerConfig.json`

When that file does not yet exist, version 0.2.0 imports the old
`DaybreakDamageTracker.Server.json` once, saves the native file, and retains the old file as a
rollback reference. After migration, only the native underscore-named file is authoritative.

On a Linux dedicated server, the account running tModLoader must own or have write access to the
`ModConfigs` directory. A typical private service directory is owner `terraria:terraria`, mode
`0750`; the generated JSON can remain mode `0640`.

The server sends the effective content/recipient snapshot. Client ModConfig preferences only control
whether a hidden result panel opens automatically (`AutoShowResults`), how many of the most recent
results are kept locally (`HistoryCount`, 1–10), the dark background opacity
(`PanelBackgroundOpacity`, 0–1, default `0.90`), and the automatic panel lifetime from 1–120 seconds
(`AutoHideSeconds`). Result sections, chat summary, and row counts exist only in Server Settings;
clients cannot override that display policy or inspect another player's private source tree. Clients
may only reorder sections already authorized by the server; that order remains local and is never
sent over the network.

In host-and-play, the host may save the Server Settings page directly. On a dedicated server,
remote clients can open and inspect the page but saves are rejected by default. A server operator
may temporarily authorize one current connection from the dedicated-server console with
`dtserver config grant <slot>`, then revoke it with `dtserver config revoke <slot>` or clear every
grant with `dtserver config clear`. Grants are tied to the exact connection generation and are
removed on disconnect, world unload, or restart; names and client-supplied claims are never used as
authorization. Use the server console's active-player list to identify the numeric slot.

Useful fields:

- No panel timing or appearance field is server-controlled. Automatic opening and total lifetime
  use the local `AutoShowResults` and `AutoHideSeconds` preferences; the short closing fade is a
  fixed client animation. Legacy `Presentation.AutoShow`, `AutoShowSeconds`, and `FadeSeconds`
  remain readable only for schema/wire compatibility and are not applied as server policy.
- Both native pages embed the real result-panel renderer. Client Settings previews the active
  server policy with that page's unsaved local opacity and behavior summary. Server Settings
  previews its unsaved section and row policy directly; clients cannot override those choices.
- per-section `Show...` switches. `ShowFinalBlow` controls the per-boss "Final blow" line: the
  direct hitter, or the dominant damage-over-time owner when a debuff tick ends the boss;
  kills that cannot be attributed to a player show no line.
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

Native server configs are kept below tModLoader's single-packet limit. The current safe bounds are
32 overrides; 80 characters for `BossKey` and literal `NameOverride`; 160 characters for other text
fields and each NPC type value; 64 type values per role, 128 per override, 256 across the config;
8,192 UTF-16 code units across all override text; and 48 KiB for the final compact UTF-8 JSON.
Over-limit remote changes are rejected before they can be applied. Trusted legacy/disk input is
trimmed in stable list order (including duplicate post-trim Boss keys), then rejected without
changing the active snapshot if it still cannot fit.

Config packet callbacks only publish a deep runtime snapshot to a latest-value mailbox. Applying
Boss adapters and broadcasting presentation always happens from `PostUpdateWorld`; a config save
therefore never enumerates or mutates an active encounter from tModLoader's network thread.

Schema 2 added `EncounterGroupKey` and `ContinueAfterPartyWipe`. Schema 3 added the optional
`ControllerNpcTypes` lifecycle role. Schema 4 is the native tModLoader configuration layout used
from 0.2.0. Older legacy files remain importable; omitted fields keep ordinary single-boss behavior.

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
- NPCs spawned through a standard tModLoader NPC-parent source are assigned automatically when the
  parent belongs to exactly one active Boss. Explicit override ownership wins, and ambiguous or
  unrelated targets remain unassigned rather than being guessed.
- `KeepAliveNpcTypes` says which forms keep the encounter active through transitions.
- `ControllerNpcTypes` identifies independently rooted Boss/control NPC types whose `OnKill` may
  become a repeat-clear victory candidate. Do not list Body/Tail segments that point to another
  NPC through `realLife`. Boss Checklist entries seed this automatically, and additional live
  roots are learned after NPC initialization; unusual custom bosses can set it explicitly.
- `FinalNpcTypes` is a strong victory signal when one of those forms receives `OnKill`. It is a
  lifecycle role, not an implicit damage role: list the true terminal root/controller here, and
  also list it in `BodyNpcTypes` (and normally `KeepAliveNpcTypes`) when its damage and verified
  final blow should be attributed to this Boss.
- `ContinueAfterPartyWipe` keeps a configured encounter group pending while players respawn.
  The native setting is `Inherit` by default; choose `Enabled` to opt in. Victory still wins a
  same-tick race with a party wipe.
- With native `FinishOnBodyDeathWhenNoParticipants` set to `Enabled`, repeat clears use a
  root/controller `OnKill`
  candidate: a surviving or replacement controller cancels it, while dependent body/tail cleanup
  may disappear without `OnKill`. Set it to `Disabled` for a boss with intentional NPC-free scenes,
  and provide a reliable `FinalNpcTypes` or Boss Checklist `downed` signal. `Inherit` preserves the
  automatically discovered Boss policy.

The native Schema 4 file serializes these two three-state options as the strings `"Inherit"`,
`"Enabled"`, or `"Disabled"`. Boolean `true`/`false` is accepted only in the legacy
`DaybreakDamageTracker.Server.json` schema during one-time migration; do not copy legacy booleans
into `DaybreakDamageTracker_ServerConfig.json`.

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
  "ControllerNpcTypes": ["SomeMod/SomeBossPhase1", "SomeMod/SomeBossFinal"],
  "AddNpcTypes": ["SomeMod/SomeBossMinion"],
  "BarrierNpcTypes": ["SomeMod/SomeBossShield"],
  "KeepAliveNpcTypes": ["SomeMod/SomeBossIntro", "SomeMod/SomeBossPhase1", "SomeMod/SomeBossFinal"],
  "FinalNpcTypes": ["SomeMod/SomeBossFinal"],
  "FinishOnBodyDeathWhenNoParticipants": "Disabled"
}
```

Simultaneous bosses with different keys keep fully separate damage ledgers, durations, rankings,
private source trees, and outcomes. Each result is delivered as soon as that boss resolves; for
example, a killed boss can produce `Victory` while a sibling remains active, and that sibling can
later produce `Escaped`. If a result panel is already open, the next result is appended below it.
A scripted
NPC-free phase that can outlive the normal synchronization window should set
`FinishOnBodyDeathWhenNoParticipants` to `Disabled` and provide a reliable `FinalNpcTypes`, downed
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
    "ContinueAfterPartyWipe": "Enabled",
    "FinishOnBodyDeathWhenNoParticipants": "Disabled"
  },
  {
    "BossKey": "SomeMod:WaveTwo",
    "EncounterGroupKey": "SomeMod:Trial",
    "TriggerNpcTypes": ["SomeMod/WaveTwoBoss"],
    "BodyNpcTypes": ["SomeMod/WaveTwoBoss"],
    "FinalNpcTypes": ["SomeMod/WaveTwoBoss"],
    "ContinueAfterPartyWipe": "Enabled",
    "FinishOnBodyDeathWhenNoParticipants": "Disabled"
  }
]
```

An individual wave's Boss Checklist `downed` flag and generic body-death fallback do not finish a
grouped gauntlet. Configure `FinalNpcTypes` on the true final wave. `KeepAliveNpcTypes` can include
an invisible controller or intermission NPC when the encounter has one.

On a dedicated server console:

- `dtserver reload`
- `dtserver status`
- `dtserver config grant <slot>`
- `dtserver config revoke <slot>`
- `dtserver config clear`
- `dtserver finish victory|defeat|escaped|manual`

`dtserver reload` reloads the native underscore-named file. Presentation settings apply and
broadcast immediately while idle. If any Boss encounter is armed or active, the complete new
runtime snapshot is deferred until that encounter closes, so lifecycle roles, recipients, and
display settings cannot split one fight across two configurations. Existing encounter state is
never retroactively rebuilt.

Lifecycle logs contain encounter IDs, Boss/group keys, state changes, counts, and outcomes. They do
not write player names or per-hit messages.

Expanding/collapsing the omitted-source list or projectile rows, folding an entire source branch,
and scrolling a result are client-side viewing interactions, not configuration choices. They do
not change the server-selected compact limits, sections, recipients, damage data, or privacy
boundary.
