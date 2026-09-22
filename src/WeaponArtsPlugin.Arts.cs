using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace WeaponArts
{
    public partial class WeaponArtsPlugin
    {
        // ------------------------------------------------------------------
        // art model + registry
        // ------------------------------------------------------------------
        internal enum ArtKind { DamageMult, Dot, Stagger, Vampirism }

        internal class Art
        {
            public string Id;
            public int Hash;
            public string Name;
            public string Desc;
            public ArtKind Kind;
            public Skills.SkillType Skill;
            public int Hand;            // 0 = any, 1 = one-handed, 2 = two-handed
            public bool UsesEitr;       // else stamina
            public ConfigEntry<float> Mag, Win, Cd, Cost;
        }

        private readonly List<Art> _arts = new List<Art>();
        private readonly Dictionary<int, Art> _artByHash = new Dictionary<int, Art>();

        private Art Add(string id, string name, string desc, ArtKind kind, Skills.SkillType skill, int hand,
                        float mag, float win, float cd, float cost, bool eitr)
        {
            Art a = new Art();
            a.Id = id; a.Hash = id.GetStableHashCode(); a.Name = name; a.Desc = desc;
            a.Kind = kind; a.Skill = skill; a.Hand = hand; a.UsesEitr = eitr;
            string s = "03 Arts - " + id;
            a.Mag = Config.Bind(s, "Magnitude", mag, "Base strength (see the art). Damage arts are a multiplier; DoT is poison per hit; stagger is a multiplier; lifesteal is a fraction.");
            a.Win = Config.Bind(s, "Window", win, new ConfigDescription("Seconds the art lasts (before skill/tier scaling; +50% cap).", new AcceptableValueRange<float>(0.5f, 30f)));
            a.Cd = Config.Bind(s, "Cooldown", cd, new ConfigDescription("Seconds before reuse.", new AcceptableValueRange<float>(1f, 600f)));
            a.Cost = Config.Bind(s, "Cost", cost, new ConfigDescription("Stamina (or eitr) spent.", new AcceptableValueRange<float>(0f, 200f)));
            _arts.Add(a); _artByHash[a.Hash] = a;
            return a;
        }

        private void BuildArts()
        {
            Skills.SkillType Sw = Skills.SkillType.Swords, Ax = Skills.SkillType.Axes, Sp = Skills.SkillType.Spears,
                Po = Skills.SkillType.Polearms, Kn = Skills.SkillType.Knives, Un = Skills.SkillType.Unarmed,
                Bo = Skills.SkillType.Bows, Cr = Skills.SkillType.Crossbows;

            // Phase 1: on-target combat arts (applied on the owner of the struck creature).
            Add("Onslaught", "Натиск", "меч 2H: +урон по цели", ArtKind.DamageMult, Sw, 2, 1.35f, 4f, 36f, 30f, false);
            Add("Bloodthirst", "Кровожадность", "меч 1H: вампиризм с урона", ArtKind.Vampirism, Sw, 1, 0.15f, 4f, 40f, 25f, false);
            Add("Rend", "Рассечение", "боевой топор: цель получает больше урона", ArtKind.DamageMult, Ax, 2, 1.40f, 4f, 36f, 30f, false);
            Add("Bleed", "Кровотечение", "топор 1H: DoT на ударах", ArtKind.Dot, Ax, 1, 12f, 4f, 32f, 25f, false);
            Add("Pierce", "Пробитие", "копьё: +урон сквозь броню", ArtKind.DamageMult, Sp, 1, 1.35f, 4f, 32f, 25f, false);
            Add("Impale", "Пронзание", "пика: мощный выпад", ArtKind.DamageMult, Sp, 2, 1.60f, 3f, 40f, 30f, false);
            Add("Crushing", "Дробящий", "атгейр: удары вгоняют в стаггер", ArtKind.Stagger, Po, 0, 2.5f, 4f, 40f, 30f, false);
            Add("Envenom", "Отравление", "ножи: сильный яд на ударах", ArtKind.Dot, Kn, 0, 18f, 4f, 32f, 25f, false);
            Add("Fury", "Ярость", "кулаки: критический урон", ArtKind.DamageMult, Un, 0, 1.5f, 4f, 36f, 25f, false);
            Add("Focus", "Фокус", "лук: критические выстрелы", ArtKind.DamageMult, Bo, 0, 1.8f, 4f, 40f, 20f, false);
            Add("PiercingBolts", "Бронебой", "арбалет: болты сквозь броню", ArtKind.DamageMult, Cr, 0, 1.5f, 4f, 40f, 20f, false);
        }

        // ------------------------------------------------------------------
        // weapon -> art
        // ------------------------------------------------------------------
        private static int HandOf(ItemDrop.ItemData.ItemType t)
        {
            if (t == ItemDrop.ItemData.ItemType.OneHandedWeapon) return 1;
            if (t == ItemDrop.ItemData.ItemType.TwoHandedWeapon
                || t == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft
                || t == ItemDrop.ItemData.ItemType.Attach_Atgeir) return 2;
            return 0;
        }

        internal Art ArtFor(ItemDrop.ItemData weapon)
        {
            if (weapon == null || weapon.m_shared == null) return null;
            Skills.SkillType sk = weapon.m_shared.m_skillType;
            int hand = HandOf(weapon.m_shared.m_itemType);
            for (int i = 0; i < _arts.Count; i++)
            {
                Art a = _arts[i];
                if (a.Skill != sk) continue;
                if (a.Hand != 0 && a.Hand != hand) continue;
                return a;
            }
            return null;
        }

        // Art of the local player's equipped weapon (for the HUD and activation).
        internal Art CurrentArt(Player p)
        {
            if (p == null) return null;
            return ArtFor(p.GetCurrentWeapon());
        }

        private static float WeaponTotalDamage(ItemDrop.ItemData w)
        {
            if (w == null) return 0f;
            HitData.DamageTypes d = w.GetDamage();
            return d.m_blunt + d.m_slash + d.m_pierce + d.m_fire + d.m_frost + d.m_lightning + d.m_poison + d.m_spirit;
        }

        // ------------------------------------------------------------------
        // activation
        // ------------------------------------------------------------------
        private void TryActivate(Player p)
        {
            ItemDrop.ItemData weapon = p.GetCurrentWeapon();
            Art a = ArtFor(weapon);
            if (a == null) { Message(p, "Нет активки для этого оружия"); return; }

            float now = Time.time;
            if (now < _gcdUntil) return;
            float cdUntil;
            if (_cooldownUntil.TryGetValue(a.Id, out cdUntil) && now < cdUntil)
            {
                Message(p, a.Name + ": КД " + Mathf.CeilToInt(cdUntil - now) + "с");
                return;
            }
            float cost = a.Cost.Value;
            if (cost > 0f)
            {
                bool ok = a.UsesEitr ? p.HaveEitr(cost) : p.HaveStamina(cost);
                if (!ok) { Message(p, a.Name + ": не хватает " + (a.UsesEitr ? "эйтра" : "стамины")); return; }
            }

            // scaling: tier from weapon damage, skill from the weapon's skill
            float tierNorm = Mathf.Clamp01(WeaponTotalDamage(weapon) / Mathf.Max(1f, _cfgRefWeaponDamage.Value));
            float skill = p.GetSkillFactor(a.Skill);                 // 0..1
            float s = (0.5f + 0.5f * tierNorm) * (1f + skill * _cfgSkillPowerScale.Value);
            float durBonus = Mathf.Clamp(skill * _cfgSkillDurationScale.Value * (0.5f + 0.5f * tierNorm), 0f, 0.5f);

            float power;
            if (a.Kind == ArtKind.DamageMult || a.Kind == ArtKind.Stagger)
                power = 1f + (a.Mag.Value - 1f) * s;                 // scale the bonus part
            else
                power = a.Mag.Value * s;                             // DoT amount / lifesteal fraction

            float window = a.Win.Value * (1f + durBonus);

            if (cost > 0f) { if (a.UsesEitr) p.UseEitr(cost); else p.UseStamina(cost); }

            ZDO z = OwnZdo(p);
            if (z != null)
            {
                z.Set(ZdoArt, a.Hash, false);
                z.Set(ZdoUntil, NowTicks() + (long)(window * TimeSpan.TicksPerSecond));
                z.Set(ZdoPower, power);
            }

            _cooldownUntil[a.Id] = now + a.Cd.Value;
            _gcdUntil = now + _cfgGlobalCooldown.Value;
            _lastArtId = a.Id; _lastActivated = now;

            Message(p, a.Name + "!");
            Debug("Activated " + a.Id + ": power " + FormatTime(power) + ", window " + FormatTime(window) + "s (tier " + FormatTime(tierNorm) + ", skill " + FormatTime(skill) + ")");
        }

        // Cooldown remaining for the HUD; <=0 means ready.
        internal float CooldownLeft(Art a)
        {
            float u;
            return _cooldownUntil.TryGetValue(a.Id, out u) ? (u - Time.time) : 0f;
        }
    }
}
