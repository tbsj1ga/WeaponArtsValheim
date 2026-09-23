using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace WeaponArts
{
    public partial class WeaponArtsPlugin
    {
        private ConfigEntry<bool> _cfgEnabled;
        private ConfigEntry<bool> _cfgDebug;
        private ConfigEntry<KeyboardShortcut> _cfgKey;
        private ConfigEntry<bool> _cfgShowHud;

        private ConfigEntry<float> _cfgGlobalCooldown;
        private ConfigEntry<float> _cfgBossFactor;
        private ConfigEntry<float> _cfgSkillPowerScale;     // up to +this at skill 100
        private ConfigEntry<float> _cfgTierPowerScale;      // up to +this at full tier
        private ConfigEntry<float> _cfgSkillDurationScale;  // duration bonus, capped +50%
        private ConfigEntry<float> _cfgRefWeaponDamage;     // tier normaliser

        private void BindConfig()
        {
            _cfgEnabled = Config.Bind("01 General", "Enabled", true, "Master switch.");
            _cfgDebug = Config.Bind("01 General", "Debug", false, "Log every activation and applied effect.");
            _cfgKey = Config.Bind("01 General", "AbilityKey", new KeyboardShortcut(KeyCode.C),
                "Key that triggers the art of the currently equipped weapon.");
            _cfgShowHud = Config.Bind("01 General", "ShowHud", true, "Show the current art and its state at the bottom of the screen.");

            _cfgGlobalCooldown = Config.Bind("02 Balance", "GlobalCooldown", 3f,
                new ConfigDescription("Seconds after any activation during which no art can fire (stops weapon-swap spam).", new AcceptableValueRange<float>(0f, 30f)));
            _cfgBossFactor = Config.Bind("02 Balance", "BossEffectFactor", 0.5f,
                new ConfigDescription("Multiplier of art strength against bosses. Crowd control does not apply to bosses regardless (vanilla immunity).", new AcceptableValueRange<float>(0f, 1f)));
            _cfgSkillPowerScale = Config.Bind("02 Balance", "SkillPowerBonus", 0.25f,
                new ConfigDescription("Extra art strength at weapon skill 100 (0.25 = +25%). The art's Magnitude is the floor (skill 0, weakest weapon).", new AcceptableValueRange<float>(0f, 2f)));
            _cfgTierPowerScale = Config.Bind("02 Balance", "TierPowerBonus", 0.25f,
                new ConfigDescription("Extra art strength from a full-tier weapon (see RefWeaponDamage). Adds to SkillPowerBonus.", new AcceptableValueRange<float>(0f, 2f)));
            _cfgSkillDurationScale = Config.Bind("02 Balance", "SkillDurationScale", 0.5f,
                new ConfigDescription("Extra art duration from skill+tier, capped at +50% (4s -> max 6s).", new AcceptableValueRange<float>(0f, 0.5f)));
            _cfgRefWeaponDamage = Config.Bind("02 Balance", "RefWeaponDamage", 100f,
                new ConfigDescription("Weapon base damage that counts as full tier (tierNorm = weaponDamage / this, capped 1).", new AcceptableValueRange<float>(20f, 400f)));
        }

        // Strength scale of an art: 1 at skill 0 with the weakest weapon (Magnitude as configured),
        // growing with tier and skill. Used by the actor and by the proxy.
        private float PowerScale(float tierNorm, float skill)
        {
            return 1f + Sv(_cfgTierPowerScale) * tierNorm + Sv(_cfgSkillPowerScale) * skill;
        }
    }
}
