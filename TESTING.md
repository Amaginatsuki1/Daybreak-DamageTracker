# Test checklist

## 0.2.1 focused acceptance

Use a disposable test world and identical 0.2.1 packages on both clients and the dedicated
server. Do not upgrade a production server merely to execute this checklist.

1. **Totals:** finish a fight containing both identified weapon damage and an unidentified
   private DoT source. Every displayed root total must sum to the local canonical total;
   an added missing remainder must preserve already recorded unidentified damage.
2. **NPC reuse:** during two simultaneous Boss fights, finish A while B remains. Reuse an
   A-add slot for a same-type B-add and damage it with a distinguishable weapon. B must retain
   those sources. Also damage a normal enemy before the same slot/type becomes a Boss add;
   the earlier normal-enemy damage must never appear in the Boss tree. Repeat in single player.
3. **Delayed identity/DoT:** exercise NPC creation, replacement, a lethal DoT tick and result
   publication with network latency. Sources must never borrow the new occupant's weapon or
   move across Boss occurrences. A source that cannot be verified must remain unidentified.
4. **Cleanse/reapply:** apply a supported debuff, remove it before expiry, then reapply it from
   an ownerless environment/mod path. New damage must not retain the old player's ownership or
   final-blow claim. Separately verify that an ordinary refresh retains the old interval and
   assigns only the new tail to the refresher.
5. **Regression:** repeat-clear Astrum Deus and Devourer of Gods; test victory, escape, wipe,
   simultaneous Boss settlement, and one Boss Rush. Confirm no missed or duplicate result.
6. **Privacy/config/UI:** use two different connections, reconnect into a reused player slot,
   verify owner-only source trees and config grants, then check both settings previews at high
   UI scale, long Boss names, source expansion, history commands and the close button.

Automated checks:

```powershell
dotnet run --project tests/DaybreakDamageTracker.LogicTests -c Release
dotnet run --project tests/DaybreakDamageTracker.ClientIdentityTests -c Release
pwsh -NoProfile -File tests/test-isolated-load-ownership.ps1
```

The process-ownership check is synthetic and never starts or stops a game. Compilation,
deterministic replay, and dedicated-server loading do not replace the multiplayer checks above.

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
   Only a verified Victory may show a final-blow row. Defeat, escape, force-finish,
   shield/add deaths, phase replacements, and ambiguous simultaneous controller deaths
   must leave it absent rather than guessing.
6. After a wipe or disengagement, spawn an unrelated boss. Confirm the old result closes first and
   the new boss gets a fresh timer, boss list, and damage totals.
7. Spawn two or more bosses with overlapping lifetimes. Confirm every boss has an independent
   team total, body subtotal, duration, ranking, private source tree, portrait/name, and outcome.
8. Kill one simultaneous boss while another remains active. Confirm the killed boss immediately
   produces its own victory result and the surviving boss continues with its own ledger. Repeat by
   letting one Boss genuinely despawn while the other remains active; the absent Boss must immediately
   publish `Escaped` (or `Defeat` on a same-tick party wipe) without closing the surviving Boss ledger.
9. While the first result panel is still open, let the second boss despawn. Confirm its escaped
   result is appended below the first in the same scrollable panel instead of replacing it.
10. Configure two sequential waves with one `EncounterGroupKey`. Confirm the first wave does not
   produce victory, an NPC-free intermission remains pending, and only `FinalNpcTypes` ends the group.
11. With `ContinueAfterPartyWipe` enabled, wipe between waves, respawn, and re-engage. Confirm the
   encounter resumes; without it, confirm the inactive encounter resolves as defeat.
12. Hit the next boss/wave immediately as it appears after an NPC-free gap. Confirm its first hit
    is assigned to that encounter's private source rather than the old result or `Unclassified`.
13. Damage an ordinary boss, then let it despawn while a living player remains near its last
    position. After the short synchronization grace, confirm an escaped result and panel appear
    without requiring a later boss or generic timeout. Repeat with all participants dead and
    confirm defeat. A configured NPC-free cinematic/gauntlet must continue waiting instead.
