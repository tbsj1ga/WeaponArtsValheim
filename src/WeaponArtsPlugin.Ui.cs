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
                Art a = CurrentArt(Player.m_localPlayer);
                if (a == null) return;
                EnsureStyles();

                float cd = CooldownLeft(a);
                string state;
                Color c;
                if (cd > 0f) { state = "КД " + Mathf.CeilToInt(cd) + "с"; c = new Color(0.8f, 0.8f, 0.8f); }
                else { state = "готова"; c = new Color(0.6f, 1f, 0.6f); }
                _nameStyle.normal.textColor = c;

                float w = 360f;
                float x = (Screen.width - w) * 0.5f;
                float y = Screen.height * 0.86f;
                GUI.Label(new Rect(x, y, w, 22f), a.Name + "  —  " + state, _nameStyle);
                GUI.Label(new Rect(x, y + 20f, w, 20f), a.Desc, _descStyle);
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
