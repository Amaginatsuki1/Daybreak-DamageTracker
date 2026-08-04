# Contributing

Thanks for helping improve Daybreak DamageTracker.

## Before opening an issue

- Confirm the server and every client use the same mod version.
- Reproduce the problem with the smallest practical mod list.
- Check whether the boss needs a lifecycle override in `DaybreakDamageTracker.Server.json`.
- Do not attach server configuration files or logs that contain player names, addresses, tokens, or unrelated private data.

Include the Terraria version, tModLoader version, Daybreak DamageTracker version, single-player or multiplayer mode, relevant boss/mod names, and concise reproduction steps.

## Pull requests

Keep changes focused. Preserve these project boundaries:

- the internal mod ID is `DaybreakDamageTracker`;
- the chat command is `/dt`;
- team ranking may be public, but a player's source tree is visible only to that player;
- presentation policy is controlled by the server;
- unusual boss lifecycles should use explicit adapters instead of a short guessed timeout.

Run a Release build and the relevant checks in [TESTING.md](TESTING.md). Network, privacy, encounter-lifecycle, or UI changes should include a dedicated-server or multiplayer test when practical.

## Build

When the repository is outside tModLoader's `ModSources` directory, provide `TModLoaderTargets`:

```powershell
dotnet build -c Release -p:TModLoaderTargets="D:\Steam\steamapps\common\tModLoader\tMLMod.targets"
```

Use `-p:BuildMod=false` for a compile-only check.