14. Keep one simultaneous Boss active, finish another Boss, wait until the finished Boss key has
    fully disappeared, then summon that same Boss again. Confirm the dying controller does not
    create a duplicate result, the second summon gets a zeroed ledger, and both occurrences remain
    independently addressable in the panel, chat history mapping, and `/dt` history.
15. Repeat-clear a split/merge segmented Boss such as Calamity's Astrum Deus with its downed flag
    already true. Exercise the single-body, split, and merged/final phases. Killing only one split
    controller must not publish; the final loot-producing kill must publish `Victory` even if
    Body/Tail segments deactivate without `OnKill`. Repeat by disengaging without a controller kill
    and confirm the result is `Escaped`. Keep a different Boss alive during the final test and
    confirm the Astrum Deus result still publishes independently. The authoritative log should
    show `accepted overlapping controller phase handoff` during the split, followed by an automatic
    controller-final candidate being armed and confirmed for the final Head. Body/Tail spawn or
    cleanup must not mark the controller epoch incomplete.
16. Fight Calamity's Devourer of Gods with Boss Checklist enabled. Confirm its boss-marked Body and
    Tail, which are omitted from that checklist entry, contribute to the one registered Boss result
    and never create additional same-name `npc:*` results or history slots.
17. Damage a Boss-created shield, summon, and illusion that use an NPC-parent entity source. Confirm
    their damage enters that Boss's team total, ranking, and private source tree but not its body
    subtotal. Spawn an unrelated NPC of both a different type and the same type, and confirm neither
    is assigned merely because one concrete instance came from the Boss.
18. Use a fight that creates a boss-marked illusion or a nested Boss Checklist entry from the parent
    NPC. Confirm it remains part of the parent encounter and does not publish a synthetic result.
    While it exists, independently summon the same child Boss and confirm only that independent
    instance retains its own result. Repeat with an explicit server adapter for the child and confirm
    the adapter remains independent.
19. Run Calamity Boss Rush on a world where a tested Boss's downed flag is still false. Confirm the
    final controller kill publishes Victory, while a split-phase controller kill with a surviving or
    successor controller does not publish early.
    In particular, reach Astrum Deus and confirm the encounter stays open through the single-to-split
    handoff, then publishes Victory when Calamity advances Boss Rush after one split Head dies. Repeat
    outside Boss Rush and confirm an un-killed Head despawn still resolves conservatively as Escaped.
20. In a Zenith world, fight Desert Scourge through its Aquatic Scourge transformation. Confirm both
    names retain separate damage rows, the intermediate direct deactivation does not publish escape,
    and the final kill closes both as Victory. Repeat with a real final despawn and confirm Escaped.
    Separately summon Aquatic Scourge while Desert Scourge is present without using the transformation;
    confirm the two encounters are not grouped merely because they overlap.

## Source tree and layout

1. Confirm an item source shows its total, weapon-body child, and the configured number of projectile kinds.
2. Expand the omitted-projectile rows for at least two weapons at the same time. Confirm both full
   projectile lists remain open independently; collapse either list without changing the other.
   Separately fold and reopen each complete source branch.
3. Hold a different weapon while an accessory projectile deals damage; it should remain under the accessory when the spawning mod supplies an item-bearing source.
4. Exceed `SourceRootRows`, expand the omitted-source row, and confirm every expanded source retains its own branch and projectile controls.
   While two branches are expanded, append a second Boss result to the open panel and confirm the
   first card keeps its independent expansion state and current scroll position.
5. On a short viewport or long result, confirm the panel stays on screen, the mouse wheel reaches all content, the scroll indicator moves, and the close button remains fixed.
6. Any click, source toggle, or scroll interaction on an automatic result should reset the full
   automatic-display timer; it should still disappear after the refreshed interval. Repeat with a
   result shorter than the viewport: its wheel gesture must refresh the timer and must not change
   the vanilla hotbar. At the top or bottom of a long real HUD, the panel still consumes the wheel;
   only the embedded settings preview may pass a boundary gesture back to the surrounding config list.
