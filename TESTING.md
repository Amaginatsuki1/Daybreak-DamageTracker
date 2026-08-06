# Test checklist

Install the same `.tmod` on a tModLoader 1.4.4 server and every client. Start with the default server configuration and use two distinct client connections for privacy and ranking checks when possible.

## Build and load

1. Build in Release mode with no compiler errors or warnings.
2. Confirm the package contains the expected metadata, localization, icon, and source files, but no build directories, local artwork sources, or release artifacts.
3. Load the mod on an isolated dedicated server, both with and without Boss Checklist.
4. Confirm the server reaches world selection or loads a test world without errors from Daybreak DamageTracker.
5. Start an actual client with no existing client-config singleton/HUD state. Confirm `ClientConfig.OnChanged`
   completes during autoload, Daybreak remains enabled, and the client reaches the main menu without a
   `ClientRuntime.get_HistoryCapacity` or configuration-registration exception.

## Encounter and result

1. Spawn a vanilla boss. Confirm the encounter arms but its duration does not begin until the first valid player hit.
2. Damage the boss plus configured Add/Barrier NPCs. The boss result's team total and ranking should
   include all associated targets, while its body subtotal includes only configured body forms.
   Damage an unrelated or ambiguously shared NPC and confirm it is not guessed into either Boss.
3. Finish the fight. Confirm the panel shows the enabled sections and only the local player's detailed source tree.
4. Close the automatic panel with X, Escape, or `/dt close`. Confirm `/dt`, `/dt1` through the configured limit, `/dt list`, and `/dt chat` open the expected relative records without `#0`-style labels.
5. Test victory, party wipe, disconnect/escape, and `dtserver finish` independently.
6. After a wipe or disengagement, spawn an unrelated boss. Confirm the old result closes first and
   the new boss gets a fresh timer, boss list, and damage totals.
7. Spawn two or more bosses with overlapping lifetimes. Confirm every boss has an independent
   team total, body subtotal, duration, ranking, private source tree, portrait/name, and outcome.
8. Kill one simultaneous boss while another remains active. Confirm the killed boss immediately
   produces its own victory result and the surviving boss continues with its own ledger.
9. While the first result panel is still open, let the second boss despawn. Confirm its escaped
   result is appended below the first in the same scrollable panel instead of replacing it.
10. Configure two sequential waves with one `EncounterGroupKey`. Confirm the first wave does not
   produce victory, an NPC-free intermission remains pending, and only `FinalNpcTypes` ends the group.
11. With `ContinueAfterPartyWipe` enabled, wipe between waves, respawn, and re-engage. Confirm the
   encounter resumes; without it, confirm the inactive encounter resolves as defeat.
12. Hit the next boss/wave immediately as it appears after an NPC-free gap. Confirm its first hit
    is assigned to that encounter's private source rather than the old result or `Unclassified`.

## Source tree and layout

1. Confirm an item source shows its total, weapon-body child, and the configured number of projectile kinds.
2. Expand and collapse the omitted-projectile row. Separately fold and reopen the complete source branch.
3. Hold a different weapon while an accessory projectile deals damage; it should remain under the accessory when the spawning mod supplies an item-bearing source.
4. Exceed `SourceRootRows`, expand the omitted-source row, and confirm every expanded source retains its own branch and projectile controls.
5. On a short viewport or long result, confirm the panel stays on screen, the mouse wheel reaches all content, the scroll indicator moves, and the close button remains fixed.
6. Any click, source toggle, or scroll interaction on an automatic result should reset the full
   automatic-display timer; it should still disappear after the refreshed interval.

## Multiplayer privacy and reconnects

1. Confirm each player receives the public ranking and only their own source tree.
2. Join after an older encounter. Public history may load, but another connection's private source total or tree must never be substituted.
3. Set `Recipients` to `Contributors`; a non-contributor should receive neither the live result nor that encounter through history sync.
4. Reconnect repeatedly, including reuse of the same player slot. Each connection should receive presentation/history promptly, without stale request throttles.
5. Deal damage, disconnect, reconnect with the same character, and deal damage again. Confirm the
   public ranking contains one row with the combined total while the private tree reconciles only
   the current connection's damage.
6. Keep one same-name connection active while a second connection joins (where the server permits
   it). Confirm overlapping or ambiguous identities are not merged.

## Configuration and localization

1. While a result is open, change server presentation settings and run `dtserver reload`; the panel should reflow immediately. Toggle the local `AutoShowResults` setting off and confirm a hidden panel no longer opens automatically, while chat and `/dt` history continue to work.
2. Change the local history count to 1, 4, and 10. Confirm exactly that many records are listed and
   addressable as `/dt`, `/dt1`, and so on, up to `/dt9`; reducing the limit drops older local entries.
3. Add enough Boss results to exceed the selected limit. After each addition, confirm the single
   compact chat message shows only `Boss name | outcome | command`, with no `#0` prefix and the oldest
   excess record dropped.
4. Move the panel-background opacity slider through 0%, an intermediate value, and 100% while a
   result is open. Only the dark background should change; text, portraits, borders, and buttons stay visible.
5. Set the local automatic-panel duration to 1, 12, and 120 seconds. Confirm only automatically
   opened panels close on that schedule, interaction restarts the full selected duration, changing
   the setting while the panel is open restarts it immediately, and `/dt` panels remain open.
6. Join with English and Simplified Chinese clients. Boss Checklist names should resolve independently in the result panel, `/dt list`, and chat summary.
7. Test an `npc.boss` fallback and a server override with `NameLocalizationKey` plus a literal `NameOverride` fallback.
8. For a boss with NPC-free cinematics or nonstandard final forms, verify every transition, final death, wipe, and despawn path with an explicit override.

## Supported attribution boundary

Ordinary item and projectile hits are the supported attribution path. Damage-over-time effects and mod code that directly changes `npc.life` may not expose a trustworthy source in Terraria 1.4.4 and should not be assigned by guesswork.
