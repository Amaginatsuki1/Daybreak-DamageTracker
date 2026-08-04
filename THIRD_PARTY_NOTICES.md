# Acknowledgments and third-party references

Daybreak DamageTracker does not redistribute third-party source code. Its design and implementation were informed by these public projects and official examples:

- [JeseKi/KiSpaceDamageCalc](https://github.com/JeseKi/KiSpaceDamageCalc), MIT License — prior damage-summary behavior and terminology.
- [tModLoader ExampleMod UI examples](https://github.com/tModLoader/tModLoader/tree/1.4.4/ExampleMod/Common/UI/ExampleCoinsUI) — `UserInterface`, `UIState`, and interface-layer lifecycle.
- [tModLoader `NPC.StrikeNPC` patch](https://github.com/tModLoader/tModLoader/blob/1.4.4/patches/tModLoader/Terraria/NPC.cs.patch#L2533-L2568) — server-applied health-loss behavior.
- [tModLoader ExampleMod networking](https://github.com/tModLoader/tModLoader/blob/1.4.4/ExampleMod/ExampleMod.cs) — packet lifecycle and targeted `ModPacket` patterns.
- [JavidPack/BossChecklist integration example](https://github.com/JavidPack/BossChecklist/blob/1.4.4/BossChecklistIntegrationExample.cs#L27) — weak `GetBossInfoDictionary` integration.
- [JavidPack/DPSExtreme](https://github.com/JavidPack/DPSExtreme) — prior art for observing the `DamageNPC` packet.
- [ImproveGame](https://github.com/ForOne-Club/ImproveGame), MIT License — tModLoader UI and packet organization.

The linked projects remain subject to their own licenses and copyright notices.

## Artwork

The mod icon and Workshop thumbnail are derived from artwork supplied by the project owner for use in this mod. They are not covered by the MIT source-code license; contact the project owner before reusing them outside Daybreak DamageTracker.
