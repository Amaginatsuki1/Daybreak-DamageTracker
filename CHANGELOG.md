# Changelog

Notable changes are documented here. Versions follow the mod version in `build.txt`.

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
