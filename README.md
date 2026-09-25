# WeaponArts

**English** · [Русский](README-RU.md)

A Valheim mod: **active abilities ("arts") keyed to the weapon in your hands** — one
key, and the effect depends on what you hold: a critical window, bleed, poison,
guaranteed stagger, lifesteal, an exposed target, berserk, group heals, and the
tower-shield taunt. Built to work alongside players **without the mod**.

Version history is in `CHANGELOG.md`.

## Arts

Strength is the minimum (skill 0, weakest weapon); it grows up to +25% with the
weapon's tier and up to +25% with the weapon skill. Windows grow up to +50% with skill
and tier, and are never shorter than 6 s. Every art has its own cooldown, and **any**
activation starts a 30 s global cooldown (against weapon-swap chains).

| Weapon | Art | Effect | Strength | Window | Cooldown |
|---|---|---|---|---|---|
| 2H sword | Onslaught | more damage | ×1.525 | 6 s | 43 s |
| 1H sword | Bloodthirst | lifesteal from damage dealt | 30% | 6 s | 48 s |
| Battleaxe | Berserk | less damage taken + regeneration | −37.5%, 10 HP/s | 10 s | 54 s |
| 1H axe | Bleed | physical damage over time | 12/s for 6 s | 6 s | 32 s |
| 1H spear | Rend | the target is exposed: +damage from every player | +30% for 8 s | 6 s | 38 s |
| Pike | Impale | ignores resistances + damage | 95%, +22.5% | 6 s | 48 s |
| Atgeir | Crushing | every hit staggers (not bosses) | — | 6 s | 48 s |
| Knives | Envenom | poison on every hit | +18 | 6 s | 32 s |
| Fists | Fury | a critical hit on every blow | ×2 | 6 s | 43 s |
| Bow | Focus | the next shots are critical | ×2.5, 3 shots | ≤ 22.5 s | 48 s |
| Crossbow | Piercing Bolts | bolts ignore resistances + damage | 90%, +22.5%, 3 shots | ≤ 22.5 s | 48 s |
| 2H sledge | Rally | heal yourself and allies, 10 m | 15–25 → 60–100 HP | — | 60 s |
| 1H mace | Mend | heal allies only, 8 m | 10–16 → 40–65 HP | — | 50 s |
| Staves | Eitr Surge | more magic damage (costs eitr) | ×1.6 | 6 s | 60 s |
| Tower shield | Taunt | pull monsters within 15 m onto you + damage reduction | −37.5…−67.5% | 6 s | 48 s |

- Effects on creatures only (PvP-safe); bosses take ×0.5 (`BossEffectFactor`) and are
  immune to stagger.
- A tower shield takes priority over the one-handed weapon's art.
- Creatures in Valheim have no body armor, so "piercing" arts ignore a share of
  **resistances** (never immunities).
- Heals roll a random amount; the range grows with tier and skill.

## How it works

On activation the player writes the art (id, expiry on the server clock, and the
already scaled strength) into their own player ZDO. Damage is applied by the **owner
of the struck creature**: a `Character.RPC_Damage` patch reads the attacker's art from
that ZDO and edits the hit. One path for any modded attacker, whoever owns the monster.
Heals go through the game's own `RPC_Heal`, so they reach players without the mod too.
Visual and sound effects are vanilla prefabs, so everyone sees and hears them.

**Players without the mod (proxy).** With `09 Proxy > Enabled` on, a modded client near
a player without the mod acts for them: that player plays the `/challenge` emote or
types `art` in chat, and the provider reads their weapon/shield from the ZDO and applies
the art on their behalf (taunt, window arts, heals, berserk). `art?` in chat answers with
their art and its state. Their skill is assumed to be `SkillForModless` (25), their
cooldown is ×1.5, and bow/crossbow arts last 10 s instead of counting shots.

**Settings from the server.** The server sends its balance settings to every client with
the mod on connect and on every change; clients use them while connected. Local things
(key, HUD, language, proxy behaviour, cooldowns) stay per client.

