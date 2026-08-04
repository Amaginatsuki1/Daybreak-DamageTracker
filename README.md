# Daybreak DamageTracker

[简体中文](README.zh-CN.md) · [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3776927292) · [MIT License](LICENSE)

Daybreak DamageTracker is a server-controlled boss damage summary mod for Terraria 1.4.4 and tModLoader. It records the effective damage accepted by the server, presents a team ranking, and keeps each player's detailed source breakdown private to that player.

## Features

- Tracks an encounter from boss discovery through victory, defeat, escape, or a manual finish.
- Shows team damage, cumulative boss-body damage, duration, player ranking, and percentages.
- Groups a player's damage by weapon, accessory, weapon body, and projectile type.
- Supports expandable source and projectile rows in a viewport-bounded, scrollable result panel.
- Reopens recent results with `/dt`.
- Uses Boss Checklist when available and falls back to `npc.boss` for ordinary bosses.
- Lets servers define lifecycle overrides for bosses with unusual phase transitions.
- Resolves supported boss names in each client's selected language.

## Privacy model

The public result contains the team ranking. Detailed source data is collected on the owning client and is never included in public result or history packets. Server-created projectile provenance is sent only to that projectile's owner.

The server controls presentation and result recipients. Clients do not have a configuration page that can widen the data they receive.

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
/dt <number>
/dt list
/dt chat
/dt close
```

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

## Contributing

Bug reports and focused pull requests are welcome. Please read [CONTRIBUTING.md](CONTRIBUTING.md) and use [TESTING.md](TESTING.md) for multiplayer and boss-lifecycle checks.

## License and acknowledgments

Source code is available under the [MIT License](LICENSE). See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for the projects and official examples that informed the implementation. The icon and Workshop artwork are included with the project owner's permission and are not covered by the MIT source-code license.
