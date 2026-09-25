# WeaponArts

Active abilities ("arts") keyed to the weapon in your hands. Press one key (default
**C**) and the effect depends on what you hold:

| Weapon | Art | Effect |
|---|---|---|
| 2H sword | Onslaught | more damage for a window |
| 1H sword | Bloodthirst | lifesteal from damage dealt |
| Battleaxe | Berserk | take less damage and regenerate HP |
| 1H axe | Bleed | physical damage over time |
| 1H spear | Rend | the target is exposed: more damage from every player |
| Pike | Impale | ignore resistances + extra damage |
| Atgeir | Crushing | every hit staggers (not bosses) |
| Knives | Envenom | strong poison on every hit |
| Fists | Fury | a critical hit on every blow |
| Bow | Focus | the next shots are critical |
| Crossbow | Piercing Bolts | bolts ignore resistances + extra damage |
| 2H sledge | Rally | heal yourself and allies around |
| 1H mace | Mend | heal allies around (the greydwarf shaman's green heal) |
| Staves | Eitr Surge | more magic damage |
| Tower shield | Taunt | pull the monsters around onto you and take less damage |

- Strength and duration grow with the weapon's tier and your weapon skill. Every art has
  its own cooldown plus a shared global cooldown.
- Effects only ever touch creatures (PvP-safe); bosses take a reduced share and are
  immune to stagger.
- **Players without the mod** see every effect (vanilla prefabs) and receive heals. With
  `09 Proxy` enabled, a nearby modded player can also act for them: they use the
  `/challenge` emote or type `art` in chat.
- The server's balance settings apply to every client with the mod.
- English and Russian text (follows the game's language, or set `01 General > Language`).
- All settings live in `BepInEx/config/j1ga.weaponarts.cfg`; with BepInEx
  ConfigurationManager (F1) they apply in game without a restart.

Install on every player who wants the arts and on the host / dedicated server.

Source, full documentation and the changelog: https://github.com/tbsj1ga/WeaponArtsValheim

*Developed with the help of an AI assistant (Claude by Anthropic); the design
decisions, verification against the game code and in-game testing are the author's.*
