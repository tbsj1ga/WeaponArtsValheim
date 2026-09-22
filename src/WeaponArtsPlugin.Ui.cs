using System;
using BepInEx;
using UnityEngine;

namespace WeaponArts
{
    public partial class WeaponArtsPlugin
    {
        // ------------------------------------------------------------------
        // persistent HUD label for the equipped weapon's art (modded player only)
        // ------------------------------------------------------------------
        private GUIStyle _nameStyle, _descStyle;
        private int _styleSize = -1;

        private void OnGUI()
        {
            if (!Active || !_cfgShowHud.Value) return;
            if (Player.m_localPlayer == null || Hud.instance == null) return;
            if (Minimap.instance != null && Minimap.IsOpen()) return;
            try
            {
                Player pl = Player.m_localPlayer;
                string name, desc, state;
                Color c;
                Color grey = new Color(0.8f, 0.8f, 0.8f), green = new Color(0.6f, 1f, 0.6f), amber = new Color(1f, 0.85f, 0.3f);

                if (TauntShieldEquipped(pl))
                {
                    name = "Таунт"; desc = "башенный щит: стянуть мобов + резист";
                    if (TauntActive) { state = "активна " + FormatTime(_tauntUntil - Time.time) + "с (" + _tauntCount + ")"; c = amber; }
                    else { float cd = TauntCooldownLeft(); if (cd > 0f) { state = "КД " + Mathf.CeilToInt(cd) + "с"; c = grey; } else { state = "готова"; c = green; } }
                }
                else
                {
                    Art a = CurrentArt(pl);
                    if (a == null) return;
                    name = a.Name; desc = a.Desc;
                    float cd = CooldownLeft(a);
                    if (cd > 0f) { state = "КД " + Mathf.CeilToInt(cd) + "с"; c = grey; } else { state = "готова"; c = green; }
                }

                EnsureStyles();
                _nameStyle.normal.textColor = c;
                float w = 360f;
                float x = (Screen.width - w) * 0.5f;
                float y = Screen.height * 0.86f;
                GUI.Label(new Rect(x, y, w, 22f), name + "  —  " + state, _nameStyle);
                GUI.Label(new Rect(x, y + 20f, w, 20f), desc, _descStyle);
            }
            catch (Exception e) { Fail("OnGUI", e); }
        }

        private void EnsureStyles()
        {
            if (_nameStyle != null && _styleSize == 16) return;
            _nameStyle = new GUIStyle(GUI.skin.label);
            _nameStyle.fontSize = 16;
            _nameStyle.fontStyle = FontStyle.Bold;
            _nameStyle.alignment = TextAnchor.MiddleCenter;
            _descStyle = new GUIStyle(GUI.skin.label);
            _descStyle.fontSize = 12;
            _descStyle.alignment = TextAnchor.MiddleCenter;
            _descStyle.normal.textColor = new Color(0.75f, 0.75f, 0.72f);
            _styleSize = 16;
        }
    }
}
