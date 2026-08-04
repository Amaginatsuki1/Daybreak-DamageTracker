# Test checklist

Install the same `.tmod` on a tModLoader 1.4.4 server and every client. Start with the default server configuration and use two distinct client connections for privacy and ranking checks when possible.

## Build and load

1. Build in Release mode with no compiler errors or warnings.
2. Confirm the package contains the expected metadata, localization, icon, and source files, but no build directories, local artwork sources, or release artifacts.
3. Load the mod on an isolated dedicated server, both with and without Boss Checklist.
4. Confirm the server reaches world selection or loads a test world without errors from Daybreak DamageTracker.

## Encounter and result

1. Spawn a vanilla boss. Confirm the encounter arms but its duration does not begin until the first valid player hit.
2. Damage the boss and a hostile non-boss NPC. Team totals and rankings should include both; the boss-body subtotal should include only effective damage to configured body forms.
3. Finish the fight. Confirm the panel shows the enabled sections and only the local player's detailed source tree.
4. Close the automatic panel with X, Escape, or `/dt close`. Confirm `/dt`, `/dt list`, `/dt 2`, and `/dt chat` still work.
5. Test victory, party wipe, disconnect/escape, and `dtserver finish` independently.

## Source tree and layout

1. Confirm an item source shows its total, weapon-body child, and the configured number of projectile kinds.
2. Expand and collapse the omitted-projectile row. Separately fold and reopen the complete source branch.
3. Hold a different weapon while an accessory projectile deals damage; it should remain under the accessory when the spawning mod supplies an item-bearing source.
4. Exceed `SourceRootRows`, expand the omitted-source row, and confirm every expanded source retains its own branch and projectile controls.
5. On a short viewport or long result, confirm the panel stays on screen, the mouse wheel reaches all content, the scroll indicator moves, and the close button remains fixed.
6. Any click or scroll interaction on an automatic result should keep it open for manual inspection.

## Multiplayer privacy and reconnects

1. Confirm each player receives the public ranking and only their own source tree.
2. Join after an older encounter. Public history may load, but another connection's private source total or tree must never be substituted.
3. Set `Recipients` to `Contributors`; a non-contributor should receive neither the live result nor that encounter through history sync.
4. Reconnect repeatedly, including reuse of the same player slot. Each connection should receive presentation/history promptly, without stale request throttles.

## Configuration and localization

1. While a result is open, change presentation settings and run `dtserver reload`. The panel should reflow immediately; disabling `AutoShow` should close an automatic panel but not a manually opened history panel.
2. Join with English and Simplified Chinese clients. Boss Checklist names should resolve independently in the result panel, `/dt list`, and chat summary.
3. Test an `npc.boss` fallback and a server override with `NameLocalizationKey` plus a literal `NameOverride` fallback.
4. For a boss with NPC-free cinematics or nonstandard final forms, verify every transition, final death, wipe, and despawn path with an explicit override.

## Supported attribution boundary

Ordinary item and projectile hits are the supported attribution path. Damage-over-time effects and mod code that directly changes `npc.life` may not expose a trustworthy source in Terraria 1.4.4 and should not be assigned by guesswork.
