using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace WeaponArts
{
    // Active abilities ("arts") keyed to the equipped weapon. One key; the art is chosen by what
    // is in your hands. Phase 1: the on-target combat arts run through the OWNER of the struck
    // creature, exactly like BossMeter/ShieldTaunt established:
    //
    //   * On activation the acting player bakes the art (id, expiry on server time, and the
    //     already skill/tier-scaled power) into its own player ZDO.
    //   * Character.RPC_Damage runs on the owner of the struck creature; there we read the
    //     attacker's art from its ZDO and edit the HitData: multiply damage, add poison DoT,
    //     raise the stagger multiplier, and (post-hit) lifesteal to the attacker.
    //
    // This one path works for a modded attacker whoever owns the target. Effects only ever touch
    // creatures, never players (PvP-safe); bosses take a reduced factor and are naturally immune
    // to stagger. Heals/AoE/taunt/eitr-surge, the proxy for modless actors, host config sync and
    // the config window are later phases (see TASK-weapon-arts.md).
    [BepInPlugin(Guid, Name, Version)]
    public partial class WeaponArtsPlugin : BaseUnityPlugin
    {
        public const string Guid = "j1ga.weaponarts";
        public const string Name = "Weapon Arts";
        public const string Version = "0.10.0";

        public static WeaponArtsPlugin Instance;

        private Harmony _harmony;

        // ZDO keys on the acting player's own ZDO (server-time based, read by the target's owner).
        internal static readonly int ZdoArt = "j1ga.weaponarts.art".GetStableHashCode();
        internal static readonly int ZdoUntil = "j1ga.weaponarts.until".GetStableHashCode();
        internal static readonly int ZdoPower = "j1ga.weaponarts.power".GetStableHashCode();

        // Local cooldowns, per art id, and the global GCD. Not networked - they gate activation.
        private readonly Dictionary<string, float> _cooldownUntil = new Dictionary<string, float>();
        private float _gcdUntil;
        private string _lastArtId = "";
        private float _lastActivated;

        private int _errorCount;
        private bool _disabledByErrors;
        private const int MaxErrors = 25;
        private readonly HashSet<string> _loggedErrors = new HashSet<string>();

        private void Awake()
        {
            try
            {
                Instance = this;
                BindConfig();
                BindTauntConfig();
                BindProxyConfig();
                BindSyncConfig();
                BindBleedConfig();
                BindEffectsConfig();
                BindReflection();
                BuildArts();
                RegisterCommands();
                _harmony = new Harmony(Guid);
                _harmony.PatchAll(typeof(WeaponArtsPlugin).Assembly);
                Logger.LogInfo(Name + " " + Version + " loaded. Key: " + _cfgKey.Value + ".");
            }
            catch (Exception e)
            {
                Logger.LogError("Awake failed, mod is inert: " + e);
                _disabledByErrors = true;
            }
        }

        private void OnDestroy()
        {
            try { ReleaseAllStraight(); if (_harmony != null) _harmony.UnpatchSelf(); }
            catch (Exception e) { Logger.LogWarning("OnDestroy: " + e.Message); }
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (_disabledByErrors || !_cfgEnabled.Value) return;
            try
            {
                if (_syncDirty) FlushSync();
                Player p = Player.m_localPlayer;
                if (p == null || p.IsDead()) return;
                if (_cfgKey.Value.IsDown() && !BlockedByUI()) TryActivate(p);
                TauntTick(p, Time.time);
                UpdateProxies(p, Time.time);
                BleedTick(Time.time);
            }
            catch (Exception e) { Fail("Update", e); }
        }

        // ------------------------------------------------------------------
        // helpers
        // ------------------------------------------------------------------
        private bool Active { get { return !_disabledByErrors && Sb(_cfgEnabled); } }

        // Network-consistent clock shared by the actor and the target's owner.
        internal static long NowTicks()
        {
            ZNet z = ZNet.instance;
            return z != null ? z.GetTime().Ticks : DateTime.UtcNow.Ticks;
        }

        internal static ZDO OwnZdo(Character c)
        {
            if (c == null) return null;
            ZNetView nv = c.GetComponent<ZNetView>();
            return nv != null && nv.IsValid() ? nv.GetZDO() : null;
        }

        private static bool BlockedByUI()
        {
            if (Chat.instance != null && Chat.instance.IsChatDialogWindowVisible()) return true;
            if (Console.IsVisible()) return true;
            if (TextInput.IsVisible()) return true;
            if (Menu.IsVisible()) return true;
            if (InventoryGui.IsVisible()) return true;
            if (StoreGui.IsVisible()) return true;
            if (Minimap.instance != null && Minimap.IsOpen()) return true;
            return false;
        }

        internal static string FormatTime(float seconds)
        {
            return Mathf.Max(0f, seconds).ToString("0.0", CultureInfo.InvariantCulture);
        }

        private void Message(Player p, string text)
        {
            if (p != null) p.Message(MessageHud.MessageType.Center, text);
        }

        private void Debug(string text) { if (_cfgDebug.Value) Logger.LogInfo(text); }

        private void Fail(string where, Exception e)
        {
            string key = where + ": " + e.GetType().Name + ": " + e.Message;
            if (_loggedErrors.Add(key)) Logger.LogError(key + "\n" + e.StackTrace);
            if (++_errorCount >= MaxErrors && !_disabledByErrors)
            {
                _disabledByErrors = true;
                Logger.LogError("Too many errors, " + Name + " is now inert until the game restarts.");
            }
        }
    }
}