7. Apply Cursed Inferno with a Flask of Cursed Flames. Confirm its real health loss increases the
   player's ranking and boss-body/team totals, and the private tree shows the localized weapon
   imbue root with a `Cursed Inferno · damage over time` child.
8. Refresh the same DoT from a second player before it expires. Confirm the original remaining
   interval stays with the first player and only the added tail belongs to the second. Test two
   different supported DoTs together and confirm their shares still sum to the exact life loss.
9. Disconnect/reconnect or reuse the player slot while an owned DoT remains active. Confirm its
   public damage stays on the original logical player and private increments are never delivered
   to the new connection.
10. Cleanse or otherwise remove a supported debuff before its predicted expiry, then have another
    player reapply it. Confirm no unexpired ownership segment from the removed effect survives.
11. Test stacked Bone Javelin, Tentacle Spike, Blood Butcherer, Daybreak, and Stardust Cell effects.
    Confirm each actual life-loss tick remains exact and the private row follows the projectile's
    original player/source, including after that player disconnects from the public ranking.
12. During a simultaneous fight, hit a target type that matches more than one Boss adapter, then
    finish one Boss. Confirm that ambiguous atom never appears in either private source tree after
    the active Boss list shrinks.
13. Apply Soul Drain from a player-owned projectile. Confirm its actual life loss uses the player
    and original projectile/item source; an ownerless application must remain unattributed.

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
7. Set `RankingRows` to 1 with at least three contributors. Confirm the client receives/displays only
   the first named row plus one anonymous `Other N players` aggregate; older history sync must not
   reveal the omitted names even if the client changes its local layout.
8. Create enough owned DoT updates for the bounded private queue to span more than one drain. End the
   Boss while rows are still queued. Confirm every private DoT row arrives before the result, the
   result reconciles the complete local source tree, and `EncounterCompleted`/a newly armed encounter
   cannot overtake that result.

## Configuration and localization

1. Open Mod Configuration and confirm Daybreak exposes distinct localized Client Settings and
   Server Settings pages. On a dedicated server, confirm an ordinary remote client can view but
   cannot save Server Settings. Grant that exact slot with `dtserver config grant <slot>`, save one
   change successfully, disconnect/reconnect into the same slot, and confirm the old grant no longer
   authorizes the new connection. Host-and-play hosts should save without a console grant.
2. With only the legacy `DaybreakDamageTracker.Server.json` present, load the candidate once. Confirm the
   native `DaybreakDamageTracker_ServerConfig.json` preserves presentation, recipient, lifecycle,
   three-state Boss override values, and Boss type lists while the legacy file remains unchanged.
   On later loads, confirm the native file wins and legacy changes are no longer re-imported.
   In the native Schema 4 JSON, confirm both override modes serialize as `"Inherit"`, `"Enabled"`,
   or `"Disabled"`; legacy boolean values belong only to the old dot-named migration file.
3. While idle, change server presentation settings in-game and confirm the panel reflows immediately.
   During an active Boss fight, save a different server configuration and confirm `dtserver status`
   reports a pending apply; the current fight retains one consistent configuration and the new
   snapshot applies after it closes. Also edit the native file and run `dtserver reload`.
   Toggle the local `AutoShowResults` setting off and confirm a hidden panel no longer opens
   automatically, while chat and `/dt` history continue to work.
   On Client Settings, change automatic behavior, duration, history, and background opacity without
   saving; confirm the embedded sample summary/background update immediately and no result-section
   or row controls exist. On Server Settings, change section toggles and row limits without saving
   and confirm the embedded sample panel updates immediately; confirm there is no panel duration,
   fade, opacity, auto-open, or history control. Hover either preview and use the mouse wheel inside it to
   reach the ranking and complete source tree; expand/collapse source and projectile branches and
   confirm those sample interactions neither open nor extend the real result HUD. Change each row
   limit and confirm the compact rows update before saving.
