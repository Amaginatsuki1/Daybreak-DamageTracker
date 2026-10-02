# Daybreak DamageTracker

[简体中文](README.zh-CN.md) · [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3776927292) · [GPL-3.0-only License](LICENSE)

Daybreak DamageTracker is a server-controlled boss damage summary mod for Terraria 1.4.4 and tModLoader. It records the effective damage accepted by the server, presents a team ranking, and keeps each player's detailed source breakdown private to that player.

This branch contains the **0.2.1 testing candidate**. Source updates do not publish the Workshop item.
Its network format requires identical versions on the server and every client; complete the
[focused acceptance checks](TESTING.md#021-focused-acceptance) before deploying it to a live server.

## Features

- Tracks an encounter from boss discovery through victory, defeat, escape, or a manual finish.
- Gives every boss independent team damage, body damage, duration, ranking, percentages, and owner-only source tree.
- Finalizes each simultaneous boss as soon as that boss ends, without waiting for surviving bosses.
- Shows who landed the final blow of each victory — the direct hitter, or the dominant
  damage-over-time owner when a debuff tick ends the boss. Unattributed kills show no line.
- Groups a player's damage by weapon, accessory, weapon body, and projectile type.
- Counts actual damage-over-time health loss and shows supported vanilla debuffs in the owner's private source tree.
- Supports independently expandable source/projectile branches in a viewport-bounded, scrollable
  result panel; opening one weapon no longer closes another weapon's projectile list.
- Lets each client keep 1–10 boss results: `/dt` for the latest, then `/dt1` through `/dt9` as needed.
- Lets each client choose a 1–120 second lifetime for automatically opened panels; `/dt` panels remain open until closed.
- Appends a newly resolved boss below any result panel that is already open, in the same scrollable viewport.
- Uses Boss Checklist when available and falls back to `npc.boss` for ordinary bosses.
- Folds boss-marked Body/Tail types omitted by Boss Checklist into their registered `realLife`
  controller instead of publishing duplicate encounters.
- Associates uniquely parent-spawned shields, summons, and combat illusions with their Boss for
  team damage and ranking by exact NPC instance, while keeping them out of the Boss-body subtotal.
  Unrelated same-type targets and independently summoned same-type bosses remain separate.
- Lets servers define lifecycle overrides for bosses with unusual phase transitions.
- Separates unrelated bosses after a wipe/disengagement while preserving simultaneous bosses and configured sequential gauntlets.
- Reports a victory and an escape independently, so either outcome can appear without being blocked by the other boss.
- Consolidates a non-overlapping reconnect into one public ranking row without widening private-source access.
- Resolves supported boss names in each client's selected language.

## Privacy model

The public result contains the team ranking. Detailed source data is collected on the owning client and is never included in public result or history packets. Server-created projectile provenance and server-confirmed DoT source increments are sent only to their owner.

The server exclusively controls result sections, row counts, and recipients; clients do not get
duplicate switches for them. Client-side preferences control automatic opening, its 1–120 second
duration, the 1–10 local history length, dark panel background opacity (default `0.90`), and the
order of sections the server has already authorized. They cannot alter server presentation policy
or widen the data received.

## Installation

Subscribe on the [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3776927292), or install the same `DaybreakDamageTracker.tmod` file on the dedicated server and every client. Mixed mod versions are not supported because the result packet format may change between releases.

The mod configuration menu exposes separate Client Settings and Server Settings pages. On first
load, tModLoader creates:

```text
<tModLoader SavePath>/ModConfigs/DaybreakDamageTracker_ServerConfig.json
```

Version 0.2.0 imports the old `DaybreakDamageTracker.Server.json` once when the native file is
absent and retains the old file as a rollback reference. A host-and-play host may edit Server
Settings directly. Remote dedicated-server clients are read-only by default and need a temporary,
connection-bound console grant; that grant expires on disconnect.

Both settings pages embed an interactive, internally scrollable live preview backed by the actual
result renderer. The server controls
content allowance, row limits, recipients, and Boss lifecycle only; automatic opening, panel
lifetime, local history, background opacity, and the ordering of authorized sections are client-only.
Each page labels defaults explicitly and provides a two-click Restore All Defaults action that still
requires Save.

See [SERVER_CONFIG.md](SERVER_CONFIG.md) for every setting and boss override field.

## Commands

Client chat commands:

```text
/dt
/dt1
/dt2 ... /dt9
/dt list
/dt chat
/dt close
```

History commands are relative: `/dt` is the latest result, `/dt1` is the previous result, and so on. The configured history count determines how many commands remain valid. A compact chat line refreshes the current `boss | outcome | command` mapping whenever a new result shifts those slots; it does not add `#0`-style labels.

Dedicated-server console commands:

```text
dtserver reload
dtserver status
dtserver config grant <player-slot>
dtserver config revoke <player-slot>
dtserver config clear
dtserver finish victory|defeat|escaped|manual
```

## Building from source

Requirements:

- .NET 8 SDK
- tModLoader 1.4.4

The easiest setup is to clone the repository into tModLoader's `ModSources` directory. The generated `tModLoader.targets` file in the parent directory is detected automatically.

For a checkout elsewhere, pass the installed `tMLMod.targets` path explicitly:

```powershell
dotnet build -c Release -p:TModLoaderTargets="D:\Steam\steamapps\common\tModLoader\tMLMod.targets"
```

```bash
dotnet build -c Release -p:TModLoaderTargets="$HOME/.local/share/Steam/steamapps/common/tModLoader/tMLMod.targets"
```

Use `-p:BuildMod=false` when you only want to compile the assembly without packaging or installing a `.tmod`.

## Compatibility boundaries

Terraria 1.4.4 accepts a hit's requested damage value from the client. The mod attributes the value returned by the server's `NPC.StrikeNPC` call, but it is not an anti-cheat system. Direct hits therefore use the same caliber as Terraria's own damage numbers and DPS-meter accessory: the final blow may include more damage than the target's remaining health. Damage over time, which the game applies as chunked health loss, is always counted as the health actually removed.

Ordinary item/projectile hits and the actual life loss from Terraria's DoT update are supported. Poisoned, On Fire, Cursed Inferno, Frostburn, Venom, Shadowflame, Hellfire, Frostbite, and Soul Drain retain a player owner when their application exposes one. Stacked Bone Javelin, Tentacle Spike, Blood Butcherer, Daybreak, and Stardust Cell effects are associated through their live projectile instances. A refresh owns only the newly extended duration, not the already-active interval. Reapplying a removed effect clears its old ownership even when the new application has no identifiable owner. Multiple supported effects divide each observed life-loss tick by their vanilla regeneration weights while preserving the exact total.

The team DoT total measures actual life loss, but individual shares are a vanilla-weight estimate when other mods change effect strength. Calamity weaknesses, resistances, worm reductions, and mixed unknown effects can change those proportions. Only negative regeneration exceeding the known weight sum can be separated as an unattributed remainder; a mod's simultaneous scaling can prevent that separation. An unidentified application itself stays unattributed. DoT player percentages and the dominant-owner final-blow label should not be treated as exact under such modifiers.

Unsupported modded DoT, Oiled-only bonus damage, Dryad's Bane, and ownerless/server-created effects still count in team and boss-body totals; the portion identifiable outside known effects is shown as unattributed server-side damage, subject to the scaling limitation above. Direct mod code that changes `npc.life` outside Terraria's DoT update remains outside the universal capture path. Accessory projectiles are grouped correctly when the spawning mod supplies an item-bearing entity source; opaque sources fall back to projectile type.

For ordinary repeat clears, segmented bodies are followed through their root/controller NPC. Boss
body membership and lifecycle-controller membership are separate, so Body/Tail spawn or direct
cleanup cannot impersonate an unresolved controller. A controller `OnKill` becomes final only after
no sibling or phase-successor controller replaces it; dependent body/tail cleanup does not have to
fire `OnKill` for every segment. Bosses with NPC-free
cinematics or nonstandard final forms may still need a server override. Boss-marked segments omitted
by Boss Checklist automatically follow a registered `realLife` controller. The generic no-NPC timeout
is disabled by default to avoid ending an encounter during a phase transition.

A phase may briefly expose predecessor and successor roots in the same authoritative scan. The
handoff is accepted only inside the short synchronization window and only after all old roots leave
while every surviving root belongs to the newly observed successor set; this does not excuse one
member escaping from a stable multi-controller fight.

Calamity compatibility uses controller-kill confirmation during Boss Rush and recognizes the
Zenith-world Desert Scourge to Aquatic Scourge transformation only when the authoritative NPC
parent source proves that handoff. Coincidental co-presence remains independent. This behavior was reviewed against
Calamity 2.2.4; future versions and custom implementations that expose no standard parent source,
Boss Checklist entry, `realLife`, or `OnKill`/downed signal may still require a server override.

Concurrent instances registered under exactly the same Boss key cannot be generically distinguished
as duplicate summons versus a legitimate pair, split-controller phase, or phase successor, so they
share one ledger. Simultaneous Bosses with different keys remain independent. Separating same-key
instances safely requires an upstream instance-group/phase-handoff signal or a dedicated adapter.

Sequential encounters whose waves use different Boss Checklist keys can share an `EncounterGroupKey`. A gauntlet that intentionally survives a full party wipe can additionally enable `ContinueAfterPartyWipe` and should declare a reliable final NPC.

## Contributing

Bug reports and focused pull requests are welcome. Please read [CONTRIBUTING.md](CONTRIBUTING.md) and use [TESTING.md](TESTING.md) for multiplayer and boss-lifecycle checks.

## License and acknowledgments

Beginning with version 0.1.7, the source code and project documentation are licensed under the [GNU General Public License v3.0 only](LICENSE) (`GPL-3.0-only`). Distributing a modified version based on a GPL-licensed revision requires preserving the GPL and making its Corresponding Source available under the same license. Earlier releases remain under the license shipped with each release.

See [LICENSE-SCOPE.md](LICENSE-SCOPE.md) and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for the exact scope, artwork exclusion, and projects or official examples that informed the implementation.
