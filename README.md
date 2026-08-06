# Daybreak DamageTracker

[简体中文](README.zh-CN.md) · [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3776927292) · [MIT License](LICENSE)

Daybreak DamageTracker is a server-controlled boss damage summary mod for Terraria 1.4.4 and tModLoader. It records the effective damage accepted by the server, presents a team ranking, and keeps each player's detailed source breakdown private to that player.

## Features

- Tracks an encounter from boss discovery through victory, defeat, escape, or a manual finish.
- Gives every boss independent team damage, body damage, duration, ranking, percentages, and owner-only source tree.
- Finalizes each simultaneous boss as soon as that boss ends, without waiting for surviving bosses.
- Groups a player's damage by weapon, accessory, weapon body, and projectile type.
- Supports expandable source and projectile rows in a viewport-bounded, scrollable result panel.
- Lets each client keep 1–10 boss results: `/dt` for the latest, then `/dt1` through `/dt9` as needed.
- Lets each client choose a 1–120 second lifetime for automatically opened panels; `/dt` panels remain open until closed.
- Appends a newly resolved boss below any result panel that is already open, in the same scrollable viewport.
- Uses Boss Checklist when available and falls back to `npc.boss` for ordinary bosses.
- Lets servers define lifecycle overrides for bosses with unusual phase transitions.
- Separates unrelated bosses after a wipe/disengagement while preserving simultaneous bosses and configured sequential gauntlets.
- Reports a victory and an escape independently, so either outcome can appear without being blocked by the other boss.
- Consolidates a non-overlapping reconnect into one public ranking row without widening private-source access.
- Resolves supported boss names in each client's selected language.

## Privacy model

The public result contains the team ranking. Detailed source data is collected on the owning client and is never included in public result or history packets. Server-created projectile provenance is sent only to that projectile's owner.

The server controls result content and recipients. Client-side preferences only control automatic opening, its 1–120 second duration, the 1–10 local history length, and the dark panel background opacity. They cannot enable server-disabled sections or widen the data received.

## Installation

Subscribe on the [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3776927292), or install the same `DaybreakDamageTracker.tmod` file on the dedicated server and every client. Mixed mod versions are not supported because the result packet format may change between releases.

The server creates:

```text
<tModLoader SavePath>/ModConfigs/DaybreakDamageTracker.Server.json
```

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

Terraria 1.4.4 accepts a hit's requested damage value from the client. The mod attributes the effective health loss returned by the server's `NPC.StrikeNPC` call, but it is not an anti-cheat system.

Ordinary item and projectile hits are supported. Damage-over-time effects and mod code that changes `npc.life` directly do not expose a universal, trustworthy player/source owner, so the mod does not guess. Accessory projectiles are grouped correctly when the spawning mod supplies an item-bearing entity source; opaque sources fall back to projectile type.

Bosses with NPC-free cinematics or nonstandard final forms may need a server override. The generic no-NPC timeout is disabled by default to avoid ending an encounter during a phase transition.

Sequential encounters whose waves use different Boss Checklist keys can share an `EncounterGroupKey`. A gauntlet that intentionally survives a full party wipe can additionally enable `ContinueAfterPartyWipe` and should declare a reliable final NPC.

## Contributing

Bug reports and focused pull requests are welcome. Please read [CONTRIBUTING.md](CONTRIBUTING.md) and use [TESTING.md](TESTING.md) for multiplayer and boss-lifecycle checks.

## License and acknowledgments

Source code is available under the [MIT License](LICENSE). See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for the projects and official examples that informed the implementation. The icon and Workshop artwork are included with the project owner's permission and are not covered by the MIT source-code license.
