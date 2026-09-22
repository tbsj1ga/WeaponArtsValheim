using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace WeaponArts
{
    // The tower-shield taunt, ported from ShieldTaunt as the shield case of Weapon Arts:
    // pull the monsters around you onto yourself and take reduced damage while it lasts. A
    // tower shield takes priority over the one-handed weapon's art. Phase 3 covers the modded
    // tank (claim + hold + force target + damage reduction); the proxy for a modless tank,
    // effects, skill-gain and multi-tank ZDO arbitration are later phases.
    public partial class WeaponArtsPlugin
    {
        // reflection into MonsterAI's private target fields + protected SetAlerted, and the
        // private Humanoid.GetCurrentBlocker (verified by check-refs.ps1)
        private FieldInfo _fiTargetCreature, _fiTargetStatic, _fiLastKnownTargetPos, _fiBeenAtLastPos, _fiTimeSinceSensed;
        private MethodInfo _miSetAlerted, _miGetBlocker;

        private ConfigEntry<string> _cfgShieldMode;
        private ConfigEntry<float> _cfgTowerParry;
        private ConfigEntry<float> _cfgTauntRadius;
        private ConfigEntry<float> _cfgTauntDuration;
        private ConfigEntry<float> _cfgTauntCooldown;
        private ConfigEntry<float> _cfgTauntStamina;
        private ConfigEntry<float> _cfgTauntReductionBase;
        private ConfigEntry<float> _cfgTauntReductionBest;
        private ConfigEntry<float> _cfgTauntRefBlock;
        private ConfigEntry<bool> _cfgTauntBosses;
        private ConfigEntry<bool> _cfgTauntTamed;
        private ConfigEntry<bool> _cfgTauntAggravate;

        private struct Hold { public float Until; public ZDOID Target; }
        private readonly Dictionary<ZDOID, Hold> _tainted = new Dictionary<ZDOID, Hold>();
        private readonly List<ZDOID> _tauntScratch = new List<ZDOID>();

        private float _tauntUntil, _tauntCdUntil, _tauntNextReapply, _tauntReduction;
        private int _tauntCount;

        private bool TauntActive { get { return Active && Time.time < _tauntUntil; } }

        private void BindReflection()
        {
            _fiTargetCreature = AccessTools.Field(typeof(MonsterAI), "m_targetCreature");
            _fiTargetStatic = AccessTools.Field(typeof(MonsterAI), "m_targetStatic");
            _fiLastKnownTargetPos = AccessTools.Field(typeof(MonsterAI), "m_lastKnownTargetPos");
            _fiBeenAtLastPos = AccessTools.Field(typeof(MonsterAI), "m_beenAtLastPos");
            _fiTimeSinceSensed = AccessTools.Field(typeof(MonsterAI), "m_timeSinceSensedTargetCreature");
            _miSetAlerted = AccessTools.Method(typeof(MonsterAI), "SetAlerted", new Type[] { typeof(bool) });
            _miGetBlocker = AccessTools.Method(typeof(Humanoid), "GetCurrentBlocker");
            if (!CanForceTarget || _miSetAlerted == null || _miGetBlocker == null)
                Logger.LogWarning("Some game members were not found by reflection; the taunt may be inert. Check the game version.");
        }

        private bool CanForceTarget
        {
            get { return _fiTargetCreature != null && _fiTargetStatic != null && _fiLastKnownTargetPos != null && _fiBeenAtLastPos != null && _fiTimeSinceSensed != null; }
        }

        private void BindTauntConfig()
        {
            _cfgShieldMode = Config.Bind("04 Taunt", "ShieldMode", "Tower",
                new ConfigDescription("Tower = only a tower shield taunts; AnyShield = any shield; None = no shield needed.",
                    new AcceptableValueList<string>("Tower", "AnyShield", "None")));
            _cfgTowerParry = Config.Bind("04 Taunt", "TowerParryThreshold", 1f,
                new ConfigDescription("A shield counts as a tower shield when its timed-block (parry) bonus is at most this.", new AcceptableValueRange<float>(1f, 3f)));
            _cfgTauntRadius = Config.Bind("04 Taunt", "Radius", 15f, new ConfigDescription("Monsters within this radius are pulled.", new AcceptableValueRange<float>(2f, 60f)));
            _cfgTauntDuration = Config.Bind("04 Taunt", "Duration", 4f, new ConfigDescription("Seconds the aggro is held (before +50% cap scaling).", new AcceptableValueRange<float>(1f, 60f)));
            _cfgTauntCooldown = Config.Bind("04 Taunt", "Cooldown", 40f, new ConfigDescription("Seconds before reuse.", new AcceptableValueRange<float>(0f, 600f)));
            _cfgTauntStamina = Config.Bind("04 Taunt", "StaminaCost", 25f, new ConfigDescription("Stamina spent.", new AcceptableValueRange<float>(0f, 200f)));
            _cfgTauntReductionBase = Config.Bind("04 Taunt", "DamageReductionBase", 0.25f, new ConfigDescription("Damage reduction with the weakest tower shield and Blocking 0.", new AcceptableValueRange<float>(0f, 0.9f)));
            _cfgTauntReductionBest = Config.Bind("04 Taunt", "DamageReductionBest", 0.45f, new ConfigDescription("Damage reduction with the strongest shield and Blocking 100 (shield 80%, skill 20%).", new AcceptableValueRange<float>(0f, 0.9f)));
            _cfgTauntRefBlock = Config.Bind("04 Taunt", "RefBlockPower", 100f, new ConfigDescription("Shield block power that counts as full tier for scaling.", new AcceptableValueRange<float>(10f, 400f)));
            _cfgTauntBosses = Config.Bind("04 Taunt", "AffectBosses", true, "Also pull bosses (their area attacks still hit others).");
            _cfgTauntTamed = Config.Bind("04 Taunt", "AffectTamed", false, "Also affect tamed creatures.");
            _cfgTauntAggravate = Config.Bind("04 Taunt", "AggravateNeutral", true, "Wake and anger neutral monsters so they engage.");
        }

        private ItemDrop.ItemData GetBlocker(Humanoid human)
        {
            if (human == null || _miGetBlocker == null) return null;
            return _miGetBlocker.Invoke(human, null) as ItemDrop.ItemData;
        }

        // Whether a tower shield (per the mode) is in the block hand.
        internal bool TauntShieldEquipped(Player p)
        {
            ItemDrop.ItemData b = GetBlocker(p);
            ItemDrop.ItemData.SharedData s = b != null ? b.m_shared : null;
            string mode = _cfgShieldMode.Value;
            if (mode == "None") return true;
            if (s == null || s.m_itemType != ItemDrop.ItemData.ItemType.Shield) return false;
            if (mode == "AnyShield") return true;
            return s.m_timedBlockBonus <= _cfgTowerParry.Value;   // tower shields do not parry
        }

        private void ForceTarget(MonsterAI ai, Character target)
        {
            if (ai == null || target == null || !CanForceTarget) return;
            _fiTargetCreature.SetValue(ai, target);
            _fiTargetStatic.SetValue(ai, null);
            _fiLastKnownTargetPos.SetValue(ai, target.transform.position);
            _fiBeenAtLastPos.SetValue(ai, false);
            _fiTimeSinceSensed.SetValue(ai, 0f);
            if (_miSetAlerted != null) _miSetAlerted.Invoke(ai, new object[] { true });
        }

        // ------------------------------------------------------------------
        // activation (called from TryActivate when a tower shield is equipped)
        // ------------------------------------------------------------------
        private void ActivateTaunt(Player p)
        {
            float now = Time.time;
            if (now < _gcdUntil) return;
            if (now < _tauntCdUntil) { Message(p, "Таунт: КД " + Mathf.CeilToInt(_tauntCdUntil - now) + "с"); return; }
            float cost = _cfgTauntStamina.Value;
            if (cost > 0f && !p.HaveStamina(cost)) { Message(p, "Таунт: не хватает стамины"); return; }
            if (cost > 0f) p.UseStamina(cost);

            // scaling: shield block power (tier) + Blocking skill
            ItemDrop.ItemData b = GetBlocker(p);
            float block = b != null ? b.GetBaseBlockPower(b.m_quality) : 0f;
            float shieldNorm = Mathf.Clamp01(block / Mathf.Max(1f, _cfgTauntRefBlock.Value));
            float skill = p.GetSkillFactor(Skills.SkillType.Blocking);
            float durBonus = Mathf.Clamp(skill * _cfgSkillDurationScale.Value * (0.5f + 0.5f * shieldNorm), 0f, 0.5f);

            _tauntReduction = Mathf.Clamp(Mathf.Lerp(_cfgTauntReductionBase.Value, _cfgTauntReductionBest.Value, 0.8f * shieldNorm + 0.2f * skill), 0f, 0.9f);
            _tauntUntil = now + _cfgTauntDuration.Value * (1f + durBonus);
            _tauntCdUntil = now + _cfgTauntCooldown.Value;
            _gcdUntil = now + _cfgGlobalCooldown.Value;
            _tauntNextReapply = now + ReapplyInterval;
            _tauntCount = ApplyTauntHold(p, _tauntUntil, _cfgTauntRadius.Value);
            Message(p, "Таунт! (" + _tauntCount + ")");
            Debug("Taunt: pulled " + _tauntCount + ", reduction " + FormatTime(_tauntReduction) + ", shield " + FormatTime(shieldNorm));
        }

        private const float ReapplyInterval = 0.25f;

        // Hold the monsters in radius onto a tank (us, or a modless player we taunt for).
        internal int ApplyTauntHold(Character tank, float until, float radius)
        {
            if (tank == null) return 0;
            Vector3 me = tank.transform.position;
            float r2 = radius * radius;
            ZDOID tankId = tank.GetZDOID();
            int n = 0;
            List<Character> all = Character.GetAllCharacters();
            for (int i = 0; i < all.Count; i++)
            {
                Character c = all[i];
                if (c == null || c == tank || c.IsDead() || c.IsPlayer()) continue;
                if (!_cfgTauntTamed.Value && c.IsTamed()) continue;
                if (c.IsBoss() && !_cfgTauntBosses.Value) continue;
                if ((c.transform.position - me).sqrMagnitude > r2) continue;
                MonsterAI ai = c.GetComponent<MonsterAI>();
                if (ai == null) continue;
                ZNetView nv = c.GetComponent<ZNetView>();
                if (nv == null || !nv.IsValid()) continue;

                if (!nv.IsOwner()) nv.ClaimOwnership();
                ForceTarget(ai, tank);
                if (_cfgTauntAggravate.Value && ai.IsAggravatable() && !ai.IsAggravated())
                    ai.SetAggravated(true, BaseAI.AggravatedReason.Damage);

                Hold h; h.Until = until; h.Target = tankId;
                _tainted[c.GetZDOID()] = h;
                n++;
            }
            return n;
        }

        // The tank a held monster belongs to, read by the UpdateTarget patch (runs on the owner
        // of the monster, which is us because we claimed it). May be us or a modless player.
        internal Character HeldTarget(ZDOID monster)
        {
            Hold h;
            if (!Active || !_tainted.TryGetValue(monster, out h) || Time.time >= h.Until) return null;
            Player local = Player.m_localPlayer;
            if (local != null && h.Target == local.GetZDOID()) return local;
            return ProxyTankChar(h.Target);
        }

        private void TauntTick(Player p, float now)
        {
            if (TauntActive)
            {
                if (now >= _tauntNextReapply) { _tauntNextReapply = now + ReapplyInterval; _tauntCount = ApplyTauntHold(p, _tauntUntil, _cfgTauntRadius.Value); }
            }
            if (_tainted.Count > 0)
            {
                _tauntScratch.Clear();
                foreach (KeyValuePair<ZDOID, Hold> kv in _tainted) if (now >= kv.Value.Until) _tauntScratch.Add(kv.Key);
                for (int i = 0; i < _tauntScratch.Count; i++) _tainted.Remove(_tauntScratch[i]);
            }
        }

        internal float TauntCooldownLeft() { return _tauntCdUntil - Time.time; }
    }
}
