# Changelog

Notable changes are documented here. Versions follow the mod version in `build.txt`.

## 0.1.7

- Relicenses the source code and project documentation from MIT to GPL-3.0-only. Earlier published
  releases keep the license shipped with them; the mod icon and Workshop artwork remain excluded.
- Fixed ordinary bosses disappearing without `OnKill` while a living player was still nearby,
  which previously left the encounter pending and produced no Daybreak result panel.
- Keeps explicit NPC-free cinematics and grouped gauntlets on their configured wait path instead
  of treating every temporary disappearance as an escape.
- Measures damage-over-time from the authoritative life loss applied by
  `NPC.UpdateNPC_BuffApplyDOTs`, including the final lethal tick.
- Captures melee flasks and item/mod hit callbacks inside the real
  `Player.ProcessHitAgainstNPC` application window instead of attaching after vanilla had already
  applied its debuffs.
- Attributes supported vanilla DoT debuffs by duration segment, so refreshing an existing debuff
  owns only the added tail, an early cleanse cannot leave stale ownership, and a reconnect/player-
  slot reuse cannot steal an older effect.
- Attributes stacked Bone Javelin, Tentacle Spike, Blood Butcherer, Daybreak, and Stardust Cell DoT
  through their live projectile instances while preserving the original connection and source.
- Counts unsupported or ownerless DoT in team/body totals as unattributed server-side damage rather
  than guessing a player.
- Adds owner-only DoT source details and localized debuff rows to the private source tree.
- Freezes private source attribution to the uniquely matched Boss at hit time, so a target that was
  ambiguous during a simultaneous fight cannot move into another Boss's private tree later.
- Lets explicit final-form kills and first-clear downed signals finish through lingering controllers
  or adds, without reopening and republishing an already resolved Boss result.
- Allows the same Boss key to start a fresh ledger only after it has been fully absent for an
  authoritative scan, so a second summon during a surviving simultaneous Boss is neither omitted
  nor confused with a dying controller. Each occurrence keeps a distinct history/panel identity;
  the public result wire format is now version 6.
- Adds owner-aware Soul Drain to the supported vanilla DoT set.
- Replaces unbounded per-hit-point DoT distribution with exact cycle skipping and a bounded fallback
  for pathological modded regeneration values.
- Extends deterministic lifecycle/source/DoT allocation coverage to 59 logic tests.

## 0.1.6

- Fixes a client load failure caused by applying local preferences before tModLoader finished registering the client config and HUD.
- Fixed stale encounters merging unrelated bosses and inflating duration/damage after a wipe or disengagement.
- Added encounter-group lifecycle support for sequential gauntlets without breaking simultaneous multi-boss fights.
- Treats a party wipe as a lifecycle candidate, with victory and configured gauntlet continuation taking priority.
- Stops accounting during confirmed NPC-free transitions, while buffering the first client hit
  until the server identifies it as a resumed wave or a new encounter.
- Merges a non-overlapping reconnect into one public ranking row while keeping private source reconciliation scoped to the exact connection.
- Added low-frequency, name-free lifecycle diagnostics and deterministic logic regression tests.
- Gives every Boss an independent damage ledger, body subtotal, duration, ranking, owner-only source tree, and result.
- Publishes a Boss result immediately when that Boss ends; simultaneous survivors no longer block it.
- Appends later results to an already open scrollable panel and resets (rather than disables) the automatic close timer after interaction.
- Adds a client-side 1–10 result-history setting with `/dt` through `/dt9` as needed; the server retains a ten-result public sync window.
- Simplifies each chat mapping to Boss name, outcome, and command without `#0`-style labels.
- Adds a live client-side slider for only the result panel's dark background opacity.
- Adds a client-side 1–120 second lifetime setting for automatic panels; manually opened `/dt` panels remain persistent.
- Adds a local, default-on automatic-popup preference without changing server-controlled result content or privacy.

## 0.1.5

- Added per-source and per-projectile expand/collapse controls.
- Added viewport-bounded panel height, mouse-wheel scrolling, a scroll indicator, and a fixed close button.
- Added expansion and collapse controls for source roots beyond the server-selected compact limit.
- Kept an automatic result open after the player interacts with it.

## 0.1.4

- Added client-side boss-name localization through Boss Checklist localization keys or representative NPC types.
- Added `NameLocalizationKey` for server boss overrides.
- Updated the public result packet format; servers and clients must use the same mod version.

## 0.1.3

- Updated author metadata and artwork.

## 0.1.2

- Completed the rename to Daybreak DamageTracker.
- Set the internal mod ID and assembly name to `DaybreakDamageTracker`.
- Added `/dt`, `dtserver`, and `DaybreakDamageTracker.Server.json` as the stable command and configuration names.

## 0.1.1

- Applied presentation reloads to a result panel that is already open.
- Cleared connection-slot request throttles on disconnect.

## 0.1.0

- Added server-side encounter tracking and effective-damage attribution.
- Added team rankings, boss-body damage, private source trees, recent history, and the result HUD.
- Added optional Boss Checklist integration and configurable boss lifecycle overrides.
