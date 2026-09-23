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

        private ConfigEntry<bool> _cfgTauntChargeStraight;
        private ConfigEntry<float> _cfgTauntSkillGain;
        private ConfigEntry<float> _cfgTauntSkillCap;

        // hold: until, on whom, and since which activation (world ms) — a newer hold wins
        private struct Hold { public float Until; public ZDOID Target; public long Since; }
        private readonly Dictionary<ZDOID, Hold> _tainted = new Dictionary<ZDOID, Hold>();
        private readonly List<ZDOID> _tauntScratch = new List<ZDOID>();

        // cross-client arbitration marks in the monster's ZDO (see ApplyTauntHold)
        private static readonly int HoldByHash = "j1ga.weaponarts.holdby".GetStableHashCode();
        private static readonly int HoldUntilHash = "j1ga.weaponarts.holduntil".GetStableHashCode();
        private static readonly int HoldSinceHash = "j1ga.weaponarts.holdsince".GetStableHashCode();

        // straight charge: saved circling settings per held monster
        private struct AiSettings { public MonsterAI Ai; public float CircleInterval; public bool Circulate; public bool CirculateFlying; }
        private readonly Dictionary<ZDOID, AiSettings> _straight = new Dictionary<ZDOID, AiSettings>();

        private float _tauntUntil, _tauntCdUntil, _tauntNextReapply, _tauntReduction;
        private long _tauntSinceMs;
        private int _tauntCount;

        private static long WorldMs()
        {
            ZNet z = ZNet.instance;
            return z != null ? (long)(z.GetTimeSeconds() * 1000.0) : 0L;
        }

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
            _cfgTauntDuration = Config.Bind("04 Taunt", "Duration", 6f, new ConfigDescription("Seconds the aggro is held (before +50% cap scaling).", new AcceptableValueRange<float>(1f, 60f)));
            _cfgTauntCooldown = Config.Bind("04 Taunt", "Cooldown", 48f, new ConfigDescription("Seconds before reuse.", new AcceptableValueRange<float>(0f, 600f)));
            _cfgTauntStamina = Config.Bind("04 Taunt", "StaminaCost", 25f, new ConfigDescription("Stamina spent.", new AcceptableValueRange<float>(0f, 200f)));
            _cfgTauntReductionBase = Config.Bind("04 Taunt", "DamageReductionBase", 0.375f, new ConfigDescription("Damage reduction with the weakest tower shield and Blocking 0.", new AcceptableValueRange<float>(0f, 0.9f)));
            _cfgTauntReductionBest = Config.Bind("04 Taunt", "DamageReductionBest", 0.675f, new ConfigDescription("Damage reduction with the strongest shield and Blocking 100 (shield 80%, skill 20%).", new AcceptableValueRange<float>(0f, 0.9f)));
            _cfgTauntRefBlock = Config.Bind("04 Taunt", "RefBlockPower", 100f, new ConfigDescription("Shield block power that counts as full tier for scaling.", new AcceptableValueRange<float>(10f, 400f)));
            _cfgTauntBosses = Config.Bind("04 Taunt", "AffectBosses", true, "Also pull bosses (their area attacks still hit others).");
            _cfgTauntTamed = Config.Bind("04 Taunt", "AffectTamed", false, "Also affect tamed creatures.");
            _cfgTauntAggravate = Config.Bind("04 Taunt", "AggravateNeutral", true, "Wake and anger neutral monsters so they engage.");
            _cfgTauntChargeStraight = Config.Bind("04 Taunt", "ChargeStraight", true, "Held monsters charge straight at the tank instead of circling.");
            _cfgTauntSkillGain = Config.Bind("04 Taunt", "SkillGainPerMonster", 0.5f, new ConfigDescription("Blocking experience per monster pulled, once per activation.", new AcceptableValueRange<float>(0f, 5f)));
            _cfgTauntSkillCap = Config.Bind("04 Taunt", "SkillGainCap", 3f, new ConfigDescription("Most Blocking experience one taunt can give.", new AcceptableValueRange<float>(0f, 20f)));
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
            string mode = Ss(_cfgShieldMode);
            if (mode == "None") return true;
            if (s == null || s.m_itemType != ItemDrop.ItemData.ItemType.Shield) return false;
            if (mode == "AnyShield") return true;
            return s.m_timedBlockBonus <= Sv(_cfgTowerParry);   // tower shields do not parry
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
            if (now < _gcdUntil) { Message(p, "Общий КД: " + Mathf.CeilToInt(_gcdUntil - now) + "с"); return; }
            if (now < _tauntCdUntil) { Message(p, "Таунт: КД " + Mathf.CeilToInt(_tauntCdUntil - now) + "с"); return; }
            float cost = _cfgTauntStamina.Value;
            if (cost > 0f && !p.HaveStamina(cost)) { Message(p, "Таунт: не хватает стамины"); return; }
            if (cost > 0f) p.UseStamina(cost);

            // scaling: shield block power (tier) + Blocking skill
            ItemDrop.ItemData b = GetBlocker(p);
            float block = b != null ? b.GetBaseBlockPower(b.m_quality) : 0f;
            float shieldNorm = Mathf.Clamp01(block / Mathf.Max(1f, Sv(_cfgTauntRefBlock)));
            float skill = p.GetSkillFactor(Skills.SkillType.Blocking);
            float durBonus = Mathf.Clamp(skill * Sv(_cfgSkillDurationScale) * (0.5f + 0.5f * shieldNorm), 0f, 0.5f);

            _tauntReduction = Mathf.Clamp(Mathf.Lerp(Sv(_cfgTauntReductionBase), Sv(_cfgTauntReductionBest), 0.8f * shieldNorm + 0.2f * skill), 0f, 0.9f);
            _tauntUntil = now + Sv(_cfgTauntDuration) * (1f + durBonus);
            _tauntCdUntil = now + _cfgTauntCooldown.Value;
            _gcdUntil = now + _cfgGlobalCooldown.Value;
            _tauntNextReapply = now + ReapplyInterval;
            _tauntSinceMs = WorldMs();
            _tauntCount = ApplyTauntHold(p, _tauntUntil, Sv(_cfgTauntRadius), _tauntSinceMs);
            if (_tauntCount > 0) p.RaiseSkill(Skills.SkillType.Blocking, Mathf.Min(_cfgTauntSkillCap.Value, _cfgTauntSkillGain.Value * _tauntCount));
            try { PlayActivationEffects(p, b != null && b.m_shared != null ? b.m_shared.m_blockEffect : null); } catch (Exception e) { Fail("effects", e); }
            Message(p, "Таунт! (" + _tauntCount + ")");
            Debug("Taunt: pulled " + _tauntCount + ", reduction " + FormatTime(_tauntReduction) + ", shield " + FormatTime(shieldNorm));
        }

        private const float ReapplyInterval = 0.25f;

        // Hold the monsters in radius onto a tank (us, or a modless player we taunt for). A
        // monster held on someone else - by us for another tank, or by another modded client
        // (its mark in the monster's ZDO) - goes to whichever hold was activated later; the
        // older hold drops it and takes it back only once the newer mark has run out. sinceMs is
        // the activation time (world ms) of the hold being applied.
        internal int ApplyTauntHold(Character tank, float until, float radius, long sinceMs)
        {
            if (tank == null) return 0;
            Vector3 me = tank.transform.position;
            float r2 = radius * radius;
            ZDOID tankId = tank.GetZDOID();
            float now = Time.time;
            long worldNow = WorldMs();
            long session = ZDOMan.GetSessionID();
            bool straight = Sb(_cfgTauntChargeStraight);
            bool aggravate = Sb(_cfgTauntAggravate);
            int n = 0;
            List<Character> all = Character.GetAllCharacters();
            for (int i = 0; i < all.Count; i++)
            {
                Character c = all[i];
                if (c == null || c == tank || c.IsDead() || c.IsPlayer()) continue;
                if (!Sb(_cfgTauntTamed) && c.IsTamed()) continue;
                if (c.IsBoss() && !Sb(_cfgTauntBosses)) continue;
                if ((c.transform.position - me).sqrMagnitude > r2) continue;
                MonsterAI ai = c.GetComponent<MonsterAI>();
                if (ai == null) continue;
                ZNetView nv = c.GetComponent<ZNetView>();
                if (nv == null || !nv.IsValid()) continue;

                ZDOID id = c.GetZDOID();
                Hold held;
                if (_tainted.TryGetValue(id, out held) && held.Target != tankId && now < held.Until && held.Since > sinceMs) continue;
                ZDO zdo = nv.GetZDO();
                long heldBy = zdo.GetLong(HoldByHash, 0L);
                long heldUntil = zdo.GetLong(HoldUntilHash, 0L);
                if (heldBy != 0L && heldBy != session && heldUntil > worldNow)
                {
                    long heldSince = zdo.GetLong(HoldSinceHash, 0L);
                    if (heldSince > sinceMs || (heldSince == sinceMs && heldBy < session))
                    {
                        if (_tainted.Remove(id)) ReleaseStraight(id);   // theirs is newer
                        continue;
                    }
                }

                if (!nv.IsOwner()) nv.ClaimOwnership();
                ForceTarget(ai, tank);
                if (straight) HoldStraight(id, ai);
                if (aggravate && ai.IsAggravatable() && !ai.IsAggravated())
                    ai.SetAggravated(true, BaseAI.AggravatedReason.Damage);

                held.Until = until; held.Target = tankId; held.Since = sinceMs;
                _tainted[id] = held;

                long untilMs = worldNow + (long)((until - now) * 1000f);
                if (heldBy != session || zdo.GetLong(HoldSinceHash, 0L) != sinceMs || heldUntil < untilMs - 500L)
                {
                    zdo.Set(HoldByHash, session);
                    zdo.Set(HoldSinceHash, sinceMs);
                    zdo.Set(HoldUntilHash, untilMs);
                }
                n++;
            }
            return n;
        }

        // ------------------------------------------------------------------
        // straight charge: no circling while held (per-instance fields on the owner, us)
        // ------------------------------------------------------------------
        private void HoldStraight(ZDOID id, MonsterAI ai)
        {
            if (ai == null) return;
            AiSettings saved;
            if (_straight.TryGetValue(id, out saved))
            {
                if (saved.Ai == ai) return;
                RestoreAi(saved);
            }
            saved.Ai = ai;
            saved.CircleInterval = ai.m_circleTargetInterval;
            saved.Circulate = ai.m_circulateWhileCharging;
            saved.CirculateFlying = ai.m_circulateWhileChargingFlying;
            _straight[id] = saved;
            ai.m_circleTargetInterval = 0f;
            ai.m_circulateWhileCharging = false;
            ai.m_circulateWhileChargingFlying = false;
        }

        private void ReleaseStraight(ZDOID id)
        {
            AiSettings saved;
            if (!_straight.TryGetValue(id, out saved)) return;
            _straight.Remove(id);
            RestoreAi(saved);
        }

        internal void ReleaseAllStraight()
        {
            foreach (KeyValuePair<ZDOID, AiSettings> kv in _straight) RestoreAi(kv.Value);
            _straight.Clear();
        }

        private static void RestoreAi(AiSettings s)
        {
            if (s.Ai == null) return;
            s.Ai.m_circleTargetInterval = s.CircleInterval;
            s.Ai.m_circulateWhileCharging = s.Circulate;
            s.Ai.m_circulateWhileChargingFlying = s.CirculateFlying;
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
                if (now >= _tauntNextReapply) { _tauntNextReapply = now + ReapplyInterval; _tauntCount = ApplyTauntHold(p, _tauntUntil, Sv(_cfgTauntRadius), _tauntSinceMs); }
            }
            if (_tainted.Count > 0)
            {
                _tauntScratch.Clear();
                foreach (KeyValuePair<ZDOID, Hold> kv in _tainted) if (now >= kv.Value.Until) _tauntScratch.Add(kv.Key);
                for (int i = 0; i < _tauntScratch.Count; i++) { _tainted.Remove(_tauntScratch[i]); ReleaseStraight(_tauntScratch[i]); }
            }
        }

        internal float TauntCooldownLeft() { return _tauntCdUntil - Time.time; }
    }
}
