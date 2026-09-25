using System;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace WeaponArts
{
    public partial class WeaponArtsPlugin
    {
        // ------------------------------------------------------------------
        // persistent HUD label for the equipped weapon's art (modded player only)
        // ------------------------------------------------------------------
        private ConfigEntry<float> _cfgHudX;
        private ConfigEntry<float> _cfgHudY;
        private ConfigEntry<int> _cfgHudFont;

        private GUIStyle _nameStyle, _descStyle, _shadowStyle;
        private int _styleSize = -1;

        private void BindHudConfig()
        {
            _cfgHudX = Config.Bind("08 HUD", "PositionX", 0.5f,
                new ConfigDescription("Horizontal centre of the label, fraction of screen width (0.5 = middle).", new AcceptableValueRange<float>(0f, 1f)));
            _cfgHudY = Config.Bind("08 HUD", "PositionY", 0.76f,
                new ConfigDescription("Top of the label, fraction of screen height. 0.76 sits above the stamina bar; ~0.95 goes below the adrenaline bar.", new AcceptableValueRange<float>(0f, 1f)));
            _cfgHudFont = Config.Bind("08 HUD", "FontSize", 16,
                new ConfigDescription("Font size of the art name (the description is 4 smaller).", new AcceptableValueRange<int>(10, 32)));
        }

        // Not over menus, the map, chat, pause, a hidden HUD, build mode or a cutscene.
        private bool HudHidden(Player p)
        {
            if (p == null || p.IsDead() || Hud.instance == null) return true;
            if (BlockedByUI()) return true;
            if (Game.IsPaused() || Hud.IsUserHidden() || Hud.IsPieceSelectionVisible()) return true;
            if (p.InCutscene() || p.InPlaceMode()) return true;
            return false;
        }

        private void OnGUI()
        {
            if (!Active || !_cfgShowHud.Value) return;
            Player pl = Player.m_localPlayer;
            if (HudHidden(pl)) return;
            try
            {
                string name, desc, state;
                Color c;
                Color grey = new Color(0.8f, 0.8f, 0.8f), green = new Color(0.6f, 1f, 0.6f), amber = new Color(1f, 0.85f, 0.3f);

                if (TauntShieldEquipped(pl))
                {
                    name = L("Taunt", "Таунт"); desc = L("tower shield: pull monsters + damage reduction", "башенный щит: стянуть мобов + резист");
                    if (TauntActive) { state = L("active ", "активна ") + FormatTime(_tauntUntil - Time.time) + L("s (", "с (") + _tauntCount + ")"; c = amber; }
                    else
                    {
                        float cd = Mathf.Max(TauntCooldownLeft(), GcdLeft());
                        if (cd > 0f) { state = L("cooldown ", "КД ") + Mathf.CeilToInt(cd) + L("s", "с"); c = grey; }
                        else { state = L("ready", "готова"); c = green; }
                    }
                }
                else
                {
                    Art a = CurrentArt(pl);
                    if (a == null) return;
                    name = a.Name; desc = a.Desc;
                    float act = ActiveLeft(a);
                    float cd = CooldownLeft(a);
                    int shots = ShotsLeft(a);
                    if (act > 0f && shots > 0) { state = L("active: ", "активна: ") + shots + L(" shots (", " выстр. (") + Mathf.CeilToInt(act) + L("s)", "с)"); c = amber; }
                    else if (act > 0f) { state = L("active ", "активна ") + FormatTime(act) + L("s", "с"); c = amber; }
                    else if (Mathf.Max(cd, GcdLeft()) > 0f) { state = L("cooldown ", "КД ") + Mathf.CeilToInt(Mathf.Max(cd, GcdLeft())) + L("s", "с"); c = grey; }   // own CD or the global one
                    else { state = L("ready", "готова"); c = green; }
                }

                EnsureStyles();
                _nameStyle.normal.textColor = c;
                float w = 420f;
                float x = Screen.width * _cfgHudX.Value - w * 0.5f;
                float y = Screen.height * _cfgHudY.Value;
                float h1 = _cfgHudFont.Value + 8f;
                string line = name + "  —  " + state;
                Shadowed(new Rect(x, y, w, h1), line, _nameStyle);
                Shadowed(new Rect(x, y + h1 - 2f, w, h1 - 4f), desc, _descStyle);
            }
            catch (Exception e) { Fail("OnGUI", e); }
        }

        private void Shadowed(Rect r, string text, GUIStyle style)
        {
            _shadowStyle.fontSize = style.fontSize;
            _shadowStyle.fontStyle = style.fontStyle;
            GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, _shadowStyle);
            GUI.Label(r, text, style);
        }

        private void EnsureStyles()
        {
            int size = _cfgHudFont.Value;
            if (_nameStyle != null && _styleSize == size) return;
            _nameStyle = new GUIStyle(GUI.skin.label);
            _nameStyle.fontSize = size;
            _nameStyle.fontStyle = FontStyle.Bold;
            _nameStyle.alignment = TextAnchor.MiddleCenter;
            _descStyle = new GUIStyle(GUI.skin.label);
            _descStyle.fontSize = Mathf.Max(8, size - 4);
            _descStyle.alignment = TextAnchor.MiddleCenter;
            _descStyle.normal.textColor = new Color(0.8f, 0.8f, 0.76f);
            _shadowStyle = new GUIStyle(GUI.skin.label);
            _shadowStyle.alignment = TextAnchor.MiddleCenter;
            _shadowStyle.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
            _styleSize = size;
        }
    }
}
