using System;
using System.Text;
using BepInEx;
using UnityEngine;

namespace WeaponArts
{
    public partial class WeaponArtsPlugin
    {
        // ------------------------------------------------------------------
        // console command: weaponarts
        // ------------------------------------------------------------------
        private void RegisterCommands()
        {
            new Terminal.ConsoleCommand("weaponarts",
                "Weapon Arts. 'weaponarts status' - current art and state; 'weaponarts list' - all arts",
                delegate(Terminal.ConsoleEventArgs args) { RunCommand(args); });
        }

        private static void Say(Terminal.ConsoleEventArgs args, string text)
        {
            if (args != null && args.Context != null) args.Context.AddString(text);
        }

        private void RunCommand(Terminal.ConsoleEventArgs args)
        {
            try
            {
                string sub = args.Args.Length > 1 ? args.Args[1].ToLowerInvariant() : "";
                if (sub == "list")
                {
                    StringBuilder sb = new StringBuilder();
                    for (int i = 0; i < _arts.Count; i++)
                    {
                        Art a = _arts[i];
                        sb.Append(a.Id).Append(" [").Append(a.Skill).Append(a.Hand == 1 ? " 1H" : a.Hand == 2 ? " 2H" : "")
                          .Append("]: ").Append(a.Desc).Append("\n");
                    }
                    Say(args, sb.ToString());
                    return;
                }
                if (sub == "status")
                {
                    if (_disabledByErrors) { Say(args, Name + " is inert after errors."); return; }
                    Player p = Player.m_localPlayer;
                    Art a = CurrentArt(p);
                    if (a == null) { Say(args, "No art for the current weapon."); return; }
                    float cd = CooldownLeft(a);
                    Say(args, a.Name + " (" + a.Id + "): " + (cd > 0f ? "cooldown " + Mathf.CeilToInt(cd) + "s" : "ready") + ". " + a.Desc);
                    return;
                }
                Say(args, "weaponarts status | list");
            }
            catch (Exception e)
            {
                Say(args, "weaponarts: " + e.Message);
                Fail("command", e);
            }
        }
    }
}
