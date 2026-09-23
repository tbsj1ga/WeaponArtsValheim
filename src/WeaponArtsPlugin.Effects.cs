using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
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
            _cfgHealVisual = Config.Bind("07 Art Effects", "HealVisual", "fx_guardstone_activate", "Visual for Rally (sledge heal) instead of the one above. " + VisualHelp);
            _cfgMendVisual = Config.Bind("07 Art Effects", "MendVisual", "ShamanHeal", "Visual for Mend (mace heal): ShamanHeal (the greydwarf shaman's heal cast, also shown on each healed ally), or anything HealVisual takes.");
            _cfgMendAnimation = Config.Bind("07 Art Effects", "MendAnimation", "StaffShield", "Player animation for Mend: the attack animation of this item prefab (StaffShield = the staff-of-protection cast), a raw animator trigger name, or None.");
        }

        private ConfigEntry<string> _cfgMendVisual;
        private ConfigEntry<string> _cfgMendAnimation;
        private EffectList _shamanCast, _shamanHit;
        private bool _shamanResolved;

        private static readonly System.Reflection.FieldInfo s_zanim = AccessTools.Field(typeof(Character), "m_zanim");

        // The greydwarf shaman's heal attack effects (start/trigger at the caster, hit on the
        // healed). Resolved once from its default items; vanilla prefabs, visible to everyone.
        private void ResolveShaman()
        {
            if (_shamanResolved || ZNetScene.instance == null) return;
            _shamanResolved = true;
            GameObject shaman = ZNetScene.instance.GetPrefab("Greydwarf_Shaman");
            Humanoid h = shaman != null ? shaman.GetComponent<Humanoid>() : null;
            if (h == null || h.m_defaultItems == null) { Logger.LogWarning("Greydwarf_Shaman not found; Mend uses HealVisual."); return; }
            foreach (GameObject go in h.m_defaultItems)
            {
                ItemDrop d = go != null ? go.GetComponent<ItemDrop>() : null;
                if (d == null || go.name.IndexOf("heal", StringComparison.OrdinalIgnoreCase) < 0) continue;
                Attack at = d.m_itemData.m_shared.m_attack;
                if (at == null) continue;
                _shamanCast = new EffectList();
                List<EffectList.EffectData> all = new List<EffectList.EffectData>();
                if (at.m_startEffect != null && at.m_startEffect.m_effectPrefabs != null) all.AddRange(at.m_startEffect.m_effectPrefabs);
                if (at.m_triggerEffect != null && at.m_triggerEffect.m_effectPrefabs != null) all.AddRange(at.m_triggerEffect.m_effectPrefabs);
                _shamanCast.m_effectPrefabs = all.ToArray();
                _shamanHit = at.m_hitEffect;
                Debug("Mend visual from " + go.name + ": " + all.Count + " cast effects, hit " + (_shamanHit != null && _shamanHit.HasEffects()));
                return;
            }
            Logger.LogWarning("Greydwarf_Shaman has no heal item; Mend uses HealVisual.");
        }

        private bool UseShaman(Art a)
        {
            if (a == null || !a.Heal1H || !string.Equals((_cfgMendVisual.Value ?? "").Trim(), "ShamanHeal", StringComparison.OrdinalIgnoreCase)) return false;
            ResolveShaman();
            return _shamanCast != null && _shamanCast.HasEffects();
        }

        // Mend: the shaman's heal effect on a healed ally.
        private void PlayHealHit(Art a, Character target)
        {
            if (target == null || !UseShaman(a) || _shamanHit == null) return;
            Play(_shamanHit, target.GetCenterPoint(), target.transform.rotation, target.transform, target.GetZDOID());
        }

        // Mend: a cast animation on the local player (synced by ZSyncAnimation to everyone).
        private void PlayMendAnimation(Humanoid actor, Art a)
        {
            if (a == null || !a.Heal1H || actor != Player.m_localPlayer) return;
            string anim = (_cfgMendAnimation.Value ?? "").Trim();
            if (anim.Length == 0 || anim.Equals("None", StringComparison.OrdinalIgnoreCase)) return;
            GameObject item = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(anim) : null;
            ItemDrop d = item != null ? item.GetComponent<ItemDrop>() : null;
            if (d != null && d.m_itemData.m_shared.m_attack != null) anim = d.m_itemData.m_shared.m_attack.m_attackAnimation;
            ZSyncAnimation z = s_zanim != null ? s_zanim.GetValue(actor) as ZSyncAnimation : null;
            if (z != null && !string.IsNullOrEmpty(anim)) z.SetTrigger(anim);
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
            if (UseShaman(a))
            {
                PlayEffects(actor, _cfgArtSound.Value, "None", null);
                Play(_shamanCast, actor.GetCenterPoint(), actor.transform.rotation, actor.transform, actor.GetZDOID());
                PlayMendAnimation(actor, a);
                return;
            }
            string visual = a.Kind == ArtKind.AoEHeal ? _cfgHealVisual.Value : _cfgArtVisual.Value;
            PlayEffects(actor, _cfgArtSound.Value, visual, null);
            PlayMendAnimation(actor, a);
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
