using System;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace WeaponArts
{
    public partial class WeaponArtsPlugin
    {
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
            private static void Prefix(Character __instance, HitData hit, out float __state)
            {
                __state = -1f;
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
                    ZDO z = OwnZdo(attacker);
                    if (z == null) return;
                    int artHash = z.GetInt(ZdoArt, 0);
                    if (artHash == 0) return;
                    long until = z.GetLong(ZdoUntil, 0L);
                    if (until == 0L || NowTicks() >= until) return;
                    Art a;
                    if (!p._artByHash.TryGetValue(artHash, out a)) return;
                    float power = z.GetFloat(ZdoPower, 0f);
                    if (power <= 0f) return;
                    bool boss = __instance.IsBoss();
                    float bf = boss ? p._cfgBossFactor.Value : 1f;

                    switch (a.Kind)
                    {
                        case ArtKind.DamageMult:
                            hit.ApplyModifier(1f + (power - 1f) * bf);
                            break;
                        case ArtKind.Dot:
                            hit.m_damage.m_poison += power * bf;        // applied as poison DoT
                            break;
                        case ArtKind.Stagger:
                            if (!boss) hit.m_staggerMultiplier *= power; // bosses are stagger-immune
                            break;
                        case ArtKind.Vampirism:
                            __state = __instance.GetHealth();           // measured in the postfix
                            break;
                    }
                }
                catch (Exception e) { p.Fail("RPC_Damage", e); }
            }

            private static void Postfix(Character __instance, HitData hit, float __state)
            {
                WeaponArtsPlugin p = Instance;
                if (p == null || __state < 0f || __instance == null || hit == null) return;
                try
                {
                    float dealt = __state - __instance.GetHealth();
                    if (dealt <= 0f) return;
                    Character attacker = hit.GetAttacker();
                    if (attacker == null) return;
                    ZDO z = OwnZdo(attacker);
                    if (z == null) return;
                    float frac = z.GetFloat(ZdoPower, 0f);
                    if (frac <= 0f) return;
                    float bf = __instance.IsBoss() ? p._cfgBossFactor.Value : 1f;
                    float heal = dealt * frac * bf;
                    if (heal <= 0f) return;

                    ZNetView nv = attacker.GetComponent<ZNetView>();
                    if (nv == null || !nv.IsValid()) return;
                    if (nv.IsOwner()) attacker.Heal(heal, false);
                    else nv.InvokeRPC(nv.GetZDO().GetOwner(), "Heal", heal, false);
                }
                catch (Exception e) { p.Fail("RPC_Damage.vamp", e); }
            }
        }
    }
}
