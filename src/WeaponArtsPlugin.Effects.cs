using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace WeaponArts
{
    // Activation effects for the taunt: vanilla prefabs only, spawned the way the game spawns
    // them, so a client without the mod sees and hears them through the usual sync. Ported from
    // ShieldTaunt. Sound and visual are chosen in the config (local, cosmetic - not synced).
    public partial class WeaponArtsPlugin
    {
        private ConfigEntry<string> _cfgTauntSound;
        private ConfigEntry<string> _cfgTauntVisual;
        private readonly HashSet<string> _warnedEffects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private ConfigEntry<string> _cfgArtSound;
        private ConfigEntry<string> _cfgArtVisual;
        private ConfigEntry<string> _cfgHealVisual;

        private const string VisualHelp = "None; GuardianPower (the forsaken-power activation flash); or a registered prefab name, e.g. fx_eikthyr_stomp, fx_Adrenaline1, fx_guardstone_activate, vfx_perfectblock, fx_gjall_taunt.";

        private void BindEffectsConfig()
        {
            _cfgTauntSound = Config.Bind("06 Taunt Effects", "Sound", "Perfect",
                new ConfigDescription("Sound on taunt: Block (the shield's own block effect), Perfect (perfect-block sparks), None.",
                    new AcceptableValueList<string>("Block", "Perfect", "None")));
            _cfgTauntVisual = Config.Bind("06 Taunt Effects", "Visual", "GuardianPower", "Visual on taunt, attached to the tank. " + VisualHelp);

            _cfgArtSound = Config.Bind("07 Art Effects", "Sound", "Perfect",
                new ConfigDescription("Sound when a weapon art is activated: Perfect (perfect-block sparks), None.",
                    new AcceptableValueList<string>("Perfect", "None")));
            _cfgArtVisual = Config.Bind("07 Art Effects", "Visual", "GuardianPower", "Visual when a weapon art is activated, attached to you. " + VisualHelp);
            _cfgHealVisual = Config.Bind("07 Art Effects", "HealVisual", "fx_guardstone_activate", "Visual for the heal arts (Rally, Mend) instead of the one above. " + VisualHelp);
        }

        // tank is us or a player we taunt for; blockEffect is the block effect of the shield in
        // that tank's hand (null: fall back to the perfect-block sparks).
        private void PlayActivationEffects(Humanoid tank, EffectList blockEffect)
        {
            PlayEffects(tank, _cfgTauntSound.Value, _cfgTauntVisual.Value, blockEffect);
        }

        // Weapon arts: us or a modless player we act for.
        private void PlayArtEffects(Humanoid actor, Art a)
        {
            if (a == null) return;
            string visual = a.Kind == ArtKind.AoEHeal ? _cfgHealVisual.Value : _cfgArtVisual.Value;
            PlayEffects(actor, _cfgArtSound.Value, visual, null);
        }

        private void PlayEffects(Humanoid actor, string sound, string visual, EffectList blockEffect)
        {
            if (actor == null) return;
            Vector3 pos = actor.GetCenterPoint();
            Quaternion rot = actor.transform.rotation;
            ZDOID id = actor.GetZDOID();

            sound = sound ?? "";
            if (sound.Equals("Block", StringComparison.OrdinalIgnoreCase) && blockEffect != null && blockEffect.HasEffects())
                Play(blockEffect, pos, rot, null, id);
            else if (!sound.Equals("None", StringComparison.OrdinalIgnoreCase))
                Play(actor.m_perfectBlockEffect, pos, rot, null, id);

            visual = (visual ?? "").Trim();
            if (visual.Length == 0 || visual.Equals("None", StringComparison.OrdinalIgnoreCase)) return;
            Play(VisualEffects(visual), pos, rot, actor.transform, id);
        }

        private static void Play(EffectList list, Vector3 pos, Quaternion rot, Transform parent, ZDOID id)
        {
            if (list == null || !list.HasEffects()) return;
            list.Create(pos, rot, parent, 1f, -1, id);
        }

        // GuardianPower: the start effects of a forsaken power's status effect (the activation
        // flash). Anything else: a registered prefab, attached to the tank.
        private EffectList VisualEffects(string name)
        {
            if (name.Equals("GuardianPower", StringComparison.OrdinalIgnoreCase))
            {
                ObjectDB db = ObjectDB.instance;
                StatusEffect se = db != null ? db.GetStatusEffect("GP_Eikthyr".GetStableHashCode()) : null;
                if (se != null && se.m_startEffects != null && se.m_startEffects.HasEffects()) return se.m_startEffects;
                if (_warnedEffects.Add(name)) Logger.LogWarning("GP_Eikthyr has no start effects in this build; no taunt visual.");
                return null;
            }

            GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(name) : null;
            if (prefab == null)
            {
                if (_warnedEffects.Add(name)) Logger.LogWarning("Visual effect prefab '" + name + "' is not registered; no taunt visual. Try GuardianPower, fx_eikthyr_stomp, fx_Adrenaline1, fx_guardstone_activate, vfx_perfectblock or fx_gjall_taunt.");
                return null;
            }
            EffectList.EffectData data = new EffectList.EffectData();
            data.m_prefab = prefab;
            data.m_enabled = true;
            data.m_attach = true;
            data.m_inheritParentRotation = true;
            EffectList list = new EffectList();
            list.m_effectPrefabs = new EffectList.EffectData[] { data };
            return list;
        }
    }
}
