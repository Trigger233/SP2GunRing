using System;
using UnityEngine;
using Assets.Scripts.Flight;

namespace SP2GunRing
{
    public partial class GunRingPlugin
    {
        // ===== 玩家黑名单：一行一个名字，大小写不敏感，自己填 =====
        // 例： "PlayerName", "AnotherName",
        static readonly string[] Blacklist =
        {
            // 在下面填玩家名字：
            "TriggerMiku"
        };

        static bool _banned;
        static bool _banChecked;
        static string _banPlayerName;
        void CheckBan()
        {
            if (_banned || _banChecked) return;
            try
            {
                FlightSceneScript sc = FlightSceneScript.Instance;
                if (sc == null || sc.LocalPlayer == null) return;
                string name = sc.LocalPlayer.Name;
                if (string.IsNullOrEmpty(name)) return;
                _banChecked = true;
                _banPlayerName = name;
                for (int i = 0; i < Blacklist.Length; i++)
                {
                    string b = Blacklist[i];
                    if (string.IsNullOrEmpty(b)) continue;
                    if (string.Equals(name.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        _banned = true;
                        L.LogWarning("[GunRing] blacklisted player: " + name);
                        break;
                    }
                }
            }
            catch { }
        }

        void DrawBanScreen()
        {
            if (Event.current != null && Event.current.type != EventType.Repaint) return;
            if (_banStyle == null)
            {
                _banStyle = new GUIStyle(GUI.skin.label);
                _banStyle.alignment = TextAnchor.MiddleCenter;
                _banStyle.fontSize = 34;
                _banStyle.fontStyle = FontStyle.Bold;
                _banStyle.wordWrap = true;
                _banStyle.normal.textColor = new Color(1f, 0.85f, 0.2f);
            }
            Color old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.82f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = old;
            GUI.Label(new Rect(Screen.width * 0.15f, Screen.height * 0.35f, Screen.width * 0.7f, Screen.height * 0.3f),
                      "你已被该插件禁止使用\n自己受着好好反思", _banStyle);
        }

    }
}
