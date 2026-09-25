# Changelog

**English** · [Русский](CHANGELOG-RU.md)

The version is set in one place — `WeaponArtsPlugin.Version` in `src/WeaponArtsPlugin.cs`.

## 0.14.0 — English, publishing prep

- All in-game text (HUD, messages, proxy chat lines, art names and descriptions) in
  English and Russian. 01 General > Language: Auto (follows the game's language),
  English, Russian; the setting is local and never taken from the server.
- Documentation in English (README.md, CHANGELOG.md) with Russian copies (-RU.md), a
  Thunderstore README; code and build-script comments in English.
- Icon 256×256.

## 0.13.3 — Cooldown in the HUD, balance

- HUD: while the global cooldown runs, every weapon shows a grey "cooldown Xs" (the
  larger of its own and the global cooldown) instead of "ready".
- Bow: x2.5, 3 shots. Fists: x2. 1H sword: 30% lifesteal. 1H axe: bleed 12/s for 6s.
  Berserk: fixed regeneration of 10 HP/s (no longer scaled by tier/skill), window 10s.
- Debug log line for every Berserk regeneration tick.

## 0.13.2 — Green particles of the shaman heal

- The shaman's heal attack has no effects of its own, so Mend fell back to the
  fx_guardstone_activate dome. Now the shaman's heal object itself (shaman_heal_aoe, taken
  from the attack's m_spawnOnTrigger) is spawned as a pure visual: networked, so everyone
  sees it, with every hit/damage/status switched off on the copy — it heals and hits
  nobody. Plus vfx_greydwarf_shaman_pray and sfx_greydwarf_shaman_heal if registered, and
  the AoE's hit effect on every healed player.
- ShamanHeal can be set for Rally too: 07 Art Effects > HealVisual = ShamanHeal.

## 0.13.1 — Minimum window 6s

- No art or taunt window is shorter than 6s: MinWindow in the code (clamped on activation,
  also for the proxy and for values from the server) and the lower bound in the config.
  Bleed, Envenom, Impale, Crushing: window 6s.

## 0.13.0 — Berserk, balance ×1.5, global cooldown 30s

- **Berserk (2H battleaxe)**: 12s window — damage taken −37.5% and 4.5 HP/s
  regeneration (both grow with tier/skill, reduction capped at 80%). For a player without
  the mod through the proxy: regeneration through RPC_Heal, the reduction applies to hits
  from monsters the provider owns.
- **Rend (exposed target)** moved to the 1H spear; Pierce removed.
- **Crits (Fury, Focus)** apply to every hit/shot in the window, sneak attacks included
  (stacking with the sneak bonus).
- **Balance**: the bonus part of strength ×1.5, windows ×1.5, cooldowns ×1.2 (except
  heals, bleed, poison); taunt: reduction 37.5–67.5%, 6s, cooldown 48s. Bow 4 shots,
  crossbow 3, time cap 22.5s; proxy window for bow/crossbow 10s.
- **Global cooldown 30s** after any activation (the taunt included): shown in the HUD,
  as a message on a weapon/shield swap and on an activation attempt.

## 0.12.0 — Second round of test fixes

- **Fixed: heals did not reach other players** (Rally → vanilla players, Mend → modded
  players, lifesteal on a non-owner): the RPC `Heal` was invoked, the game registers
  `RPC_Heal`.
- **Mend (1H mace)**: its own visual — the greydwarf shaman's heal cast (its vanilla
  effects, on the caster and on every healed player) + the staff-of-protection cast
  animation. 07 Art Effects: MendVisual, MendAnimation.
- **Crushing (atgeir)**: every hit on a non-boss in the window is a guaranteed stagger
  (m_staggerMultiplier ≥ 100 → the game staggers at once). Before, the stagger-damage
  multiplier did not break the threshold of heavy monsters. Window 3s.