4. With Calamity enabled, confirm both previews use Yharon's localized name and portrait. Repeat an
   isolated load without Calamity: both config pages must still open with the localized Yharon name
   fallback, no missing-content exception, and no hard Calamity dependency.
5. On Client Settings, move each of the six content sections with the arrow buttons and confirm the
   embedded panel reorders immediately. Save and open a real `/dt` result to confirm the same order.
   Disable one section in Server Settings and confirm it remains absent regardless of its local
   order. Boss identity, outcome, and final blow must remain fixed at the top. Then disable the Boss
   header while keeping final blow enabled; a confirmed victory must still show an independent
   `Boss name · final blow · method` row rather than silently hiding the enabled field.
6. Confirm every setting with a default displays that default in both English and Simplified
   Chinese. On each config page, click Restore All Defaults once and confirm the five-second pulsing
   warning without changing settings; click again and confirm every pending value resets, including
   local section order or the server Boss override list. Cancel without saving and confirm active
   settings are unchanged; repeat and Save, then confirm the reset applies through ordinary client
   or authorized server-config rules.
   Add several Boss overrides before the server reset and verify the native editor itself immediately
   shows an empty list after confirmation, with no stale rows returning when Save is pressed.
7. Change the local history count to 1, 4, and 10. Confirm exactly that many records are listed and
   addressable as `/dt`, `/dt1`, and so on, up to `/dt9`; reducing the limit drops older local entries.
8. Add enough Boss results to exceed the selected limit. Confirm the compact chat message shows only
   `Boss name | outcome | command`, with no `#0` prefix and the oldest excess record dropped. Resolve
   several simultaneous Bosses in one short burst and confirm the client prints one final history
   mapping after the burst, not one full mapping per result packet.
9. Move the panel-background opacity slider through 0%, an intermediate value, and 100% while a
   result is open. Confirm the default/reset value is displayed as `0.90` without a floating-point
   tail. Only the dark background should change; text, portraits, borders, and buttons stay visible.
10. Set the local automatic-panel duration to 1, 12, and 120 seconds. Confirm only automatically
   opened panels close on that schedule, interaction restarts the full selected duration, changing
   the setting while the panel is open restarts it immediately, and `/dt` panels remain open.
11. Join with English and Simplified Chinese clients. Boss Checklist names should resolve independently in the result panel, `/dt list`, and chat summary.
    In Simplified Chinese, also show at least five preview ranking rows and confirm every sample name,
    including player 5 and later, is localized. At narrow window widths and high UI scale, verify long
    Boss/player/source names and values are trimmed without overlap, invalid surrogate halves, or
    drawing outside the panel; the preview summary and interaction hint must remain legible.
12. Test an `npc.boss` fallback and a server override with `NameLocalizationKey` plus a literal `NameOverride` fallback.
13. For a boss with NPC-free cinematics or nonstandard final forms, verify every transition, final death, wipe, and despawn path with an explicit override.
14. For an override that lists segmented forms, set `ControllerNpcTypes` only to independently
   rooted control forms. Confirm dependent Body/Tail types continue contributing to the configured
   subtotal without becoming automatic-final candidates.
15. Attempt an over-limit native server config (too many overrides, a Boss key over 80 characters,
    or compact JSON above 48 KiB). A client save must fail before constructing an oversized packet;
    `dtserver reload` must deterministically sanitize trusted disk input or retain the last applied
    snapshot. Saving an ordinary config during packet traffic must produce no collection-modified or
    cross-thread Boss-catalog exception.

## Supported attribution boundary

Ordinary item/projectile hits and Terraria's authoritative DoT life loss are captured. Supported
vanilla debuffs keep duration-segment ownership. Unsupported or ownerless DoT remains included in
team/body totals as unattributed server-side damage; mod code that changes `npc.life` outside the
DoT updater still has no universal capture path and must not be assigned by guesswork.
