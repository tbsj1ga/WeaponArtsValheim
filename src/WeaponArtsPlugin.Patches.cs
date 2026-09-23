using System;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace WeaponArts
{
    public partial class WeaponArtsPlugin
    {
        // Resistance ignore for the hit currently being applied in RPC_Damage: set in its prefix
        // for a Pierce art (or a bleed tick), read by the HitData.ApplyResistance patch (called
        // once inside RPC_Damage), cleared in the postfix and a finalizer. Creatures have no body
        // armor in Valheim (Character.GetBodyArmor is a constant 0), resistances are their armor.
        internal static bool s_penActive;
        internal static float s_penFraction;

        private static readonly System.Reflection.FieldInfo s_backstabTime = AccessTools.Field(typeof(Character), "m_backstabTime");
        private const float BackstabCooldown = 300f;   // Character.RPC_Damage: one sneak bonus per 5 min

        // Same condition the game uses for the sneak bonus: a weapon bonus, an unalerted AI, and
        // no sneak bonus on this victim in the last 5 minutes.
        private static bool WillBackstab(Character victim, HitData hit)
        {
            if (victim == null || hit.m_backstabBonus <= 1f) return false;
            BaseAI ai = victim.GetBaseAI();
            if (ai == null || ai.IsAlerted()) return false;
            if (s_backstabTime == null) return true;
            return Time.time - (float)s_backstabTime.GetValue(victim) > BackstabCooldown;
        }

        // Per-hit record for the postfix (vampirism + the debug damage log).
        private class HitState
        {
            public Art Art;
            public float Hp;
            public float Before;
            public float After;
        }

        // ------------------------------------------------------------------
        // taunt: hold the pulled monsters on the tank (owner side)
        // ------------------------------------------------------------------
        // MonsterAI.UpdateTarget runs on the owner of the monster (us, since we claimed it);
        // overwrite the recomputed target with the tank while the hold lasts.
        [HarmonyPatch(typeof(MonsterAI), "UpdateTarget")]
        private static class MonsterAI_UpdateTarget_Patch
        {
            private static void Postfix(MonsterAI __instance)
            {
                WeaponArtsPlugin p = Instance;
                if (p == null || p._tainted.Count == 0 || __instance == null) return;
                try
                {
                    Character c = __instance.GetComponent<Character>();
                    if (c == null) return;
                    Character tank = p.HeldTarget(c.GetZDOID());
                    if (tank == null || tank.IsDead()) return;
                    if (__instance.GetTargetCreature() != tank) p.ForceTarget(__instance, tank);
                }
                catch (Exception e) { p.Fail("UpdateTarget", e); }
            }
        }

        // ------------------------------------------------------------------
        // apply the attacker's art on the OWNER of the struck creature
        // ------------------------------------------------------------------
        // Character.RPC_Damage runs on the owner of the victim; the attacker's active art is read
        // from its ZDO (power already baked with skill/tier at activation) and the HitData edited.
        // Only ever against creatures (never players); bosses take BossEffectFactor and are immune
        // to stagger.
        [HarmonyPatch(typeof(Character), "RPC_Damage")]
        private static class Character_RPC_Damage_Patch
        {
            private static void Prefix(Character __instance, HitData hit, out HitState __state)
            {
                __state = null;
                if (s_bleedTick) { s_penActive = true; s_penFraction = 1f; return; }   // bleed tick: true damage, no art
                s_penActive = false;                                   // clean per hit
                WeaponArtsPlugin p = Instance;
                if (p == null || !p.Active || __instance == null || hit == null) return;
                try
                {
                    // taunt: reduce damage taken by the local tank while the taunt holds
                    if (p.TauntActive && __instance == Player.m_localPlayer)
                    {
                        float red = Mathf.Clamp(p._tauntReduction, 0f, 0.9f);
                        if (red > 0f) hit.ApplyModifier(1f - red);
                    }
                    if (__instance.IsPlayer()) return;                 // PvP-safe: creatures only
                    Character attacker = hit.GetAttacker();
                    if (attacker == null || !attacker.IsPlayer()) return;
                    float before = TotalDamage(hit.m_damage);
                    bool boss = __instance.IsBoss();
                    float bf = boss ? p.Sv(p._cfgBossFactor) : 1f;

                    // exposed (battleaxe mark): every player's hit on this creature hurts more
                    float exposed = p.ExposedFraction(__instance);
                    if (exposed > 0f) hit.ApplyModifier(1f + exposed);

                    Art a; float power;
                    if (!p.AttackerArt(attacker, out a, out power))
                    {
                        if (exposed > 0f && p._cfgDebug.Value)
                        {
                            __state = new HitState();
                            __state.Hp = __instance.GetHealth(); __state.Before = before; __state.After = TotalDamage(hit.m_damage);
                        }
                        return;
                    }

                    switch (a.Kind)
                    {
                        case ArtKind.DamageMult:
                            // crit arts do not stack on top of a real sneak hit. m_backstabBonus is
                            // the weapon's multiplier and is set on EVERY hit; the game applies it
                            // only on an unalerted AI, once per 5 min (Character.RPC_Damage).
                            if (a.NoStackSneak && WillBackstab(__instance, hit)) break;
                            hit.ApplyModifier(1f + (power - 1f) * bf);
                            break;
                        case ArtKind.Dot:
                            hit.m_damage.m_poison += power * bf;        // poison DoT (knives)
                            break;
                        case ArtKind.Bleed:
                            p.RegisterBleed(__instance, power * bf, hit.m_attacker);   // physical DoT (axe)
                            break;
                        case ArtKind.Stagger:
                            // RPC_Damage staggers outright when the multiplier is >= 100, before
                            // any stagger-bar maths; bosses are stagger-immune.
                            if (!boss) hit.m_staggerMultiplier = Mathf.Max(hit.m_staggerMultiplier, 100f);
                            break;
                        case ArtKind.Pierce:
                            s_penActive = true;                         // ApplyResistance patch reads this
                            s_penFraction = Mathf.Clamp(power * bf, 0f, 0.95f);
                            if (a.Bonus != null) hit.ApplyModifier(1f + p.Sv(a.Bonus) * bf);
                            break;
                        case ArtKind.Expose:
                            p.MarkExposed(__instance, Mathf.Clamp(power * bf, 0f, 1f), p.Sv(a.Linger));
                            break;
                    }
                    if (a.Kind == ArtKind.Vampirism || p._cfgDebug.Value)
                    {
                        __state = new HitState();
                        __state.Art = a; __state.Hp = __instance.GetHealth();
                        __state.Before = before; __state.After = TotalDamage(hit.m_damage);
                    }
                }
                catch (Exception e) { p.Fail("RPC_Damage", e); }
            }

            private static void Finalizer() { s_penActive = false; }

            private static void Postfix(Character __instance, HitData hit, HitState __state)
            {
                s_penActive = false;                                   // resistances already applied
                WeaponArtsPlugin p = Instance;
                if (p == null || __state == null || __instance == null || hit == null) return;
                try
                {
                    float dealt = __state.Hp - __instance.GetHealth();
                    Art a = __state.Art;
                    p.Debug("hit " + __instance.name + " by " + (a != null ? a.Id : "-")
                            + ": damage " + __state.Before.ToString("0.0") + " -> " + __state.After.ToString("0.0")
                            + " (x" + (__state.Before > 0f ? __state.After / __state.Before : 1f).ToString("0.00")
                            + "), hp lost " + dealt.ToString("0.0") + ", exposed " + p.ExposedFraction(__instance).ToString("0.00")
                            + ", staggering " + __instance.IsStaggering());
                    if (a == null || a.Kind != ArtKind.Vampirism || dealt <= 0f) return;
                    Character attacker = hit.GetAttacker();
                    if (attacker == null) return;
                    Art cur; float frac;
                    if (!p.AttackerArt(attacker, out cur, out frac) || cur.Kind != ArtKind.Vampirism || frac <= 0f) return;
                    float bf = __instance.IsBoss() ? p.Sv(p._cfgBossFactor) : 1f;
                    float heal = dealt * frac * bf;
                    if (heal <= 0f) return;

                    ZNetView nv = attacker.GetComponent<ZNetView>();
                    if (nv == null || !nv.IsValid()) return;
                    if (nv.IsOwner()) attacker.Heal(heal, false);
                    else nv.InvokeRPC(nv.GetZDO().GetOwner(), "RPC_Heal", heal, false);   // registered in Character.Awake
                }
                catch (Exception e) { p.Fail("RPC_Damage.post", e); }
            }
        }

        // ------------------------------------------------------------------
        // resistance ignore: give back a share of what resistances took (not immunities)
        // ------------------------------------------------------------------
        [HarmonyPatch(typeof(HitData), "ApplyResistance")]
        private static class HitData_ApplyResistance_Patch
        {
            private static void Prefix(HitData __instance, out HitData.DamageTypes __state)
            {
                __state = __instance.m_damage;                         // struct copy
            }

            private static void Postfix(HitData __instance, HitData.DamageTypes __state)
            {
                if (!s_penActive || s_penFraction <= 0f) return;
                float f = s_penFraction;
                __instance.m_damage.m_blunt = Restore(__state.m_blunt, __instance.m_damage.m_blunt, f);
                __instance.m_damage.m_slash = Restore(__state.m_slash, __instance.m_damage.m_slash, f);
                __instance.m_damage.m_pierce = Restore(__state.m_pierce, __instance.m_damage.m_pierce, f);
                __instance.m_damage.m_fire = Restore(__state.m_fire, __instance.m_damage.m_fire, f);
                __instance.m_damage.m_frost = Restore(__state.m_frost, __instance.m_damage.m_frost, f);
                __instance.m_damage.m_lightning = Restore(__state.m_lightning, __instance.m_damage.m_lightning, f);
                __instance.m_damage.m_poison = Restore(__state.m_poison, __instance.m_damage.m_poison, f);
                __instance.m_damage.m_spirit = Restore(__state.m_spirit, __instance.m_damage.m_spirit, f);
            }

            // reduced but not zeroed (immune stays immune); weaknesses are left as they are
            private static float Restore(float before, float after, float f)
            {
                if (after <= 0f || after >= before) return after;
                return after + (before - after) * f;
            }
        }

        // ------------------------------------------------------------------
        // shot-counted arts: count the local player's projectiles
        // ------------------------------------------------------------------
        [HarmonyPatch(typeof(Projectile), "Setup")]
        private static class Projectile_Setup_Patch
        {
            private static void Postfix(Character owner, ItemDrop.ItemData item)
            {
                WeaponArtsPlugin p = Instance;
                if (p == null || owner == null || owner != Player.m_localPlayer) return;
                try { p.OnLocalShot(item); } catch (Exception e) { p.Fail("Projectile.Setup", e); }
            }
        }

        // ------------------------------------------------------------------
        // proxy: trigger by chat word, and reduce a proxy-tank's incoming hits
        // ------------------------------------------------------------------
        [HarmonyPatch(typeof(Chat), "OnNewChatMessage")]
        private static class Chat_OnNewChatMessage_Patch
        {
            private static void Postfix(long senderID, Talker.Type type, string text)
            {
                WeaponArtsPlugin p = Instance;
                if (p == null || !p.Active) return;
                try { p.OnChat(senderID, type, text); } catch (Exception e) { p.Fail("chat", e); }
            }
        }

        // Character.Damage on our client, before a hit is sent to its victim: a hit from a
        // monster held for a modless tank is reduced here (their client cannot do it).
        [HarmonyPatch(typeof(Character), "Damage")]
        private static class Character_Damage_Patch
        {
            private static void Prefix(Character __instance, HitData hit)
            {
                WeaponArtsPlugin p = Instance;
                if (p == null || hit == null || p._proxies.Count == 0) return;
                try { p.ReduceProxyHit(__instance, hit); } catch (Exception e) { p.Fail("Damage", e); }
            }
        }
    }
}
