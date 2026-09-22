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
        internal enum ArtKind { DamageMult, Dot, Stagger, Vampirism, AoEHeal, AoEBurst }

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
            // instant arts (AoEHeal / AoEBurst)
            public ConfigEntry<float> Radius, HLo0, HLo1, HHi0, HHi1;
            public bool IncludeSelf;
            public bool NoStackSneak;   // crit arts: do not add on top of a backstab
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

        // AoE heal: rolls random(lo, hi) once; the window [lo0..lo1],[hi0..hi1] grows with
        // weapon tier + skill (see TASK-weapon-arts.md §5.1). Not a timed window.
        private Art AddHeal(string id, string name, string desc, Skills.SkillType skill, int hand,
                            float cd, float cost, float lo0, float lo1, float hi0, float hi1, float radius, bool self)
        {
            Art a = new Art();
            a.Id = id; a.Hash = id.GetStableHashCode(); a.Name = name; a.Desc = desc;
            a.Kind = ArtKind.AoEHeal; a.Skill = skill; a.Hand = hand; a.IncludeSelf = self;
            string s = "03 Arts - " + id;
            a.Cd = Config.Bind(s, "Cooldown", cd, new ConfigDescription("Seconds before reuse.", new AcceptableValueRange<float>(1f, 600f)));
            a.Cost = Config.Bind(s, "Cost", cost, new ConfigDescription("Stamina spent.", new AcceptableValueRange<float>(0f, 200f)));
            a.Radius = Config.Bind(s, "Radius", radius, new ConfigDescription("Heal radius, m.", new AcceptableValueRange<float>(2f, 40f)));
            a.HLo0 = Config.Bind(s, "HealLoMin", lo0, "Lower bound of the heal roll at min tier/skill.");
            a.HLo1 = Config.Bind(s, "HealLoMax", lo1, "Lower bound at max tier/skill.");
            a.HHi0 = Config.Bind(s, "HealHiMin", hi0, "Upper bound of the heal roll at min tier/skill.");
            a.HHi1 = Config.Bind(s, "HealHiMax", hi1, "Upper bound at max tier/skill.");
            _arts.Add(a); _artByHash[a.Hash] = a;
            return a;
        }

        // AoE burst: instant damage to creatures around you, Mag = fraction of weapon damage.
        private Art AddBurst(string id, string name, string desc, Skills.SkillType skill, int hand,
                             float mag, float cd, float cost, float radius)
        {
            Art a = new Art();
            a.Id = id; a.Hash = id.GetStableHashCode(); a.Name = name; a.Desc = desc;
            a.Kind = ArtKind.AoEBurst; a.Skill = skill; a.Hand = hand;
            string s = "03 Arts - " + id;
            a.Mag = Config.Bind(s, "Magnitude", mag, "Damage as a fraction of weapon damage (1.3 = 130%).");
            a.Cd = Config.Bind(s, "Cooldown", cd, new ConfigDescription("Seconds before reuse.", new AcceptableValueRange<float>(1f, 600f)));
            a.Cost = Config.Bind(s, "Cost", cost, new ConfigDescription("Stamina spent.", new AcceptableValueRange<float>(0f, 200f)));
            a.Radius = Config.Bind(s, "Radius", radius, new ConfigDescription("Burst radius, m.", new AcceptableValueRange<float>(1f, 20f)));
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
            Add("Fury", "Ярость", "кулаки: критический урон", ArtKind.DamageMult, Un, 0, 1.5f, 4f, 36f, 25f, false).NoStackSneak = true;
            Add("Focus", "Фокус", "лук: критические выстрелы", ArtKind.DamageMult, Bo, 0, 1.8f, 4f, 40f, 20f, false).NoStackSneak = true;
            Add("PiercingBolts", "Бронебой", "арбалет: болты сквозь броню", ArtKind.DamageMult, Cr, 0, 1.5f, 4f, 40f, 20f, false);

            // Phase 2: heals, AoE burst, eitr surge.
            Skills.SkillType Cl = Skills.SkillType.Clubs, El = Skills.SkillType.ElementalMagic, Bl = Skills.SkillType.BloodMagic;
            AddHeal("Rally", "Клич", "кувалда 2H: хил себе и союзникам", Cl, 2, 60f, 40f, 15f, 60f, 25f, 100f, 10f, true);
            AddHeal("Mend", "Исцеление", "булава 1H: хил только союзникам", Cl, 1, 50f, 30f, 10f, 40f, 16f, 65f, 8f, false);
            Add("EitrSurgeElem", "Вспышка эйтра", "посох стихий: +магический урон", ArtKind.DamageMult, El, 0, 1.4f, 4f, 50f, 30f, true);
            Add("EitrSurgeBlood", "Вспышка эйтра", "посох крови: +магический урон", ArtKind.DamageMult, Bl, 0, 1.4f, 4f, 50f, 30f, true);
            // Whirlwind (кистень): flail skillType не подтверждён (в ваниле может совпасть с
            // Clubs 2H и конфликтовать с Rally) — регистрируется после проверки в игре.
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

        internal static float TotalDamage(HitData.DamageTypes d)
        {
            return d.m_blunt + d.m_slash + d.m_pierce + d.m_fire + d.m_frost + d.m_lightning + d.m_poison + d.m_spirit;
        }

        private static float WeaponTotalDamage(ItemDrop.ItemData w)
        {
            return w == null ? 0f : TotalDamage(w.GetDamage());
        }

        // The attacker's active art and its baked power: from the attacker's own ZDO (modded),
        // else from the proxy table if we are the provider (modless). Used by the damage patches.
        internal bool AttackerArt(Character attacker, out Art a, out float power)
        {
            a = null; power = 0f;
            if (attacker == null) return false;
            int hash = 0;
            ZDO z = OwnZdo(attacker);
            if (z != null)
            {
                hash = z.GetInt(ZdoArt, 0);
                if (hash != 0)
                {
                    long until = z.GetLong(ZdoUntil, 0L);
                    if (until == 0L || NowTicks() >= until) hash = 0;
                    else power = z.GetFloat(ZdoPower, 0f);
                }
            }
            if (hash == 0 && !ProxyWindowArt(attacker, out hash, out power)) return false;
            return _artByHash.TryGetValue(hash, out a) && power > 0f;
        }

        // ------------------------------------------------------------------
        // activation
        // ------------------------------------------------------------------
        private void TryActivate(Player p)
        {
            if (TauntShieldEquipped(p)) { ActivateTaunt(p); return; }   // tower shield wins over the 1H art
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
            float tierNorm = Mathf.Clamp01(WeaponTotalDamage(weapon) / Mathf.Max(1f, Sv(_cfgRefWeaponDamage)));
            float skill = p.GetSkillFactor(a.Skill);                 // 0..1
            float s = (0.5f + 0.5f * tierNorm) * (1f + skill * Sv(_cfgSkillPowerScale));
            float p01 = 0.5f * tierNorm + 0.5f * skill;

            if (cost > 0f) { if (a.UsesEitr) p.UseEitr(cost); else p.UseStamina(cost); }

            string info;
            if (a.Kind == ArtKind.AoEHeal)
            {
                info = a.Name + " (" + DoHeal(p, a, p01) + ")";
            }
            else if (a.Kind == ArtKind.AoEBurst)
            {
                info = a.Name + " (" + DoBurst(p, a, weapon, s) + ")";
            }
            else
            {
                float mag = Sv(a.Mag);
                float power = (a.Kind == ArtKind.DamageMult || a.Kind == ArtKind.Stagger)
                    ? 1f + (mag - 1f) * s                            // scale the bonus part
                    : mag * s;                                      // DoT amount / lifesteal fraction
                float durBonus = Mathf.Clamp(skill * Sv(_cfgSkillDurationScale) * (0.5f + 0.5f * tierNorm), 0f, 0.5f);
                float window = Sv(a.Win) * (1f + durBonus);
                ZDO z = OwnZdo(p);
                if (z != null)
                {
                    z.Set(ZdoArt, a.Hash, false);
                    z.Set(ZdoUntil, NowTicks() + (long)(window * TimeSpan.TicksPerSecond));
                    z.Set(ZdoPower, power);
                }
                info = a.Name + "!";
            }

            _cooldownUntil[a.Id] = now + a.Cd.Value;
            _gcdUntil = now + _cfgGlobalCooldown.Value;
            _lastArtId = a.Id; _lastActivated = now;
            Message(p, info);
            Debug("Activated " + a.Id + " (tier " + FormatTime(tierNorm) + ", skill " + FormatTime(skill) + ")");
        }

        // ------------------------------------------------------------------
        // instant arts
        // ------------------------------------------------------------------
        private int DoHeal(Player p, Art a, float p01)
        {
            float lo = Sv(a.HLo0) + (Sv(a.HLo1) - Sv(a.HLo0)) * p01;
            float hi = Sv(a.HHi0) + (Sv(a.HHi1) - Sv(a.HHi0)) * p01;
            if (hi < lo) hi = lo;
            float amount = UnityEngine.Random.Range(lo, hi);
            float r2 = Sv(a.Radius) * Sv(a.Radius);
            Vector3 me = p.transform.position;
            int n = 0;
            List<Player> players = Player.GetAllPlayers();
            for (int i = 0; i < players.Count; i++)
            {
                Player q = players[i];
                if (q == null || q.IsDead()) continue;
                if (q == p && !a.IncludeSelf) continue;
                if ((q.transform.position - me).sqrMagnitude > r2) continue;
                HealChar(q, amount);
                n++;
            }
            Debug(a.Id + ": heal " + FormatTime(amount) + " to " + n + " (roll " + FormatTime(lo) + "-" + FormatTime(hi) + ")");
            return n;
        }

        private static void HealChar(Character c, float amt)
        {
            if (c == null || amt <= 0f) return;
            ZNetView nv = c.GetComponent<ZNetView>();
            if (nv == null || !nv.IsValid()) return;
            if (nv.IsOwner()) c.Heal(amt, true);
            else nv.InvokeRPC(nv.GetZDO().GetOwner(), "Heal", amt, false);
        }

        private int DoBurst(Player p, Art a, ItemDrop.ItemData weapon, float s)
        {
            if (weapon == null) return 0;
            float mag = Sv(a.Mag) * s;
            float r2 = Sv(a.Radius) * Sv(a.Radius);
            Vector3 me = p.transform.position;
            int n = 0;
            List<Character> all = Character.GetAllCharacters();
            for (int i = 0; i < all.Count; i++)
            {
                Character c = all[i];
                if (c == null || c == p || c.IsPlayer() || c.IsDead() || c.IsTamed()) continue;
                if ((c.transform.position - me).sqrMagnitude > r2) continue;
                float bf = c.IsBoss() ? Sv(_cfgBossFactor) : 1f;
                HitData hit = new HitData();
                hit.m_damage = weapon.GetDamage();
                hit.ApplyModifier(mag * bf);
                hit.SetAttacker(p);
                hit.m_point = c.transform.position;
                hit.m_dir = (c.transform.position - me).normalized;
                c.Damage(hit);
                n++;
            }
            return n;
        }

        // Cooldown remaining for the HUD; <=0 means ready.
        internal float CooldownLeft(Art a)
        {
            float u;
            return _cooldownUntil.TryGetValue(a.Id, out u) ? (u - Time.time) : 0f;
        }
    }
}
