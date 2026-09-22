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
            _cfgSkillPowerScale = Config.Bind("02 Balance", "SkillPowerScale", 0.5f,
                new ConfigDescription("Extra art strength at weapon skill 100 (0.5 = +50%). Weapon tier adds on top.", new AcceptableValueRange<float>(0f, 2f)));
            _cfgSkillDurationScale = Config.Bind("02 Balance", "SkillDurationScale", 0.5f,
                new ConfigDescription("Extra art duration from skill+tier, capped at +50% (4s -> max 6s).", new AcceptableValueRange<float>(0f, 0.5f)));
            _cfgRefWeaponDamage = Config.Bind("02 Balance", "RefWeaponDamage", 100f,
                new ConfigDescription("Weapon base damage that counts as full tier (tierNorm = weaponDamage / this, capped 1).", new AcceptableValueRange<float>(20f, 400f)));
        }
    }
}