- **Creatures have 0 armor in Valheim** (Character.GetBodyArmor is always 0) — "armor
  ignore" did nothing. Pierce/Impale/Piercing Bolts now ignore a share of
  **resistances** (not immunities) + DamageBonus (+15%). The GetBodyArmor patch is gone,
  HitData.ApplyResistance is patched instead.
- **Rend (battleaxe)** → "Exposed": hits in the window mark the target (ZDO) for
  ExposeSeconds (8s), it takes +20% damage from **every** player, vanilla ones included.
- **Focus/Piercing Bolts**: the art lasts N shots (Shots: bow 3, crossbow 2), the window
  is a 15s cap; after the last shot 2.5s for the arrows in flight. The HUD shows the shots
  left. For a player without the mod (proxy) — a plain 6s window.
- **Strength scaling**: Magnitude is now the minimum (skill 0, weakest weapon), on top
  TierPowerBonus (+25% at top tier) and SkillPowerBonus (+25% at skill 100). Before, the
  bonus was halved at a low tier (Onslaught 1.35 → effectively 1.17).
- **Crit does not stack with a sneak attack** — the game's 5-minute timer
  (m_backstabTime) is checked: on an unalerted monster (or a test dummy) the crit now
  works, except on the hit that really got the sneak bonus.
- **Damage debug log**: with 01 General > Debug every hit under an art logs the damage
  before/after, the multiplier, HP lost, exposure and stagger.

## 0.11.0 — In-game test fixes

- **Fixed: Focus (bow) and Fury (fists) did nothing.** The "no crit on top of a sneak
  attack" check looked at m_backstabBonus, and the game sets it on EVERY hit (it is the
  weapon's multiplier) — the crit was always dropped. Now the condition is the game's:
  weapon bonus > 1 AND the target's AI is not alerted.
- **Activation while other keys are held** (moving, blocking): KeyboardShortcut.IsDown()
  required no other key to be down; now only the key and its modifiers are checked.
- **HUD**: above the stamina bar by default (PositionY 0.76), position and font in 08
  HUD; hidden in the menu (Esc), on the map, in chat/console, on pause, with the UI
  hidden, in build mode and cutscenes; a shadow for readability.
- **Countdown of the active window** in the HUD ("active 3.2s"), the duration in the
  activation message.
- **Activation effects for every art** (07 Art Effects: Sound Perfect/None, Visual, a
  separate HealVisual for heals); for the proxy too.

## 0.10.0 — Taunt activation effects

- Effects ported from ShieldTaunt: sound and visual on taunt, vanilla prefabs only (clients
  without the mod see them too). Config 06 Taunt Effects: Sound (Block/Perfect/None),
  Visual (None/GuardianPower/a prefab name: fx_eikthyr_stomp, fx_Adrenaline1,
  fx_guardstone_activate, vfx_perfectblock, fx_gjall_taunt). Works through the proxy too.

## 0.9.0 — Physical bleed

- Bleed (1H axe) is now its own physical ticking DoT (ArtKind.Bleed), not poison:
  registered on the target's owner, ticks physical damage once a second, bypasses armor
  (like poison), computed by the owner, the attacker is kept for credit. The tick is
  flagged so it does not recurse through RPC_Damage. Separate from the knives' poison
  (Envenom). Config 05 Bleed: BleedSeconds, TickInterval.

## 0.8.0 — True armor ignore

- Pierce/Rend/Impale/Piercing Bolts reduce the target's armor instead of multiplying
  damage: a new ArtKind.Pierce, a static pen flag for the duration of the hit, a
  Character.GetBodyArmor postfix multiplies the armor by (1 - share). Superseded in
  0.12.0 (creatures have no armor).

## 0.7.0 — Polish (part)

- Crits (Fury/Focus) do not stack with a sneak attack (m_backstabBonus check).
- Taunt: Blocking skill gain per pulled monster; straight charge (monsters come straight
  instead of circling); multi-tank arbitration through ZDO marks (holdby/until/since) —
  two modded tanks do not fight over monsters, the later activation wins.
