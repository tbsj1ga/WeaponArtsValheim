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

## Compatibility

Tested with **Valheim 1.0.16** (network version 40), **BepInEx 5.4.23.5** (BepInExPack_Valheim 5.4.2351).

## Who needs it

| Who | What |
|---|---|
| Players with the mod | use their arts with the key (default **C**) |
| Host / dedicated server | recommended: its balance settings apply to every client with the mod |
| Players without the mod | see every effect and receive heals; to use arts themselves, `09 Proxy > Enabled` must be on for a modded player near them (**off by default**) |

## Known conflicts

- Combat overhauls that rework damage (`Character.RPC_Damage`) or monster targeting may change how strong the arts are or override the taunt.
- Another mod bound to **C** — change `01 General > AbilityKey`.

## Bugs and feedback

GitHub Issues: https://github.com/tbsj1ga/WeaponArtsValheim/issues — please attach `BepInEx/LogOutput.log`.

## Screenshots

<!-- Uncomment each line once the file is in docs/media/ and pushed. -->
<!-- ![the atgeir art: every hit staggers](https://raw.githubusercontent.com/tbsj1ga/WeaponArtsValheim/main/docs/media/crushing.gif) -->
<!-- ![the mace heal with the shaman's green particles](https://raw.githubusercontent.com/tbsj1ga/WeaponArtsValheim/main/docs/media/mend.gif) -->
<!-- ![the tower-shield taunt pulling monsters](https://raw.githubusercontent.com/tbsj1ga/WeaponArtsValheim/main/docs/media/taunt.gif) -->
<!-- ![the HUD with the art and its cooldown](https://raw.githubusercontent.com/tbsj1ga/WeaponArtsValheim/main/docs/media/hud.png) -->

Source, full documentation and the changelog: https://github.com/tbsj1ga/WeaponArtsValheim

*Developed with the help of an AI assistant (Claude by Anthropic); the design
decisions, verification against the game code and in-game testing are the author's.*
