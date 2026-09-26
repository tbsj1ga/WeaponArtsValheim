# WeaponArts

Active abilities ("arts") keyed to the weapon in your hands. Press one key (default
**C**) and the effect depends on what you hold.

| | |
|---|---|
| ![Crushing (atgeir): every hit staggers](https://raw.githubusercontent.com/tbsj1ga/WeaponArtsValheim/main/docs/media/crushing.webp) | ![Taunt (tower shield): the monsters come to you](https://raw.githubusercontent.com/tbsj1ga/WeaponArtsValheim/main/docs/media/taunt.webp) |
| Crushing (atgeir): every hit staggers | Taunt (tower shield): the monsters come to you |
| ![Rally (sledge): heal yourself and your allies](https://raw.githubusercontent.com/tbsj1ga/WeaponArtsValheim/main/docs/media/rally.webp) | ![Berserk (battleaxe): less damage taken, HP regenerates](https://raw.githubusercontent.com/tbsj1ga/WeaponArtsValheim/main/docs/media/berserk.webp) |
| Rally (sledge): heal yourself and your allies | Berserk (battleaxe): less damage taken, HP regenerates |

![Focus (bow): the next shots are critical, counted in the HUD](https://raw.githubusercontent.com/tbsj1ga/WeaponArtsValheim/main/docs/media/focus.webp)

*Focus (bow): the next shots are critical, counted in the HUD*

![The HUD: ready, active, cooldown](https://raw.githubusercontent.com/tbsj1ga/WeaponArtsValheim/main/docs/media/hud-states.png)

*The HUD: ready, active, cooldown*

**Arts by weapon:**

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

## More mods by j1gA

| | Mod |
|---|---|
| [![LivingMap](https://raw.githubusercontent.com/tbsj1ga/LivingMapValheim/main/docs/media/icon-128.png)](https://thunderstore.io/c/valheim/p/j1gA/LivingMap/) | **[LivingMap](https://thunderstore.io/c/valheim/p/j1gA/LivingMap/)** — Your buildings, roads and cleared forest on the map and the minimap. |
| [![StationSpeed](https://raw.githubusercontent.com/tbsj1ga/StationSpeedValheim/main/docs/media/icon-128.png)](https://thunderstore.io/c/valheim/p/j1gA/StationSpeed/) | **[StationSpeed](https://thunderstore.io/c/valheim/p/j1gA/StationSpeed/)** — Faster smelters, kilns, fermenters and crops — consistent even for players without the mod. |
| [![ExtendedBosses](https://raw.githubusercontent.com/tbsj1ga/ExtendedBossesValheim/main/docs/media/icon-128.png)](https://thunderstore.io/c/valheim/p/j1gA/ExtendedBosses/) | **[ExtendedBosses](https://thunderstore.io/c/valheim/p/j1gA/ExtendedBosses/)** — Raid-style boss fights: phases, adds, nests, shields, marks — built from vanilla parts. |
| [![HostOwner](https://raw.githubusercontent.com/tbsj1ga/HostOwnerValheim/main/docs/media/icon-128.png)](https://thunderstore.io/c/valheim/p/j1gA/HostOwner/) | **[HostOwner](https://thunderstore.io/c/valheim/p/j1gA/HostOwner/)** — The host takes ownership of stations and bosses near it, so its mods work for everyone. |