- Postponed (needs testing/tuning): true armor ignore, physical bleed, taunt activation
  effects, flail registration (confirm the flail's skill type in game).

## 0.6.0 — Settings window (ConfigurationManager)

- Settings apply in game without a restart: the mod reads the values at activation and
  caches nothing, so edits from BepInEx ConfigurationManager (window on F1) take effect at
  once. Documented in the README; no hard dependency added.

## 0.5.0 — Config sync with the host

- The server sends its balance settings to every client with the mod (on connect and on
  change), the client applies them through the Sv/Sb/Ss resolvers while connected.
  Everything is synced except the proxy section (client behaviour) and local pacing
  (cooldowns/costs/global cooldown). A generic packet of every ConfigEntry
  (float/bool/string). Key 10 Sync > SyncConfig (on by default).
- Patches ZNet.Awake (RPC registration), RPC_PeerInfo (send after login), OnDestroy
  (reset).

## 0.4.0 — Proxy for a player without the mod

- Default art key — C.
- Arts for a player WITHOUT the mod: triggered by a vanilla emote (/challenge) or a chat
  word; a provider (the nearest modded player, Nearby mode, works on a dedicated server
  too) reads the weapon/shield from their ZDO, picks the art (taunt with a tower shield
  has priority) and applies it: window arts through the proxy table in RPC_Damage, the
  taunt as holding monsters on them + reduction in Character.Damage, heals instantly.
- Scaled by SkillForModless + the tier from the ZDO; own cooldown (×CooldownFactor).
- Chat feedback: activation, "ready" after the cooldown, a query word (art?).
- A mod mark in the ZDO (modded players are not taunted for).

## 0.3.0 — Tower-shield taunt ported

- The taunt as the tower shield's art, **priority over the 1H weapon's art**: shield
  detection (Tower/AnyShield/None by `m_timedBlockBonus`), claiming monsters in the radius
  (`ClaimOwnership`), forcing the target onto the tank through reflection on `MonsterAI`
  fields, holding it with a `MonsterAI.UpdateTarget` postfix, damage reduction in
  `Character.RPC_Damage`. Scaling (reduction from the shield's block power, duration from
  Blocking with a +50% cap).
- The HUD shows Taunt (active/cooldown/ready) when a shield is equipped.

## 0.2.0 — Heals, AoE burst, eitr surge

- **Rally** (2H sledge) and **Mend** (1H mace) — AoE heal with a random roll
  `random(lo, hi)`, the range grows with weapon tier and skill; Rally heals
  yourself + allies, Mend only allies; delivered through `RPC_Heal` (reaches vanilla allies
  too).
- **Eitr surge** (elemental and blood staves) — more magic damage for a window, costs
  eitr.
- **AoE burst** (kind implemented): instant damage to creatures around through
  `Character.Damage(HitData)`. The flail (Whirlwind) is not registered — the flail's
  `skillType` has to be confirmed in game.

## 0.1.0 — Skeleton and combat arts

- The art is chosen by the equipped weapon (`skillType` + 1H/2H), one key.
- Own cooldown per art + a global cooldown (against weapon-swap abuse); stamina/eitr
  cost; strength and duration scale with weapon tier and skill (duration capped at +50%).
- One path: on activation the player writes the art (id/expiry on server time/power)
  into their ZDO; a `Character.RPC_Damage` patch on the target's owner applies it.
- Implemented: Onslaught, Rend, Pierce, Impale, Fury, Focus, Piercing Bolts (damage
  multiplier); Bleed, Envenom (poison DoT); Crushing (stagger); Bloodthirst (lifesteal).
- Balance: effects on creatures only (PvP-safe), ×`BossEffectFactor` on bosses, bosses
  are immune to stagger.
- HUD at the bottom of the screen (name + description + ready/cooldown); console
  `weaponarts`.
- Build `build.ps1`, check `check-refs.ps1`.
