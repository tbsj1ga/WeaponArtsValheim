using System;
using BepInEx;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;

namespace WeaponArts
{
    public partial class WeaponArtsPlugin
    {
        // ------------------------------------------------------------------
        // persistent HUD label for the equipped weapon's art (modded player only)
        //
        // A uGUI object in the game's HUD (hudroot/WeaponArts), not IMGUI: it hides with the
        // HUD (Ctrl+F3), scales with the UI, uses the game's font, and a HUD mod such as
        // HudLayout can find it and move it. Its anchor is PositionX / PositionY (fractions of
        // the screen), written only when those change, so a HUD mod's offset on top of it stays.
        // ------------------------------------------------------------------
        private ConfigEntry<float> _cfgHudX;
        private ConfigEntry<float> _cfgHudY;
        private ConfigEntry<int> _cfgHudFont;

        private const string HudObjectName = "WeaponArts";

        private Hud _labelHud;
        private RectTransform _label;
        private TextMeshProUGUI _nameText, _descText;
        private float _labelX = -1f, _labelY = -1f;
        private int _labelFont = -1;

        private static readonly Color Grey = new Color(0.8f, 0.8f, 0.8f);
        private static readonly Color Green = new Color(0.6f, 1f, 0.6f);
        private static readonly Color Amber = new Color(1f, 0.85f, 0.3f);
        private static readonly Color DescColor = new Color(0.8f, 0.8f, 0.76f);

        private void BindHudConfig()
        {
            _cfgHudX = Config.Bind("08 HUD", "PositionX", 0.5f,
                new ConfigDescription("Horizontal centre of the label, fraction of screen width (0.5 = middle).", new AcceptableValueRange<float>(0f, 1f)));
            _cfgHudY = Config.Bind("08 HUD", "PositionY", 0.81f,
                new ConfigDescription("Top of the label, fraction of screen height measured from the TOP (0 = top edge, 1 = bottom). 0.81 sits above the stamina bar.", new AcceptableValueRange<float>(0f, 1f)));
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

        private void LateUpdate()
        {
            if (_disabledByErrors) return;
            try { UpdateLabel(); }
            catch (Exception e) { Fail("HUD label", e); }
        }

        private void UpdateLabel()
        {
            if (!EnsureLabel()) return;
            Player pl = Player.m_localPlayer;
            string line = null, desc = null;
            Color c = Grey;
            bool show = Active && _cfgShowHud.Value && !HudHidden(pl) && LabelText(pl, out line, out desc, out c);
            if (_label.gameObject.activeSelf != show) _label.gameObject.SetActive(show);
            if (!show) return;

            PlaceLabel();
            if (_nameText.text != line) _nameText.text = line;
            if (_nameText.color != c) _nameText.color = c;
            if (_descText.text != desc) _descText.text = desc;
        }

        // The art's name and state, and its description; false when there is nothing to show.
        private bool LabelText(Player pl, out string line, out string desc, out Color c)
        {
            string name, state;
            line = desc = null;
            c = Grey;
            if (TauntShieldEquipped(pl))
            {
                name = L("Taunt", "Таунт"); desc = L("tower shield: pull monsters + damage reduction", "башенный щит: стянуть мобов + резист");
                if (TauntActive) { state = L("active ", "активна ") + FormatTime(_tauntUntil - Time.time) + L("s (", "с (") + _tauntCount + ")"; c = Amber; }
                else
                {
                    float cd = Mathf.Max(TauntCooldownLeft(), GcdLeft());
                    if (cd > 0f) { state = L("cooldown ", "КД ") + Mathf.CeilToInt(cd) + L("s", "с"); c = Grey; }
                    else { state = L("ready", "готова"); c = Green; }
                }
            }
            else
            {
                Art a = CurrentArt(pl);
                if (a == null) return false;
                name = a.Name; desc = a.Desc;
                float act = ActiveLeft(a);
                float cd = CooldownLeft(a);
                int shots = ShotsLeft(a);
                if (act > 0f && shots > 0) { state = L("active: ", "активна: ") + shots + L(" shots (", " выстр. (") + Mathf.CeilToInt(act) + L("s)", "с)"); c = Amber; }
                else if (act > 0f) { state = L("active ", "активна ") + FormatTime(act) + L("s", "с"); c = Amber; }
                else if (Mathf.Max(cd, GcdLeft()) > 0f) { state = L("cooldown ", "КД ") + Mathf.CeilToInt(Mathf.Max(cd, GcdLeft())) + L("s", "с"); c = Grey; }   // own CD or the global one
                else { state = L("ready", "готова"); c = Green; }
            }
            line = name + "  —  " + state;
            return true;
        }

        // The label object, made once per Hud (the HUD is rebuilt with every world).
        private bool EnsureLabel()
        {
            Hud hud = Hud.instance;
            if (hud == null || hud.m_rootObject == null) { _labelHud = null; _label = null; return false; }
            if (hud == _labelHud && _label != null) return true;

            _labelHud = hud;
            Transform root = hud.m_rootObject.transform;
            Transform old = root.Find(HudObjectName);
            if (old != null) Destroy(old.gameObject);

            GameObject go = new GameObject(HudObjectName, typeof(RectTransform));
            go.layer = root.gameObject.layer;
            _label = (RectTransform)go.transform;
            _label.SetParent(root, false);
            _label.pivot = new Vector2(0.5f, 1f);   // PositionY is the top of the label
            _label.sizeDelta = new Vector2(420f, 48f);

            // the game's font and material (outline) from the health number
            TMP_Text sample = hud.m_healthText;
            _nameText = MakeText(_label, "Name", sample);
            _nameText.fontStyle = FontStyles.Bold;
            _descText = MakeText(_label, "Description", sample);
            _descText.color = DescColor;

            _labelX = _labelY = -1f;
            _labelFont = -1;
            go.SetActive(false);
            return true;
        }

        private static TextMeshProUGUI MakeText(RectTransform parent, string name, TMP_Text sample)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
            if (sample != null)
            {
                t.font = sample.font;
                t.fontSharedMaterial = sample.fontSharedMaterial;
            }
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
            return t;
        }

        // Anchor and sizes from the config, only when it changed: anything else (a HUD mod's
        // offset) stays on the object between changes.
        private void PlaceLabel()
        {
            float x = _cfgHudX.Value, y = _cfgHudY.Value;
            int font = _cfgHudFont.Value;
            if (x != _labelX || y != _labelY)
            {
                Vector2 a = new Vector2(x, 1f - y);   // the config counts from the top (as the old IMGUI label did), uGUI from the bottom
                _label.anchorMin = a;
                _label.anchorMax = a;
                _label.anchoredPosition = Vector2.zero;
                _labelX = x; _labelY = y;
            }
            if (font != _labelFont)
            {
                float h1 = font + 8f, h2 = font + 4f;
                _nameText.fontSize = font;
                _descText.fontSize = Mathf.Max(8, font - 4);
                ((RectTransform)_nameText.transform).sizeDelta = new Vector2(0f, h1);
                RectTransform d = (RectTransform)_descText.transform;
                d.sizeDelta = new Vector2(0f, h2);
                d.anchoredPosition = new Vector2(0f, -(h1 - 2f));
                _label.sizeDelta = new Vector2(420f, h1 - 2f + h2);
                _labelFont = font;
            }
        }
    }
}