## Installation

Through r2modman / Thunderstore, or put `build/WeaponArts.dll` into
`BepInEx\plugins\WeaponArts\`. Install it on every player who wants the arts, and on the
host / dedicated server so the balance is shared. Players without the mod join as usual.

## Settings

`BepInEx\config\j1ga.weaponarts.cfg`, created on first start.

| Section | What |
|---|---|
| 01 General | `Enabled`, `Debug` (logs every activation and hit), `AbilityKey` (default **C**), `ShowHud`, `Language` (Auto / English / Russian) |
| 02 Balance | `GlobalCooldown`, `BossEffectFactor`, `SkillPowerBonus`, `TierPowerBonus`, `SkillDurationScale`, `RefWeaponDamage` |
| 03 Arts - \<id\> | per art: `Magnitude`, `Window`, `Cooldown`, `Cost`, plus `Shots`, `DamageBonus`, `ExposeSeconds`, `RegenPerSecond` or the heal ranges where they apply |
| 04 Taunt | shield mode, radius, duration, cooldown, damage reduction, bosses/tamed, straight charge, Blocking skill gain |
| 05 Bleed | bleed duration and tick interval |
| 06 Taunt Effects / 07 Art Effects | sound and visual on activation; `HealVisual`, `MendVisual` (`ShamanHeal` = the greydwarf shaman's green heal), `MendAnimation` |
| 08 HUD | position and font size |
| 09 Proxy | acting for players without the mod (off by default) |
| 10 Sync | the server hands its settings to clients |

**In-game settings window:** install **BepInEx ConfigurationManager** (e.g.
`Azumatt-Official_BepInEx_ConfigurationManager` on Thunderstore) and press F1. Values
apply at once — the mod reads them at activation and caches nothing.

Console (F5): `weaponarts status | list`.

## Building

```
powershell -ExecutionPolicy Bypass -File .\build.ps1            # build and check references
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Install   # ... and copy into plugins
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Package   # ... and make the Thunderstore zip
```

The compiler is `csc.exe` from the .NET Framework (C# 5); references come straight from
the game folder and the r2modman profile (paths at the top of `build.ps1`,
`check-refs.ps1` and `src\WeaponArts.csproj`). After the build `check-refs.ps1` checks
every type/member reference, reflection target and Harmony patch target against the
installed game with Mono.Cecil.

| File | Contents |
|---|---|
| `WeaponArtsPlugin.cs` | plugin entry, update loop, helpers |
| `WeaponArtsPlugin.Config.cs` | general and balance settings, language, strength scaling |
| `WeaponArtsPlugin.Arts.cs` | the art registry, activation, heals, berserk, exposed mark, shot counting |
| `WeaponArtsPlugin.Patches.cs` | the damage patch (applies arts on the victim's owner), resistance ignore, shot hook |
| `WeaponArtsPlugin.Taunt.cs` | the tower-shield taunt |
| `WeaponArtsPlugin.Proxy.cs` | acting for players without the mod |
| `WeaponArtsPlugin.Bleed.cs` | the physical bleed DoT |
| `WeaponArtsPlugin.Effects.cs` | activation effects, the shaman heal visual |
| `WeaponArtsPlugin.Sync.cs` | settings sync from the server |
| `WeaponArtsPlugin.Ui.cs` | the HUD |
| `WeaponArtsPlugin.Commands.cs` | the `weaponarts` console command |

## Repository

Branch `main` on GitHub: https://github.com/tbsj1ga/WeaponArtsValheim. Versioned: sources,
`.csproj`, scripts, documentation, the Thunderstore template and `build\WeaponArts.dll`.
Not versioned: the BepInEx config, `bin/`, `obj/`, zip packages — see `.gitignore`.
License: MIT (`LICENSE`).

## AI assistance

This mod was developed with the help of an AI assistant (Claude by Anthropic). The code
and the documentation were written together with it and checked against the game's IL;
the design decisions, in-game testing and releases are the author's.
