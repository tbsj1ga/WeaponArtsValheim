using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace WeaponArts
{
    // Physical bleed (1H axe), distinct from the poison DoT of the knives. Registered on the
    // OWNER of the struck creature when a Bleed art hits, then ticked here as real physical
    // damage that bypasses armor (like poison does). Only creatures we currently own are
    // ticked; the attacker is carried for credit. The tick's own hit is flagged so the
    // RPC_Damage patch does not re-apply an art to it (no recursion).
    public partial class WeaponArtsPlugin
    {
        internal static bool s_bleedTick;

        private ConfigEntry<float> _cfgBleedSeconds;
        private ConfigEntry<float> _cfgBleedInterval;

        private struct Bleed { public float Dps; public float Until; public ZDOID Attacker; }
        private readonly Dictionary<ZDOID, Bleed> _bleeds = new Dictionary<ZDOID, Bleed>();
        private readonly List<ZDOID> _bleedScratch = new List<ZDOID>();
        private float _nextBleedTick;

        private void BindBleedConfig()
        {
            _cfgBleedSeconds = Config.Bind("05 Bleed", "BleedSeconds", 5f,
                new ConfigDescription("How long a bleed keeps ticking after the last hit that applied it.", new AcceptableValueRange<float>(1f, 30f)));
            _cfgBleedInterval = Config.Bind("05 Bleed", "TickInterval", 1f,
                new ConfigDescription("Seconds between bleed ticks.", new AcceptableValueRange<float>(0.25f, 5f)));
        }

        // Called from the RPC_Damage patch (owner of the victim) for a Bleed art hit.
        internal void RegisterBleed(Character victim, float dps, ZDOID attacker)
        {
            if (victim == null || dps <= 0f) return;
            Bleed b; b.Dps = dps; b.Until = Time.time + Sv(_cfgBleedSeconds); b.Attacker = attacker;
            _bleeds[victim.GetZDOID()] = b;
        }

        private void BleedTick(float now)
        {
            if (_bleeds.Count == 0 || now < _nextBleedTick) return;
            float interval = Mathf.Max(0.25f, _cfgBleedInterval.Value);
            _nextBleedTick = now + interval;

            _bleedScratch.Clear();
            foreach (KeyValuePair<ZDOID, Bleed> kv in _bleeds)
            {
                ZDOID id = kv.Key; Bleed b = kv.Value;
                Character c = ProxyTankChar(id);                 // FindInstance -> Character (any creature)
                if (c == null || c.IsDead() || now >= b.Until) { _bleedScratch.Add(id); continue; }
                ZNetView nv = c.GetComponent<ZNetView>();
                if (nv == null || !nv.IsValid() || !nv.IsOwner()) continue;   // only tick what we own

                HitData hit = new HitData();
                hit.m_damage.m_slash = b.Dps * interval;         // physical
                hit.m_attacker = b.Attacker;                     // credit the bleeder
                hit.m_point = c.transform.position;
                hit.m_dir = Vector3.up;
                s_bleedTick = true;
                try { c.Damage(hit); } finally { s_bleedTick = false; }
            }
            for (int i = 0; i < _bleedScratch.Count; i++) _bleeds.Remove(_bleedScratch[i]);
        }
    }
}
