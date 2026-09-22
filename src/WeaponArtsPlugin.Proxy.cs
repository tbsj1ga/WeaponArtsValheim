using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace WeaponArts
{
    // Arts for players WITHOUT the mod, run by a nearby modded client on their behalf.
    //
    //   * Trigger: the vanilla emote they play (ZDO emote/emoteID) or a chat word.
    //   * Their weapon/shield, tier and upgrade level are in their ZDO; their skill is not, so a
    //     config stand-in (SkillForModless) is used.
    //   * Window arts (damage/DoT/stagger/pierce/crit/eitr): stored in a proxy table and applied
    //     on the OWNER of the struck creature (us) via the RPC_Damage patch.
    //   * Taunt: monsters are held onto the modless tank and their hits on it are reduced on our
    //     Character.Damage.
    //   * Heals / AoE burst: run instantly, centred on the modless player.
    //   * Feedback: chat (they have no HUD) - activation, "ready", and a query word.
    //
    // Never for a player who has the mod (they act for themselves). Provider election matches
    // ShieldTaunt: of the modded players near enough to own the monsters, the host else the
    // lowest session id - every client agrees without talking; works on a dedicated server.
    public partial class WeaponArtsPlugin
    {
        private ConfigEntry<bool> _cfgProxy;
        private ConfigEntry<string> _cfgProxyProvider;
        private ConfigEntry<string> _cfgProxyEmote;
        private ConfigEntry<string> _cfgProxyChat;
        private ConfigEntry<string> _cfgProxyQuery;
        private ConfigEntry<float> _cfgProxySkill;
        private ConfigEntry<float> _cfgProxyCooldownFactor;
        private ConfigEntry<bool> _cfgProxyAnnounce;
        private ConfigEntry<bool> _cfgProxyAnnounceReady;

        private static readonly int ModMarkHash = "j1ga.weaponarts.mod".GetStableHashCode();
        private float _nextMark;

        private class Proxy
        {
            public ZDOID Id;
            public string Name;
            public int LastEmoteId;
            public float LastSeen;
            public float CooldownUntil;
            public bool ReadyAnnounced;
            public string ArtName = "";
            // window art
            public int ArtHash;
            public float ArtUntil;
            public float ArtPower;
            // taunt
            public float TauntUntil;
            public float TauntReduction;
            public int TauntCount;
            public float NextReapply;
            public long SinceMs;
        }

        private readonly Dictionary<ZDOID, Proxy> _proxies = new Dictionary<ZDOID, Proxy>();
        private readonly List<ZDOID> _proxyScratch = new List<ZDOID>();
        private float _nextProxyPoll;
        private const float ProxyForgetAfter = 120f;

        private void BindProxyConfig()
        {
            _cfgProxy = Config.Bind("09 Proxy", "Enabled", false,
                "Run arts for players WITHOUT the mod near you: they play the trigger emote (or say the trigger word) and this client applies the art of their equipped weapon/shield on their behalf. Players with the mod act for themselves.");
            _cfgProxyProvider = Config.Bind("09 Proxy", "Provider", "Nearby",
                new ConfigDescription("Which modded client acts for a modless player. Nearby = of the modded players near enough to own the monsters, the host else the lowest session id (works on a dedicated server). Host = only the hosting player.",
                    new AcceptableValueList<string>("Nearby", "Host")));
            _cfgProxyEmote = Config.Bind("09 Proxy", "TriggerEmote", "challenge", "Vanilla emote that triggers the art (e.g. /challenge). Empty: off.");
            _cfgProxyChat = Config.Bind("09 Proxy", "TriggerChat", "art", "Exact chat word that triggers the art for its sender. Empty: off.");
            _cfgProxyQuery = Config.Bind("09 Proxy", "QueryWord", "art?", "Exact chat word a modless player types to be told their art and its state. Empty: off.");
            _cfgProxySkill = Config.Bind("09 Proxy", "SkillForModless", 25f,
                new ConfigDescription("Weapon skill assumed for players without the mod (their real skill is not visible).", new AcceptableValueRange<float>(0f, 100f)));
            _cfgProxyCooldownFactor = Config.Bind("09 Proxy", "CooldownFactor", 1.5f,
                new ConfigDescription("Their cooldown is the art's cooldown times this (no stamina/eitr is spent for them).", new AcceptableValueRange<float>(1f, 5f)));
            _cfgProxyAnnounce = Config.Bind("09 Proxy", "Announce", true, "Say the result in chat (they have no HUD).");
            _cfgProxyAnnounceReady = Config.Bind("09 Proxy", "AnnounceReady", true, "Say in chat when their art comes off cooldown.");
        }

        private bool ProxyOn { get { return _cfgProxy.Value; } }

        // ------------------------------------------------------------------
        // provider election
        // ------------------------------------------------------------------
        private bool IsProviderFor(Player p)
        {
            long provider = ProviderFor(p);
            return provider != 0L && provider == ZDOMan.GetSessionID();
        }

        private long ProviderFor(Player p)
        {
            ZNet znet = ZNet.instance;
            if (znet == null || p == null) return 0L;
            if (_cfgProxyProvider.Value == "Host") return znet.IsServer() ? ZDOMan.GetSessionID() : 0L;

            Player local = Player.m_localPlayer;
            Vector3 at = p.transform.position;
            long best = 0L;
            bool bestHost = false;
            List<Player> players = Player.GetAllPlayers();
            for (int i = 0; i < players.Count; i++)
            {
                Player q = players[i];
                if (q == null || q == p) continue;
                ZNetView nv = q.GetComponent<ZNetView>();
                ZDO zdo = nv != null ? nv.GetZDO() : null;
                if (zdo == null) continue;
                if (q != local && !HasMod(zdo)) continue;
                if (!ZNetScene.InActiveArea(at, ZoneSystem.GetZone(q.transform.position))) continue;
                long uid = zdo.GetOwner();
                bool host = IsHostSession(znet, uid);
                if (best == 0L || (host && !bestHost) || (host == bestHost && uid < best)) { best = uid; bestHost = host; }
            }
            return best;
        }

        private static bool IsHostSession(ZNet znet, long uid)
        {
            if (znet.IsServer()) return uid == ZDOMan.GetSessionID();
            ZNetPeer server = znet.GetServerPeer();
            return server != null && uid == server.m_uid;
        }

        internal Character ProxyTankChar(ZDOID id)
        {
            if (ZNetScene.instance == null || id.IsNone()) return null;
            GameObject go = ZNetScene.instance.FindInstance(id);
            return go != null ? go.GetComponent<Character>() : null;
        }

        // ------------------------------------------------------------------
        // per frame
        // ------------------------------------------------------------------
        private void UpdateProxies(Player local, float now)
        {
            MarkSelf(local, now);
            if (!ProxyOn) { if (_proxies.Count > 0) _proxies.Clear(); return; }

            if (now >= _nextProxyPoll) { _nextProxyPoll = now + ReapplyInterval; PollProxies(local, now); }

            foreach (Proxy px in _proxies.Values)
            {
                if (now < px.TauntUntil && now >= px.NextReapply)
                {
                    px.NextReapply = now + ReapplyInterval;
                    Character tank = ProxyTankChar(px.Id);
                    if (tank != null && !tank.IsDead()) px.TauntCount = ApplyTauntHold(tank, px.TauntUntil, Sv(_cfgTauntRadius), px.SinceMs);
                }
                if (_cfgProxyAnnounceReady.Value && !px.ReadyAnnounced && px.CooldownUntil > 0f && now >= px.CooldownUntil)
                {
                    px.ReadyAnnounced = true;
                    Announce(px.Name + ": " + px.ArtName + " готово");
                }
            }

            _proxyScratch.Clear();
            foreach (KeyValuePair<ZDOID, Proxy> kv in _proxies)
                if (now - kv.Value.LastSeen > ProxyForgetAfter) _proxyScratch.Add(kv.Key);
            for (int i = 0; i < _proxyScratch.Count; i++) _proxies.Remove(_proxyScratch[i]);
        }

        private void MarkSelf(Player local, float now)
        {
            if (now < _nextMark) return;
            _nextMark = now + 1f;
            ZDO zdo = OwnZdo(local);
            if (zdo != null && zdo.GetInt(ModMarkHash, 0) != 1) zdo.Set(ModMarkHash, 1, false);
        }

        private static bool HasMod(ZDO zdo) { return zdo != null && zdo.GetInt(ModMarkHash, 0) != 0; }

        private Proxy GetOrAdd(Player p, ZDO zdo, float now)
        {
            ZDOID id = p.GetZDOID();
            Proxy px;
            if (!_proxies.TryGetValue(id, out px))
            {
                px = new Proxy();
                px.Id = id; px.Name = p.GetPlayerName(); px.LastEmoteId = zdo.GetInt(ZDOVars.s_emoteID, 0);
                _proxies[id] = px;
            }
            px.LastSeen = now;
            return px;
        }

        private void PollProxies(Player local, float now)
        {
            string want = (_cfgProxyEmote.Value ?? "").Trim();
            List<Player> players = Player.GetAllPlayers();
            for (int i = 0; i < players.Count; i++)
            {
                Player p = players[i];
                if (p == null || p == local) continue;
                ZDO zdo = OwnZdo(p);
                if (zdo == null || HasMod(zdo)) continue;

                bool isNew = !_proxies.ContainsKey(p.GetZDOID());
                Proxy px = GetOrAdd(p, zdo, now);
                if (isNew || want.Length == 0) continue;

                int emoteId = zdo.GetInt(ZDOVars.s_emoteID, 0);
                if (emoteId == px.LastEmoteId) continue;
                px.LastEmoteId = emoteId;
                string emote = zdo.GetString(ZDOVars.s_emote, "");
                if (string.Equals(emote, want, StringComparison.OrdinalIgnoreCase)) TryActivateProxy(p, zdo, px, now, "emote");
            }
        }

        // Chat on our client from a player without the mod: trigger or query.
        private void OnChat(long senderId, Talker.Type type, string text)
        {
            if (!ProxyOn || type == Talker.Type.Ping || text == null) return;
            string t = text.Trim();
            string trigger = (_cfgProxyChat.Value ?? "").Trim();
            string query = (_cfgProxyQuery.Value ?? "").Trim();
            bool isTrigger = trigger.Length > 0 && string.Equals(t, trigger, StringComparison.OrdinalIgnoreCase);
            bool isQuery = query.Length > 0 && string.Equals(t, query, StringComparison.OrdinalIgnoreCase);
            if (!isTrigger && !isQuery) return;

            Player local = Player.m_localPlayer;
            List<Player> players = Player.GetAllPlayers();
            for (int i = 0; i < players.Count; i++)
            {
                Player p = players[i];
                if (p == null || p == local || p.GetOwner() != senderId) continue;
                ZDO zdo = OwnZdo(p);
                if (zdo == null || HasMod(zdo)) return;
                Proxy px = GetOrAdd(p, zdo, Time.time);
                if (isQuery) ReplyQuery(p, zdo, px);
                else TryActivateProxy(p, zdo, px, Time.time, "chat");
                return;
            }
        }

        // ------------------------------------------------------------------
        // resolve the modless player's art from their ZDO
        // ------------------------------------------------------------------
        private static ItemDrop.ItemData ProtoItem(int hash)
        {
            GameObject prefab = hash != 0 && ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(hash) : null;
            ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            return drop != null ? drop.m_itemData : null;
        }

        // Their art: taunt if a tower shield is in the left hand (priority), else the weapon art.
        private Art ResolveProxyArt(ZDO zdo, out bool taunt, out ItemDrop.ItemData item, out int quality)
        {
            taunt = false; item = null; quality = 1;
            ItemDrop.ItemData shield = ProtoItem(zdo.GetInt(ZDOVars.s_leftItem, 0));
            if (shield != null && shield.m_shared != null && shield.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield)
            {
                string mode = Ss(_cfgShieldMode);
                if (mode == "None" || mode == "AnyShield" || shield.m_shared.m_timedBlockBonus <= Sv(_cfgTowerParry))
                {
                    taunt = true; item = shield; quality = zdo.GetInt(ZDOVars.s_leftItemQuality, 1);
                    return null;   // taunt handled separately
                }
            }
            item = ProtoItem(zdo.GetInt(ZDOVars.s_rightItem, 0));
            quality = zdo.GetInt(ZDOVars.s_rightItemQuality, 1);
            return ArtFor(item);
        }

        private void TryActivateProxy(Player p, ZDO zdo, Proxy px, float now, string how)
        {
            if (!IsProviderFor(p)) { Debug("proxy for " + px.Name + " (" + how + "): provider is elsewhere"); return; }
            if (now < px.CooldownUntil) { Announce(px.Name + ": КД " + Mathf.CeilToInt(px.CooldownUntil - now) + "с"); return; }

            bool taunt; ItemDrop.ItemData item; int quality;
            Art a = ResolveProxyArt(zdo, out taunt, out item, out quality);
            float skill = Mathf.Clamp01(_cfgProxySkill.Value / 100f);

            if (taunt)
            {
                float block = item != null && item.m_shared != null ? item.m_shared.m_blockPower + Mathf.Max(0, quality - 1) * item.m_shared.m_blockPowerPerLevel : 0f;
                float shieldNorm = Mathf.Clamp01(block / Mathf.Max(1f, Sv(_cfgTauntRefBlock)));
                float durBonus = Mathf.Clamp(skill * Sv(_cfgSkillDurationScale) * (0.5f + 0.5f * shieldNorm), 0f, 0.5f);
                px.TauntReduction = Mathf.Clamp(Mathf.Lerp(Sv(_cfgTauntReductionBase), Sv(_cfgTauntReductionBest), 0.8f * shieldNorm + 0.2f * skill), 0f, 0.9f);
                px.TauntUntil = now + Sv(_cfgTauntDuration) * (1f + durBonus);
                px.NextReapply = now + ReapplyInterval;
                px.SinceMs = WorldMs();
                px.ArtName = "Таунт";
                px.TauntCount = ApplyTauntHold(p, px.TauntUntil, Sv(_cfgTauntRadius), px.SinceMs);
                px.CooldownUntil = now + _cfgTauntCooldown.Value * _cfgProxyCooldownFactor.Value;
                px.ReadyAnnounced = false;
                Announce("Таунт за " + px.Name + ": " + px.TauntCount + " мобов");
                return;
            }

            if (a == null) { Announce(px.Name + ": нет активки для оружия"); return; }
            px.ArtName = a.Name;

            float tierNorm = Mathf.Clamp01(TotalDamage(item.GetDamage(quality, 0f)) / Mathf.Max(1f, Sv(_cfgRefWeaponDamage)));
            float s = (0.5f + 0.5f * tierNorm) * (1f + skill * Sv(_cfgSkillPowerScale));
            float p01 = 0.5f * tierNorm + 0.5f * skill;

            if (a.Kind == ArtKind.AoEHeal) { int n = DoHeal(p, a, p01); Announce(a.Name + " за " + px.Name + " (" + n + ")"); }
            else if (a.Kind == ArtKind.AoEBurst) { int n = DoBurst(p, a, item, s); Announce(a.Name + " за " + px.Name + " (" + n + ")"); }
            else
            {
                float power = (a.Kind == ArtKind.DamageMult || a.Kind == ArtKind.Stagger) ? 1f + (Sv(a.Mag) - 1f) * s : Sv(a.Mag) * s;
                float durBonus = Mathf.Clamp(skill * Sv(_cfgSkillDurationScale) * (0.5f + 0.5f * tierNorm), 0f, 0.5f);
                px.ArtHash = a.Hash; px.ArtPower = power; px.ArtUntil = now + Sv(a.Win) * (1f + durBonus);
                Announce(a.Name + " за " + px.Name + ": " + Sv(a.Win).ToString("0") + "с");
            }
            px.CooldownUntil = now + a.Cd.Value * _cfgProxyCooldownFactor.Value;
            px.ReadyAnnounced = false;
        }

        private void ReplyQuery(Player p, ZDO zdo, Proxy px)
        {
            bool taunt; ItemDrop.ItemData item; int quality;
            Art a = ResolveProxyArt(zdo, out taunt, out item, out quality);
            string name = taunt ? "Таунт" : (a != null ? a.Name : "нет активки");
            string desc = taunt ? "стянуть мобов + резист" : (a != null ? a.Desc : "");
            float now = Time.time;
            string state = now < px.TauntUntil || now < px.ArtUntil ? "активна"
                : (now < px.CooldownUntil ? "КД " + Mathf.CeilToInt(px.CooldownUntil - now) + "с" : "готова");
            Announce(px.Name + ": " + name + " — " + state + (desc.Length > 0 ? " (" + desc + ")" : ""));
        }

        private void Announce(string text)
        {
            if (!_cfgProxyAnnounce.Value || Chat.instance == null) return;
            Chat.instance.SendText(Talker.Type.Normal, text);
        }

        // ------------------------------------------------------------------
        // reads by the damage patches
        // ------------------------------------------------------------------
        // Window art of a modless attacker we are the provider for (for the RPC_Damage patch).
        internal bool ProxyWindowArt(Character attacker, out int hash, out float power)
        {
            hash = 0; power = 0f;
            if (attacker == null || _proxies.Count == 0) return false;
            Proxy px;
            if (!_proxies.TryGetValue(attacker.GetZDOID(), out px) || px.ArtHash == 0 || Time.time >= px.ArtUntil) return false;
            hash = px.ArtHash; power = px.ArtPower; return true;
        }

        // A hit from a held monster (ours) on a modless tank we taunt for: reduce it here, since
        // the tank's client cannot.
        private void ReduceProxyHit(Character victim, HitData hit)
        {
            if (victim == null || !victim.IsPlayer() || victim == Player.m_localPlayer) return;
            Proxy px;
            if (!_proxies.TryGetValue(victim.GetZDOID(), out px) || Time.time >= px.TauntUntil) return;
            Character attacker = hit.GetAttacker();
            if (attacker == null) return;
            Hold h;
            if (!_tainted.TryGetValue(attacker.GetZDOID(), out h) || h.Target != px.Id || Time.time >= h.Until) return;
            float red = Mathf.Clamp(px.TauntReduction, 0f, 0.9f);
            if (red > 0f) hit.ApplyModifier(1f - red);
        }
    }
}
